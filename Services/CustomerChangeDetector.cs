using System.Security.Cryptography;
using System.Text;
using Api2Cart.Connector.Domain;
using Nop.Core.Domain.Customers;
using Nop.Data;

namespace Api2Cart.Connector.Services
{
  public class CustomerChangeDetector : ICustomerChangeDetector
  {
    private readonly IRepository<CustomerFingerprint> _fingerprintRepo;

    public CustomerChangeDetector(IRepository<CustomerFingerprint> fingerprintRepo)
    {
      _fingerprintRepo = fingerprintRepo;
    }

    public async Task<bool> IsMaterialChangeAsync(Customer current)
    {
      var newHash = ComputeFingerprint(current);

      var stored = _fingerprintRepo.Table.Where(f => f.CustomerId == current.Id).FirstOrDefault();

      if (stored == null) {
        await _fingerprintRepo.InsertAsync(
          new CustomerFingerprint {
            CustomerId = current.Id,
            Fingerprint = newHash,
            UpdatedOnUtc = DateTime.UtcNow,
          }
        );

        return true;
      }

      if (stored.Fingerprint == newHash) {
        return false;
      }

      stored.Fingerprint = newHash;
      stored.UpdatedOnUtc = DateTime.UtcNow;
      await _fingerprintRepo.UpdateAsync(stored);

      return true;
    }

    private static string ComputeFingerprint(Customer c)
    {
      var src = string.Join(
        "|",
        c.Email ?? string.Empty,
        c.Username ?? string.Empty,
        c.Active.ToString(),
        c.Deleted.ToString(),
        c.RegisteredInStoreId.ToString(),
        c.IsTaxExempt.ToString(),
        c.VendorId.ToString(),
        c.AffiliateId.ToString()
      );

      var hash = SHA1.HashData(Encoding.UTF8.GetBytes(src));

      return Convert.ToHexString(hash);
    }
  }
}
