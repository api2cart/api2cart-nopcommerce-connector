using Nop.Data.Mapping;

namespace Api2Cart.Connector.Models
{
  public class TaxRateNameCompatibility : INameCompatibility
  {
    public Dictionary<Type, string> TableNames => new()
    {
      { typeof(TaxRateRecord), "TaxRate" },
    };

    public Dictionary<(Type, string), string> ColumnName => new();
  }
}
