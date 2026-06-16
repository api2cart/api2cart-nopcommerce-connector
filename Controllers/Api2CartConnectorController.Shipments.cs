using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Seo;
using Nop.Services.Shipping;
using Nop.Services.Stores;
using Nop.Services.Vendors;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Shipments

    [HttpPost("shipments-list")]
    public async Task<IActionResult> ShipmentsList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] int? order_id = null,
        [FromQuery] string? ids = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? fields = null,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var requestedFields = ParseFields(fields);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var shipmentRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Shipping.Shipment>>();
        var orderRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.Order>>();
        var parsedIds = ParseIntIds(ids);
        var idSet = parsedIds != null && parsedIds.Length > 0
          ? new HashSet<int>(parsedIds)
          : null;

        var createdFromUtc = ParseDateFilter(created_from);
        var createdToUtc = ParseDateFilter(created_to, isUpperBound: true);

        var query = shipmentRepo.Table;

        if (order_id.HasValue) {
          query = query.Where(s => s.OrderId == order_id.Value);
        }

        if (store_id > 0) {
          var orderIdsInStore = orderRepo.Table
            .Where(o => o.StoreId == store_id)
            .Select(o => o.Id);

          query = query.Where(s => orderIdsInStore.Contains(s.OrderId));
        }

        if (idSet != null) {
          query = query.Where(s => idSet.Contains(s.Id));
        }

        if (createdFromUtc.HasValue) {
          query = query.Where(s => s.CreatedOnUtc >= createdFromUtc.Value);
        }

        if (createdToUtc.HasValue) {
          query = query.Where(s => s.CreatedOnUtc <= createdToUtc.Value);
        }

        query = query.OrderByDescending(s => s.CreatedOnUtc);

        var totalCount = query.Count();
        var shipments = query
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        var includeItems = requestedFields == null || IsFieldRequested(requestedFields, "shipment_items");
        var result = new List<Dictionary<string, object?>>();

        foreach (var shipment in shipments) {
          result.Add(await BuildShipmentDataAsync(shipment, includeItems));
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { shipments = result, total_count = totalCount },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private sealed class ShipmentItemInput
    {
      public int order_item_id { get; set; }
      public decimal quantity { get; set; }
    }

    [HttpPost("shipment-add")]
    public async Task<IActionResult> ShipmentAdd(
        [FromQuery] int order_id = 0,
        [FromQuery] int store_id = 0,
        [FromQuery] string? items = null,
        [FromQuery] int? warehouse_id = null,
        [FromQuery] string? tracking_number = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] bool is_shipped = true,
        [FromQuery] bool send_notifications = false
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(tracking_number, admin_comment);

        if (charsError != null) {
          return charsError;
        }

        if (order_id <= 0) {
          return ParamError("Parameter 'order_id' is required.");
        }

        List<ShipmentItemInput>? requestedItems = null;

        if (!string.IsNullOrEmpty(items)) {
          try {
            requestedItems = JsonSerializer.Deserialize<List<ShipmentItemInput>>(items, _jsonOptions);
          } catch (JsonException ex) {
            return ParamError($"Parameter 'items' is not valid JSON: {ex.Message}");
          }
        }

        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var shipmentService = EngineContext.Current.Resolve<IShipmentService>();
        var orderProcessingService = EngineContext.Current.Resolve<IOrderProcessingService>();
        var productService = EngineContext.Current.Resolve<IProductService>();
        var pwiRepo = EngineContext.Current.Resolve<IRepository<ProductWarehouseInventory>>();

        var order = await orderService.GetOrderByIdAsync(order_id);

        if (order == null || order.Deleted) {
          return NotFoundError($"Order with id {order_id} not found.");
        }

        if (store_id > 0 && order.StoreId != store_id) {
          return NotFoundError($"Order with id {order_id} not found in store {store_id}.");
        }

        if (order.PickupInStore) {
          return StoreError("Order is pickup-in-store; shipments are not applicable.");
        }

        if (requestedItems != null) {
          foreach (var input in requestedItems) {
            if (input.order_item_id <= 0) {
              return ParamError("Item 'order_item_id' must be positive.");
            }

            if (input.quantity <= 0) {
              return ParamError($"Item 'quantity' must be > 0 (order_item_id={input.order_item_id}).");
            }

            if (Math.Abs(input.quantity - Math.Truncate(input.quantity)) > 0.0001m) {
              return StoreError(
                $"Item 'quantity' must be an integer (order_item_id={input.order_item_id}); fractional quantities are not supported by NopCommerce."
              );
            }
          }
        }

        var shippableOrderItems = (await orderService.GetOrderItemsAsync(order.Id, isShipEnabled: true)).ToList();

        var resolved = new List<(Nop.Core.Domain.Orders.OrderItem orderItem, int qty)>();

        if (requestedItems == null || requestedItems.Count == 0) {
          foreach (var orderItem in shippableOrderItems) {
            var maxQty = await orderService.GetTotalNumberOfItemsCanBeAddedToShipmentAsync(orderItem);

            if (maxQty <= 0) {
              continue;
            }

            resolved.Add((orderItem, maxQty));
          }
        } else {
          foreach (var input in requestedItems) {
            var orderItem = shippableOrderItems.FirstOrDefault(oi => oi.Id == input.order_item_id);

            if (orderItem == null) {
              return NotFoundError($"Order item {input.order_item_id} not found in order {order.Id} or is not shippable.");
            }

            var maxQty = await orderService.GetTotalNumberOfItemsCanBeAddedToShipmentAsync(orderItem);

            if (maxQty <= 0) {
              return ExistsError("One or more items have been already shipped.");
            }

            var qty = (int)input.quantity;

            if (qty > maxQty) {
              return StoreError($"The specified quantity ({qty}) is larger than is available ({maxQty}) for order item {orderItem.Id}.");
            }

            resolved.Add((orderItem, qty));
          }
        }

        if (resolved.Count == 0) {
          return ExistsError("All items have been already shipped.");
        }

        var productByOrderItem = new Dictionary<int, Nop.Core.Domain.Catalog.Product>();
        var associatedByOrderItem = new Dictionary<int, List<int>>();
        decimal totalWeight = 0m;

        foreach (var (orderItem, qty) in resolved) {
          totalWeight += (orderItem.ItemWeight ?? 0m) * qty;

          var product = await productService.GetProductByIdAsync(orderItem.ProductId);

          if (product == null) {
            associatedByOrderItem[orderItem.Id] = new List<int>();
            continue;
          }

          productByOrderItem[orderItem.Id] = product;

          List<int> associated;

          if (product.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock
              && product.UseMultipleWarehouses) {
            associated = pwiRepo.Table
              .Where(p => p.ProductId == product.Id)
              .Select(p => p.WarehouseId)
              .ToList();
          } else if (product.WarehouseId > 0) {
            associated = new List<int> { product.WarehouseId };
          } else {
            associated = new List<int>();
          }

          associatedByOrderItem[orderItem.Id] = associated;
        }

        int? sharedWarehouse = warehouse_id.HasValue && warehouse_id.Value > 0
          ? warehouse_id
          : null;

        if (!sharedWarehouse.HasValue) {
          var constrainedAssociations = associatedByOrderItem.Values.Where(a => a.Count > 0).ToList();

          if (constrainedAssociations.Count > 0) {
            IEnumerable<int> intersection = constrainedAssociations[0];

            for (var i = 1; i < constrainedAssociations.Count; i++) {
              intersection = intersection.Intersect(constrainedAssociations[i]);
            }

            var commonWarehouses = intersection.ToList();

            if (commonWarehouses.Count == 0) {
              return NotFoundError("Products associated with different warehouses can't be shipped in the same shipment.");
            }

            sharedWarehouse = commonWarehouses.Min();
          }
        }

        var perItemWarehouse = new List<int>(resolved.Count);

        foreach (var (orderItem, _) in resolved) {
          var associated = associatedByOrderItem[orderItem.Id];

          if (sharedWarehouse.HasValue && associated.Count > 0
              && !associated.Contains(sharedWarehouse.Value)) {
            return NotFoundError(
              $"order_item_id={orderItem.Id} is not associated with warehouse_id={sharedWarehouse.Value}."
            );
          }

          int warehouseId;

          if (sharedWarehouse.HasValue) {
            warehouseId = sharedWarehouse.Value;
          } else if (productByOrderItem.TryGetValue(orderItem.Id, out var product)) {
            warehouseId = product.WarehouseId;
          } else {
            warehouseId = 0;
          }

          perItemWarehouse.Add(warehouseId);
        }

        var normalizedTracking = string.IsNullOrEmpty(tracking_number) ? null : tracking_number;
        var normalizedAdminComment = string.IsNullOrEmpty(admin_comment) ? null : admin_comment;

        var shipment = new Nop.Core.Domain.Shipping.Shipment {
          OrderId = order.Id,
          TrackingNumber = normalizedTracking,
          AdminComment = normalizedAdminComment,
          TotalWeight = totalWeight > 0m ? (decimal?)totalWeight : null,
          CreatedOnUtc = DateTime.UtcNow,
        };

        await shipmentService.InsertShipmentAsync(shipment);

        for (var i = 0; i < resolved.Count; i++) {
          var (orderItem, qty) = resolved[i];
          var si = new Nop.Core.Domain.Shipping.ShipmentItem {
            ShipmentId = shipment.Id,
            OrderItemId = orderItem.Id,
            Quantity = qty,
            WarehouseId = perItemWarehouse[i],
          };

          await shipmentService.InsertShipmentItemAsync(si);
        }

        if (is_shipped) {
          await orderProcessingService.ShipAsync(shipment, send_notifications);
          order = await orderService.GetOrderByIdAsync(order.Id) ?? order;
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              id = shipment.Id,
              shipping_status_id = order.ShippingStatusId,
              order_status_id = order.OrderStatusId,
              shipped_date_utc = shipment.ShippedDateUtc?.ToString("o"),
            },
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("shipment-update")]
    public async Task<IActionResult> ShipmentUpdate(
        [FromQuery] int shipment_id = 0,
        [FromQuery] int order_id = 0,
        [FromQuery] int store_id = 0,
        [FromQuery] string? tracking_number = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] bool replace = true,
        [FromQuery] bool? is_shipped = null,
        [FromQuery] string? delivered_at = null,
        [FromQuery] bool send_notifications = false
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(tracking_number, admin_comment);

        if (charsError != null) {
          return charsError;
        }

        if (shipment_id <= 0) {
          return ParamError("Parameter 'shipment_id' is required and must be positive.");
        }

        if (order_id <= 0) {
          return ParamError("Parameter 'order_id' is required and must be positive.");
        }

        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var shipmentService = EngineContext.Current.Resolve<IShipmentService>();
        var orderProcessingService = EngineContext.Current.Resolve<IOrderProcessingService>();

        var shipment = await shipmentService.GetShipmentByIdAsync(shipment_id);

        if (shipment == null) {
          return NotFoundError($"Shipment with id {shipment_id} not found.");
        }

        if (shipment.OrderId != order_id) {
          return NotFoundError($"Shipment {shipment_id} does not belong to order {order_id}.");
        }

        var order = await orderService.GetOrderByIdAsync(shipment.OrderId);

        if (order == null || order.Deleted) {
          return NotFoundError($"Order with id {shipment.OrderId} not found.");
        }

        if (store_id > 0 && order.StoreId != store_id) {
          return NotFoundError($"Shipment {shipment_id} does not belong to store {store_id}.");
        }

        bool changed = false;
        bool shipShipped = false;

        var newTracking = string.IsNullOrEmpty(tracking_number) ? null : tracking_number;

        if (newTracking != null) {
          if (replace) {
            if (shipment.TrackingNumber != newTracking) {
              shipment.TrackingNumber = newTracking;
              changed = true;
            }
          } else {
            if (string.IsNullOrEmpty(shipment.TrackingNumber) || shipment.TrackingNumber != newTracking) {
              shipment.TrackingNumber = newTracking;
              changed = true;
            }
          }
        }

        if (admin_comment != null && shipment.AdminComment != admin_comment) {
          shipment.AdminComment = string.IsNullOrEmpty(admin_comment) ? null : admin_comment;
          changed = true;
        }

        if (delivered_at != null) {
          if (delivered_at.Length == 0) {
            if (shipment.DeliveryDateUtc.HasValue) {
              shipment.DeliveryDateUtc = null;
              changed = true;
            }
          } else {
            var deliveredUtc = ParseDateFilter(delivered_at);

            if (!deliveredUtc.HasValue) {
              return ParamError($"Parameter 'delivered_at' has invalid value '{delivered_at}'; expected ISO-8601.");
            }

            if (!shipment.ShippedDateUtc.HasValue && (!is_shipped.HasValue || !is_shipped.Value)) {
              return StoreError("Cannot mark shipment as delivered before it is shipped.");
            }

            if (shipment.DeliveryDateUtc != deliveredUtc.Value) {
              shipment.DeliveryDateUtc = deliveredUtc.Value;
              changed = true;
            }
          }
        }

        bool wasShippedBeforeUpdate = shipment.ShippedDateUtc.HasValue;

        if (is_shipped.HasValue) {
          if (is_shipped.Value && !shipment.ShippedDateUtc.HasValue) {
            if (changed) {
              await shipmentService.UpdateShipmentAsync(shipment);
              changed = false;
            }

            await orderProcessingService.ShipAsync(shipment, send_notifications);
            shipShipped = true;
          } else if (!is_shipped.Value && shipment.ShippedDateUtc.HasValue) {
            shipment.ShippedDateUtc = null;
            changed = true;
          }
        }

        if (changed) {
          await shipmentService.UpdateShipmentAsync(shipment);
        }

        if (is_shipped.HasValue && !is_shipped.Value && wasShippedBeforeUpdate) {
          var freshOrder = await orderService.GetOrderByIdAsync(shipment.OrderId);

          if (freshOrder != null) {
            order = freshOrder;
            await orderProcessingService.CheckOrderStatusAsync(order);
          }
        }

        var postOrder = await orderService.GetOrderByIdAsync(shipment.OrderId) ?? order;
        var updatedItems = (changed || shipShipped) ? 1 : 0;

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              updated_items = updatedItems,
              shipping_status_id = postOrder.ShippingStatusId,
            },
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("shipment-delete")]
    public async Task<IActionResult> ShipmentDelete(
        [FromQuery] int shipment_id = 0,
        [FromQuery] int order_id = 0,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (shipment_id <= 0) {
          return ParamError("Parameter 'shipment_id' is required and must be positive.");
        }

        if (order_id <= 0) {
          return ParamError("Parameter 'order_id' is required and must be positive.");
        }

        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var shipmentService = EngineContext.Current.Resolve<IShipmentService>();

        var shipment = await shipmentService.GetShipmentByIdAsync(shipment_id);

        if (shipment == null) {
          return NotFoundError($"Shipment with id {shipment_id} not found.");
        }

        if (shipment.OrderId != order_id) {
          return NotFoundError($"Shipment {shipment_id} does not belong to order {order_id}.");
        }

        if (store_id > 0) {
          var order = await orderService.GetOrderByIdAsync(shipment.OrderId);

          if (order == null || order.StoreId != store_id) {
            return NotFoundError($"Shipment {shipment_id} does not belong to store {store_id}.");
          }
        }

        await shipmentService.DeleteShipmentAsync(shipment);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { deleted = 1 },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Shipment Data Builder

    private static async Task<Dictionary<string, object?>> BuildShipmentDataAsync(
        Nop.Core.Domain.Shipping.Shipment shipment,
        bool includeItems
    )
    {
      var data = new Dictionary<string, object?>
      {
        ["id"] = shipment.Id,
        ["order_id"] = shipment.OrderId,
        ["tracking_number"] = shipment.TrackingNumber,
        ["total_weight"] = shipment.TotalWeight,
        ["admin_comment"] = shipment.AdminComment,
        ["shipped_date_utc"] = shipment.ShippedDateUtc?.ToString("o"),
        ["delivery_date_utc"] = shipment.DeliveryDateUtc?.ToString("o"),
        ["created_on_utc"] = shipment.CreatedOnUtc.ToString("o"),
      };

      if (includeItems) {
        data["shipment_items"] = await BuildShipmentItemsAsync(shipment.Id);
      }

      return data;
    }

    #endregion

    #region Shipment Items Builder

    private static async Task<List<Dictionary<string, object?>>> BuildShipmentItemsAsync(
        int shipmentId
    )
    {
      var shipmentItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Shipping.ShipmentItem>>();
      var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();

      var shipmentItems = shipmentItemRepo.Table
        .Where(si => si.ShipmentId == shipmentId)
        .ToList();

      if (!shipmentItems.Any()) {
        return new List<Dictionary<string, object?>>();
      }

      var orderItemIds = shipmentItems.Select(si => si.OrderItemId).Distinct().ToArray();
      var orderItems = orderItemRepo.Table
        .Where(oi => orderItemIds.Contains(oi.Id))
        .ToList();
      var orderItemMap = orderItems.ToDictionary(oi => oi.Id);

      var productIds = orderItems.Select(oi => oi.ProductId).Distinct().ToArray();
      var productService = EngineContext.Current.Resolve<IProductService>();
      var products = await productService.GetProductsByIdsAsync(productIds);
      var productMap = products.ToDictionary(p => p.Id);

      var result = new List<Dictionary<string, object?>>();

      foreach (var item in shipmentItems) {
        orderItemMap.TryGetValue(item.OrderItemId, out var orderItem);
        var product = orderItem != null && productMap.TryGetValue(orderItem.ProductId, out var p) ? p : null;

        result.Add(
          new Dictionary<string, object?> {
            ["id"] = item.Id,
            ["order_item_id"] = item.OrderItemId,
            ["warehouse_id"] = item.WarehouseId,
            ["quantity"] = item.Quantity,
            ["product_id"] = orderItem?.ProductId,
            ["product_name"] = product?.Name,
            ["sku"] = product?.Sku,
            ["unit_price_incl_tax"] = orderItem?.UnitPriceInclTax,
          }
        );
      }

      return result;
    }

    #endregion
  }
}
