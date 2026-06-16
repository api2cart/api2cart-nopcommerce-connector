using Api2Cart.Connector.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Api2Cart.Connector.Data.Mapping.Builders
{
  public class WebhookLogBuilder : NopEntityBuilder<WebhookLog>
  {
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
      table
        .WithColumn(nameof(WebhookLog.WebhookId)).AsInt32().NotNullable()
        .WithColumn(nameof(WebhookLog.Entity)).AsString(64).NotNullable()
        .WithColumn(nameof(WebhookLog.Action)).AsString(16).NotNullable()
        .WithColumn(nameof(WebhookLog.EntityIds)).AsString(4000).Nullable()
        .WithColumn(nameof(WebhookLog.HttpStatus)).AsInt32().Nullable()
        .WithColumn(nameof(WebhookLog.ResponseBody)).AsString(1000).Nullable()
        .WithColumn(nameof(WebhookLog.DurationMs)).AsInt32().NotNullable()
        .WithColumn(nameof(WebhookLog.Success)).AsBoolean().NotNullable()
        .WithColumn(nameof(WebhookLog.CreatedOnUtc)).AsDateTime().NotNullable();
    }
  }
}
