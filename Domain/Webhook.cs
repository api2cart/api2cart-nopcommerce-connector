using Nop.Core;

namespace Api2Cart.Connector.Domain
{
  public class Webhook : BaseEntity
  {
    public int StoreId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public byte Status { get; set; }
    public int FailedCount { get; set; }
    public DateTime? LastFailureUtc { get; set; }
    public string? Name { get; set; }
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
  }
}
