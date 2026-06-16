using Api2Cart.Connector.Services;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Events;
using Nop.Services.Events;

namespace Api2Cart.Connector.Infrastructure
{
  /// <summary>
  /// Subscribes to NopCommerce entity events and enqueues webhook deliveries for 8 entity
  /// types (23 IConsumers): order, order.shipment, subscriber, productReview, product,
  /// category, customer, product.child_item (ProductAttributeCombination).
  ///
  /// Entity/action strings match Api2cart_Webhook constants verbatim:
  ///   library/Api2cart/Webhook.php:39-41 (ACTION_*), :57-66 (ENTITY_*).
  ///
  /// Soft-delete guard: NopCommerce flips Deleted=true via an Update event, not a Delete event,
  /// for all ISoftDeletedEntity types (Product, Category, Customer, Order). The Update handler
  /// remaps to *_delete to avoid emitting a phantom *_update before *_delete.
  ///
  /// Customer noisy-event filter: customer.update normally fires on every login (LastLoginDateUtc
  /// touch), every admin pageview (LastActivityDateUtc), every cart-related action. The
  /// CustomerChangeDetector compares a fingerprint of material fields and drops events with no
  /// real change. See ICustomerChangeDetector.
  /// </summary>
  public class WebhookEventConsumer :
    IConsumer<EntityInsertedEvent<Order>>,
    IConsumer<EntityUpdatedEvent<Order>>,
    IConsumer<EntityDeletedEvent<Order>>,
    IConsumer<EntityInsertedEvent<Shipment>>,
    IConsumer<EntityUpdatedEvent<Shipment>>,
    IConsumer<EntityDeletedEvent<Shipment>>,
    IConsumer<EntityInsertedEvent<NewsLetterSubscription>>,
    IConsumer<EntityUpdatedEvent<NewsLetterSubscription>>,
    IConsumer<EntityInsertedEvent<ProductReview>>,
    IConsumer<EntityUpdatedEvent<ProductReview>>,
    IConsumer<EntityDeletedEvent<ProductReview>>,
    IConsumer<EntityInsertedEvent<Product>>,
    IConsumer<EntityUpdatedEvent<Product>>,
    IConsumer<EntityDeletedEvent<Product>>,
    IConsumer<EntityInsertedEvent<Category>>,
    IConsumer<EntityUpdatedEvent<Category>>,
    IConsumer<EntityDeletedEvent<Category>>,
    IConsumer<EntityInsertedEvent<Customer>>,
    IConsumer<EntityUpdatedEvent<Customer>>,
    IConsumer<EntityDeletedEvent<Customer>>,
    IConsumer<EntityInsertedEvent<ProductAttributeCombination>>,
    IConsumer<EntityUpdatedEvent<ProductAttributeCombination>>,
    IConsumer<EntityDeletedEvent<ProductAttributeCombination>>
  {
    private readonly IEnqueueService _enqueueService;
    private readonly ICustomerChangeDetector _customerChangeDetector;

    public WebhookEventConsumer(
        IEnqueueService enqueueService,
        ICustomerChangeDetector customerChangeDetector
    )
    {
      _enqueueService = enqueueService;
      _customerChangeDetector = customerChangeDetector;
    }

    // ----- Order -----

    public Task HandleEventAsync(EntityInsertedEvent<Order> evt)
    {
      return _enqueueService.EnqueueAsync("order", "add", evt.Entity.Id, evt.Entity.StoreId);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<Order> evt)
    {
      if (evt.Entity.Deleted) {
        return _enqueueService.EnqueueAsync("order", "delete", evt.Entity.Id, evt.Entity.StoreId);
      }

      return _enqueueService.EnqueueAsync("order", "update", evt.Entity.Id, evt.Entity.StoreId);
    }

    public Task HandleEventAsync(EntityDeletedEvent<Order> evt)
    {
      return _enqueueService.EnqueueAsync("order", "delete", evt.Entity.Id, evt.Entity.StoreId);
    }

    // ----- Shipment (no StoreId column on Shipment; pass 0 = all stores) -----

    public Task HandleEventAsync(EntityInsertedEvent<Shipment> evt)
    {
      return _enqueueService.EnqueueAsync("order.shipment", "add", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<Shipment> evt)
    {
      return _enqueueService.EnqueueAsync("order.shipment", "update", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityDeletedEvent<Shipment> evt)
    {
      return _enqueueService.EnqueueAsync("order.shipment", "delete", evt.Entity.Id, 0);
    }

    // ----- NewsLetterSubscription (subscriber) -----

    public Task HandleEventAsync(EntityInsertedEvent<NewsLetterSubscription> evt)
    {
      return _enqueueService.EnqueueAsync("subscriber", "add", evt.Entity.Id, evt.Entity.StoreId);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<NewsLetterSubscription> evt)
    {
      return _enqueueService.EnqueueAsync("subscriber", "update", evt.Entity.Id, evt.Entity.StoreId);
    }

    // ----- ProductReview -----

    public Task HandleEventAsync(EntityInsertedEvent<ProductReview> evt)
    {
      return _enqueueService.EnqueueAsync("productReview", "add", evt.Entity.Id, evt.Entity.StoreId);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<ProductReview> evt)
    {
      return _enqueueService.EnqueueAsync("productReview", "update", evt.Entity.Id, evt.Entity.StoreId);
    }

    public Task HandleEventAsync(EntityDeletedEvent<ProductReview> evt)
    {
      return _enqueueService.EnqueueAsync("productReview", "delete", evt.Entity.Id, evt.Entity.StoreId);
    }

    // ----- Product (IStoreMappingSupported; pass 0 = all stores) -----

    public Task HandleEventAsync(EntityInsertedEvent<Product> evt)
    {
      return _enqueueService.EnqueueAsync("product", "add", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<Product> evt)
    {
      if (evt.Entity.Deleted) {
        return _enqueueService.EnqueueAsync("product", "delete", evt.Entity.Id, 0);
      }

      return _enqueueService.EnqueueAsync("product", "update", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityDeletedEvent<Product> evt)
    {
      return _enqueueService.EnqueueAsync("product", "delete", evt.Entity.Id, 0);
    }

    // ----- Category (IStoreMappingSupported; pass 0 = all stores) -----

    public Task HandleEventAsync(EntityInsertedEvent<Category> evt)
    {
      return _enqueueService.EnqueueAsync("category", "add", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<Category> evt)
    {
      if (evt.Entity.Deleted) {
        return _enqueueService.EnqueueAsync("category", "delete", evt.Entity.Id, 0);
      }

      return _enqueueService.EnqueueAsync("category", "update", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityDeletedEvent<Category> evt)
    {
      return _enqueueService.EnqueueAsync("category", "delete", evt.Entity.Id, 0);
    }

    // ----- Customer (RegisteredInStoreId, soft-delete + noisy-event filter) -----

    public Task HandleEventAsync(EntityInsertedEvent<Customer> evt)
    {
      return _enqueueService.EnqueueAsync("customer", "add", evt.Entity.Id, evt.Entity.RegisteredInStoreId);
    }

    public async Task HandleEventAsync(EntityUpdatedEvent<Customer> evt)
    {
      if (evt.Entity.Deleted) {
        await _enqueueService.EnqueueAsync("customer", "delete", evt.Entity.Id, evt.Entity.RegisteredInStoreId);
        return;
      }

      // Drop noisy login/activity-only events (LastLoginDateUtc, LastActivityDateUtc,
      // LastIpAddress, UpdatedOnUtc). Bridge-side fingerprint compare; ~100x reduction on
      // active storefronts.
      if (!await _customerChangeDetector.IsMaterialChangeAsync(evt.Entity)) {
        return;
      }

      await _enqueueService.EnqueueAsync("customer", "update", evt.Entity.Id, evt.Entity.RegisteredInStoreId);
    }

    public Task HandleEventAsync(EntityDeletedEvent<Customer> evt)
    {
      return _enqueueService.EnqueueAsync("customer", "delete", evt.Entity.Id, evt.Entity.RegisteredInStoreId);
    }

    // ----- ProductAttributeCombination (product.child_item) -----
    // No StoreId, no Deleted flag — combinations are per-product, hard delete only.

    public Task HandleEventAsync(EntityInsertedEvent<ProductAttributeCombination> evt)
    {
      return _enqueueService.EnqueueAsync("product.child_item", "add", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityUpdatedEvent<ProductAttributeCombination> evt)
    {
      return _enqueueService.EnqueueAsync("product.child_item", "update", evt.Entity.Id, 0);
    }

    public Task HandleEventAsync(EntityDeletedEvent<ProductAttributeCombination> evt)
    {
      return _enqueueService.EnqueueAsync("product.child_item", "delete", evt.Entity.Id, 0);
    }
  }
}
