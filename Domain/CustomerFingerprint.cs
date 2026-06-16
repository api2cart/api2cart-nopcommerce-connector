using Nop.Core;

namespace Api2Cart.Connector.Domain
{
  /// <summary>
  /// Tracks last-seen "material" fields per Customer so noisy events
  /// (LastLoginDateUtc, LastActivityDateUtc, LastIpAddress, UpdatedOnUtc auto-touch)
  /// don't trigger webhook deliveries. Material fields are hashed into Fingerprint;
  /// subsequent updates with identical hash are dropped on the bridge side.
  /// </summary>
  public class CustomerFingerprint : BaseEntity
  {
    public int CustomerId { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTime UpdatedOnUtc { get; set; }
  }
}
