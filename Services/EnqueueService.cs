using Api2Cart.Connector.Domain;
using Nop.Data;

namespace Api2Cart.Connector.Services
{
  public class EnqueueService : IEnqueueService
  {
    private const int MaxBatchCsvLength = 3500;  // ~250 ids of 14 chars each

    private readonly IRepository<Webhook> _webhookRepo;
    private readonly IRepository<WebhookQueueItem> _queueRepo;

    public EnqueueService(IRepository<Webhook> webhookRepo, IRepository<WebhookQueueItem> queueRepo)
    {
      _webhookRepo = webhookRepo;
      _queueRepo = queueRepo;
    }

    public async Task EnqueueAsync(string entity, string action, int entityId, int storeId)
    {
      // Find every Active webhook subscribed to this topic, in scope of this store.
      // StoreId=0 is the wildcard on BOTH sides:
      //   - Subscription with StoreId=0 fires for events from any store.
      //   - Event with storeId=0 (entities without a direct StoreId column —
      //     Shipment, Product, Category, ProductAttributeCombination) fires to every
      //     subscription on this topic regardless of its StoreId, since we cannot
      //     prove which store the event "belongs to". Receivers must dedupe by
      //     entity+action+id at api2cart layer (the canonical hash check already does this).
      var subscriptions = _webhookRepo.Table
        .Where(w =>
          w.Entity == entity
          && w.Action == action
          && w.Status == 1
          && (w.StoreId == 0 || storeId == 0 || w.StoreId == storeId)
        )
        .ToList();

      if (subscriptions.Count == 0) {
        return;
      }

      var idStr = entityId.ToString();
      var now = DateTime.UtcNow;

      foreach (var sub in subscriptions) {
        // Look for an open coalesce batch (Attempts=0, not full).
        var open = _queueRepo.Table
          .Where(q => q.WebhookId == sub.Id && q.Entity == entity && q.Action == action && q.Attempts == 0 && q.StoreId == storeId)
          .OrderByDescending(q => q.Id)
          .FirstOrDefault();

        if (open != null && open.EntityIds.Length + idStr.Length + 1 < MaxBatchCsvLength) {
          // Append; dedupe trivially (don't add same id twice in same batch).
          var ids = open.EntityIds.Split(',');
          if (Array.IndexOf(ids, idStr) < 0) {
            open.EntityIds = open.EntityIds + "," + idStr;
            await _queueRepo.UpdateAsync(open);
          }
        } else {
          await _queueRepo.InsertAsync(
            new WebhookQueueItem {
              WebhookId = sub.Id,
              Entity = entity,
              Action = action,
              EntityIds = idStr,
              StoreId = storeId,
              Attempts = 0,
              NextAttemptUtc = now,
              CreatedOnUtc = now,
            }
          );
        }
      }
    }
  }
}
