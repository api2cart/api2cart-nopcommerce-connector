using System.Text;
using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.ScheduleTasks;
using Nop.Core.Domain.Stores;
using Nop.Core.Infrastructure;
using Nop.Services.Configuration;
using Nop.Services.Logging;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;
using Nop.Web.Framework.Menu;

namespace Api2Cart.Connector
{
  public class Api2CartConnectorPlugin : BasePlugin
  {
    public const string MaxSupportedNopVersion = "4.90";

    private const string ConfigFileName = "connector.config.json";
    private const string TokenPlaceholder = "PASTE_SECURITY_TOKEN_HERE";
    private const int CallbackTimeoutSeconds = 10;

    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly INopFileProvider _fileProvider;
    private readonly ILogger _logger;
    private readonly IScheduleTaskService _scheduleTaskService;

    public Api2CartConnectorPlugin(
        ISettingService settingService,
        IStoreContext storeContext,
        INopFileProvider fileProvider,
        ILogger logger,
        IScheduleTaskService scheduleTaskService
    )
    {
      _settingService = settingService;
      _storeContext = storeContext;
      _fileProvider = fileProvider;
      _logger = logger;
      _scheduleTaskService = scheduleTaskService;
    }

    public override async Task InstallAsync()
    {
      ValidateNopVersion();

      var store = await _storeContext.GetCurrentStoreAsync();
      var existing = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(store.Id);

      if (string.IsNullOrEmpty(existing.SecurityToken)) {
        existing.SecurityToken = "nop_" + Guid.NewGuid().ToString("N");
      }

      // APITOCART-45718: the plugin owns the webhook signing secret. Generate one store-level secret
      // on install; the merchant copies it into API2Cart (account.cart.add nopcommerce_secret). It is
      // persisted at shared scope (storeId 0) as well so the dispatch task can sign every batch,
      // including wildcard (storeId 0) deliveries.
      if (string.IsNullOrEmpty(existing.WebhookSecret)) {
        existing.WebhookSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
      }

      AssignIfPresent(ReadConfigValue("ConnectorPublicKey"), v => existing.ConnectorPublicKeyPem = v);
      AssignIfPresent(ReadConfigValue("ConnectorKeyId"),     v => existing.ConnectorKeyId        = v);
      AssignIfPresent(ReadConfigValue("CallbackUrl"),     v => existing.CallbackUrl        = v);
      existing.StoreUrl = store.Url?.TrimEnd('/') ?? string.Empty;

      await _settingService.SaveSettingAsync(existing, store.Id);
      await _settingService.SaveSettingAsync(existing, x => x.WebhookSecret, 0);

      await EnsureScheduledTaskAsync(
        new ScheduleTask {
          Name = "API2Cart webhook dispatch",
          Type = "Api2Cart.Connector.Tasks.WebhookDispatchTask, Api2Cart.Connector",
          Seconds = 15,
          Enabled = true,
          StopOnError = false,
          LastEnabledUtc = DateTime.UtcNow,
        }
      );

      await EnsureScheduledTaskAsync(
        new ScheduleTask {
          Name = "API2Cart webhook log cleanup",
          Type = "Api2Cart.Connector.Tasks.WebhookLogCleanupTask, Api2Cart.Connector",
          Seconds = 604800,
          Enabled = true,
          StopOnError = false,
          LastEnabledUtc = DateTime.UtcNow,
        }
      );

      await base.InstallAsync();
      await NotifyCallbackUrlAsync(existing, store);
    }

    public override string GetConfigurationPageUrl()
    {
      return $"/api/{ConnectorConfig.Slug}/configure";
    }

    public override async Task UninstallAsync()
    {
      foreach (var name in new[] {"API2Cart webhook dispatch", "API2Cart webhook log cleanup"}) {
        var task = (await _scheduleTaskService.GetAllTasksAsync(true)).FirstOrDefault(t => t.Name == name);

        if (task != null) {
          await _scheduleTaskService.DeleteTaskAsync(task);
        }
      }

      await base.UninstallAsync();
    }

    private async Task EnsureScheduledTaskAsync(ScheduleTask task)
    {
      var existing = (await _scheduleTaskService.GetAllTasksAsync(false)).FirstOrDefault(t => t.Name == task.Name);

      if (existing == null) {
        await _scheduleTaskService.InsertTaskAsync(task);
      }
    }

    private static void ValidateNopVersion()
    {
      var current = NopVersion.CURRENT_VERSION;

      if (CompareVersions(current, MaxSupportedNopVersion) > 0) {
        throw new NopException(
          $"This plugin supports NopCommerce up to version {MaxSupportedNopVersion}. " +
          $"Current installed version is {current}. Please upgrade the plugin to a build that supports this NopCommerce version."
        );
      }
    }

    private static int CompareVersions(string a, string b)
    {
      var pa = ParseVersionParts(a);
      var pb = ParseVersionParts(b);
      var len = Math.Max(pa.Length, pb.Length);

      for (var i = 0; i < len; i++) {
        var ai = i < pa.Length ? pa[i] : 0;
        var bi = i < pb.Length ? pb[i] : 0;

        if (ai != bi) {
          return ai.CompareTo(bi);
        }
      }

      return 0;
    }

    private static int[] ParseVersionParts(string version)
    {
      if (string.IsNullOrEmpty(version)) {
        return [];
      }

      var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
      var result = new int[parts.Length];

      for (var i = 0; i < parts.Length; i++) {
        result[i] = int.TryParse(parts[i], out var n) ? n : 0;
      }

      return result;
    }

    private static void AssignIfPresent(string value, Action<string> assign)
    {
      if (!string.IsNullOrEmpty(value)) {
        assign(value);
      }
    }

    private string ReadConfigValue(string key, Func<string>? fallback = null)
    {
      var pluginDir = _fileProvider.MapPath("~/Plugins/Api2Cart.Connector");
      var configPath = Path.Combine(pluginDir, ConfigFileName);

      if (File.Exists(configPath)) {
        try {
          var json = File.ReadAllText(configPath);
          var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

          if (config != null
            && config.TryGetValue(key, out var value)
            && !string.IsNullOrEmpty(value)
            && value != TokenPlaceholder) {
            return value;
          }
        } catch {
          // fall through to fallback
        }
      }

      return fallback?.Invoke() ?? string.Empty;
    }

    private async Task NotifyCallbackUrlAsync(Api2CartConnectorSettings settings, Store store)
    {
      var callbackUrl = settings.CallbackUrl;

      if (string.IsNullOrEmpty(callbackUrl)) {
        return;
      }

      var pluginVersion = ReadConfigValue("PluginVersion", fallback: () => "1.0.0");
      var payload = new {
        @event = "plugin_installed",
        plugin = "Api2Cart.Connector",
        plugin_version = pluginVersion,
        store_url = settings.StoreUrl,
        security_token = settings.SecurityToken,
        webhook_secret = settings.WebhookSecret,
        connector_key_id = settings.ConnectorKeyId,
        nop_version = NopVersion.CURRENT_VERSION,
        timestamp = DateTime.UtcNow.ToString("o"),
      };

      try {
        using var http = new HttpClient {
          Timeout = TimeSpan.FromSeconds(CallbackTimeoutSeconds),
        };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await http.PostAsync(callbackUrl, content);
      } catch (Exception ex) {
        await _logger.WarningAsync($"[{ConnectorConfig.FriendlyName}] Install callback POST failed: " + ex.Message, ex);
      }
    }
  }
}
