using Nop.Core;

namespace Api2Cart.Connector.Domain
{
  public class WebhookLog : BaseEntity
  {
    public int WebhookId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityIds { get; set; } = string.Empty;
    public int? HttpStatus { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedOnUtc { get; set; }
  }
}
