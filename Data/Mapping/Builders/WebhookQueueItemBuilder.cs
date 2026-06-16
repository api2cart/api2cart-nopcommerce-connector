using Api2Cart.Connector.Domain;
using FluentMigrator.Builders.Create.Table;
using Nop.Data.Mapping.Builders;

namespace Api2Cart.Connector.Data.Mapping.Builders
{
  public class WebhookQueueItemBuilder : NopEntityBuilder<WebhookQueueItem>
  {
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
      table
        .WithColumn(nameof(WebhookQueueItem.WebhookId)).AsInt32().NotNullable()
        .WithColumn(nameof(WebhookQueueItem.Entity)).AsString(64).NotNullable()
        .WithColumn(nameof(WebhookQueueItem.Action)).AsString(16).NotNullable()
        .WithColumn(nameof(WebhookQueueItem.EntityIds)).AsString(4000).NotNullable()
        .WithColumn(nameof(WebhookQueueItem.StoreId)).AsInt32().NotNullable()
        .WithColumn(nameof(WebhookQueueItem.Attempts)).AsByte().NotNullable().WithDefaultValue(0)
        .WithColumn(nameof(WebhookQueueItem.NextAttemptUtc)).AsDateTime().NotNullable()
        .WithColumn(nameof(WebhookQueueItem.CreatedOnUtc)).AsDateTime().NotNullable();
    }
  }
}
