using Nop.Core;

namespace Api2Cart.Connector.Domain
{
  public class WebhookQueueItem : BaseEntity
  {
    public int WebhookId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityIds { get; set; } = string.Empty;
    public int StoreId { get; set; }
    public byte Attempts { get; set; }
    public DateTime NextAttemptUtc { get; set; }
    public DateTime CreatedOnUtc { get; set; }
  }
}
