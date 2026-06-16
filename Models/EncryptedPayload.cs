using System.Text.Json.Serialization;

namespace Api2Cart.Connector.Models
{
  public class EncryptedPayload
  {
    [JsonPropertyName("encryptedKey")]
    public string EncryptedKey { get; set; } = string.Empty;

    [JsonPropertyName("iv")]
    public string Iv { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
  }
}
