using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
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
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Wishlist

    [HttpPost("wishlist-list")]
    public async Task<IActionResult> WishlistList(
        [FromQuery] string? customer_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] int page = 0,
        [FromQuery] int size = 10
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(customer_id, out var parsedCustomerId)) {
          return NotFoundError($"Customer with id {customer_id} not found.");
        }

        var customer = await _customerService.GetCustomerByIdAsync(parsedCustomerId);

        if (customer == null || customer.Deleted) {
          return NotFoundError($"Customer with id {customer_id} not found.");
        }

        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var cartRepo = EngineContext.Current.Resolve<IRepository<ShoppingCartItem>>();
        var productService = EngineContext.Current.Resolve<IProductService>();

        var baseQuery = cartRepo.Table
          .Where(sci => sci.CustomerId == parsedCustomerId
            && sci.ShoppingCartTypeId == (int)ShoppingCartType.Wishlist);

        if (store_id > 0) {
          baseQuery = baseQuery.Where(sci => sci.StoreId == store_id);
        }

        // Step 1: get distinct wishlist IDs with pagination at DB level
        var allWishlistIds = baseQuery
          .Select(sci => sci.CustomWishlistId)
          .Distinct()
          .OrderBy(id => id ?? 0)
          .ToList();

        var totalCount = allWishlistIds.Count;
        var pagedWishlistIds = allWishlistIds
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        // Step 2: load items only for paged wishlists
        var items = baseQuery
          .Where(sci => pagedWishlistIds.Contains(sci.CustomWishlistId))
          .OrderBy(sci => sci.Id)
          .ToList();

        // Step 3: load wishlist names
        var wishlistNames = new Dictionary<int, string>();
        var customIds = pagedWishlistIds
          .Where(id => id.HasValue)
          .Select(id => id!.Value)
          .ToList();

        if (customIds.Any()) {
          try {
            var cwRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.CustomWishlist>>();
            var customWishlists = cwRepo.Table
              .Where(cw => customIds.Contains(cw.Id))
              .ToList();

            foreach (var cw in customWishlists) {
              wishlistNames[cw.Id] = cw.Name ?? "Wishlist";
            }
          } catch {
            // CustomWishlist table may not exist in older NopCommerce versions
          }
        }

        // Step 4: load products
        var productIds = items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await productService.GetProductsByIdsAsync(productIds);
        var productById = products.Where(p => p != null).ToDictionary(p => p.Id);

        // Step 5: build response grouped by wishlist
        var pagedWishlists = new List<object>();

        foreach (var wishlistId in pagedWishlistIds) {
          var wishlistItems = items
            .Where(sci => sci.CustomWishlistId == wishlistId)
            .ToList();

          var name = wishlistId.HasValue && wishlistNames.TryGetValue(wishlistId.Value, out var n)
            ? n : "Wishlist";

          var itemList = new List<object>();

          foreach (var item in wishlistItems) {
            var itemData = new Dictionary<string, object?>
            {
              ["id"] = item.Id,
              ["product_id"] = item.ProductId,
              ["store_id"] = item.StoreId,
              ["quantity"] = item.Quantity,
              ["created_at"] = item.CreatedOnUtc.ToString("o"),
              ["updated_at"] = item.UpdatedOnUtc.ToString("o"),
            };

            if (productById.TryGetValue(item.ProductId, out var product)) {
              itemData["product_name"] = product.Name;
              itemData["sku"] = product.Sku;
              itemData["price"] = product.Price;
              itemData["product_type_id"] = product.ProductTypeId;
              itemData["vendor_id"] = product.VendorId;
              itemData["parent_grouped_product_id"] = product.ParentGroupedProductId;
            }

            if (!string.IsNullOrEmpty(item.AttributesXml)) {
              itemData["attributes_xml"] = item.AttributesXml;
            }

            itemList.Add(itemData);
          }

          pagedWishlists.Add(
            new Dictionary<string, object?>
              {
                ["id"] = wishlistId ?? 0,
                ["name"] = name,
                ["customer_id"] = parsedCustomerId,
                ["items"] = itemList,
              }
          );
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { wishlists = pagedWishlists, total_count = totalCount, customer_id = parsedCustomerId },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }




    #endregion
  }
}
