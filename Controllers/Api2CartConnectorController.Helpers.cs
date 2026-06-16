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
    #region Query Builders & Filters

    private static Dictionary<int, int> GetPlannedQuantityByWarehouse(int productId)
    {
      var orderItemRepo = EngineContext.Current
        .Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
      var orderRepo = EngineContext.Current
        .Resolve<IRepository<Nop.Core.Domain.Orders.Order>>();
      var shipmentItemRepo = EngineContext.Current
        .Resolve<IRepository<Nop.Core.Domain.Shipping.ShipmentItem>>();

      var cancelledStatusId = 40;

      var orderItems = orderItemRepo.Table
        .Where(oi => oi.ProductId == productId)
        .Join(orderRepo.Table.Where(o => o.OrderStatusId != cancelledStatusId),
          oi => oi.OrderId, o => o.Id, (oi, o) => oi)
        .ToList();

      if (!orderItems.Any()) {
        return new Dictionary<int, int>();
      }

      var orderItemIds = orderItems.Select(oi => oi.Id).ToList();

      var shippedByItem = shipmentItemRepo.Table
        .Where(si => orderItemIds.Contains(si.OrderItemId))
        .GroupBy(si => new { si.OrderItemId, si.WarehouseId })
        .Select(g => new
        {
          g.Key.OrderItemId,
          g.Key.WarehouseId,
          ShippedQty = g.Sum(si => si.Quantity),
        })
        .ToList();

      var result = new Dictionary<int, int>();

      foreach (var oi in orderItems) {
        var remaining = oi.Quantity;

        foreach (var shipped in shippedByItem.Where(s => s.OrderItemId == oi.Id)) {
          remaining -= shipped.ShippedQty;
        }

        if (remaining <= 0) {
          continue;
        }

        var warehouseId = shippedByItem
          .Where(s => s.OrderItemId == oi.Id)
          .Select(s => s.WarehouseId)
          .FirstOrDefault();

        if (result.ContainsKey(warehouseId)) {
          result[warehouseId] += remaining;
        } else {
          result[warehouseId] = remaining;
        }
      }

      return result;
    }

    private static IQueryable<Product> BuildFilteredProductQuery(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo,
        List<int>? categoryIds,
        ProductType? productType,
        string? sku,
        bool? published,
        int? manufacturerId = null,
        string? findValue = null,
        int storeId = 0,
        int vendorId = 0,
        int customerRoleId = 0,
        bool? availSale = null,
        string? findWhere = null,
        int langId = 0
    )
    {
      var productRepository = EngineContext.Current.Resolve<IRepository<Product>>();
      var query = productRepository.Table.Where(p => !p.Deleted);

      if (createdFrom.HasValue) {
        query = query.Where(p => p.CreatedOnUtc >= createdFrom.Value);
      }

      if (createdTo.HasValue) {
        query = query.Where(p => p.CreatedOnUtc <= createdTo.Value);
      }

      if (modifiedFrom.HasValue) {
        query = query.Where(p => p.UpdatedOnUtc >= modifiedFrom.Value);
      }

      if (modifiedTo.HasValue) {
        query = query.Where(p => p.UpdatedOnUtc <= modifiedTo.Value);
      }

      if (published.HasValue) {
        query = query.Where(p => p.Published == published.Value);
      }

      if (availSale.HasValue) {
        if (availSale.Value) {
          query = query.Where(p => p.Published && (p.StockQuantity > p.MinStockQuantity || p.BackorderModeId > 0));
        } else {
          query = query.Where(p => !p.Published || (p.StockQuantity <= p.MinStockQuantity && p.BackorderModeId == 0));
        }
      }

      if (productType.HasValue) {
        query = query.Where(p => p.ProductTypeId == (int)productType.Value);
      }

      if (!string.IsNullOrEmpty(sku)) {
        if (sku.Contains('_') || sku.Contains('%')) {
          var skuValue = sku;

          query = query.Where(p => p.Sku != null && p.Sku == skuValue);
        } else {
          query = query.Where(p => p.Sku != null && p.Sku.Contains(sku));
        }
      }

      if (categoryIds != null && categoryIds.Any(id => id > 0)) {
        var validIds = categoryIds.Where(id => id > 0).ToList();
        var categoryMappingRepository = EngineContext.Current.Resolve<IRepository<ProductCategory>>();
        var productIdsInCategory = categoryMappingRepository.Table
          .Where(pc => validIds.Contains(pc.CategoryId))
          .Select(pc => pc.ProductId);

        query = query.Where(p => productIdsInCategory.Contains(p.Id));
      }

      if (manufacturerId.HasValue && manufacturerId.Value > 0) {
        var manufacturerMappingRepository = EngineContext.Current.Resolve<IRepository<ProductManufacturer>>();
        var productIdsWithManufacturer = manufacturerMappingRepository.Table
          .Where(pm => pm.ManufacturerId == manufacturerId.Value)
          .Select(pm => pm.ProductId);

        query = query.Where(p => productIdsWithManufacturer.Contains(p.Id));
      }

      if (!string.IsNullOrEmpty(findValue)) {
        var searchInName = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("name", StringComparison.OrdinalIgnoreCase);
        var searchInSku = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("sku", StringComparison.OrdinalIgnoreCase);
        var searchInDesc = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("description", StringComparison.OrdinalIgnoreCase);

        var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
        var lpQuery = lpRepository.Table.Where(lp => lp.LocaleKeyGroup == "Product");

        if (langId > 0) {
          lpQuery = lpQuery.Where(lp => lp.LanguageId == langId);
        }

        var localizedNameIds = lpQuery
          .Where(lp => lp.LocaleKey == "Name" && lp.LocaleValue.Contains(findValue))
          .Select(lp => lp.EntityId);

        var localizedDescIds = lpQuery
          .Where(lp => lp.LocaleKey == "ShortDescription" && lp.LocaleValue.Contains(findValue))
          .Select(lp => lp.EntityId);

        if (searchInName && searchInSku && searchInDesc) {
          query = query.Where(p =>
            (p.Name != null && p.Name.Contains(findValue))
            || (p.Sku != null && p.Sku.Contains(findValue))
            || (p.ShortDescription != null && p.ShortDescription.Contains(findValue))
            || localizedNameIds.Contains(p.Id) || localizedDescIds.Contains(p.Id));
        } else if (searchInName && searchInSku) {
          query = query.Where(p =>
            (p.Name != null && p.Name.Contains(findValue))
            || (p.Sku != null && p.Sku.Contains(findValue))
            || localizedNameIds.Contains(p.Id));
        } else if (searchInName && searchInDesc) {
          query = query.Where(p =>
            (p.Name != null && p.Name.Contains(findValue))
            || (p.ShortDescription != null && p.ShortDescription.Contains(findValue))
            || localizedNameIds.Contains(p.Id) || localizedDescIds.Contains(p.Id));
        } else if (searchInSku && searchInDesc) {
          query = query.Where(p =>
            (p.Sku != null && p.Sku.Contains(findValue))
            || (p.ShortDescription != null && p.ShortDescription.Contains(findValue))
            || localizedDescIds.Contains(p.Id));
        } else if (searchInSku) {
          query = query.Where(p => p.Sku != null && p.Sku.Contains(findValue));
        } else if (searchInName) {
          query = query.Where(p =>
            (p.Name != null && p.Name.Contains(findValue)) || localizedNameIds.Contains(p.Id));
        } else if (searchInDesc) {
          query = query.Where(p =>
            (p.ShortDescription != null && p.ShortDescription.Contains(findValue)) || localizedDescIds.Contains(p.Id));
        }
      }

      if (storeId > 0) {
        var storeMappingRepository = EngineContext.Current
          .Resolve<IRepository<Nop.Core.Domain.Stores.StoreMapping>>();
        var mappedProductIds = storeMappingRepository.Table
          .Where(sm => sm.EntityName == "Product" && sm.StoreId == storeId)
          .Select(sm => sm.EntityId);

        query = query.Where(p => !p.LimitedToStores || mappedProductIds.Contains(p.Id));
      }

      if (vendorId > 0) {
        query = query.Where(p => p.VendorId == vendorId);
      }

      if (customerRoleId > 0) {
        var aclRepository = EngineContext.Current
          .Resolve<IRepository<Nop.Core.Domain.Security.AclRecord>>();
        var aclProductIds = aclRepository.Table
          .Where(ar => ar.EntityName == "Product" && ar.CustomerRoleId == customerRoleId)
          .Select(ar => ar.EntityId);

        query = query.Where(p => !p.SubjectToAcl || aclProductIds.Contains(p.Id));
      }

      return query;
    }

    private static List<Product> ApplyDateFilter(
        IList<Product> products,
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo
    )
    {
      var result = products.AsEnumerable();

      if (createdFrom.HasValue) {
        result = result.Where(p => p.CreatedOnUtc >= createdFrom.Value);
      }

      if (createdTo.HasValue) {
        result = result.Where(p => p.CreatedOnUtc <= createdTo.Value);
      }

      if (modifiedFrom.HasValue) {
        result = result.Where(p => p.UpdatedOnUtc >= modifiedFrom.Value);
      }

      if (modifiedTo.HasValue) {
        result = result.Where(p => p.UpdatedOnUtc <= modifiedTo.Value);
      }

      return result.ToList();
    }

    private static bool IsAvailableForSale(Product p)
    {
      return p.Published && (p.StockQuantity > p.MinStockQuantity || p.BackorderModeId > 0);
    }

    private static bool HasDateFilter(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo
    )
    {
      return createdFrom.HasValue || createdTo.HasValue
        || modifiedFrom.HasValue || modifiedTo.HasValue;
    }

    #endregion

    #region Parsers

    private static DateTime? ParseDateFilter(string? value, bool isUpperBound = false)
    {
      if (string.IsNullOrEmpty(value)) {
        return null;
      }

      if (DateTime.TryParse(
        value,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.RoundtripKind,
        out var result)) {
        var utc = result.ToUniversalTime();

        if (isUpperBound) {
          utc = utc.TimeOfDay == TimeSpan.Zero
            ? utc.Date.AddDays(1).AddTicks(-1)
            : new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc)
                .AddSeconds(1).AddTicks(-1);
        }

        return utc;
      }

      return null;
    }

    private static HashSet<string>? ParseFields(string? value)
    {
      if (string.IsNullOrEmpty(value)) {
        return null;
      }

      return new HashSet<string>(
        value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
        StringComparer.OrdinalIgnoreCase);
    }

    private static int[] ParseProductIds(string value)
    {
      return value.Split(',')
        .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
        .Where(id => id > 0)
        .ToArray();
    }

    private static List<int> ParseParentProductIds(string? productIds)
    {
      var parentIds = new List<int>();

      if (!string.IsNullOrEmpty(productIds)) {
        foreach (var idStr in productIds.Split(',')) {
          if (int.TryParse(idStr.Trim(), out var pid) && pid > 0 && !parentIds.Contains(pid)) {
            parentIds.Add(pid);
          }
        }
      }

      return parentIds;
    }

    private static int[]? ParseIntIds(string? value)
    {
      if (string.IsNullOrEmpty(value)) {
        return null;
      }

      return value.Split(',')
        .Select(s => int.TryParse(s.Trim(), out var id) ? id : 0)
        .Where(id => id > 0)
        .ToArray();
    }

    private static ProductType? ParseProductType(string? value)
    {
      if (string.IsNullOrEmpty(value)) {
        return null;
      }

      if (int.TryParse(value, out var typeId) && Enum.IsDefined(typeof(ProductType), typeId)) {
        return (ProductType)typeId;
      }

      return value.ToLowerInvariant() switch
      {
        "simple" => ProductType.SimpleProduct,
        "grouped" => ProductType.GroupedProduct,
        _ => null
      };
    }

    private static bool IsFieldRequested(HashSet<string>? requestedFields, string fieldName)
    {
      return requestedFields != null && requestedFields.Contains(fieldName);
    }

    private async Task<int> ResolveLanguageIdAsync(string? languageCode)
    {
      if (string.IsNullOrEmpty(languageCode)) {
        return 0;
      }

      var store = await _storeContext.GetCurrentStoreAsync();
      var languages = await _languageService.GetAllLanguagesAsync(storeId: store.Id);

      var lang = languages.FirstOrDefault(l =>
        l.LanguageCulture.Equals(languageCode, StringComparison.OrdinalIgnoreCase)
        || l.UniqueSeoCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase));

      return lang?.Id ?? 0;
    }

    private async Task<int[]> GetNonGuestRoleIdsAsync()
    {
      var allRoles = await _customerService.GetAllCustomerRolesAsync();
      var guestRole = await _customerService.GetCustomerRoleBySystemNameAsync(
        NopCustomerDefaults.GuestsRoleName);

      return allRoles
        .Where(r => guestRole == null || r.Id != guestRole.Id)
        .Select(r => r.Id)
        .ToArray();
    }

    #endregion

    #region Response Helpers

    private ContentResult JsonContent<T>(T data, int statusCode = 200)
    {
      return new ContentResult
      {
        Content     = JsonSerializer.Serialize(data, _jsonOptions),
        ContentType = "application/json",
        StatusCode  = statusCode,
      };
    }

    private ContentResult ErrorResponse(string code, Exception ex)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 0,
          Error = new ConnectorError {
            Code = code,
            Message = ex.Message,
            Trace = ExceptionHelper.GetFilteredTrace(ex),
          },
        },
        500
      );
    }

    private ContentResult ErrorResponse(string code, string message, int statusCode = 400)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 0,
          Error = new ConnectorError {Code = code, Message = message},
        },
        statusCode
      );
    }

    private ContentResult ParamError(string message)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 4,
          Error = new ConnectorError {Code = "INVALID_PARAM", Message = message},
        }
      );
    }

    private static bool HasUnsupportedChars(params string?[] values)
    {
      foreach (var v in values) {
        if (string.IsNullOrEmpty(v)) {
          continue;
        }

        foreach (var c in v) {
          if (char.IsHighSurrogate(c) || c == '\0') {
            return true;
          }
        }
      }

      return false;
    }

    private ContentResult? ValidateSupportedChars(params string?[] values)
    {
      return HasUnsupportedChars(values) ? UnsupportedCharsError() : null;
    }

    private static bool IsUnsupportedCharsError(Exception ex)
    {
      for (var e = ex; e != null; e = e.InnerException) {
        var msg = e.Message ?? string.Empty;
        if (msg.Contains("Incorrect string value", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("error 1366", StringComparison.OrdinalIgnoreCase)) {
          return true;
        }
      }
      return false;
    }

    private ContentResult UnsupportedCharsError()
    {
      return ParamError(
        "One of text fields contains characters not supported by the store database "
        + "(e.g. 4-byte UTF-8 emoji or NULL byte). Remove unsupported characters or upgrade the column charset to utf8mb4."
      );
    }

    private sealed class EmailLockEntry
    {
      public readonly SemaphoreSlim Semaphore = new(1, 1);
      public int RefCount;
    }

    private static readonly Dictionary<string, EmailLockEntry> _emailLocks = new();
    private static readonly object _emailLocksMutex = new();

    private sealed class ReleaseOnDispose : IAsyncDisposable
    {
      private readonly string _key;
      private readonly EmailLockEntry _entry;

      public ReleaseOnDispose(string key, EmailLockEntry entry)
      {
        _key = key;
        _entry = entry;
      }

      public ValueTask DisposeAsync()
      {
        _entry.Semaphore.Release();
        ReleaseEmailLockEntry(_key, _entry);

        return ValueTask.CompletedTask;
      }
    }

    private static async Task<ReleaseOnDispose> AcquireEmailLockAsync(string email)
    {
      var key = email.ToLowerInvariant();

      EmailLockEntry entry;

      lock (_emailLocksMutex) {
        if (!_emailLocks.TryGetValue(key, out var existing)) {
          existing = new EmailLockEntry();
          _emailLocks[key] = existing;
        }

        existing.RefCount++;
        entry = existing;
      }

      try {
        await entry.Semaphore.WaitAsync();
      } catch {
        ReleaseEmailLockEntry(key, entry);
        throw;
      }

      return new ReleaseOnDispose(key, entry);
    }

    private static void ReleaseEmailLockEntry(string key, EmailLockEntry entry)
    {
      lock (_emailLocksMutex) {
        entry.RefCount--;

        if (entry.RefCount == 0) {
          _emailLocks.Remove(key);
          entry.Semaphore.Dispose();
        }
      }
    }

    private static bool TryParsePositiveInt(string? value, out int result)
    {
      result = 0;
      return !string.IsNullOrEmpty(value) && int.TryParse(value, out result) && result > 0;
    }

    private ContentResult NotFoundError(string message)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 3,
          Error = new ConnectorError {Code = "NOT_FOUND", Message = message},
        }
      );
    }

    private ContentResult ExistsError(string message)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 5,
          Error = new ConnectorError {Code = "ALREADY_EXISTS", Message = message},
        }
      );
    }

    private ContentResult StoreError(string message)
    {
      return JsonContent(
        new ConnectorResponse<object> {
          ResponseCode = 6,
          Error = new ConnectorError {Code = "STORE_ERROR", Message = message},
        }
      );
    }

    #endregion

    #region Atomic SQL

    private static async Task AdjustProductStockAtomicallyAsync(int productId, int delta)
    {
      if (delta == 0) {
        return;
      }

      var dataProvider = EngineContext.Current.Resolve<INopDataProvider>();
      var method = typeof(INopDataProvider).GetMethod("ExecuteNonQueryAsync")
        ?? throw new InvalidOperationException("INopDataProvider.ExecuteNonQueryAsync not found.");
      var dataParameterArrayType = method.GetParameters()[1].ParameterType;
      var emptyParams = Array.CreateInstance(dataParameterArrayType.GetElementType()!, 0);
      var sql = "UPDATE Product SET StockQuantity = CASE "
        + $"WHEN StockQuantity + ({delta}) < 0 THEN 0 "
        + $"ELSE StockQuantity + ({delta}) END WHERE Id = {productId}";
      var task = (Task<int>)method.Invoke(dataProvider, new object?[] { sql, emptyParams })!;

      await task;
    }

    private static async Task AdjustWarehouseInventoryAsync(int productId, int warehouseId, int delta)
    {
      if (delta == 0) {
        return;
      }

      var repo = EngineContext.Current.Resolve<IRepository<ProductWarehouseInventory>>();
      var row = repo.Table.FirstOrDefault(r => r.ProductId == productId && r.WarehouseId == warehouseId);

      if (row == null) {
        row = new ProductWarehouseInventory {
          ProductId = productId,
          WarehouseId = warehouseId,
          StockQuantity = Math.Max(0, delta),
          ReservedQuantity = 0,
        };
        await repo.InsertAsync(row);
      } else {
        row.StockQuantity += delta;
        await repo.UpdateAsync(row);
      }
    }

    #endregion
  }
}
