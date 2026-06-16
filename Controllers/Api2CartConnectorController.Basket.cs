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
    #region Basket

    [HttpPost("basket-info")]
    public async Task<IActionResult> BasketInfo(
        [FromQuery] string? id = null,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var customerId)) {
          return NotFoundError($"Customer with id {id} not found.");
        }

        var customer = await _customerService.GetCustomerByIdAsync(customerId);

        if (customer == null || customer.Deleted) {
          return NotFoundError($"Customer with id {id} not found.");
        }

        var cartRepo = EngineContext.Current.Resolve<IRepository<ShoppingCartItem>>();
        var productService = EngineContext.Current.Resolve<IProductService>();
        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var productAttributeParser = EngineContext.Current.Resolve<IProductAttributeParser>();

        var query = cartRepo.Table
          .Where(sci => sci.CustomerId == customerId
            && sci.ShoppingCartTypeId == (int)ShoppingCartType.ShoppingCart);

        if (store_id > 0) {
          query = query.Where(sci => sci.StoreId == store_id);
        }

        var cartItems = query.OrderBy(sci => sci.Id).ToList();

        if (!cartItems.Any()) {
          return NotFoundError($"No basket found for customer_id={customerId}.");
        }

        var items = new List<object>();

        var productIds = cartItems.Select(ci => ci.ProductId).Distinct().ToArray();
        var products = await productService.GetProductsByIdsAsync(productIds);
        var productById = products.Where(p => p != null).ToDictionary(p => p.Id);

        foreach (var item in cartItems) {
          productById.TryGetValue(item.ProductId, out var product);

          var itemData = new Dictionary<string, object?>
          {
            ["id"] = item.Id,
            ["product_id"] = item.ProductId,
            ["store_id"] = item.StoreId,
            ["quantity"] = item.Quantity,
            ["customer_entered_price"] = item.CustomerEnteredPrice,
            ["created_at"] = item.CreatedOnUtc.ToString("o"),
            ["updated_at"] = item.UpdatedOnUtc.ToString("o"),
          };

          if (product != null) {
            itemData["product_name"] = product.Name;
            itemData["sku"] = product.Sku;
            itemData["price"] = product.Price;
            itemData["weight"] = product.Weight;
            itemData["product_type_id"] = product.ProductTypeId;
            itemData["vendor_id"] = product.VendorId;
            itemData["parent_grouped_product_id"] = product.ParentGroupedProductId;
            itemData["tax_category_id"] = product.TaxCategoryId;
          }

          if (!string.IsNullOrEmpty(item.AttributesXml) && product != null) {
            itemData["attributes_xml"] = item.AttributesXml;

            var combination = await productAttributeParser
              .FindProductAttributeCombinationAsync(product, item.AttributesXml);

            if (combination != null) {
              itemData["combination_id"] = combination.Id;
              itemData["combination_sku"] = combination.Sku;
            }

            var attributeMappings = await productAttributeParser
              .ParseProductAttributeMappingsAsync(item.AttributesXml);
            var options = new List<object>();

            foreach (var mapping in attributeMappings) {
              var productAttribute = await productAttributeService
                .GetProductAttributeByIdAsync(mapping.ProductAttributeId);
              var values = await productAttributeParser
                .ParseProductAttributeValuesAsync(item.AttributesXml, mapping.Id);

              foreach (var val in values) {
                options.Add(new Dictionary<string, object?> {
                  ["attribute_id"] = mapping.ProductAttributeId,
                  ["attribute_name"] = productAttribute?.Name,
                  ["value_id"] = val.Id,
                  ["value"] = val.Name,
                });
              }
            }

            if (options.Any()) {
              itemData["options"] = options;
            }
          }

          items.Add(itemData);
        }

        var basketData = new Dictionary<string, object?>
        {
          ["customer_id"] = customerId,
          ["customer_email"] = customer.Email,
          ["customer_first_name"] = customer.FirstName,
          ["customer_last_name"] = customer.LastName,
          ["store_id"] = store_id > 0 ? store_id : cartItems.First().StoreId,
          ["created_at"] = cartItems.Min(ci => ci.CreatedOnUtc).ToString("o"),
          ["updated_at"] = cartItems.Max(ci => ci.UpdatedOnUtc).ToString("o"),
          ["items"] = items,
        };

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { basket = basketData },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion
  }
}
