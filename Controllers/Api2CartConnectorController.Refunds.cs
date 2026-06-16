using System.Text.Json;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Refund Create

    [HttpPost("refund-create")]
    public async Task<IActionResult> RefundCreate(
        [FromQuery] int order_id,
        [FromQuery] int store_id = 0,
        [FromQuery] bool is_online = false,
        [FromQuery] decimal? amount = null,
        [FromQuery] string? items = null,
        [FromQuery] decimal shipping_amount = 0,
        [FromQuery] decimal adjustment_positive = 0,
        [FromQuery] decimal adjustment_negative = 0,
        [FromQuery] bool notify = false,
        [FromQuery] string? message = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var orderRepo = EngineContext.Current.Resolve<IRepository<Order>>();
        var order = orderRepo.Table.FirstOrDefault(o => o.Id == order_id);

        if (order == null) {
          return NotFoundError($"Order with id = {order_id} not found.");
        }

        if (store_id > 0 && order.StoreId != store_id) {
          return NotFoundError($"Order with id = {order_id} not found in store {store_id}.");
        }

        var itemsList = new List<RefundItemPayload>();

        if (!string.IsNullOrEmpty(items)) {
          try {
            itemsList = JsonSerializer.Deserialize<List<RefundItemPayload>>(items) ?? new();
          } catch (JsonException ex) {
            return ParamError($"Parameter 'items' is not valid JSON: {ex.Message}");
          }
        }

        if (itemsList.Count == 0 && !amount.HasValue) {
          return ParamError("Parameter 'items' or 'amount' is required.");
        }

        var orderItemRepo = EngineContext.Current.Resolve<IRepository<OrderItem>>();
        var orderItems = orderItemRepo.Table.Where(oi => oi.OrderId == order_id).ToList();
        var returnRequestRepo = EngineContext.Current.Resolve<IRepository<ReturnRequest>>();
        var orderItemIds = orderItems.Select(oi => oi.Id).ToList();
        var refundedByOrderItem = returnRequestRepo.Table
          .Where(rr => orderItemIds.Contains(rr.OrderItemId))
          .GroupBy(rr => rr.OrderItemId)
          .Select(g => new { OrderItemId = g.Key, Total = g.Sum(rr => rr.Quantity) })
          .ToDictionary(x => x.OrderItemId, x => x.Total);

        var requestedByOrderItem = new Dictionary<int, int>();

        if (!amount.HasValue) {
          foreach (var item in itemsList) {
            var orderItem = orderItems.FirstOrDefault(oi => oi.Id == item.order_product_id);

            if (orderItem == null) {
              return ParamError($"Order product {item.order_product_id} does not belong to order {order_id}.");
            }

            if (item.quantity <= 0) {
              return ParamError($"Refund quantity must be > 0 (order_product_id={item.order_product_id}).");
            }

            var alreadyRefundedQty = refundedByOrderItem.TryGetValue(orderItem.Id, out var ref0) ? ref0 : 0;
            var pendingForThisRequest = requestedByOrderItem.TryGetValue(orderItem.Id, out var pend) ? pend : 0;
            var availableQty = orderItem.Quantity - alreadyRefundedQty - pendingForThisRequest;

            if (availableQty <= 0) {
              return StoreError(
                $"Order product {item.order_product_id} has no items left to refund "
                  + $"({orderItem.Quantity} ordered, {alreadyRefundedQty} already refunded)."
              );
            }

            if (item.quantity > availableQty) {
              return StoreError(
                $"Refund qty ({item.quantity}) exceeds available to refund ({availableQty}) "
                  + $"for order product {item.order_product_id}."
              );
            }

            requestedByOrderItem[orderItem.Id] = pendingForThisRequest + item.quantity;
          }
        }

        decimal refundAmount;

        if (amount.HasValue) {
          refundAmount = amount.Value;
        } else {
          refundAmount = 0m;

          foreach (var item in itemsList) {
            var orderItem = orderItems.First(oi => oi.Id == item.order_product_id);
            var perUnit = orderItem.Quantity > 0
              ? orderItem.PriceInclTax / orderItem.Quantity
              : orderItem.PriceInclTax;
            refundAmount += perUnit * item.quantity;
          }

          refundAmount += shipping_amount + adjustment_positive - adjustment_negative;
        }

        if (refundAmount <= 0) {
          return ParamError("Refund amount must be > 0.");
        }

        var alreadyRefunded = order.RefundedAmount;
        var availableToRefund = order.OrderTotal - alreadyRefunded;

        if (refundAmount > availableToRefund) {
          return StoreError($"Refund amount ({refundAmount:F2}) exceeds available to refund ({availableToRefund:F2}).");
        }

        var orderProcessingService = EngineContext.Current.Resolve<IOrderProcessingService>();
        var paymentService = EngineContext.Current.Resolve<IPaymentService>();

        if (is_online) {
          if (string.IsNullOrEmpty(order.PaymentMethodSystemName)) {
            return StoreError("Order payment method is not set; online refund is not possible.");
          }

          var isPartial = refundAmount < availableToRefund;
          var canRefund = isPartial
            ? await paymentService.SupportPartiallyRefundAsync(order.PaymentMethodSystemName)
            : await paymentService.SupportRefundAsync(order.PaymentMethodSystemName);

          if (!canRefund) {
            return StoreError("Order payment method does not support online refunds.");
          }

          var warnings = isPartial
            ? await orderProcessingService.PartiallyRefundAsync(order, refundAmount)
            : await orderProcessingService.RefundAsync(order);

          if (warnings != null && warnings.Count > 0) {
            return StoreError(string.Join("; ", warnings));
          }
        } else {
          var isPartial = refundAmount < availableToRefund;
          var canRefundOffline = isPartial
            ? orderProcessingService.CanPartiallyRefundOffline(order, refundAmount)
            : orderProcessingService.CanRefundOffline(order);

          if (!canRefundOffline) {
            return StoreError(
              $"Order cannot be refunded offline (payment status = {(Nop.Core.Domain.Payments.PaymentStatus)order.PaymentStatusId}). "
                + "Offline refund requires payment status Paid or PartiallyRefunded."
            );
          }

          if (isPartial) {
            await orderProcessingService.PartiallyRefundOfflineAsync(order, refundAmount);
          } else {
            await orderProcessingService.RefundOfflineAsync(order);
          }
        }

        if (notify) {
          var workflowMessageService = EngineContext.Current.Resolve<IWorkflowMessageService>();
          var langService = EngineContext.Current.Resolve<ILanguageService>();
          var customerLang = order.CustomerLanguageId > 0
            ? order.CustomerLanguageId
            : ((await langService.GetAllLanguagesAsync()).FirstOrDefault()?.Id ?? 1);
          await workflowMessageService.SendOrderRefundedCustomerNotificationAsync(order, refundAmount, customerLang);
        }

        var orderNoteService = EngineContext.Current.Resolve<IOrderService>();
        var refundNote = new OrderNote {
          OrderId           = order_id,
          Note              = $"[api2cart-refund] Amount = {refundAmount:F2}"
            + (!string.IsNullOrEmpty(message) ? $". {message}" : string.Empty),
          DisplayToCustomer = !string.IsNullOrEmpty(message),
          CreatedOnUtc      = DateTime.UtcNow,
        };
        await orderNoteService.InsertOrderNoteAsync(refundNote);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              refund_id        = refundNote.Id.ToString(),
              amount_refunded  = refundAmount,
            },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private class RefundItemPayload
    {
      public int order_product_id { get; set; }
      public int quantity { get; set; }
    }

    #endregion
  }
}
