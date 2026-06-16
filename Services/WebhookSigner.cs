using System.Security.Cryptography;
using System.Text;

namespace Api2Cart.Connector.Services
{
  public static class WebhookSigner
  {
    public static string Sign(string body, string secretKey)
    {
      using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
      var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));

      return Convert.ToBase64String(hash);
    }
  }
}
