using Api2Cart.Connector.Domain;
using FluentMigrator;
using Nop.Data.Extensions;
using Nop.Data.Migrations;

namespace Api2Cart.Connector.Migrations
{
  [NopMigration(
    "2026/05/20 00:00:00",
    "Api2Cart.Connector — native webhook tables",
    MigrationProcessType.NoMatter
  )]
  public class AddWebhookTables : AutoReversingMigration
  {
    public override void Up()
    {
      Create.TableFor<Webhook>();
      Create.TableFor<WebhookQueueItem>();
      Create.TableFor<WebhookLog>();

      Create.Index("IX_Webhook_Entity_Action_Status").OnTable(nameof(Webhook))
        .OnColumn(nameof(Webhook.Entity)).Ascending()
        .OnColumn(nameof(Webhook.Action)).Ascending()
        .OnColumn(nameof(Webhook.Status)).Ascending();

      Create.Index("IX_WebhookQueue_NextAttempt").OnTable(nameof(WebhookQueueItem))
        .OnColumn(nameof(WebhookQueueItem.NextAttemptUtc)).Ascending();

      Create.Index("IX_WebhookLog_CreatedOn").OnTable(nameof(WebhookLog))
        .OnColumn(nameof(WebhookLog.CreatedOnUtc)).Ascending();
    }
  }
}
