using System.Text.Json;
using Api2Cart.Connector.Filters;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Api2Cart.Connector.Controllers
{
  [Route("api/api2cart")]
  [TypeFilter(typeof(DecryptionFilter))]
  public partial class Api2CartConnectorController : BasePluginController
  {
    private const string PluginVersion = "1.0.0";
    private const string TokenHeader = "X-Connection-Token";
    private const string TokenQueryParam = "token";

    private readonly ISettingService _settingService;
    private readonly IStoreContext _storeContext;
    private readonly IStoreService _storeService;
    private readonly ICurrencyService _currencyService;
    private readonly ILanguageService _languageService;
    private readonly ICustomerService _customerService;
    private readonly CurrencySettings _currencySettings;

    private Api2CartConnectorSettings? _resolvedSettings;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public Api2CartConnectorController(
        ISettingService settingService,
        IStoreContext storeContext,
        IStoreService storeService,
        ICurrencyService currencyService,
        ILanguageService languageService,
        ICustomerService customerService,
        CurrencySettings currencySettings
    )
    {
      _settingService = settingService;
      _storeContext = storeContext;
      _storeService = storeService;
      _currencyService = currencyService;
      _languageService = languageService;
      _customerService = customerService;
      _currencySettings = currencySettings;
    }

    #region Base Method Overrides (prevent route ambiguity)

    [NonAction]
    public override Task<IActionResult> SaveSelectedTabNameAsync(string tabName = "", bool persistForTheNextRequest = true)
      => Task.FromResult<IActionResult>(NotFound());

    [NonAction]
    public override void SaveSelectedCardName(string cardName = "", bool persistForTheNextRequest = true) { }

    #endregion

    #region Configuration

    [Area(AreaNames.ADMIN)]
    [AutoValidateAntiforgeryToken]
    [AuthorizeAdmin]
    [HttpGet("configure")]
    public async Task<IActionResult> Configure()
    {
      var store = await _storeContext.GetCurrentStoreAsync();
      var settings = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(store.Id);

      var model = BuildConfigureModel(settings, store);

      return View("~/Plugins/Api2Cart.Connector/Views/Configure.cshtml", model);
    }

    [Area(AreaNames.ADMIN)]
    [AutoValidateAntiforgeryToken]
    [AuthorizeAdmin]
    [HttpPost("configure")]
    public async Task<IActionResult> Configure(ConfigureModel model, [FromForm] string? action = null)
    {
      var store = await _storeContext.GetCurrentStoreAsync();
      var settings = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(store.Id);

      if (action == "regenerate") {
        settings.SecurityToken = "nop_" + Guid.NewGuid().ToString("N");
      }

      if (action == "regenerate_webhook_secret") {
        settings.WebhookSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await _settingService.SaveSettingAsync(settings, x => x.WebhookSecret, 0);
      }

      var disabled = new HashSet<string>();
      var entitiesWithWrite = ScopeRegistry.GetEntitiesWithWriteActions();

      foreach (var entity in ScopeRegistry.EntityNames) {
        var readKey = $"scope_{entity}_Read";

        if (Request.Form[readKey].FirstOrDefault() != "on") {
          disabled.Add($"{entity}.Read");
        }

        if (!entitiesWithWrite.Contains(entity)) {
          continue;
        }

        var writeKey = $"scope_{entity}_Write";

        if (Request.Form[writeKey].FirstOrDefault() != "on") {
          disabled.Add($"{entity}.Write");
        }
      }

      settings.DisabledScopes = ScopeRegistry.SerializeDisabledScopes(disabled);
      await _settingService.SaveSettingAsync(settings, store.Id);

      var updatedModel = BuildConfigureModel(settings, store);

      return View("~/Plugins/Api2Cart.Connector/Views/Configure.cshtml", updatedModel);
    }

    private ConfigureModel BuildConfigureModel(Api2CartConnectorSettings settings, Nop.Core.Domain.Stores.Store store)
    {
      var disabled = ScopeRegistry.ParseDisabledScopes(settings.DisabledScopes);
      var entitiesWithWrite = ScopeRegistry.GetEntitiesWithWriteActions();

      var model = new ConfigureModel
      {
        SecurityToken = settings.SecurityToken,
        WebhookSecret = settings.WebhookSecret,
        ConnectorUrl = $"{store.Url.TrimEnd('/')}/api/api2cart/",
      };

      foreach (var entity in ScopeRegistry.EntityNames) {
        model.EntityScopes.Add(
          new EntityScopeModel {
            EntityName      = entity,
            ReadEnabled     = !disabled.Contains($"{entity}.Read"),
            WriteEnabled    = !disabled.Contains($"{entity}.Write"),
            HasWriteActions = entitiesWithWrite.Contains(entity),
          }
        );
      }

      return model;
    }

    #endregion

    #region Auth

    private async Task<IActionResult?> AuthorizeRequest()
    {
      var requestToken = Request.Headers[TokenHeader].FirstOrDefault()
        ?? Request.Query[TokenQueryParam].FirstOrDefault();

      if (string.IsNullOrEmpty(requestToken)) {
        return ErrorResponse("AUTH_FAILED", "Invalid or missing security token.", 401);
      }

      var store = await _storeContext.GetCurrentStoreAsync();
      var settings = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(store.Id);

      if (settings.SecurityToken != requestToken) {
        var globalSettings = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(0);

        if (globalSettings.SecurityToken == requestToken) {
          settings = globalSettings;
        } else {
          return ErrorResponse("AUTH_FAILED", "Invalid or missing security token.", 401);
        }
      }

      var actionName = Request.Path.Value?.Replace("/api/api2cart/", "").TrimEnd('/');

      if (!string.IsNullOrEmpty(actionName)) {
        var scope = ScopeRegistry.GetScopeForAction(actionName);

        if (scope != null) {
          var disabled = ScopeRegistry.ParseDisabledScopes(settings.DisabledScopes);

          if (disabled.Contains(scope)) {
            return ErrorResponse(
              "ACTION_DISABLED",
              $"Action '{actionName}' is disabled. Scope '{scope}' is not enabled.",
              403
            );
          }
        }
      }

      _resolvedSettings = settings;

      return null;
    }

    #endregion
  }
}
