using Api2Cart.Connector.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Api2Cart.Connector.Data.Mapping.Builders
{
  public class CustomerFingerprintBuilder : NopEntityBuilder<CustomerFingerprint>
  {
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
      table
        .WithColumn(nameof(CustomerFingerprint.CustomerId)).AsInt32().NotNullable()
        .WithColumn(nameof(CustomerFingerprint.Fingerprint)).AsString(40).NotNullable()
        .WithColumn(nameof(CustomerFingerprint.UpdatedOnUtc)).AsDateTime().NotNullable();
    }
  }
}
