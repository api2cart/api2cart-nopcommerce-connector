using Api2Cart.Connector.Domain;
using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Api2Cart.Connector.Migrations
{
  [NopMigration(
    "2026/05/22 00:00:00",
    "Api2Cart.Connector — customer noisy-event filter fingerprint table",
    MigrationProcessType.NoMatter
  )]
  public class AddCustomerFingerprintTable : AutoReversingMigration
  {
    public override void Up()
    {
      Create.TableFor<CustomerFingerprint>();

      Create.Index("IX_CustomerFingerprint_CustomerId").OnTable(nameof(CustomerFingerprint))
        .OnColumn(nameof(CustomerFingerprint.CustomerId)).Ascending()
        .WithOptions().Unique();
    }
  }
}
