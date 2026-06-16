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
using Nop.Services.Vendors;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Orders

    [HttpPost("orders-list")]
    public async Task<IActionResult> OrdersList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] int store_id = 0,
        [FromQuery] string? ids = null,
        [FromQuery] string? order_status_ids = null,
        [FromQuery] string? payment_status_ids = null,
        [FromQuery] string? shipping_status_ids = null,
        [FromQuery] int? customer_id = null,
        [FromQuery] string? customer_email = null,
        [FromQuery] int? vendor_id = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? fields = null,
        [FromQuery] string? sort_by = null,
        [FromQuery] string? sort_direction = null
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
        var orderService = EngineContext.Current.Resolve<IOrderService>();

        var orderStatusIds = ParseIntIds(order_status_ids);
        var paymentStatusIds = ParseIntIds(payment_status_ids);
        var shippingStatusIds = ParseIntIds(shipping_status_ids);
        var parsedIds = ParseIntIds(ids);
        var hasIdFilter = !string.IsNullOrEmpty(ids);

        if (hasIdFilter && (parsedIds == null || parsedIds.Length == 0)) {
          return JsonContent(
            new ConnectorResponse<object> {
              Result = new { orders = Array.Empty<object>(), total_count = 0 },
            }
          );
        }

        var orders = await orderService.SearchOrdersAsync(
          storeId: store_id,
          vendorId: vendor_id ?? 0,
          customerId: customer_id ?? 0,
          billingEmail: customer_email,
          osIds: orderStatusIds?.ToList(),
          psIds: paymentStatusIds?.ToList(),
          ssIds: shippingStatusIds?.ToList(),
          createdFromUtc: ParseDateFilter(created_from),
          createdToUtc: ParseDateFilter(created_to, isUpperBound: true),
          pageIndex: hasIdFilter ? 0 : pageIndex,
          pageSize: hasIdFilter ? int.MaxValue : pageSize
        );

        IList<Nop.Core.Domain.Orders.Order> filteredOrders = orders;

        if (hasIdFilter) {
          var idSet = new HashSet<int>(parsedIds!);
          filteredOrders = filteredOrders.Where(o => idSet.Contains(o.Id)).ToList();
        }

        var isAsc = string.Equals(sort_direction, "asc", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrEmpty(sort_by) || !string.IsNullOrEmpty(sort_direction)) {
          filteredOrders = sort_by?.ToLowerInvariant() switch {
            "id" => isAsc
              ? filteredOrders.OrderBy(o => o.Id).ToList()
              : filteredOrders.OrderByDescending(o => o.Id).ToList(),
            _ => isAsc
              ? filteredOrders.OrderBy(o => o.CreatedOnUtc).ToList()
              : filteredOrders.OrderByDescending(o => o.CreatedOnUtc).ToList(),
          };
        }

        var totalCount = hasIdFilter ? filteredOrders.Count : orders.TotalCount;

        if (hasIdFilter) {
          filteredOrders = filteredOrders.Skip(pageIndex * pageSize).Take(pageSize).ToList();
        }

        var countryService = EngineContext.Current.Resolve<ICountryService>();
        var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();
        var addressService = EngineContext.Current.Resolve<IAddressService>();
        var result = new List<Dictionary<string, object?>>();

        foreach (var order in filteredOrders) {
          result.Add(await BuildOrderDataAsync(order, orderService, countryService, stateService, addressService, requestedFields));
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { orders = result, total_count = totalCount },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("order-statuses-list")]
    public async Task<IActionResult> OrderStatusesList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var statuses = Enum.GetValues<Nop.Core.Domain.Orders.OrderStatus>()
          .Select(s => new { id = (int)s, name = s.ToString() })
          .ToArray();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { statuses },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("payment-statuses-list")]
    public async Task<IActionResult> PaymentStatusesList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var statuses = Enum.GetValues<Nop.Core.Domain.Payments.PaymentStatus>()
          .Select(s => new { id = (int)s, name = s.ToString() })
          .ToArray();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { statuses },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("shipping-statuses-list")]
    public async Task<IActionResult> ShippingStatusesList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var statuses = Enum.GetValues<Nop.Core.Domain.Shipping.ShippingStatus>()
          .Select(s => new { id = (int)s, name = s.ToString() })
          .ToArray();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { statuses },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Orders Write (add / update / calculate)

    private record OrderItemInput
    {
      public int product_id { get; init; }
      public int? variant_id { get; init; }
      public int quantity { get; init; }
      public decimal? price { get; init; }
      public bool? price_includes_tax { get; init; }
      public List<OrderItemOption>? options { get; init; }
      public int? parent_index { get; init; }
      public string? parent_option_name { get; init; }
    }

    private record OrderItemOption
    {
      public string? name { get; init; }
      public string? value { get; init; }
    }

    private record OrderCalcAddressInput
    {
      public string? first_name { get; init; }
      public string? last_name { get; init; }
      public string? address1 { get; init; }
      public string? address2 { get; init; }
      public string? city { get; init; }
      public string? postcode { get; init; }
      public string? company { get; init; }
      public string? phone { get; init; }
      public string? fax { get; init; }
      public string? country_code { get; init; }
      public string? state { get; init; }
    }

    private sealed record ResolvedOrderItem(
      Nop.Core.Domain.Catalog.Product Product,
      int Qty,
      decimal UnitExcl,
      decimal UnitIncl,
      string AttrXml,
      string AttrDesc
    );

    private record CalcAddressInput(
      string? first_name,
      string? last_name,
      string? address1,
      string? address2,
      string? city,
      string? postcode,
      string? state,
      string? country_code,
      string? company,
      string? phone
    );

    [HttpPost("order-calculate")]
    public async Task<IActionResult> OrderCalculate(
        [FromQuery] string? billing_address = null,
        [FromQuery] string? shipping_address = null,
        [FromQuery] string? order_items = null,
        [FromQuery] string? currency = null,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (string.IsNullOrWhiteSpace(order_items)) {
          return ParamError("Parameter 'order_items' is required.");
        }

        List<OrderItemInput>? items;

        try {
          items = JsonSerializer.Deserialize<List<OrderItemInput>>(order_items, _jsonOptions);
        } catch (JsonException ex) {
          return ParamError($"Parameter 'order_items' is not valid JSON: {ex.Message}");
        }

        if (items == null || items.Count == 0) {
          return ParamError("'order_items' must contain at least one item.");
        }

        foreach (var item in items) {
          if (item.quantity <= 0) {
            return ParamError($"Item quantity must be > 0 (product_id={item.product_id}).");
          }

          if (item.variant_id.HasValue && item.variant_id.Value <= 0) {
            return ParamError($"Item variant_id must be positive (product_id={item.product_id}).");
          }
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var productAttrService = EngineContext.Current.Resolve<IProductAttributeService>();
        var workContext = EngineContext.Current.Resolve<IWorkContext>();
        var storeContext = EngineContext.Current.Resolve<IStoreContext>();
        var countryService = EngineContext.Current.Resolve<ICountryService>();
        var stateProvinceService = EngineContext.Current.Resolve<IStateProvinceService>();
        var shippingService = EngineContext.Current.Resolve<Nop.Services.Shipping.IShippingService>();
        var totalCalcService = EngineContext.Current.Resolve<Nop.Services.Orders.IOrderTotalCalculationService>();

        var customer = await workContext.GetCurrentCustomerAsync();
        var currentStore = await storeContext.GetCurrentStoreAsync();
        int resolvedStoreId = store_id > 0 ? store_id : currentStore.Id;

        Nop.Core.Domain.Directory.Currency? targetCurrency = null;

        if (!string.IsNullOrWhiteSpace(currency)) {
          targetCurrency = await _currencyService.GetCurrencyByCodeAsync(currency);

          if (targetCurrency == null) {
            return ParamError($"Currency '{currency}' is not configured in the store.");
          }
        }

        var distinctProductIds = items.Select(i => i.product_id).Distinct().ToArray();
        var loadedProducts = await productService.GetProductsByIdsAsync(distinctProductIds);
        var productsById = loadedProducts
          .Where(p => !p.Deleted)
          .ToDictionary(p => p.Id, p => p);

        foreach (var item in items) {
          if (!productsById.ContainsKey(item.product_id)) {
            return NotFoundError($"Product with id {item.product_id} not found.");
          }
        }

        var combosByVariantId = new Dictionary<int, Nop.Core.Domain.Catalog.ProductAttributeCombination>();

        foreach (var item in items) {
          if (!item.variant_id.HasValue || item.variant_id.Value <= 0) {
            continue;
          }

          var vid = item.variant_id.Value;

          if (combosByVariantId.ContainsKey(vid)) {
            continue;
          }

          var combo = await productAttrService.GetProductAttributeCombinationByIdAsync(vid);

          if (combo == null) {
            return NotFoundError($"Variant with id {vid} not found.");
          }

          if (combo.ProductId != item.product_id) {
            return NotFoundError($"Variant with id {vid} does not belong to product {item.product_id}.");
          }

          combosByVariantId[vid] = combo;
        }

        var cartItems = items
          .Select(
            i => new ShoppingCartItem {
              CustomerId = customer.Id,
              StoreId = resolvedStoreId,
              ShoppingCartTypeId = (int)ShoppingCartType.ShoppingCart,
              ProductId = i.product_id,
              AttributesXml = (i.variant_id.HasValue && combosByVariantId.TryGetValue(i.variant_id.Value, out var c))
                ? (c.AttributesXml ?? string.Empty)
                : string.Empty,
              CustomerEnteredPrice = i.price ?? 0m,
              Quantity = i.quantity,
              CreatedOnUtc = DateTime.UtcNow,
              UpdatedOnUtc = DateTime.UtcNow,
            }
          )
          .ToList();

        Nop.Core.Domain.Common.Address? shippingAddressEntity = null;

        if (!string.IsNullOrWhiteSpace(shipping_address)) {
          var (addr, error) = await BuildAddressFromInputAsync(shipping_address, "shipp", countryService, stateProvinceService);

          if (error != null) {
            return error;
          }

          shippingAddressEntity = addr;
        } else if (!string.IsNullOrWhiteSpace(billing_address)) {
          var (addr, error) = await BuildAddressFromInputAsync(billing_address, "bill", countryService, stateProvinceService);

          if (error != null) {
            return error;
          }

          shippingAddressEntity = addr;
        }

        Nop.Core.Domain.Common.Address? billingAddressEntity = null;

        if (!string.IsNullOrWhiteSpace(billing_address)) {
          var (addr, error) = await BuildAddressFromInputAsync(billing_address, "bill", countryService, stateProvinceService);

          if (error != null) {
            return error;
          }

          billingAddressEntity = addr;
        } else if (!string.IsNullOrWhiteSpace(shipping_address)) {
          var (addr, error) = await BuildAddressFromInputAsync(shipping_address, "shipp", countryService, stateProvinceService);

          if (error != null) {
            return error;
          }

          billingAddressEntity = addr;
        }

        decimal subtotalDiscountAmount = 0m;
        decimal subtotalExclTax = 0m;
        decimal subtotalInclTax = 0m;
        var appliedDiscountRows = new List<(string name, decimal amount, string type)>();

        try {
          var subResult = await totalCalcService.GetShoppingCartSubTotalAsync(cartItems, includingTax: false);
          subtotalDiscountAmount = subResult.discountAmount;
          subtotalExclTax = subResult.subTotalWithoutDiscount;

          if (subResult.appliedDiscounts != null && subResult.appliedDiscounts.Count > 0 && subResult.discountAmount > 0) {
            var names = subResult.appliedDiscounts
              .Select(d => string.IsNullOrEmpty(d.Name) ? $"discount-{d.Id}" : d.Name)
              .ToArray();

            appliedDiscountRows.Add((string.Join(", ", names), subResult.discountAmount, "subtotal"));
          }
        } catch {
          subtotalExclTax = items.Sum(
            i => {
              var product = productsById[i.product_id];
              decimal unit = i.price ?? (
                i.variant_id.HasValue
                  && combosByVariantId.TryGetValue(i.variant_id.Value, out var combo)
                  && combo.OverriddenPrice.HasValue
                  ? combo.OverriddenPrice.Value
                  : product.Price
              );

              return unit * i.quantity;
            }
          );
        }

        try {
          var subResultIncl = await totalCalcService.GetShoppingCartSubTotalAsync(cartItems, includingTax: true);
          subtotalInclTax = subResultIncl.subTotalWithoutDiscount;
        } catch {
          subtotalInclTax = subtotalExclTax;
        }

        decimal totalTax = 0m;

        try {
          var taxResult = await totalCalcService.GetTaxTotalAsync(cartItems);
          totalTax = taxResult.taxTotal;
        } catch {
          totalTax = Math.Max(0m, subtotalInclTax - subtotalExclTax);
        }

        var rateOptions = new List<(string code, string name, decimal price)>();
        decimal shippingPrice = 0m;

        if (shippingAddressEntity != null) {
          try {
            var shippingResponse = await shippingService.GetShippingOptionsAsync(
              cartItems,
              shippingAddressEntity,
              customer,
              storeId: resolvedStoreId
            );

            if (shippingResponse?.ShippingOptions != null) {
              foreach (var opt in shippingResponse.ShippingOptions) {
                var systemName = opt.ShippingRateComputationMethodSystemName ?? string.Empty;
                var methodName = opt.Name ?? string.Empty;
                var code = string.IsNullOrEmpty(methodName) ? systemName : $"{systemName}:{methodName}";
                var name = string.IsNullOrEmpty(opt.Description) ? methodName : opt.Description;

                rateOptions.Add((code, name, opt.Rate));
              }
            }
          } catch {
            // Shipping calc failed (no methods, missing config) — leave rates empty.
          }
        }

        try {
          var shipTotal = await totalCalcService.GetShoppingCartShippingTotalAsync(cartItems);

          if (shipTotal.HasValue) {
            shippingPrice = shipTotal.Value;
          }
        } catch {
          // Shipping total calc failed — keep 0.
        }

        var paymentRows = new List<(string code, string name, decimal fee)>();

        try {
          var paymentPluginManager = EngineContext.Current.Resolve<Nop.Services.Payments.IPaymentPluginManager>();
          var paymentService = EngineContext.Current.Resolve<Nop.Services.Payments.IPaymentService>();
          int filterByCountryId = billingAddressEntity?.CountryId ?? 0;
          var activePlugins = await paymentPluginManager.LoadActivePluginsAsync(customer, resolvedStoreId, filterByCountryId);

          foreach (var pm in activePlugins) {
            bool hide = false;

            try {
              hide = await pm.HidePaymentMethodAsync(cartItems);
            } catch {
              // Plugin hide check failed — assume visible.
            }

            if (hide) {
              continue;
            }

            var systemName = pm.PluginDescriptor.SystemName ?? string.Empty;
            var friendlyName = pm.PluginDescriptor.FriendlyName ?? systemName;
            decimal fee = 0m;

            try {
              fee = await paymentService.GetAdditionalHandlingFeeAsync(cartItems, systemName);
            } catch {
              // Plugin fee calc failed — leave at 0.
            }

            paymentRows.Add((systemName, friendlyName, fee));
          }
        } catch {
          // No payment plugins active — leave list empty.
        }

        decimal? orderTotal = null;
        decimal totalDiscount = 0m;

        try {
          var totalResult = await totalCalcService.GetShoppingCartTotalAsync(cartItems);
          orderTotal = totalResult.shoppingCartTotal;
          totalDiscount = totalResult.discountAmount;

          if (totalResult.appliedDiscounts != null && totalResult.appliedDiscounts.Count > 0 && totalResult.discountAmount > 0) {
            var names = totalResult.appliedDiscounts
              .Select(d => string.IsNullOrEmpty(d.Name) ? $"discount-{d.Id}" : d.Name)
              .ToArray();

            appliedDiscountRows.Add((string.Join(", ", names), totalResult.discountAmount, "total"));
          }
        } catch {
          orderTotal = subtotalExclTax + totalTax + shippingPrice - subtotalDiscountAmount;
        }

        decimal aggregatedDiscount = subtotalDiscountAmount + totalDiscount;
        decimal totalPrice = orderTotal ?? (subtotalExclTax + totalTax + shippingPrice - subtotalDiscountAmount);

        async Task<decimal> Convert(decimal amount)
        {
          if (targetCurrency == null) {
            return amount;
          }

          return await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(amount, targetCurrency);
        }

        var shippingRates = new List<object>();

        foreach (var r in rateOptions) {
          shippingRates.Add(
            new {
              code = r.code,
              name = r.name,
              price = await Convert(r.price),
            }
          );
        }

        var paymentMethods = new List<object>();

        foreach (var p in paymentRows) {
          paymentMethods.Add(
            new {
              code = p.code,
              name = p.name,
              fee = await Convert(p.fee),
            }
          );
        }

        var discounts = new List<object>();

        foreach (var d in appliedDiscountRows) {
          discounts.Add(
            new {
              name = d.name,
              amount = await Convert(d.amount),
              type = d.type,
            }
          );
        }

        var result = new {
          subtotal_price = await Convert(subtotalExclTax),
          subtotal_discount = await Convert(subtotalDiscountAmount),
          tax_price = await Convert(totalTax),
          shipping_price = await Convert(shippingPrice),
          total_price = await Convert(totalPrice),
          total_discount = await Convert(aggregatedDiscount),
          shipping_rates = shippingRates,
          payment_methods = paymentMethods,
          discounts = discounts,
          currency = targetCurrency?.CurrencyCode ?? string.Empty,
        };

        return JsonContent(
          new ConnectorResponse<object> { Result = result }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private async Task<(Nop.Core.Domain.Common.Address? Address, ContentResult? Error)> BuildAddressFromInputAsync(
      string addressJson,
      string paramPrefix,
      ICountryService countryService,
      IStateProvinceService stateProvinceService
    )
    {
      CalcAddressInput? dto;

      try {
        dto = JsonSerializer.Deserialize<CalcAddressInput>(addressJson, _jsonOptions);
      } catch (JsonException ex) {
        return (null, ParamError($"Parameter '{paramPrefix}_address' is not valid JSON: {ex.Message}"));
      }

      if (dto == null) {
        return (null, null);
      }

      int? countryId = null;
      int? stateId = null;

      if (!string.IsNullOrWhiteSpace(dto.country_code)) {
        var allCountries = await countryService.GetAllCountriesAsync();
        var country = allCountries.FirstOrDefault(c =>
          string.Equals(c.TwoLetterIsoCode, dto.country_code, StringComparison.OrdinalIgnoreCase)
        );

        if (country != null) {
          countryId = country.Id;

          if (!string.IsNullOrWhiteSpace(dto.state)) {
            var states = (await stateProvinceService.GetStateProvincesByCountryIdAsync(country.Id)).ToList();
            StateProvince? matched = null;

            if (int.TryParse(dto.state, out var maybeId) && maybeId > 0) {
              matched = states.FirstOrDefault(s => s.Id == maybeId);
            }

            matched ??= states.FirstOrDefault(s =>
              string.Equals(s.Abbreviation, dto.state, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Name, dto.state, StringComparison.OrdinalIgnoreCase)
            );

            if (matched != null) {
              stateId = matched.Id;
            } else if (states.Any()) {
              var allowed = string.Join(", ", states.Select(s => s.Name));
              return (null, ParamError($"Parameter '{paramPrefix}_state' value '{dto.state}' is not supported. Allowed only: {allowed}"));
            }
          }
        }
      }

      var address = new Nop.Core.Domain.Common.Address {
        FirstName = dto.first_name ?? string.Empty,
        LastName = dto.last_name ?? string.Empty,
        Company = dto.company ?? string.Empty,
        Address1 = dto.address1 ?? string.Empty,
        Address2 = dto.address2 ?? string.Empty,
        City = dto.city ?? string.Empty,
        ZipPostalCode = dto.postcode ?? string.Empty,
        PhoneNumber = dto.phone ?? string.Empty,
        CountryId = countryId,
        StateProvinceId = stateId,
        CreatedOnUtc = DateTime.UtcNow,
      };

      return (address, null);
    }

    [HttpPost("orders-add")]
    public async Task<IActionResult> OrdersAdd(
        [FromQuery] string? customer_email = null,
        [FromQuery] string? customer_first_name = null,
        [FromQuery] string? customer_last_name = null,
        [FromQuery] string? customer_phone = null,
        [FromQuery] string? customer_fax = null,
        [FromQuery] string? customer_birthday = null,
        [FromQuery] string? billing_address = null,
        [FromQuery] string? shipping_address = null,
        [FromQuery] string? order_items = null,
        [FromQuery] int? order_status = null,
        [FromQuery] int? payment_status = null,
        [FromQuery] int? shipping_status = null,
        [FromQuery] string? payment_method = null,
        [FromQuery] string? shipping_method = null,
        [FromQuery] string? comment = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] string? send_notifications = null,
        [FromQuery] string? create_invoice = null,
        [FromQuery] string? invoice_admin_comment = null,
        [FromQuery] decimal? tax_price = null,
        [FromQuery] decimal? shipping_price = null,
        [FromQuery] decimal? total_price = null,
        [FromQuery] string? prices_inc_tax = null,
        [FromQuery] string? currency = null,
        [FromQuery] string? capture_transaction_id = null,
        [FromQuery] int store_id = 0
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(
          customer_email,
          customer_first_name,
          customer_last_name,
          customer_phone,
          customer_fax,
          customer_birthday,
          comment,
          admin_comment,
          invoice_admin_comment,
          billing_address,
          shipping_address,
          order_items
        );

        if (charsError != null) {
          return charsError;
        }

        DateTime? customerDateOfBirth = null;

        if (!string.IsNullOrWhiteSpace(customer_birthday)) {
          customerDateOfBirth = ParseDateFilter(customer_birthday);

          if (customerDateOfBirth == null) {
            return ParamError($"Parameter 'customer_birthday' has invalid value '{customer_birthday}'.");
          }
        }

        if (string.IsNullOrWhiteSpace(customer_email)) {
          return ParamError("Parameter 'customer_email' is required.");
        }

        if (string.IsNullOrWhiteSpace(order_items)) {
          return ParamError("Parameter 'order_items' is required.");
        }

        List<OrderItemInput>? items;

        try {
          items = JsonSerializer.Deserialize<List<OrderItemInput>>(order_items, _jsonOptions);
        } catch (JsonException ex) {
          return ParamError($"Parameter 'order_items' is not valid JSON: {ex.Message}");
        }

        if (items == null || items.Count == 0) {
          return ParamError("'order_items' must contain at least one item.");
        }

        foreach (var item in items) {
          if (item.product_id <= 0) {
            return ParamError("Item product_id must be positive.");
          }

          if (item.quantity <= 0) {
            return ParamError($"Item quantity must be > 0 (product_id={item.product_id}).");
          }

          if (item.variant_id.HasValue && item.variant_id.Value <= 0) {
            return ParamError($"Item variant_id must be positive (product_id={item.product_id}).");
          }
        }

        if (order_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Orders.OrderStatus), order_status.Value)) {
          return ParamError($"Parameter 'order_status' has invalid value '{order_status.Value}'.");
        }

        if (payment_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Payments.PaymentStatus), payment_status.Value)) {
          return ParamError($"Parameter 'payment_status' has invalid value '{payment_status.Value}'.");
        }

        if (shipping_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Shipping.ShippingStatus), shipping_status.Value)) {
          return ParamError($"Parameter 'shipping_status' has invalid value '{shipping_status.Value}'.");
        }

        OrderCalcAddressInput? billing = null;
        OrderCalcAddressInput? shipping = null;

        if (!string.IsNullOrEmpty(billing_address)) {
          try {
            billing = JsonSerializer.Deserialize<OrderCalcAddressInput>(billing_address, _jsonOptions);
          } catch (JsonException ex) {
            return ParamError($"Parameter 'billing_address' is not valid JSON: {ex.Message}");
          }
        }

        if (!string.IsNullOrEmpty(shipping_address)) {
          try {
            shipping = JsonSerializer.Deserialize<OrderCalcAddressInput>(shipping_address, _jsonOptions);
          } catch (JsonException ex) {
            return ParamError($"Parameter 'shipping_address' is not valid JSON: {ex.Message}");
          }
        }

        if (billing == null) {
          return ParamError("Parameter 'billing_address' is required.");
        }

        var resolvedStoreId = store_id;

        if (resolvedStoreId == 0) {
          var currentStore = await _storeContext.GetCurrentStoreAsync();
          resolvedStoreId = currentStore.Id;
        } else {
          var store = await _storeService.GetStoreByIdAsync(resolvedStoreId);

          if (store == null) {
            return NotFoundError($"Store with id {resolvedStoreId} not found.");
          }
        }

        Nop.Core.Domain.Directory.Currency? resolvedCurrency;

        if (!string.IsNullOrWhiteSpace(currency)) {
          resolvedCurrency = await _currencyService.GetCurrencyByCodeAsync(currency);

          if (resolvedCurrency == null) {
            return ParamError($"Currency '{currency}' is not configured in the store.");
          }
        } else {
          resolvedCurrency = await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId);

          if (resolvedCurrency == null) {
            return ErrorResponse("INTERNAL_ERROR", new InvalidOperationException("Primary store currency is not configured."));
          }
        }

        if (!CommonHelper.IsValidEmail(customer_email)) {
          return ParamError($"Parameter 'customer_email' has invalid value '{customer_email}'.");
        }

        var rawEmail = customer_email!;

        Nop.Core.Domain.Customers.Customer customer;

        await using (var emailLock = await AcquireEmailLockAsync(rawEmail)) {
          var existing = await _customerService.GetCustomerByEmailAsync(rawEmail);

          if (existing != null && !existing.Deleted) {
            customer = existing;
          } else {
            customer = new Nop.Core.Domain.Customers.Customer {
              CustomerGuid = Guid.NewGuid(),
              Email = rawEmail,
              FirstName = customer_first_name ?? string.Empty,
              LastName = customer_last_name ?? string.Empty,
              Phone = customer_phone ?? string.Empty,
              Fax = customer_fax ?? string.Empty,
              DateOfBirth = customerDateOfBirth,
              CreatedOnUtc = DateTime.UtcNow,
              LastActivityDateUtc = DateTime.UtcNow,
              Active = true,
              Deleted = false,
              IsSystemAccount = false,
              RegisteredInStoreId = resolvedStoreId,
            };
            await _customerService.InsertCustomerAsync(customer);

            var registeredRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);

            if (registeredRole != null) {
              await _customerService.AddCustomerRoleMappingAsync(
                new CustomerCustomerRoleMapping {
                  CustomerId = customer.Id,
                  CustomerRoleId = registeredRole.Id,
                }
              );
            }
          }
        }

        var addressService = EngineContext.Current.Resolve<IAddressService>();
        var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();

        var billingResult = await BuildAndInsertOrderAddressAsync(billing, customer, addressService, stateService, _customerService, "bill");

        if (billingResult.Error != null) {
          return billingResult.Error;
        }

        int billingAddressId = billingResult.AddressId!.Value;
        int? shippingAddressId = null;

        customer.BillingAddressId = billingAddressId;

        if (shipping != null && (!string.IsNullOrEmpty(shipping.address1) || !string.IsNullOrEmpty(shipping.city))) {
          var shipResult = await BuildAndInsertOrderAddressAsync(shipping, customer, addressService, stateService, _customerService, "shipp");

          if (shipResult.Error != null) {
            return shipResult.Error;
          }

          shippingAddressId = shipResult.AddressId;
          customer.ShippingAddressId = shippingAddressId;
        }

        await _customerService.UpdateCustomerAsync(customer);

        bool pricesIncTax = ParseBool(prices_inc_tax, fallback: false);

        var productService = EngineContext.Current.Resolve<IProductService>();
        var productAttrService = EngineContext.Current.Resolve<IProductAttributeService>();
        var taxService = EngineContext.Current.Resolve<Nop.Services.Tax.ITaxService>();
        var attrFormatter = EngineContext.Current.Resolve<Nop.Services.Catalog.IProductAttributeFormatter>();

        var distinctProductIds = items.Select(i => i.product_id).Distinct().ToArray();
        var loadedProducts = await productService.GetProductsByIdsAsync(distinctProductIds);
        var productsById = loadedProducts
          .Where(p => !p.Deleted)
          .ToDictionary(p => p.Id, p => p);

        var variantsById = new Dictionary<int, Nop.Core.Domain.Catalog.ProductAttributeCombination>();

        foreach (var item in items) {
          if (!item.variant_id.HasValue || item.variant_id.Value <= 0) {
            continue;
          }

          var vid = item.variant_id.Value;

          if (variantsById.ContainsKey(vid)) {
            continue;
          }

          var c = await productAttrService.GetProductAttributeCombinationByIdAsync(vid);

          if (c == null) {
            return NotFoundError($"Variant with id {vid} not found.");
          }

          if (c.ProductId != item.product_id) {
            return NotFoundError($"Variant with id {vid} does not belong to product {item.product_id}.");
          }

          variantsById[vid] = c;
        }

        var resolvedItems = new List<ResolvedOrderItem>();

        decimal subtotalExclTax = 0m;
        decimal subtotalInclTax = 0m;
        decimal totalTax = 0m;

        var productAttributeParser = EngineContext.Current.Resolve<IProductAttributeParser>();

        var pamsByProduct = new Dictionary<int, Dictionary<string, ProductAttributeMapping>>();
        var pavsByMapping = new Dictionary<int, Dictionary<string, ProductAttributeValue>>();

        var productsNeedingOptions = items
          .Where(i => (!i.variant_id.HasValue || i.variant_id.Value <= 0) && i.options != null && i.options.Count > 0)
          .Select(i => i.product_id)
          .Distinct()
          .ToArray();

        foreach (var productId in productsNeedingOptions) {
          var pams = await productAttrService.GetProductAttributeMappingsByProductIdAsync(productId);
          var pamsByName = new Dictionary<string, ProductAttributeMapping>(StringComparer.OrdinalIgnoreCase);

          foreach (var pam in pams) {
            var pa = await productAttrService.GetProductAttributeByIdAsync(pam.ProductAttributeId);

            if (pa?.Name != null && !pamsByName.ContainsKey(pa.Name)) {
              pamsByName[pa.Name] = pam;
            }

            if (!pavsByMapping.ContainsKey(pam.Id)) {
              var pavs = await productAttrService.GetProductAttributeValuesAsync(pam.Id);
              var pavsByName = new Dictionary<string, ProductAttributeValue>(StringComparer.OrdinalIgnoreCase);

              foreach (var pav in pavs) {
                if (pav.Name != null && !pavsByName.ContainsKey(pav.Name)) {
                  pavsByName[pav.Name] = pav;
                }
              }

              pavsByMapping[pam.Id] = pavsByName;
            }
          }

          pamsByProduct[productId] = pamsByName;
        }

        foreach (var item in items) {
          if (!productsById.TryGetValue(item.product_id, out var product)) {
            return NotFoundError($"Product with id {item.product_id} not found.");
          }

          decimal unitPrice = item.price ?? product.Price;
          string attrXml = string.Empty;
          string attrDesc = string.Empty;
          bool itemInclTax = item.price_includes_tax ?? pricesIncTax;

          if (item.variant_id.HasValue && item.variant_id.Value > 0) {
            if (!variantsById.TryGetValue(item.variant_id.Value, out var combo)) {
              return NotFoundError($"Variant with id {item.variant_id.Value} not found.");
            }

            attrXml = combo.AttributesXml ?? string.Empty;

            if (combo.OverriddenPrice.HasValue && !item.price.HasValue) {
              unitPrice = combo.OverriddenPrice.Value;
            }
          } else if (item.options != null && item.options.Count > 0) {
            pamsByProduct.TryGetValue(product.Id, out var pamsByName);

            foreach (var opt in item.options) {
              if (string.IsNullOrEmpty(opt.name) || string.IsNullOrEmpty(opt.value)) {
                continue;
              }

              if (pamsByName == null || !pamsByName.TryGetValue(opt.name, out var matchingPam)) {
                return ParamError($"Option '{opt.name}' is not defined for product {product.Id}.");
              }

              ProductAttributeValue? pav = null;

              if (pavsByMapping.TryGetValue(matchingPam.Id, out var pavsByName)) {
                pavsByName.TryGetValue(opt.value, out pav);
              }

              attrXml = pav != null
                ? productAttributeParser.AddProductAttribute(attrXml, matchingPam, pav.Id.ToString())
                : productAttributeParser.AddProductAttribute(attrXml, matchingPam, opt.value);
            }
          }

          if (!string.IsNullOrEmpty(attrXml)) {
            try {
              attrDesc = await attrFormatter.FormatAttributesAsync(product, attrXml);
            } catch {
              // Attribute description is cosmetic — non-fatal.
            }
          }

          if (item.parent_index.HasValue && item.parent_index.Value > 0) {
            var parentRef = string.IsNullOrEmpty(item.parent_option_name)
              ? $"[parent: item #{item.parent_index.Value}]"
              : $"[parent: item #{item.parent_index.Value} / {item.parent_option_name}]";

            attrDesc = string.IsNullOrEmpty(attrDesc) ? parentRef : $"{attrDesc} {parentRef}";
          }

          decimal taxRatePct = 0m;

          try {
            var (_, rate) = await taxService.GetProductPriceAsync(product, unitPrice, false, customer);

            taxRatePct = rate;
          } catch {
            // Default to zero tax rate if unavailable.
          }

          decimal taxRate = taxRatePct / 100m;
          decimal unitExcl;
          decimal unitIncl;

          if (itemInclTax) {
            unitIncl = unitPrice;
            unitExcl = taxRate > 0 ? unitPrice / (1 + taxRate) : unitPrice;
          } else {
            unitExcl = unitPrice;
            unitIncl = unitPrice * (1 + taxRate);
          }

          decimal lineExcl = unitExcl * item.quantity;
          decimal lineIncl = unitIncl * item.quantity;

          subtotalExclTax += lineExcl;
          subtotalInclTax += lineIncl;
          totalTax += lineIncl - lineExcl;

          resolvedItems.Add(new ResolvedOrderItem(product, item.quantity, unitExcl, unitIncl, attrXml, attrDesc));
        }

        decimal taxAmount = tax_price ?? totalTax;
        decimal shippingExcl = shipping_price ?? 0m;
        decimal shippingIncl = shipping_price ?? 0m;
        decimal totalAmount = total_price ?? (subtotalInclTax + shippingIncl);

        DateTime createdOn = DateTime.UtcNow;

        int resolvedOrderStatus = order_status ?? (int)Nop.Core.Domain.Orders.OrderStatus.Pending;
        int resolvedPaymentStatus = payment_status ?? (int)Nop.Core.Domain.Payments.PaymentStatus.Pending;
        int resolvedShippingStatus = shipping_status
          ?? (int)(shippingAddressId.HasValue
            ? Nop.Core.Domain.Shipping.ShippingStatus.NotYetShipped
            : Nop.Core.Domain.Shipping.ShippingStatus.ShippingNotRequired);
        int taxDisplayTypeId = pricesIncTax
          ? (int)Nop.Core.Domain.Tax.TaxDisplayType.IncludingTax
          : (int)Nop.Core.Domain.Tax.TaxDisplayType.ExcludingTax;

        var order = new Nop.Core.Domain.Orders.Order {
          OrderGuid = Guid.NewGuid(),
          CustomerId = customer.Id,
          StoreId = resolvedStoreId,
          BillingAddressId = billingAddressId,
          ShippingAddressId = shippingAddressId,
          PickupInStore = false,
          OrderStatusId = resolvedOrderStatus,
          PaymentStatusId = resolvedPaymentStatus,
          ShippingStatusId = resolvedShippingStatus,
          PaymentMethodSystemName = payment_method ?? string.Empty,
          ShippingMethod = shipping_method ?? string.Empty,
          ShippingRateComputationMethodSystemName = string.Empty,
          CustomerCurrencyCode = resolvedCurrency.CurrencyCode,
          CurrencyRate = resolvedCurrency.Rate,
          CustomerTaxDisplayTypeId = taxDisplayTypeId,
          VatNumber = string.Empty,
          OrderSubtotalInclTax = subtotalInclTax,
          OrderSubtotalExclTax = subtotalExclTax,
          OrderSubTotalDiscountInclTax = 0m,
          OrderSubTotalDiscountExclTax = 0m,
          OrderShippingInclTax = shippingIncl,
          OrderShippingExclTax = shippingExcl,
          PaymentMethodAdditionalFeeInclTax = 0m,
          PaymentMethodAdditionalFeeExclTax = 0m,
          TaxRates = string.Empty,
          OrderTax = taxAmount,
          OrderDiscount = 0m,
          OrderTotal = totalAmount,
          RefundedAmount = 0m,
          PaidDateUtc = resolvedPaymentStatus == (int)Nop.Core.Domain.Payments.PaymentStatus.Paid ? createdOn : (DateTime?)null,
          CustomOrderNumber = string.Empty,
          CustomerIp = string.Empty,
          AllowStoringCreditCardNumber = false,
          CardType = string.Empty,
          CardName = string.Empty,
          CardNumber = string.Empty,
          MaskedCreditCardNumber = string.Empty,
          CardCvv2 = string.Empty,
          CardExpirationMonth = string.Empty,
          CardExpirationYear = string.Empty,
          AuthorizationTransactionId = string.Empty,
          AuthorizationTransactionCode = string.Empty,
          AuthorizationTransactionResult = string.Empty,
          CaptureTransactionId = capture_transaction_id ?? string.Empty,
          CaptureTransactionResult = string.Empty,
          SubscriptionTransactionId = string.Empty,
          CheckoutAttributeDescription = string.Empty,
          CheckoutAttributesXml = string.Empty,
          CustomerLanguageId = customer.LanguageId ?? 0,
          AffiliateId = 0,
          Deleted = false,
          CreatedOnUtc = createdOn,
        };

        var orderService = EngineContext.Current.Resolve<IOrderService>();

        await orderService.InsertOrderAsync(order);

        order.CustomOrderNumber = order.Id.ToString();
        await orderService.UpdateOrderAsync(order);

        foreach (var resolved in resolvedItems) {
          var orderItem = new Nop.Core.Domain.Orders.OrderItem {
            OrderItemGuid = Guid.NewGuid(),
            OrderId = order.Id,
            ProductId = resolved.Product.Id,
            Quantity = resolved.Qty,
            UnitPriceInclTax = resolved.UnitIncl,
            UnitPriceExclTax = resolved.UnitExcl,
            PriceInclTax = resolved.UnitIncl * resolved.Qty,
            PriceExclTax = resolved.UnitExcl * resolved.Qty,
            DiscountAmountInclTax = 0m,
            DiscountAmountExclTax = 0m,
            OriginalProductCost = resolved.Product.ProductCost,
            AttributesXml = resolved.AttrXml,
            AttributeDescription = resolved.AttrDesc,
            DownloadCount = 0,
            IsDownloadActivated = false,
            LicenseDownloadId = null,
            ItemWeight = resolved.Product.Weight * resolved.Qty,
          };

          await orderService.InsertOrderItemAsync(orderItem);
        }

        if (!string.IsNullOrEmpty(comment)) {
          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = comment,
              DisplayToCustomer = true,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
        }

        if (!string.IsNullOrEmpty(admin_comment)) {
          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = admin_comment,
              DisplayToCustomer = false,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
        }

        if (ParseBool(create_invoice, fallback: false)) {
          var invoiceNote = string.IsNullOrEmpty(invoice_admin_comment)
            ? "Invoice created via API."
            : $"Invoice created via API. {invoice_admin_comment}";

          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = invoiceNote,
              DisplayToCustomer = false,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
        }

        if (ParseBool(send_notifications, fallback: false)) {
          try {
            var workflowService = EngineContext.Current.Resolve<Nop.Services.Messages.IWorkflowMessageService>();

            await workflowService.SendOrderPlacedCustomerNotificationAsync(order, customer.LanguageId ?? 0);
          } catch {
            // Notification failures are non-fatal.
          }
        }

        var resultPayload = new {
          order_id = order.Id,
          customer_id = customer.Id,
          custom_order_number = order.CustomOrderNumber,
        };

        return JsonContent(
          new ConnectorResponse<object> { Result = resultPayload }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("orders-update")]
    public async Task<IActionResult> OrdersUpdate(
        [FromQuery] int id = 0,
        [FromQuery] int? order_status = null,
        [FromQuery] int? payment_status = null,
        [FromQuery] int? shipping_status = null,
        [FromQuery] string? comment = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] string? create_invoice = null,
        [FromQuery] string? invoice_admin_comment = null,
        [FromQuery] string? send_notifications = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (id <= 0) {
          return ParamError("Parameter 'id' is required and must be positive.");
        }

        if (order_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Orders.OrderStatus), order_status.Value)) {
          return ParamError($"Parameter 'order_status' has invalid value '{order_status.Value}'.");
        }

        if (payment_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Payments.PaymentStatus), payment_status.Value)) {
          return ParamError($"Parameter 'payment_status' has invalid value '{payment_status.Value}'.");
        }

        if (shipping_status.HasValue
          && !Enum.IsDefined(typeof(Nop.Core.Domain.Shipping.ShippingStatus), shipping_status.Value)) {
          return ParamError($"Parameter 'shipping_status' has invalid value '{shipping_status.Value}'.");
        }

        var charsError = ValidateSupportedChars(comment, admin_comment, invoice_admin_comment);

        if (charsError != null) {
          return charsError;
        }

        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var order = await orderService.GetOrderByIdAsync(id);

        if (order == null || order.Deleted) {
          return NotFoundError($"Order with id {id} not found.");
        }

        bool dirty = false;

        if (order_status.HasValue && order.OrderStatusId != order_status.Value) {
          order.OrderStatusId = order_status.Value;
          dirty = true;
        }

        if (payment_status.HasValue && order.PaymentStatusId != payment_status.Value) {
          order.PaymentStatusId = payment_status.Value;

          if (payment_status.Value == (int)Nop.Core.Domain.Payments.PaymentStatus.Paid && !order.PaidDateUtc.HasValue) {
            order.PaidDateUtc = DateTime.UtcNow;
          }

          dirty = true;
        }

        if (shipping_status.HasValue && order.ShippingStatusId != shipping_status.Value) {
          order.ShippingStatusId = shipping_status.Value;
          dirty = true;
        }

        if (dirty) {
          await orderService.UpdateOrderAsync(order);
        }

        Nop.Core.Domain.Customers.Customer? customer = null;

        if (!string.IsNullOrEmpty(comment)) {
          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = comment,
              DisplayToCustomer = true,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
          dirty = true;
        }

        if (!string.IsNullOrEmpty(admin_comment)) {
          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = admin_comment,
              DisplayToCustomer = false,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
          dirty = true;
        }

        if (ParseBool(create_invoice, fallback: false)) {
          var note = string.IsNullOrEmpty(invoice_admin_comment)
            ? "Invoice created via API."
            : $"Invoice created via API. {invoice_admin_comment}";

          await orderService.InsertOrderNoteAsync(
            new Nop.Core.Domain.Orders.OrderNote {
              OrderId = order.Id,
              Note = note,
              DisplayToCustomer = false,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
          dirty = true;
        }

        if (ParseBool(send_notifications, fallback: false)) {
          try {
            customer ??= await _customerService.GetCustomerByIdAsync(order.CustomerId);
            var workflowService = EngineContext.Current.Resolve<Nop.Services.Messages.IWorkflowMessageService>();

            if (payment_status.HasValue && payment_status.Value == (int)Nop.Core.Domain.Payments.PaymentStatus.Paid) {
              await workflowService.SendOrderPaidCustomerNotificationAsync(order, customer?.LanguageId ?? 0);
            } else if (order_status.HasValue && order_status.Value == (int)Nop.Core.Domain.Orders.OrderStatus.Complete) {
              await workflowService.SendOrderCompletedCustomerNotificationAsync(order, customer?.LanguageId ?? 0);
            } else {
              await workflowService.SendOrderPlacedCustomerNotificationAsync(order, customer?.LanguageId ?? 0);
            }
          } catch {
            // Notification failures are non-fatal.
          }
        }

        var resultPayload = new {
          order_id = order.Id,
          updated_items = dirty ? 1 : 0,
        };

        return JsonContent(
          new ConnectorResponse<object> { Result = resultPayload }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private static async Task<(int? AddressId, ContentResult? Error)> BuildAndInsertOrderAddressAsync(
        OrderCalcAddressInput input,
        Nop.Core.Domain.Customers.Customer customer,
        IAddressService addressService,
        Nop.Services.Directory.IStateProvinceService stateService,
        ICustomerService customerService,
        string paramPrefix
    )
    {
      int? countryId = null;
      int? stateId = null;

      if (!string.IsNullOrEmpty(input.country_code)) {
        var country = await ResolveCountryByIsoAsync(input.country_code);

        if (country == null) {
          return (null, JsonError("NOT_FOUND", $"Country with ISO '{input.country_code}' not found.", 3));
        }

        countryId = country.Id;

        if (!string.IsNullOrEmpty(input.state)) {
          var state = await ResolveStateAsync(stateService, country.Id, input.state);

          if (state == null) {
            var states = (await stateService.GetStateProvincesByCountryIdAsync(country.Id)).ToList();

            if (states.Any()) {
              var allowed = string.Join(", ", states.Select(s => s.Name));
              var msg = $"Parameter '{paramPrefix}_state' value '{input.state}' is not supported. Allowed only: {allowed}";
              return (null, JsonError("INVALID_PARAM", msg, 4));
            }

            return (null, JsonError("NOT_FOUND", $"State '{input.state}' not found in country '{input.country_code}'.", 3));
          }

          stateId = state.Id;
        }
      }

      var addr = new Nop.Core.Domain.Common.Address {
        FirstName = input.first_name ?? customer.FirstName,
        LastName = input.last_name ?? customer.LastName,
        Email = customer.Email,
        Company = input.company,
        Address1 = input.address1,
        Address2 = input.address2,
        City = input.city,
        ZipPostalCode = input.postcode,
        PhoneNumber = input.phone,
        FaxNumber = input.fax,
        CountryId = countryId,
        StateProvinceId = stateId,
        CreatedOnUtc = DateTime.UtcNow,
      };

      await addressService.InsertAddressAsync(addr);
      await customerService.InsertCustomerAddressAsync(customer, addr);

      return (addr.Id, null);
    }

    private static ContentResult JsonError(string code, string message, int responseCode)
    {
      return new ContentResult {
        Content = JsonSerializer.Serialize(
          new ConnectorResponse<object> {
            ResponseCode = responseCode,
            Error = new ConnectorError { Code = code, Message = message },
          },
          _jsonOptions
        ),
        ContentType = "application/json",
        StatusCode = 200,
      };
    }

    #endregion

    #region Order Data Builder

    private async Task<Dictionary<string, object?>> BuildOrderDataAsync(
        Nop.Core.Domain.Orders.Order order,
        IOrderService orderService,
        ICountryService countryService,
        Nop.Services.Directory.IStateProvinceService stateService,
        IAddressService addressService,
        HashSet<string>? requestedFields = null
    )
    {
      var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

      var data = new Dictionary<string, object?>
      {
        ["id"] = order.Id,
        ["custom_order_number"] = order.CustomOrderNumber,
        ["store_id"] = order.StoreId,
        ["customer_id"] = order.CustomerId,
        ["order_guid"] = order.OrderGuid.ToString(),
        ["order_status_id"] = order.OrderStatusId,
        ["shipping_status_id"] = order.ShippingStatusId,
        ["payment_status_id"] = order.PaymentStatusId,
        ["payment_method_system_name"] = order.PaymentMethodSystemName,
        ["shipping_method"] = order.ShippingMethod,
        ["customer_currency_code"] = order.CustomerCurrencyCode,
        ["currency_rate"] = order.CurrencyRate,
        ["order_subtotal_incl_tax"] = order.OrderSubtotalInclTax,
        ["order_subtotal_excl_tax"] = order.OrderSubtotalExclTax,
        ["order_shipping_incl_tax"] = order.OrderShippingInclTax,
        ["order_shipping_excl_tax"] = order.OrderShippingExclTax,
        ["payment_method_additional_fee_incl_tax"] = order.PaymentMethodAdditionalFeeInclTax,
        ["payment_method_additional_fee_excl_tax"] = order.PaymentMethodAdditionalFeeExclTax,
        ["order_tax"] = order.OrderTax,
        ["order_discount"] = order.OrderDiscount,
        ["order_total"] = order.OrderTotal,
        ["refunded_amount"] = order.RefundedAmount,
        ["order_subtotal_discount_incl_tax"] = order.OrderSubTotalDiscountInclTax,
        ["order_subtotal_discount_excl_tax"] = order.OrderSubTotalDiscountExclTax,
        ["customer_email"] = customer?.Email,
        ["customer_first_name"] = customer?.FirstName,
        ["customer_last_name"] = customer?.LastName,
        ["customer_ip"] = order.CustomerIp,
        ["paid_date_utc"] = order.PaidDateUtc?.ToString("o"),
        ["created_on_utc"] = order.CreatedOnUtc.ToString("o"),
      };

      if (IsFieldRequested(requestedFields, "billing_address")) {
        data["billing_address"] = await BuildAddressDataAsync(order.BillingAddressId, addressService, countryService, stateService);
      }

      if (IsFieldRequested(requestedFields, "shipping_address")) {
        data["shipping_address"] = order.ShippingAddressId.HasValue
          ? await BuildAddressDataAsync(order.ShippingAddressId.Value, addressService, countryService, stateService)
          : null;
      }

      if (IsFieldRequested(requestedFields, "order_items")) {
        data["order_items"] = await BuildOrderItemsAsync(order.Id);
      }

      if (IsFieldRequested(requestedFields, "order_notes")) {
        data["order_notes"] = await BuildOrderNotesAsync(order.Id, orderService);
      }

      if (IsFieldRequested(requestedFields, "shipments")) {
        data["shipments"] = await BuildOrderShipmentsAsync(order.Id);
      }

      if (IsFieldRequested(requestedFields, "return_requests")) {
        data["return_requests"] = await BuildReturnRequestsAsync(order.Id);
      }

      return data;
    }

    #endregion

    #region Order Address Builder

    private static async Task<Dictionary<string, object?>> BuildAddressDataAsync(
        int addressId,
        IAddressService addressService,
        ICountryService countryService,
        Nop.Services.Directory.IStateProvinceService stateService
    )
    {
      var addr = await addressService.GetAddressByIdAsync(addressId);

      if (addr == null) {
        return new Dictionary<string, object?>
        {
          ["id"] = addressId,
        };
      }

      string? countryName = null;
      string? countryTwoLetterIso = null;
      string? countryThreeLetterIso = null;

      if (addr.CountryId.HasValue && addr.CountryId.Value > 0) {
        var country = await countryService.GetCountryByIdAsync(addr.CountryId.Value);

        if (country != null) {
          countryName = country.Name;
          countryTwoLetterIso = country.TwoLetterIsoCode;
          countryThreeLetterIso = country.ThreeLetterIsoCode;
        }
      }

      string? stateProvinceName = null;
      string? stateProvinceAbbreviation = null;

      if (addr.StateProvinceId.HasValue && addr.StateProvinceId.Value > 0) {
        var state = await stateService.GetStateProvinceByIdAsync(addr.StateProvinceId.Value);

        if (state != null) {
          stateProvinceName = state.Name;
          stateProvinceAbbreviation = state.Abbreviation;
        }
      }

      return new Dictionary<string, object?>
      {
        ["id"] = addr.Id,
        ["first_name"] = addr.FirstName,
        ["last_name"] = addr.LastName,
        ["email"] = addr.Email,
        ["company"] = addr.Company,
        ["city"] = addr.City,
        ["address1"] = addr.Address1,
        ["address2"] = addr.Address2,
        ["zip_postal_code"] = addr.ZipPostalCode,
        ["phone_number"] = addr.PhoneNumber,
        ["fax_number"] = addr.FaxNumber,
        ["country_id"] = addr.CountryId,
        ["country_name"] = countryName,
        ["country_two_letter_iso_code"] = countryTwoLetterIso,
        ["country_three_letter_iso_code"] = countryThreeLetterIso,
        ["state_province_id"] = addr.StateProvinceId,
        ["state_province_name"] = stateProvinceName,
        ["state_province_abbreviation"] = stateProvinceAbbreviation,
      };
    }

    #endregion

    #region Order Items Builder

    private static async Task<List<Dictionary<string, object?>>> BuildOrderItemsAsync(
        int orderId
    )
    {
      var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
      var orderItems = orderItemRepo.Table
        .Where(oi => oi.OrderId == orderId)
        .ToList();

      if (!orderItems.Any()) {
        return new List<Dictionary<string, object?>>();
      }

      var productIds = orderItems.Select(oi => oi.ProductId).Distinct().ToArray();
      var productService = EngineContext.Current.Resolve<IProductService>();
      var products = await productService.GetProductsByIdsAsync(productIds);
      var productMap = products.ToDictionary(p => p.Id);

      var productAttributeParser = EngineContext.Current.Resolve<IProductAttributeParser>();
      var result = new List<Dictionary<string, object?>>();

      foreach (var item in orderItems) {
        productMap.TryGetValue(item.ProductId, out var product);

        int? combinationId = null;

        if (product != null && !string.IsNullOrEmpty(item.AttributesXml)) {
          var combination = await productAttributeParser.FindProductAttributeCombinationAsync(product, item.AttributesXml);

          if (combination != null) {
            combinationId = combination.Id;
          }
        }

        var itemData = new Dictionary<string, object?>
        {
          ["id"] = item.Id,
          ["product_id"] = item.ProductId,
          ["product_attribute_combination_id"] = combinationId,
          ["quantity"] = item.Quantity,
          ["unit_price_incl_tax"] = item.UnitPriceInclTax,
          ["unit_price_excl_tax"] = item.UnitPriceExclTax,
          ["price_incl_tax"] = item.PriceInclTax,
          ["price_excl_tax"] = item.PriceExclTax,
          ["discount_amount_incl_tax"] = item.DiscountAmountInclTax,
          ["discount_amount_excl_tax"] = item.DiscountAmountExclTax,
          ["original_product_cost"] = item.OriginalProductCost,
          ["attribute_description"] = item.AttributeDescription,
          ["sku"] = product?.Sku,
          ["product_name"] = product?.Name,
          ["product_type_id"] = product?.ProductTypeId,
          ["vendor_id"] = product?.VendorId,
          ["is_download"] = product?.IsDownload ?? false,
          ["download_count"] = item.DownloadCount,
          ["is_gift_card"] = product?.IsGiftCard ?? false,
          ["gift_card_type_id"] = product?.GiftCardTypeId,
          ["is_rental"] = product?.IsRental ?? false,
          ["rental_start_date_utc"] = item.RentalStartDateUtc?.ToString("o"),
          ["rental_end_date_utc"] = item.RentalEndDateUtc?.ToString("o"),
          ["item_weight"] = item.ItemWeight,
        };

        result.Add(itemData);
      }

      return result;
    }

    #endregion

    #region Order Notes Builder

    private static async Task<List<Dictionary<string, object?>>> BuildOrderNotesAsync(
        int orderId,
        IOrderService orderService
    )
    {
      var notes = await orderService.GetOrderNotesByOrderIdAsync(orderId);
      var result = new List<Dictionary<string, object?>>();

      foreach (var note in notes) {
        result.Add(
          new Dictionary<string, object?> {
            ["id"] = note.Id,
            ["note"] = note.Note,
            ["display_to_customer"] = note.DisplayToCustomer,
            ["created_on_utc"] = note.CreatedOnUtc.ToString("o"),
          }
        );
      }

      return result;
    }

    #endregion

    #region Order Shipments Builder

    private static async Task<List<Dictionary<string, object?>>> BuildOrderShipmentsAsync(
        int orderId
    )
    {
      var shipmentService = EngineContext.Current.Resolve<IShipmentService>();
      var shipments = await shipmentService.GetShipmentsByOrderIdAsync(orderId);
      var result = new List<Dictionary<string, object?>>();

      foreach (var shipment in shipments) {
        result.Add(
          new Dictionary<string, object?> {
            ["id"] = shipment.Id,
            ["tracking_number"] = shipment.TrackingNumber,
            ["total_weight"] = shipment.TotalWeight,
            ["shipped_date_utc"] = shipment.ShippedDateUtc?.ToString("o"),
            ["delivery_date_utc"] = shipment.DeliveryDateUtc?.ToString("o"),
          }
        );
      }

      return result;
    }

    #endregion

    #region Order Return Requests Builder

    private static Task<List<Dictionary<string, object?>>> BuildReturnRequestsAsync(
        int orderId
    )
    {
      var orderItemRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.OrderItem>>();
      var returnRequestRepo = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Orders.ReturnRequest>>();

      var orderItemIds = orderItemRepo.Table
        .Where(oi => oi.OrderId == orderId)
        .Select(oi => oi.Id)
        .ToList();

      var returnRequests = returnRequestRepo.Table
        .Where(rr => orderItemIds.Contains(rr.OrderItemId))
        .OrderByDescending(rr => rr.CreatedOnUtc)
        .ToList();

      var result = new List<Dictionary<string, object?>>();

      foreach (var rr in returnRequests) {
        result.Add(
          new Dictionary<string, object?> {
            ["id"] = rr.Id,
            ["order_item_id"] = rr.OrderItemId,
            ["quantity"] = rr.Quantity,
            ["reason_for_return"] = rr.ReasonForReturn,
            ["requested_action"] = rr.RequestedAction,
            ["customer_comments"] = rr.CustomerComments,
            ["staff_notes"] = rr.StaffNotes,
            ["return_request_status_id"] = rr.ReturnRequestStatusId,
            ["created_on_utc"] = rr.CreatedOnUtc.ToString("o"),
            ["updated_on_utc"] = rr.UpdatedOnUtc.ToString("o"),
          }
        );
      }

      return Task.FromResult(result);
    }

    #endregion
  }
}
