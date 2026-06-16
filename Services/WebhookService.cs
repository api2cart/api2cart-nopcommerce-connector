using Api2Cart.Connector.Domain;
using Nop.Data;

namespace Api2Cart.Connector.Services
{
  public class WebhookService : IWebhookService
  {
    private const int FailureThreshold = 10;

    private readonly IRepository<Webhook> _repo;

    public WebhookService(IRepository<Webhook> repo)
    {
      _repo = repo;
    }

    public Task<Webhook?> GetByIdAsync(int id) => _repo.GetByIdAsync(id);

    public Task<IList<Webhook>> ListAsync(
        string? entity = null,
        string? action = null,
        byte? status = null,
        int[]? ids = null
    )
    {
      var q = _repo.Table.AsQueryable();

      if (!string.IsNullOrEmpty(entity)) {
        q = q.Where(w => w.Entity == entity);
      }

      if (!string.IsNullOrEmpty(action)) {
        q = q.Where(w => w.Action == action);
      }

      if (status.HasValue) {
        q = q.Where(w => w.Status == status.Value);
      }

      if (ids != null && ids.Length > 0) {
        q = q.Where(w => ids.Contains(w.Id));
      }

      return Task.FromResult<IList<Webhook>>(q.ToList());
    }

    public async Task<int> InsertAsync(Webhook webhook)
    {
      webhook.CreatedOnUtc = DateTime.UtcNow;
      await _repo.InsertAsync(webhook);

      return webhook.Id;
    }

    public async Task UpdateAsync(Webhook webhook)
    {
      webhook.UpdatedOnUtc = DateTime.UtcNow;
      await _repo.UpdateAsync(webhook);
    }

    public Task DeleteAsync(int id) => _repo.DeleteAsync(new Webhook { Id = id });

    public async Task RecordFailureAsync(int webhookId)
    {
      var webhook = await _repo.GetByIdAsync(webhookId);

      if (webhook == null) {
        return;
      }

      webhook.FailedCount++;
      webhook.LastFailureUtc = DateTime.UtcNow;

      if (webhook.FailedCount >= FailureThreshold) {
        webhook.Status = 2;  // Failed
      }

      await UpdateAsync(webhook);
    }
  }
}
