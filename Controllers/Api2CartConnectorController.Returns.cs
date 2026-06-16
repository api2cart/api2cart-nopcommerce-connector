using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Infrastructure;
using Nop.Data;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Returns List

    [HttpPost("returns-list")]
    public async Task<IActionResult> ReturnsList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? ids = null,
        [FromQuery] int? order_id = null,
        [FromQuery] string? order_ids = null,
        [FromQuery] int? customer_id = null,
        [FromQuery] int? status_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? fields = null
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

        var returnRequestRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequest>>();
        var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
        var orderRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.Order>>();

        HashSet<string>? idStringSet = null;
        HashSet<int>? idIntSet = null;

        if (!string.IsNullOrEmpty(ids)) {
          var tokens = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
          idStringSet = new HashSet<string>(tokens);
          idIntSet = new HashSet<int>(
            tokens
              .Select(t => int.TryParse(t, out var n) ? (int?)n : null)
              .Where(n => n.HasValue)
              .Select(n => n!.Value)
          );

          if (idStringSet.Count == 0) {
            return JsonContent(
              new ConnectorResponse<object> {
                Result = new { return_requests = Array.Empty<object>(), total_count = 0 },
              }
            );
          }
        }

        var hasOrderIdFilter = !string.IsNullOrEmpty(order_ids);
        var parsedOrderIds = ParseIntIds(order_ids);

        if (hasOrderIdFilter && (parsedOrderIds == null || parsedOrderIds.Length == 0)) {
          return JsonContent(
            new ConnectorResponse<object> {
              Result = new { return_requests = Array.Empty<object>(), total_count = 0 },
            }
          );
        }

        var createdFromUtc = ParseDateFilter(created_from);
        var createdToUtc = ParseDateFilter(created_to, isUpperBound: true);

        var query = returnRequestRepo.Table;

        if (idStringSet != null) {
          query = query.Where(rr =>
            (rr.CustomNumber != null && idStringSet.Contains(rr.CustomNumber))
            || (idIntSet != null && idIntSet.Contains(rr.Id))
          );
        }

        if (customer_id.HasValue) {
          query = query.Where(rr => rr.CustomerId == customer_id.Value);
        }

        if (status_id.HasValue) {
          query = query.Where(rr => rr.ReturnRequestStatusId == status_id.Value);
        }

        if (createdFromUtc.HasValue) {
          query = query.Where(rr => rr.CreatedOnUtc >= createdFromUtc.Value);
        }

        if (createdToUtc.HasValue) {
          query = query.Where(rr => rr.CreatedOnUtc <= createdToUtc.Value);
        }

        // Join to OrderItem to resolve OrderId for order_id and store_id filters
        var joinedQuery = query
          .Join(
            orderItemRepo.Table,
            rr => rr.OrderItemId,
            oi => oi.Id,
            (rr, oi) => new { ReturnRequest = rr, OrderItem = oi }
          );

        if (order_id.HasValue) {
          joinedQuery = joinedQuery.Where(x => x.OrderItem.OrderId == order_id.Value);
        }

        if (parsedOrderIds != null && parsedOrderIds.Length > 0) {
          var orderIdSet = new HashSet<int>(parsedOrderIds);
          joinedQuery = joinedQuery.Where(x => orderIdSet.Contains(x.OrderItem.OrderId));
        }

        if (store_id > 0) {
          var orderIdsInStore = orderRepo.Table
            .Where(o => o.StoreId == store_id)
            .Select(o => o.Id);

          joinedQuery = joinedQuery.Where(x => orderIdsInStore.Contains(x.OrderItem.OrderId));
        }

        var orderedQuery = joinedQuery.OrderByDescending(x => x.ReturnRequest.CreatedOnUtc);

        var totalCount = orderedQuery.Count();
        var items = orderedQuery
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .Select(
            x => new {
              x.ReturnRequest,
              x.OrderItem.OrderId,
              x.OrderItem.ProductId,
            }
          )
          .ToList();

        var includeProductName = requestedFields == null || IsFieldRequested(requestedFields, "product_name");
        var includeSku = requestedFields == null || IsFieldRequested(requestedFields, "sku");
        var needProductEnrichment = includeProductName || includeSku;

        Dictionary<int, (string? Name, string? Sku)> productMap = new();

        if (needProductEnrichment && items.Any()) {
          var productIds = items.Select(x => x.ProductId).Distinct().ToArray();
          var productService = EngineContext.Current.Resolve<Nop.Services.Catalog.IProductService>();
          var products = await productService.GetProductsByIdsAsync(productIds);
          productMap = products.ToDictionary(p => p.Id, p => ((string?)p.Name, (string?)p.Sku));
        }

        var reasonRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestReason>>();
        var actionRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestAction>>();
        var reasonMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in reasonRepo.Table.ToList()) {
          reasonMap.TryAdd(r.Name, r.Id);
        }

        var actionMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in actionRepo.Table.ToList()) {
          actionMap.TryAdd(a.Name, a.Id);
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var item in items) {
          var rr = item.ReturnRequest;

          var data = new Dictionary<string, object?> {
            ["id"] = !string.IsNullOrEmpty(rr.CustomNumber) ? (object)rr.CustomNumber! : rr.Id.ToString(),
            ["record_id"] = rr.Id,
            ["custom_number"] = rr.CustomNumber,
            ["order_id"] = item.OrderId,
            ["customer_id"] = rr.CustomerId,
            ["store_id"] = rr.StoreId,
            ["order_item_id"] = rr.OrderItemId,
            ["product_id"] = item.ProductId,
            ["quantity"] = rr.Quantity,
            ["reason_for_return"] = rr.ReasonForReturn,
            ["reason_id"] = reasonMap.TryGetValue(rr.ReasonForReturn, out var reasonId) ? reasonId : (int?)null,
            ["requested_action"] = rr.RequestedAction,
            ["action_id"] = actionMap.TryGetValue(rr.RequestedAction, out var actionId) ? actionId : (int?)null,
            ["customer_comments"] = rr.CustomerComments,
            ["staff_notes"] = rr.StaffNotes,
            ["return_request_status_id"] = rr.ReturnRequestStatusId,
            ["created_on_utc"] = rr.CreatedOnUtc.ToString("o"),
            ["updated_on_utc"] = rr.UpdatedOnUtc.ToString("o"),
          };

          if (needProductEnrichment && productMap.TryGetValue(item.ProductId, out var product)) {
            if (includeProductName) {
              data["product_name"] = product.Name;
            }

            if (includeSku) {
              data["sku"] = product.Sku;
            }
          }

          result.Add(data);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { return_requests = result, total_count = totalCount },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Return Statuses List

    [HttpPost("return-statuses-list")]
    public async Task<IActionResult> ReturnStatusesList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var statuses = Enum.GetValues<Nop.Core.Domain.Orders.ReturnRequestStatus>()
          .Select(s => new { id = (int)s, name = s.ToString() })
          .ToArray();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { return_statuses = statuses },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Return Reasons List

    [HttpPost("return-reasons-list")]
    public async Task<IActionResult> ReturnReasonsList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var reasonRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestReason>>();

        var reasons = reasonRepo.Table
          .OrderBy(r => r.DisplayOrder)
          .ToList()
          .Select(
            r => new Dictionary<string, object?> {
              ["id"] = r.Id,
              ["name"] = r.Name,
            }
          )
          .ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { return_reasons = reasons },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Return Actions List

    [HttpPost("return-actions-list")]
    public async Task<IActionResult> ReturnActionsList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var actionRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestAction>>();

        var actions = actionRepo.Table
          .OrderBy(a => a.DisplayOrder)
          .ToList()
          .Select(
            a => new Dictionary<string, object?> {
              ["id"] = a.Id,
              ["name"] = a.Name,
            }
          )
          .ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { return_actions = actions },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Return Create

    [HttpPost("return-create")]
    public async Task<IActionResult> ReturnCreate(
        [FromQuery] int order_id,
        [FromQuery] int? status_id = null,
        [FromQuery] int? action_id = null,
        [FromQuery] int? reason_id = null,
        [FromQuery] string items = "",
        [FromQuery] string? customer_comments = null,
        [FromQuery] string? staff_notes = null,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      if (status_id.HasValue && !Enum.IsDefined(typeof(Nop.Core.Domain.Orders.ReturnRequestStatus), status_id.Value)) {
        return ParamError($"status_id={status_id.Value} is not a valid ReturnRequestStatus.");
      }

      try {
        var orderRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.Order>>();
        var order = orderRepo.Table.FirstOrDefault(o => o.Id == order_id);

        if (order == null) {
          return NotFoundError($"Order with id = {order_id} not found.");
        }

        var itemsList = new List<ReturnItemPayload>();

        if (!string.IsNullOrEmpty(items)) {
          try {
            itemsList = JsonSerializer.Deserialize<List<ReturnItemPayload>>(items, _jsonOptions) ?? new();
          } catch (JsonException ex) {
            return ParamError($"Parameter 'items' is not valid JSON: {ex.Message}");
          }
        }

        if (itemsList.Count == 0) {
          return ParamError("Parameter 'items' is required and must not be empty.");
        }

        string? actionName = null;
        string? reasonName = null;

        if (action_id.HasValue) {
          var actionRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestAction>>();
          var action = actionRepo.Table.FirstOrDefault(a => a.Id == action_id.Value);

          if (action == null) {
            return NotFoundError($"ReturnRequestAction with id = {action_id.Value} not found.");
          }

          actionName = action.Name;
        }

        if (reason_id.HasValue) {
          var reasonRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestReason>>();
          var reason = reasonRepo.Table.FirstOrDefault(r => r.Id == reason_id.Value);

          if (reason == null) {
            return NotFoundError($"ReturnRequestReason with id = {reason_id.Value} not found.");
          }

          reasonName = reason.Name;
        }

        var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
        var orderItems = orderItemRepo.Table.Where(oi => oi.OrderId == order_id).ToList();
        var returnRequestRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequest>>();
        var orderItemIds = orderItems.Select(oi => oi.Id).ToList();
        var returnedByOrderItem = returnRequestRepo.Table
          .Where(rr => orderItemIds.Contains(rr.OrderItemId))
          .GroupBy(rr => rr.OrderItemId)
          .Select(g => new { OrderItemId = g.Key, Total = g.Sum(rr => rr.Quantity) })
          .ToDictionary(x => x.OrderItemId, x => x.Total);

        var productService = EngineContext.Current.Resolve<Nop.Services.Catalog.IProductService>();
        var productIds = orderItems.Select(oi => oi.ProductId).Distinct().ToArray();
        var productsById = (await productService.GetProductsByIdsAsync(productIds)).ToDictionary(p => p.Id, p => p);
        var requestedByOrderItem = new Dictionary<int, int>();

        foreach (var item in itemsList) {
          var orderItem = orderItems.FirstOrDefault(oi => oi.Id == item.order_product_id);

          if (orderItem == null) {
            return ParamError($"Order product {item.order_product_id} does not belong to order {order_id}.");
          }

          if (item.quantity <= 0) {
            return ParamError($"Return quantity must be > 0 (order_product_id={item.order_product_id}).");
          }

          if (productsById.TryGetValue(orderItem.ProductId, out var product)) {
            if (product.IsDownload || product.IsGiftCard) {
              return StoreError($"Order product {item.order_product_id} is a digital good (downloadable/gift-card) and cannot be returned.");
            }
          }

          var alreadyReturned = returnedByOrderItem.TryGetValue(orderItem.Id, out var ret) ? ret : 0;
          var pendingForThisRequest = requestedByOrderItem.TryGetValue(orderItem.Id, out var pend) ? pend : 0;
          var availableQty = orderItem.Quantity - alreadyReturned - pendingForThisRequest;

          if (availableQty <= 0) {
            return StoreError(
              $"Order product {item.order_product_id} has no items left to return "
                + $"({orderItem.Quantity} ordered, {alreadyReturned} already returned)."
            );
          }

          if (item.quantity > availableQty) {
            return StoreError(
              $"Return qty ({item.quantity}) exceeds available to return ({availableQty}) "
                + $"for order product {item.order_product_id}."
            );
          }

          if (!string.IsNullOrEmpty(item.customer_comment) && item.customer_comment.Length > 255) {
            return ParamError($"Customer comment for order product {item.order_product_id} exceeds 255 chars.");
          }

          requestedByOrderItem[orderItem.Id] = pendingForThisRequest + item.quantity;
        }

        var customerId = order.CustomerId;
        var effectiveStoreId = store_id > 0 ? store_id : order.StoreId;

        var insertedIds = new List<int>();
        string? customNumber = null;

        foreach (var item in itemsList) {
          var rr = new Nop.Core.Domain.Orders.ReturnRequest {
            StoreId = effectiveStoreId,
            OrderItemId = item.order_product_id,
            CustomerId = customerId,
            Quantity = item.quantity,
            ReasonForReturn = reasonName ?? "",
            RequestedAction = actionName ?? "",
            CustomerComments = !string.IsNullOrEmpty(item.customer_comment) ? item.customer_comment : (customer_comments ?? ""),
            StaffNotes = staff_notes ?? "",
            ReturnRequestStatusId = status_id ?? 0,
            CustomNumber = "",
            CreatedOnUtc = DateTime.UtcNow,
            UpdatedOnUtc = DateTime.UtcNow,
          };

          await returnRequestRepo.InsertAsync(rr);

          if (customNumber == null) {
            customNumber = rr.Id.ToString();
          }

          rr.CustomNumber = customNumber;
          await returnRequestRepo.UpdateAsync(rr);
          insertedIds.Add(rr.Id);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              custom_number = customNumber,
              ids = insertedIds,
              action_name = actionName,
            },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private class ReturnItemPayload
    {
      public int order_product_id { get; set; }
      public int quantity { get; set; }
      public string? customer_comment { get; set; }
    }

    #endregion

    #region Return Update

    [HttpPost("return-update")]
    public async Task<IActionResult> ReturnUpdate(
        [FromQuery] string custom_number = "",
        [FromQuery] int order_id = 0,
        [FromQuery] int? status_id = null,
        [FromQuery] int? action_id = null,
        [FromQuery] int? reason_id = null,
        [FromQuery] string? customer_comments = null,
        [FromQuery] string? staff_notes = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      if (string.IsNullOrWhiteSpace(custom_number)) {
        return ParamError("Parameter 'custom_number' is required.");
      }

      if (order_id <= 0) {
        return ParamError("Parameter 'order_id' is required.");
      }

      if (status_id.HasValue && !Enum.IsDefined(typeof(Nop.Core.Domain.Orders.ReturnRequestStatus), status_id.Value)) {
        return ParamError($"status_id={status_id.Value} is not a valid ReturnRequestStatus.");
      }

      try {
        var returnRequestRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequest>>();
        var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
        var rows = returnRequestRepo.Table
          .Join(orderItemRepo.Table, rr => rr.OrderItemId, oi => oi.Id, (rr, oi) => new { rr, oi })
          .Where(x => x.rr.CustomNumber == custom_number && x.oi.OrderId == order_id)
          .Select(x => x.rr)
          .ToList();

        if (rows.Count == 0) {
          return NotFoundError($"Return with custom_number = {custom_number} not found for order {order_id}.");
        }

        string? actionName = null;
        string? reasonName = null;

        if (action_id.HasValue) {
          var actionRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestAction>>();
          var action = actionRepo.Table.FirstOrDefault(a => a.Id == action_id.Value);

          if (action == null) {
            return NotFoundError($"ReturnRequestAction with id = {action_id.Value} not found.");
          }

          actionName = action.Name;
        }

        if (reason_id.HasValue) {
          var reasonRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequestReason>>();
          var reason = reasonRepo.Table.FirstOrDefault(r => r.Id == reason_id.Value);

          if (reason == null) {
            return NotFoundError($"ReturnRequestReason with id = {reason_id.Value} not found.");
          }

          reasonName = reason.Name;
        }

        foreach (var rr in rows) {
          if (status_id.HasValue) {
            rr.ReturnRequestStatusId = status_id.Value;
          }

          if (actionName != null) {
            rr.RequestedAction = actionName;
          }

          if (reasonName != null) {
            rr.ReasonForReturn = reasonName;
          }

          if (customer_comments != null) {
            rr.CustomerComments = customer_comments;
          }

          if (staff_notes != null) {
            rr.StaffNotes = staff_notes;
          }

          rr.UpdatedOnUtc = DateTime.UtcNow;
          await returnRequestRepo.UpdateAsync(rr);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              updated = rows.Count,
              action_name = actionName,
            },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Return Delete

    [HttpPost("return-delete")]
    public async Task<IActionResult> ReturnDelete(
        [FromQuery] string custom_number = "",
        [FromQuery] int order_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      if (string.IsNullOrWhiteSpace(custom_number)) {
        return ParamError("Parameter 'custom_number' is required.");
      }

      if (order_id <= 0) {
        return ParamError("Parameter 'order_id' is required.");
      }

      try {
        var returnRequestRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequest>>();
        var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
        var rows = returnRequestRepo.Table
          .Join(orderItemRepo.Table, rr => rr.OrderItemId, oi => oi.Id, (rr, oi) => new { rr, oi })
          .Where(x => x.rr.CustomNumber == custom_number && x.oi.OrderId == order_id)
          .Select(x => x.rr)
          .ToList();

        if (rows.Count == 0) {
          return NotFoundError($"Return with custom_number = {custom_number} not found for order {order_id}.");
        }

        foreach (var rr in rows) {
          await returnRequestRepo.DeleteAsync(rr);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { deleted = rows.Count },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion
  }
}
