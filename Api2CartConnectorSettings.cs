using Nop.Core.Configuration;

namespace Api2Cart.Connector
{
  public class Api2CartConnectorSettings : ISettings
  {
    public string SecurityToken { get; set; } = string.Empty;

    public string DisabledScopes { get; set; } = string.Empty;

    public string ConnectorPublicKeyPem { get; set; } = string.Empty;

    public string ConnectorKeyId { get; set; } = string.Empty;

    public string CallbackUrl { get; set; } = string.Empty;

    public string StoreUrl { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;
  }
}
