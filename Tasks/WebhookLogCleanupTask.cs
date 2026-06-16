using Api2Cart.Connector.Domain;
using Nop.Data;
using Nop.Services.ScheduleTasks;

namespace Api2Cart.Connector.Tasks
{
  /// <summary>
  /// Trims the WebhookLog table by removing rows older than 30 days. Intended to run weekly.
  /// </summary>
  public class WebhookLogCleanupTask : IScheduleTask
  {
    private const int RetentionDays = 30;
    private const int DeleteBatchSize = 1000;

    private readonly IRepository<WebhookLog> _logRepo;

    public WebhookLogCleanupTask(IRepository<WebhookLog> logRepo)
    {
      _logRepo = logRepo;
    }

    public async Task ExecuteAsync()
    {
      var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);

      while (true) {
        var batch = _logRepo.Table
          .Where(l => l.CreatedOnUtc < cutoff)
          .OrderBy(l => l.Id)
          .Take(DeleteBatchSize)
          .ToList();

        if (batch.Count == 0) {
          break;
        }

        await _logRepo.DeleteAsync(batch);
      }
    }
  }
}
