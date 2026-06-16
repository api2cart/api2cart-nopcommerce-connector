using Api2Cart.Connector.Domain;
using Api2Cart.Connector.Models;
using Api2Cart.Connector.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Infrastructure;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Webhooks

    private const string WebhooksApiVersion = "1.1.0";

    private static readonly HashSet<string> AllowedWebhookEntities = new(StringComparer.Ordinal) {
      "order",
      "order.shipment",
      "subscriber",
      "productReview",
      "product",
      "category",
      "customer",
      "product.child_item",
    };

    private static readonly HashSet<string> AllowedWebhookActions = new(StringComparer.Ordinal) {
      "add",
      "update",
      "delete",
    };

    [HttpPost("webhooks-list")]
    public async Task<IActionResult> ListWebhooks(
        [FromQuery] string? entity = null,
        [FromQuery] string? action = null,
        [FromQuery] string? active = null,
        [FromQuery] string? ids = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        byte? statusFilter = null;

        if (!string.IsNullOrEmpty(active)) {
          if (bool.TryParse(active, out var parsedBool)) {
            statusFilter = parsedBool ? WebhookStatus.Active : WebhookStatus.Inactive;
          } else if (byte.TryParse(active, out var parsedByte)) {
            statusFilter = parsedByte;
          }
        }

        var idFilter = ParseIntIds(ids);
        var webhookService = EngineContext.Current.Resolve<IWebhookService>();
        var webhooks = await webhookService.ListAsync(entity, action, statusFilter, idFilter);

        var result = webhooks.Select(BuildWebhookData).ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {webhooks = result, total_count = result.Count},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("webhooks-add")]
    public async Task<IActionResult> AddWebhook(
        [FromQuery] string? store_id = null,
        [FromQuery] string? entity = null,
        [FromQuery] string? action = null,
        [FromQuery] string? callback_url = null,
        [FromQuery] string? status = null,
        [FromQuery] string? name = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (string.IsNullOrWhiteSpace(entity) || !AllowedWebhookEntities.Contains(entity)) {
          return ParamError(
            "Parameter 'entity' must be one of: order, order.shipment, subscriber, productReview, "
            + "product, category, customer, product.child_item."
          );
        }

        var readScope = ScopeRegistry.GetReadScopeForWebhookEntity(entity);

        if (readScope != null && ScopeRegistry.ParseDisabledScopes(_resolvedSettings!.DisabledScopes).Contains(readScope)) {
          return ErrorResponse(
            "ACTION_DISABLED",
            $"Cannot subscribe to '{entity}' webhooks: scope '{readScope}' is not enabled.",
            403
          );
        }

        if (string.IsNullOrWhiteSpace(action) || !AllowedWebhookActions.Contains(action)) {
          return ParamError("Parameter 'action' must be one of: add, update, delete.");
        }

        // subscriber has no delete semantics (NewsLetterSubscription unsubscribe = Active=false update,
        // never a row delete). All other entities support add/update/delete.
        if (entity == "subscriber" && action == "delete") {
          return ParamError($"Action 'delete' is not supported for entity '{entity}'.");
        }

        if (string.IsNullOrWhiteSpace(callback_url)) {
          return ParamError("Parameter 'callback_url' is required.");
        }

        if (string.IsNullOrWhiteSpace(name)) {
          return ParamError("Parameter 'name' is required.");
        }

        var storeIdValue = ParseInt(store_id, 0);

        if (storeIdValue < 0) {
          return ParamError("Parameter 'store_id' must be a non-negative integer.");
        }

        byte statusValue = WebhookStatus.Active;

        if (!string.IsNullOrEmpty(status)) {
          if (bool.TryParse(status, out var parsedBool)) {
            statusValue = parsedBool ? WebhookStatus.Active : WebhookStatus.Inactive;
          } else if (byte.TryParse(status, out var parsedByte)) {
            statusValue = parsedByte;
          } else {
            return ParamError("Parameter 'status' must be 0, 1, true, or false.");
          }
        }

        var webhook = new Webhook {
          StoreId = storeIdValue,
          Entity = entity,
          Action = action,
          CallbackUrl = callback_url,
          Status = statusValue,
          FailedCount = 0,
          Name = name,
        };

        var webhookService = EngineContext.Current.Resolve<IWebhookService>();
        var newId = await webhookService.InsertAsync(webhook);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {id = newId},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("webhooks-version")]
    public async Task<IActionResult> GetWebhookVersion()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      return JsonContent(
        new ConnectorResponse<object> {
          Result = new {version = WebhooksApiVersion},
        }
      );
    }

    [HttpPost("webhooks-update")]
    public async Task<IActionResult> UpdateWebhook([FromQuery] string? id = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var webhookId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var webhookService = EngineContext.Current.Resolve<IWebhookService>();
        var webhook = await webhookService.GetByIdAsync(webhookId);

        if (webhook == null) {
          return NotFoundError($"Webhook with id {webhookId} not found.");
        }

        // v1: update is a no-op. Subscription mutation is not supported in this version.
        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {updated = true},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("webhooks-delete")]
    public async Task<IActionResult> DeleteWebhook([FromQuery] string? id = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var webhookId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var webhookService = EngineContext.Current.Resolve<IWebhookService>();
        var webhook = await webhookService.GetByIdAsync(webhookId);

        if (webhook == null) {
          return NotFoundError($"Webhook with id {webhookId} not found.");
        }

        await webhookService.DeleteAsync(webhookId);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {deleted = true},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private static Dictionary<string, object?> BuildWebhookData(Webhook webhook)
    {
      return new Dictionary<string, object?> {
        ["id"] = webhook.Id,
        ["store_id"] = webhook.StoreId,
        ["entity"] = webhook.Entity,
        ["action"] = webhook.Action,
        ["callback_url"] = webhook.CallbackUrl,
        ["status"] = webhook.Status,
        ["name"] = webhook.Name,
        ["failed_count"] = webhook.FailedCount,
        ["last_failure_at"] = webhook.LastFailureUtc?.ToString("o"),
        ["created_at"] = webhook.CreatedOnUtc.ToString("o"),
        ["updated_at"] = webhook.UpdatedOnUtc?.ToString("o"),
      };
    }

    #endregion
  }
}
