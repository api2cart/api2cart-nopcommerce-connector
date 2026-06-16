namespace Api2Cart.Connector.Services
{
  public interface IEnqueueService
  {
    Task EnqueueAsync(string entity, string action, int entityId, int storeId);
  }
}
