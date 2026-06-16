using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Api2Cart.Connector.Models
{
  public record ConfigureModel : BaseNopModel
  {
    [NopResourceDisplayName("Store Key")]
    public string SecurityToken { get; set; } = string.Empty;

    [NopResourceDisplayName("Webhook Secret")]
    public string WebhookSecret { get; set; } = string.Empty;

    public string ConnectorUrl { get; set; } = string.Empty;

    public List<EntityScopeModel> EntityScopes { get; set; } = [];
  }

  public class EntityScopeModel
  {
    public string EntityName { get; set; } = string.Empty;

    public bool ReadEnabled { get; set; } = true;

    public bool WriteEnabled { get; set; } = true;

    public bool HasWriteActions { get; set; }
  }
}
