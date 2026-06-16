using Nop.Core.Domain.Customers;

namespace Api2Cart.Connector.Services
{
  public interface ICustomerChangeDetector
  {
    /// <summary>
    /// Returns true if the customer's material fields changed since the last seen
    /// fingerprint. Updates the stored fingerprint as a side effect on first observation
    /// or on a real change. Returns false for noisy events (LastLogin/LastActivity/UpdatedOnUtc
    /// auto-touch).
    /// </summary>
    Task<bool> IsMaterialChangeAsync(Customer current);
  }
}
