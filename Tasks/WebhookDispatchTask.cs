using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api2Cart.Connector;
using Api2Cart.Connector.Domain;
using Api2Cart.Connector.Services;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Shipping;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Configuration;
using Nop.Services.ScheduleTasks;

namespace Api2Cart.Connector.Tasks
{
  /// <summary>
  /// Drains the webhook queue: pulls up to <see cref="BatchPullSize"/> due batches per run,
  /// signs each payload with HMAC-SHA256, POSTs to the subscriber endpoint, and applies
  /// the tier-2 backoff schedule on transient failures.
  /// </summary>
  public class WebhookDispatchTask : IScheduleTask
  {
    private const int BatchPullSize = 100;
    private const int MaxAttempts = 4;
    // Fallback timeout if IHttpClientFactory is not available (factory-configured client uses 30s
    // via Api2CartConnectorApiStartup.cs::ConfigureServices). 30s tolerates dotnet HttpClient cold-start
    // in Docker (DNS + TLS handshake on a fresh connection can take 5-15s after long idle).
    private const int HttpTimeoutSeconds = 30;
    private const string PluginIdentifier = "Api2Cart.Connector";

    // APITOCART-45718 delivery schedule: attempt 1 fires immediately (NextAttemptUtc set on
    // enqueue); attempts 2-4 are retried at +5m, +10m, +30m after the previous failure.
    private static readonly TimeSpan[] RetryBackoff = new[] {
      TimeSpan.FromMinutes(5),
      TimeSpan.FromMinutes(10),
      TimeSpan.FromMinutes(30),
    };

    private readonly IRepository<WebhookQueueItem> _queueRepo;
    private readonly IRepository<WebhookLog> _logRepo;
    private readonly IRepository<ProductAttributeCombination> _combinationRepo;
    private readonly IRepository<Shipment> _shipmentRepo;
    private readonly IWebhookService _webhookService;
    private readonly ISettingService _settingService;
    private readonly IHttpClientFactory? _httpClientFactory;

    public WebhookDispatchTask(
        IRepository<WebhookQueueItem> queueRepo,
        IRepository<WebhookLog> logRepo,
        IRepository<ProductAttributeCombination> combinationRepo,
        IRepository<Shipment> shipmentRepo,
        IWebhookService webhookService,
        ISettingService settingService
    )
    {
      _queueRepo = queueRepo;
      _logRepo = logRepo;
      _combinationRepo = combinationRepo;
      _shipmentRepo = shipmentRepo;
      _webhookService = webhookService;
      _settingService = settingService;

      // IHttpClientFactory is registered by NopCommerce on the root provider; resolve
      // optionally to keep the constructor signature simple and avoid coupling to NC
      // dependency-registrar wiring (which lives outside this batch).
      _httpClientFactory =
        EngineContext.Current.Resolve<IServiceProvider>()
          ?.GetService<IHttpClientFactory>();
    }

    public async Task ExecuteAsync()
    {
      var now = DateTime.UtcNow;

      var dueBatches = _queueRepo.Table
        .Where(q => q.NextAttemptUtc <= now)
        .OrderBy(q => q.Id)
        .Take(BatchPullSize)
        .ToList();

      if (dueBatches.Count == 0) {
        return;
      }

      using var http = CreateHttpClient();

      foreach (var batch in dueBatches) {
        await ProcessBatchAsync(batch, http);
      }
    }

    private HttpClient CreateHttpClient()
    {
      var client = _httpClientFactory?.CreateClient("Api2Cart.Webhook") ?? new HttpClient();
      client.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

      return client;
    }

    private async Task ProcessBatchAsync(WebhookQueueItem batch, HttpClient http)
    {
      var webhook = await _webhookService.GetByIdAsync(batch.WebhookId);

      if (webhook == null) {
        // Subscriber gone, drop batch.
        await _queueRepo.DeleteAsync(batch);

        return;
      }

      var settings = await _settingService.LoadSettingAsync<Api2CartConnectorSettings>(webhook.StoreId);
      var readScope = ScopeRegistry.GetReadScopeForWebhookEntity(batch.Entity);

      if (readScope != null && ScopeRegistry.ParseDisabledScopes(settings.DisabledScopes).Contains(readScope)) {
        await _queueRepo.DeleteAsync(batch);
        await WriteLogAsync(batch, null, $"Skipped: scope '{readScope}' is disabled.", 0, false);

        return;
      }

      var topic = $"{batch.Entity}_{batch.Action}";
      var unixTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      var ids = batch.EntityIds
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(s => int.TryParse(s, out var n) ? n : 0)
        .Where(n => n > 0)
        .ToArray();

      var parentIds = new Dictionary<int, int>();

      if (batch.Action != "delete" && ids.Length > 0) {
        if (batch.Entity == "order.shipment") {
          parentIds = _shipmentRepo.Table.Where(s => ids.Contains(s.Id)).ToDictionary(s => s.Id, s => s.OrderId);
        } else if (batch.Entity == "product.child_item") {
          parentIds = _combinationRepo.Table.Where(c => ids.Contains(c.Id)).ToDictionary(c => c.Id, c => c.ProductId);
        }
      }

      var body = JsonSerializer.Serialize(
        new {
          topic,
          entity_ids = ids,
          parent_ids = parentIds,
          store_id = batch.StoreId,
          plugin = PluginIdentifier,
          timestamp = unixTs,
        }
      );

      // APITOCART-45718: webhooks are signed with the store-level secret generated by this plugin
      // (shared-scope setting), not a per-hook key. The merchant copies it into API2Cart; without
      // it no delivery can be signed, so drop the batch and record the failure.
      var webhookSecret = settings.WebhookSecret;

      if (string.IsNullOrEmpty(webhookSecret)) {
        await _queueRepo.DeleteAsync(batch);
        await WriteLogAsync(batch, null, "Webhook secret is not configured.", 0, false);
        await _webhookService.RecordFailureAsync(webhook.Id);

        return;
      }

      var signature = WebhookSigner.Sign(body, webhookSecret);
      var deliveryId = ComputeDeliveryId(batch.Id, batch.Attempts);

      var started = DateTime.UtcNow;
      HttpResponseMessage? response = null;
      string? errorMessage = null;
      var responseBody = string.Empty;

      try {
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.CallbackUrl) {
          Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Webhook-Signature", signature);
        request.Headers.TryAddWithoutValidation("X-Webhook-Id", webhook.Id.ToString());
        request.Headers.TryAddWithoutValidation("X-Webhook-Delivery-Id", deliveryId);
        request.Headers.TryAddWithoutValidation("X-Webhook-Topic", topic);
        request.Headers.TryAddWithoutValidation("X-Webhook-Timestamp", unixTs.ToString());

        response = await http.SendAsync(request);
        responseBody = await SafeReadAsync(response);
      } catch (TaskCanceledException ex) {
        errorMessage = $"Timeout: {ex.Message}";
      } catch (HttpRequestException ex) {
        errorMessage = $"Network: {ex.Message}";
      } catch (Exception ex) {
        errorMessage = $"Error: {ex.Message}";
      }

      var durationMs = (int)(DateTime.UtcNow - started).TotalMilliseconds;
      var httpStatus = response != null ? (int?)response.StatusCode : null;

      if (response != null && response.IsSuccessStatusCode) {
        // 2xx: success → delete batch, log, clear failure counter.
        await _queueRepo.DeleteAsync(batch);
        await WriteLogAsync(batch, httpStatus, responseBody, durationMs, true);

        if (webhook.FailedCount > 0) {
          webhook.FailedCount = 0;
          await _webhookService.UpdateAsync(webhook);
        }

        return;
      }

      var statusCode = response != null ? (int)response.StatusCode : 0;

      // APITOCART-45718: api2cart answers 403 (WEBHOOK_VALIDATION_FAILED / WEBHOOK_INVALID_DATA)
      // or 404 (STORE_NOT_FOUND / WEBHOOK_NOT_FOUND). Both are permanent misconfigurations that
      // retrying can never fix, so disable the webhook immediately and drop the batch.
      if (statusCode == 403 || statusCode == 404) {
        await _queueRepo.DeleteAsync(batch);
        webhook.Status = WebhookStatus.Failed;
        webhook.LastFailureUtc = DateTime.UtcNow;
        await _webhookService.UpdateAsync(webhook);
        await WriteLogAsync(batch, httpStatus, responseBody, durationMs, false);

        return;
      }

      // APITOCART-45718: only 401 / 500 / 502 are transient failures worth retrying. A timeout,
      // network or DNS error (no response at all) is transient too. Any other unexpected response
      // is non-retryable — drop the batch and record the failure without rescheduling.
      var isTransient = statusCode == 401 || statusCode == 500 || statusCode == 502 || response == null;

      if (!isTransient) {
        await _queueRepo.DeleteAsync(batch);
        await WriteLogAsync(batch, httpStatus, responseBody + (errorMessage ?? string.Empty), durationMs, false);
        await _webhookService.RecordFailureAsync(webhook.Id);

        return;
      }

      // Transient failure: schedule the next attempt on the 0/+5m/+10m/+30m ladder.
      batch.Attempts = (byte)Math.Min(batch.Attempts + 1, byte.MaxValue);

      if (batch.Attempts >= MaxAttempts) {
        // All 4 attempts exhausted — drop this delivery and record the failure.
        await _queueRepo.DeleteAsync(batch);
        await WriteLogAsync(batch, httpStatus, responseBody + (errorMessage ?? string.Empty), durationMs, false);
        await _webhookService.RecordFailureAsync(webhook.Id);

        return;
      }

      var backoffIndex = Math.Min(batch.Attempts - 1, RetryBackoff.Length - 1);
      batch.NextAttemptUtc = DateTime.UtcNow.Add(RetryBackoff[backoffIndex]);
      await _queueRepo.UpdateAsync(batch);
      await WriteLogAsync(batch, httpStatus, responseBody + (errorMessage ?? string.Empty), durationMs, false);
    }

    private async Task WriteLogAsync(
        WebhookQueueItem batch,
        int? httpStatus,
        string responseBody,
        int durationMs,
        bool success
    )
    {
      await _logRepo.InsertAsync(
        new WebhookLog {
          WebhookId = batch.WebhookId,
          Entity = batch.Entity,
          Action = batch.Action,
          EntityIds = batch.EntityIds,
          HttpStatus = httpStatus,
          ResponseBody = Truncate(responseBody, 1000),
          DurationMs = durationMs,
          Success = success,
          CreatedOnUtc = DateTime.UtcNow,
        }
      );
    }

    private static string ComputeDeliveryId(int batchId, byte attempts)
    {
      var input = $"{batchId}:{attempts}";
      var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));

      return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response)
    {
      try {
        return await response.Content.ReadAsStringAsync();
      } catch {
        return string.Empty;
      }
    }

    private static string Truncate(string? value, int max)
    {
      if (string.IsNullOrEmpty(value)) {
        return string.Empty;
      }

      return value.Length <= max ? value : value.Substring(0, max);
    }
  }
}
