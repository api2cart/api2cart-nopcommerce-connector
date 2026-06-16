using Api2Cart.Connector.Domain;

namespace Api2Cart.Connector.Services
{
  public interface IWebhookService
  {
    Task<Webhook?> GetByIdAsync(int id);
    Task<IList<Webhook>> ListAsync(string? entity = null, string? action = null, byte? status = null, int[]? ids = null);
    Task<int> InsertAsync(Webhook webhook);
    Task UpdateAsync(Webhook webhook);
    Task DeleteAsync(int id);
    Task RecordFailureAsync(int webhookId);
  }
}
