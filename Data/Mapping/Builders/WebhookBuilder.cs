using Api2Cart.Connector.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Api2Cart.Connector.Data.Mapping.Builders
{
  public class WebhookBuilder : NopEntityBuilder<Webhook>
  {
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
      table
        .WithColumn(nameof(Webhook.StoreId)).AsInt32().NotNullable()
        .WithColumn(nameof(Webhook.Entity)).AsString(64).NotNullable()
        .WithColumn(nameof(Webhook.Action)).AsString(16).NotNullable()
        .WithColumn(nameof(Webhook.CallbackUrl)).AsString(500).NotNullable()
        .WithColumn(nameof(Webhook.Status)).AsByte().NotNullable().WithDefaultValue(1)
        .WithColumn(nameof(Webhook.FailedCount)).AsInt32().NotNullable().WithDefaultValue(0)
        .WithColumn(nameof(Webhook.LastFailureUtc)).AsDateTime().Nullable()
        .WithColumn(nameof(Webhook.Name)).AsString(200).Nullable()
        .WithColumn(nameof(Webhook.CreatedOnUtc)).AsDateTime().NotNullable()
        .WithColumn(nameof(Webhook.UpdatedOnUtc)).AsDateTime().Nullable();
    }
  }
}
