using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Shipping;
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
    #region Products

    [HttpPost("products-list")]
    public async Task<IActionResult> ProductsList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] int? category_id = null,
        [FromQuery] string? product_ids = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? product_type = null,
        [FromQuery] bool? published = null,
        [FromQuery] bool? avail_sale = null,
        [FromQuery] string? find_value = null,
        [FromQuery] string? find_where = null,
        [FromQuery] int? manufacturer_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] int vendor_id = 0,
        [FromQuery] int customer_role_id = 0,
        [FromQuery] string? fields = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var createdFrom = ParseDateFilter(created_from);
        var createdTo = ParseDateFilter(created_to, isUpperBound: true);
        var modifiedFrom = ParseDateFilter(modified_from);
        var modifiedTo = ParseDateFilter(modified_to, isUpperBound: true);
        var requestedFields = ParseFields(fields);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);
        var productService = EngineContext.Current.Resolve<IProductService>();
        var categoryIds = category_id.HasValue && category_id.Value > 0 ? new List<int> { category_id.Value } : null;
        var parsedProductType = ParseProductType(product_type);

        var manufacturerIds = manufacturer_id.HasValue && manufacturer_id.Value > 0
          ? new List<int> { manufacturer_id.Value } : null;
        var searchKeywords = !string.IsNullOrEmpty(find_value) ? find_value : sku;
        var searchInDescriptions = !string.IsNullOrEmpty(find_value)
          && (string.IsNullOrEmpty(find_where) || find_where.Contains("description", StringComparison.OrdinalIgnoreCase));
        var searchInSku = !string.IsNullOrEmpty(sku)
          || (!string.IsNullOrEmpty(find_value) && !string.IsNullOrEmpty(find_where)
            && find_where.Contains("sku", StringComparison.OrdinalIgnoreCase));

        IList<Product> filteredProducts;
        int totalCount;

        if (!string.IsNullOrEmpty(product_ids)) {
          var ids = ParseProductIds(product_ids);
          var byIds = await productService.GetProductsByIdsAsync(ids);
          var filtered = ApplyDateFilter(byIds, createdFrom, createdTo, modifiedFrom, modifiedTo);

          if (avail_sale.HasValue) {
            filtered = filtered.Where(p => IsAvailableForSale(p) == avail_sale.Value).ToList();
          }

          totalCount = filtered.Count;
          filteredProducts = filtered.Skip(pageIndex * pageSize).Take(pageSize).ToList();
        } else if (HasDateFilter(createdFrom, createdTo, modifiedFrom, modifiedTo)
          || published == false
          || avail_sale.HasValue
          || store_id > 0
          || vendor_id > 0
          || customer_role_id > 0
          || !string.IsNullOrEmpty(find_where)) {
          var langId = await ResolveLanguageIdAsync(language_id);

          var query = BuildFilteredProductQuery(
            createdFrom,
            createdTo,
            modifiedFrom,
            modifiedTo,
            categoryIds,
            parsedProductType,
            sku,
            published,
            manufacturer_id,
            find_value,
            store_id,
            vendor_id,
            customer_role_id,
            avail_sale,
            find_where,
            langId
          );

          totalCount = query.Count();
          filteredProducts = query
            .OrderBy(p => p.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToList();
        } else {
          var products = await productService.SearchProductsAsync(
            pageIndex: pageIndex,
            pageSize: pageSize,
            categoryIds: categoryIds,
            productType: parsedProductType,
            keywords: searchKeywords,
            searchDescriptions: searchInDescriptions,
            searchSku: searchInSku,
            manufacturerIds: manufacturerIds,
            vendorId: vendor_id,
            overridePublished: published,
            visibleIndividuallyOnly: false);

          totalCount = products.TotalCount;
          filteredProducts = products.OrderBy(p => p.Id).ToList();
        }

        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var ctx = await BuildProductContextAsync(currentStore, language_id);
        var pids = filteredProducts.Select(p => p.Id).ToArray();
        var productsArray = filteredProducts.ToArray();
        var categoryMap = await BatchLoadCategoriesAsync(pids);
        var manufacturerMap = await BatchLoadManufacturersAsync(pids);
        var storeMap = await BatchLoadStoreIdsAsync(pids, productsArray);

        Dictionary<int, string>? manufacturerNameMap = null;

        if (IsFieldRequested(requestedFields, "manufacturer_names")) {
          var uniqueManufacturerIds = manufacturerMap.Values.SelectMany(x => x).Distinct().ToArray();
          manufacturerNameMap = await BatchLoadManufacturerNamesAsync(uniqueManufacturerIds);
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var product in filteredProducts) {
          result.Add(
            await BuildProductDataAsync(
              product,
              productService,
              currentStore,
              requestedFields,
              ctx,
              categoryMap.GetValueOrDefault(product.Id),
              manufacturerMap.GetValueOrDefault(product.Id),
              storeMap.GetValueOrDefault(product.Id),
              manufacturerNameMap
            )
          );
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {products = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-count")]
    public async Task<IActionResult> ProductsCount(
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] int? category_id = null,
        [FromQuery] string? product_ids = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? product_type = null,
        [FromQuery] bool? published = null,
        [FromQuery] bool? avail_sale = null,
        [FromQuery] string? find_value = null,
        [FromQuery] string? find_where = null,
        [FromQuery] int? manufacturer_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] int vendor_id = 0,
        [FromQuery] int customer_role_id = 0,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var createdFrom = ParseDateFilter(created_from);
        var createdTo = ParseDateFilter(created_to, isUpperBound: true);
        var modifiedFrom = ParseDateFilter(modified_from);
        var modifiedTo = ParseDateFilter(modified_to, isUpperBound: true);
        var productService = EngineContext.Current.Resolve<IProductService>();
        var categoryIds = category_id.HasValue && category_id.Value > 0 ? new List<int> { category_id.Value } : null;
        var parsedProductType = ParseProductType(product_type);

        var manufacturerIds = manufacturer_id.HasValue && manufacturer_id.Value > 0
          ? new List<int> { manufacturer_id.Value } : null;
        var searchKeywords = !string.IsNullOrEmpty(find_value) ? find_value : sku;
        var searchInDescriptions = !string.IsNullOrEmpty(find_value)
          && (string.IsNullOrEmpty(find_where) || find_where.Contains("description", StringComparison.OrdinalIgnoreCase));
        var searchInSku = !string.IsNullOrEmpty(sku)
          || (!string.IsNullOrEmpty(find_value) && !string.IsNullOrEmpty(find_where)
            && find_where.Contains("sku", StringComparison.OrdinalIgnoreCase));

        int totalCount;

        if (!string.IsNullOrEmpty(product_ids)) {
          var ids = ParseProductIds(product_ids);
          var byIds = await productService.GetProductsByIdsAsync(ids);
          var filtered = ApplyDateFilter(byIds, createdFrom, createdTo, modifiedFrom, modifiedTo);

          if (avail_sale.HasValue) {
            filtered = filtered.Where(p => IsAvailableForSale(p) == avail_sale.Value).ToList();
          }

          totalCount = filtered.Count;
        } else if (HasDateFilter(createdFrom, createdTo, modifiedFrom, modifiedTo)
          || published == false
          || avail_sale.HasValue
          || store_id > 0
          || vendor_id > 0
          || customer_role_id > 0
          || !string.IsNullOrEmpty(find_where)) {
          var countLangId = await ResolveLanguageIdAsync(language_id);

          totalCount = BuildFilteredProductQuery(
            createdFrom,
            createdTo,
            modifiedFrom,
            modifiedTo,
            categoryIds,
            parsedProductType,
            sku,
            published,
            manufacturer_id,
            find_value,
            store_id,
            vendor_id,
            customer_role_id,
            avail_sale,
            find_where,
            countLangId
          ).Count();
        } else {
          var products = await productService.SearchProductsAsync(
            pageIndex: 0,
            pageSize: 1,
            categoryIds: categoryIds,
            productType: parsedProductType,
            keywords: searchKeywords,
            searchDescriptions: searchInDescriptions,
            searchSku: searchInSku,
            manufacturerIds: manufacturerIds,
            vendorId: vendor_id,
            overridePublished: published,
            visibleIndividuallyOnly: false);

          totalCount = products.TotalCount;
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-info")]
    public async Task<IActionResult> ProductsInfo(
        [FromQuery] string? id = null,
        [FromQuery] string? fields = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var productId)) {
          return NotFoundError($"Product with id {id} not found.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(productId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {id} not found.");
        }

        var requestedFields = ParseFields(fields);
        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var ctx = await BuildProductContextAsync(currentStore, language_id);

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
        var cats = (await categoryService.GetProductCategoriesByProductIdAsync(product.Id, true))
          .Select(pc => pc.CategoryId).ToArray();
        var mans = (await manufacturerService.GetProductManufacturersByProductIdAsync(product.Id, true))
          .Select(pm => pm.ManufacturerId).ToArray();
        var storeMap = await BatchLoadStoreIdsAsync(
          new[] { product.Id }, new[] { product });

        Dictionary<int, string>? manufacturerNameMap = null;

        if (IsFieldRequested(requestedFields, "manufacturer_names") && mans.Length > 0) {
          manufacturerNameMap = await BatchLoadManufacturerNamesAsync(mans);
        }

        var data = await BuildProductDataAsync(
          product,
          productService,
          currentStore,
          requestedFields,
          ctx,
          cats,
          mans,
          storeMap.GetValueOrDefault(product.Id),
          manufacturerNameMap
        );

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Product Child Items (Combinations)

    [HttpPost("products-child-list")]
    public async Task<IActionResult> ProductsChildList(
        [FromQuery] string? product_ids = null,
        [FromQuery] string? id = null,
        [FromQuery] int start = 0,
        [FromQuery] int count = 250,
        [FromQuery] string? fields = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var parentIds = ParseParentProductIds(product_ids);

        if (parentIds.Count == 0) {
          return ParamError("Parameter 'product_ids' is required.");
        }

        int? combinationFilterId = null;

        if (!string.IsNullOrEmpty(id)) {
          if (!TryParsePositiveInt(id, out var parsedId)) {
            return ParamError("Parameter 'id' is not valid. The required type of parameter is positive integer.");
          }

          combinationFilterId = parsedId;
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var requestedFields = ParseFields(fields);
        var allCombinations = new List<Dictionary<string, object?>>();
        var totalCount = 0;

        foreach (var pid in parentIds) {
          var parent = await productService.GetProductByIdAsync(pid);

          if (parent == null || parent.Deleted) {
            continue;
          }

          var combinations = await productAttributeService.GetAllProductAttributeCombinationsAsync(pid);

          if (combinationFilterId.HasValue) {
            combinations = combinations.Where(c => c.Id == combinationFilterId.Value).ToList();
          }

          totalCount += combinations.Count;

          foreach (var combination in combinations) {
            allCombinations.Add(await BuildCombinationDataAsync(
              combination, parent, requestedFields));
          }
        }

        var safeCount = Math.Max(1, Math.Min(count, 250));
        var safeStart = Math.Max(0, start);
        var slice = allCombinations.Skip(safeStart).Take(safeCount).ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {combinations = slice, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-child-info")]
    public async Task<IActionResult> ProductsChildInfo(
        [FromQuery] string? product_id = null,
        [FromQuery] string? id = null,
        [FromQuery] string? fields = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var parsedProductId)
          || !TryParsePositiveInt(id, out var parsedId)) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var parent = await productService.GetProductByIdAsync(parsedProductId);

        if (parent == null || parent.Deleted) {
          return NotFoundError($"Product with id {product_id} not found.");
        }

        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var combination = await productAttributeService.GetProductAttributeCombinationByIdAsync(parsedId);

        if (combination == null || combination.ProductId != parsedProductId) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        var requestedFields = ParseFields(fields);
        var data = await BuildCombinationDataAsync(
          combination, parent, requestedFields);

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Product Data Builders

    private async Task<ProductBuildContext> BuildProductContextAsync(
        Nop.Core.Domain.Stores.Store currentStore,
        string? languageCode
    )
    {
      var measureSettings = await _settingService.LoadSettingAsync<MeasureSettings>(currentStore.Id);
      var measureService = EngineContext.Current.Resolve<IMeasureService>();
      var weightUnit = await measureService.GetMeasureWeightByIdAsync(measureSettings.BaseWeightId);
      var dimensionUnit = await measureService.GetMeasureDimensionByIdAsync(measureSettings.BaseDimensionId);

      return new ProductBuildContext
      {
        LanguageId = await ResolveLanguageIdAsync(languageCode),
        LocalizationService = EngineContext.Current.Resolve<ILocalizationService>(),
        UrlRecordService = EngineContext.Current.Resolve<IUrlRecordService>(),
        DiscountService = EngineContext.Current.Resolve<Nop.Services.Discounts.IDiscountService>(),
        WeightUnit = weightUnit?.SystemKeyword,
        DimensionUnit = dimensionUnit?.SystemKeyword,
      };
    }

    private async Task<Dictionary<string, object?>> BuildProductDataAsync(
        Product product,
        IProductService productService,
        Nop.Core.Domain.Stores.Store currentStore,
        HashSet<string>? requestedFields,
        ProductBuildContext ctx,
        int[]? categoryIds = null,
        int[]? manufacturerIds = null,
        int[]? storeIds = null,
        Dictionary<int, string>? manufacturerNameMap = null
    )
    {
      var langId = ctx.LanguageId;
      var seName = await ctx.UrlRecordService.GetSeNameAsync(product, langId);

      var data = new Dictionary<string, object?>
      {
        ["id"] = product.Id,
        ["name"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.Name, langId),
        ["sku"] = product.Sku,
        ["short_description"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.ShortDescription, langId),
        ["full_description"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.FullDescription, langId),
        ["price"] = product.Price,
        ["old_price"] = product.OldPrice,
        ["cost_price"] = product.ProductCost,
        ["special_price"] = (decimal?)null,
        ["special_price_start"] = (string?)null,
        ["special_price_end"] = (string?)null,
        ["product_type_id"] = product.ProductTypeId,
        ["weight"] = product.Weight,
        ["length"] = product.Length,
        ["width"] = product.Width,
        ["height"] = product.Height,
        ["stock_quantity"] = product.StockQuantity,
        ["manage_inventory_method_id"] = product.ManageInventoryMethodId,
        ["low_stock_threshold"] = product.MinStockQuantity,
        ["min_order_quantity"] = product.OrderMinimumQuantity,
        ["max_order_quantity"] = product.OrderMaximumQuantity,
        ["backorder_mode_id"] = product.BackorderModeId,
        ["allow_back_in_stock_subscriptions"] = product.AllowBackInStockSubscriptions,
        ["published"] = product.Published,
        ["deleted"] = product.Deleted,
        ["downloadable"] = product.IsDownload,
        ["is_virtual"] = !product.IsShipEnabled,
        ["is_free_shipping"] = product.IsFreeShipping,
        ["is_gift_card"] = product.IsGiftCard,
        ["visible_individually"] = product.VisibleIndividually,
        ["gtin"] = product.Gtin,
        ["available_start_date"] = product.AvailableStartDateTimeUtc?.ToString("o"),
        ["available_end_date"] = product.AvailableEndDateTimeUtc?.ToString("o"),
        ["mpn"] = product.ManufacturerPartNumber,
        ["tax_category_id"] = product.TaxCategoryId,
        ["vendor_id"] = product.VendorId > 0 ? product.VendorId : (int?)null,
        ["created_on_utc"] = product.CreatedOnUtc.ToString("o"),
        ["updated_on_utc"] = product.UpdatedOnUtc.ToString("o"),
        ["meta_title"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.MetaTitle, langId),
        ["meta_description"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.MetaDescription, langId),
        ["meta_keywords"] = await ctx.LocalizationService.GetLocalizedAsync(product, p => p.MetaKeywords, langId),
        ["se_name"] = seName,
        ["url"] = $"{currentStore.Url.TrimEnd('/')}/{seName}",
      };

      await BuildSpecialPriceAsync(data, product, ctx);

      if (ctx.WeightUnit != null) {
        data["weight_unit"] = ctx.WeightUnit;
      }

      if (ctx.DimensionUnit != null) {
        data["dimension_unit"] = ctx.DimensionUnit;
      }

      data["categories_ids"] = categoryIds ?? Array.Empty<int>();
      data["manufacturer_ids"] = manufacturerIds ?? Array.Empty<int>();

      if (IsFieldRequested(requestedFields, "manufacturer_names") && manufacturerNameMap != null && manufacturerIds != null) {
        data["manufacturer_names"] = manufacturerIds
          .Select(id => manufacturerNameMap.TryGetValue(id, out var name) ? name : null)
          .Where(n => n != null)
          .ToArray();
      }

      data["store_ids"] = storeIds ?? Array.Empty<int>();
      data["warehouse_id"] = product.WarehouseId > 0 ? product.WarehouseId : (int?)null;

      if (IsFieldRequested(requestedFields, "related_products_ids")) {
        var relatedProducts = await productService.GetRelatedProductsByProductId1Async(product.Id, true);
        data["related_products_ids"] = relatedProducts.Select(rp => rp.ProductId2).ToArray();
      }

      if (IsFieldRequested(requestedFields, "cross_sell_products_ids")) {
        var crossSellProducts = await productService.GetCrossSellProductsByProductId1Async(product.Id, true);
        data["cross_sell_products_ids"] = crossSellProducts.Select(cs => cs.ProductId2).ToArray();
      }

      var combinationRepo = EngineContext.Current.Resolve<IRepository<ProductAttributeCombination>>();

      data["has_combinations"] = combinationRepo.Table.Any(c => c.ProductId == product.Id);

      if (IsFieldRequested(requestedFields, "warehouse_inventory")
        && product.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock) {
        var warehouseRepo = EngineContext.Current.Resolve<IRepository<Warehouse>>();
        var allWarehouseIds = warehouseRepo.Table.Select(w => w.Id).ToList();
        var plannedMap = GetPlannedQuantityByWarehouse(product.Id);
        var inventoryList = new List<Dictionary<string, object?>>();

        if (product.UseMultipleWarehouses) {
          var warehouseInventoryRepo = EngineContext.Current.Resolve<IRepository<ProductWarehouseInventory>>();
          var recordsByWarehouse = warehouseInventoryRepo.Table
            .Where(pwi => pwi.ProductId == product.Id)
            .ToList()
            .ToDictionary(pwi => pwi.WarehouseId);

          foreach (var warehouseId in allWarehouseIds) {
            if (recordsByWarehouse.TryGetValue(warehouseId, out var record)) {
              inventoryList.Add(new Dictionary<string, object?>
              {
                ["warehouse_id"] = record.WarehouseId,
                ["stock_quantity"] = record.StockQuantity,
                ["reserved_quantity"] = record.ReservedQuantity,
                ["planned_quantity"] = plannedMap.GetValueOrDefault(record.WarehouseId, 0),
              });
            } else {
              inventoryList.Add(new Dictionary<string, object?>
              {
                ["warehouse_id"] = warehouseId,
                ["stock_quantity"] = 0,
                ["reserved_quantity"] = 0,
                ["planned_quantity"] = plannedMap.GetValueOrDefault(warehouseId, 0),
              });
            }
          }
        } else {
          inventoryList.Add(new Dictionary<string, object?>
          {
            ["warehouse_id"] = 0,
            ["stock_quantity"] = product.WarehouseId == 0 ? product.StockQuantity : 0,
            ["reserved_quantity"] = 0,
            ["planned_quantity"] = plannedMap.GetValueOrDefault(0, 0),
          });

          foreach (var warehouseId in allWarehouseIds) {
            if (warehouseId == product.WarehouseId) {
              continue;
            }

            inventoryList.Add(new Dictionary<string, object?>
            {
              ["warehouse_id"] = warehouseId,
              ["stock_quantity"] = 0,
              ["reserved_quantity"] = 0,
              ["planned_quantity"] = plannedMap.GetValueOrDefault(warehouseId, 0),
            });
          }

          if (product.WarehouseId > 0) {
            inventoryList.Add(new Dictionary<string, object?>
            {
              ["warehouse_id"] = product.WarehouseId,
              ["stock_quantity"] = product.StockQuantity,
              ["reserved_quantity"] = 0,
              ["planned_quantity"] = plannedMap.GetValueOrDefault(product.WarehouseId, 0),
            });
          }
        }

        data["warehouse_inventory"] = inventoryList;
      }

      if (IsFieldRequested(requestedFields, "images")) {
        data["images"] = await BuildProductImagesAsync(product);
      }

      if (IsFieldRequested(requestedFields, "attributes")) {
        await BuildProductAttributesAsync(data, product, ctx);
        await BuildProductSpecificationAttributesAsync(data, product, ctx);
      }

      if (IsFieldRequested(requestedFields, "tier_prices")) {
        var tierPrices = await productService.GetTierPricesByProductAsync(product.Id);

        data["tier_prices"] = tierPrices.Select(tp => new Dictionary<string, object?>
        {
          ["id"] = tp.Id,
          ["quantity"] = tp.Quantity,
          ["price"] = tp.Price,
          ["customer_role_id"] = tp.CustomerRoleId,
          ["store_id"] = tp.StoreId,
        }).ToList();
      }

      if (IsFieldRequested(requestedFields, "tags")) {
        data["tags"] = await BuildProductTagsAsync(product, ctx);
      }

      return data;
    }

    private static async Task BuildSpecialPriceAsync(
        Dictionary<string, object?> data,
        Product product,
        ProductBuildContext ctx
    )
    {
      var appliedDiscounts = await ctx.DiscountService.GetAppliedDiscountsAsync(product);

      if (!appliedDiscounts.Any()) {
        return;
      }

      var bestDiscount = appliedDiscounts
        .Where(d => d.IsActive && !d.RequiresCouponCode && d.DiscountAmount > 0)
        .OrderByDescending(d => d.DiscountAmount)
        .FirstOrDefault();

      if (bestDiscount != null) {
        data["special_price"] = Math.Max(0m, product.Price - bestDiscount.DiscountAmount);
        data["special_price_start"] = bestDiscount.StartDateUtc?.ToString("o");
        data["special_price_end"] = bestDiscount.EndDateUtc?.ToString("o");
      }
    }

    private static async Task<List<object>> BuildProductImagesAsync(Product product)
    {
      var pictureService = EngineContext.Current.Resolve<IPictureService>();
      var productPictures = await pictureService.GetPicturesByProductIdAsync(product.Id);
      var imageList = new List<object>();
      var position = 0;

      foreach (var picture in productPictures) {
        var (url, _) = await pictureService.GetPictureUrlAsync(picture);

        imageList.Add(new Dictionary<string, object?>
        {
          ["id"] = picture.Id,
          ["url"] = url,
          ["position"] = position++,
        });
      }

      return imageList;
    }

    private static async Task BuildProductAttributesAsync(
        Dictionary<string, object?> data,
        Product product,
        ProductBuildContext ctx
    )
    {
      var langId = ctx.LanguageId;
      var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
      var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(product.Id);
      var attrList = new List<object>();

      foreach (var mapping in mappings) {
        var values = await productAttributeService.GetProductAttributeValuesAsync(mapping.Id);
        var attr = await productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);

        var localizedAttrName = attr != null
          ? await ctx.LocalizationService.GetLocalizedAsync(attr, a => a.Name, langId)
          : string.Empty;

        var valuesList = new List<Dictionary<string, object?>>();

        foreach (var v in values) {
          valuesList.Add(new Dictionary<string, object?>
          {
            ["id"] = v.Id,
            ["name"] = await ctx.LocalizationService.GetLocalizedAsync(v, val => val.Name, langId),
            ["price_adjustment"] = v.PriceAdjustment,
            ["price_adjustment_use_percentage"] = v.PriceAdjustmentUsePercentage,
            ["weight_adjustment"] = v.WeightAdjustment,
          });
        }

        attrList.Add(new Dictionary<string, object?>
        {
          ["id"] = mapping.Id,
          ["name"] = localizedAttrName,
          ["values"] = valuesList,
        });
      }

      data["attributes"] = attrList;

      var combinations = await productAttributeService.GetAllProductAttributeCombinationsAsync(product.Id);
      var combList = new List<object>();

      foreach (var combo in combinations) {
        combList.Add(new Dictionary<string, object?>
        {
          ["id"] = combo.Id,
          ["sku"] = combo.Sku,
          ["stock_quantity"] = combo.StockQuantity,
          ["allow_out_of_stock_orders"] = combo.AllowOutOfStockOrders,
          ["overridden_price"] = combo.OverriddenPrice,
          ["gtin"] = combo.Gtin,
          ["manufacturer_part_number"] = combo.ManufacturerPartNumber,
          ["attribute_values"] = await ParseCombinationAttributeValuesAsync(combo, productAttributeService),
        });
      }

      data["attribute_combinations"] = combList;
    }

    private static async Task BuildProductSpecificationAttributesAsync(
        Dictionary<string, object?> data,
        Product product,
        ProductBuildContext ctx
    )
    {
      var langId = ctx.LanguageId;
      var specAttrService = EngineContext.Current.Resolve<ISpecificationAttributeService>();
      var mappings = await specAttrService.GetProductSpecificationAttributesAsync(product.Id);
      var grouped = new Dictionary<int, Dictionary<string, object?>>();

      foreach (var mapping in mappings) {
        var option = await specAttrService.GetSpecificationAttributeOptionByIdAsync(
          mapping.SpecificationAttributeOptionId);

        if (option == null) {
          continue;
        }

        var specAttr = await specAttrService.GetSpecificationAttributeByIdAsync(
          option.SpecificationAttributeId);

        if (specAttr == null) {
          continue;
        }

        if (!grouped.ContainsKey(specAttr.Id)) {
          var attrName = await ctx.LocalizationService.GetLocalizedAsync(specAttr, a => a.Name, langId);

          grouped[specAttr.Id] = new Dictionary<string, object?>
          {
            ["id"] = specAttr.Id,
            ["name"] = attrName,
            ["values"] = new List<Dictionary<string, object?>>(),
          };
        }

        var optionName = await ctx.LocalizationService.GetLocalizedAsync(option, o => o.Name, langId);
        var valuesList = (List<Dictionary<string, object?>>)grouped[specAttr.Id]["values"]!;

        valuesList.Add(new Dictionary<string, object?>
        {
          ["id"] = option.Id,
          ["name"] = optionName,
        });
      }

      data["product_attributes"] = grouped.Values.ToList();
    }

    private static async Task<List<string>> BuildProductTagsAsync(
        Product product,
        ProductBuildContext ctx
    )
    {
      var productTagService = EngineContext.Current.Resolve<IProductTagService>();
      var tags = await productTagService.GetAllProductTagsByProductIdAsync(product.Id);
      var tagNames = new List<string>();

      foreach (var tag in tags) {
        tagNames.Add(await ctx.LocalizationService.GetLocalizedAsync(tag, t => t.Name, ctx.LanguageId));
      }

      return tagNames;
    }

    #endregion

    #region Combination Data Builder

    private static async Task<Dictionary<string, object?>> BuildCombinationDataAsync(
        ProductAttributeCombination combination,
        Product parent,
        HashSet<string>? requestedFields = null
    )
    {
      var data = new Dictionary<string, object?>
      {
        ["id"] = combination.Id,
        ["product_id"] = parent.Id,
        ["sku"] = !string.IsNullOrEmpty(combination.Sku) ? combination.Sku : parent.Sku,
        ["gtin"] = combination.Gtin,
        ["manufacturer_part_number"] = combination.ManufacturerPartNumber,
        ["stock_quantity"] = combination.StockQuantity,
        ["allow_out_of_stock_orders"] = combination.AllowOutOfStockOrders,
        ["overridden_price"] = combination.OverriddenPrice,
        ["parent_price"] = parent.Price,
        ["parent_old_price"] = parent.OldPrice,
        ["parent_weight"] = parent.Weight,
        ["parent_is_virtual"] = !parent.IsShipEnabled,
        ["parent_created_on_utc"] = parent.CreatedOnUtc.ToString("o"),
        ["parent_updated_on_utc"] = parent.UpdatedOnUtc.ToString("o"),
      };

      var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
      data["attribute_values"] = await ParseCombinationAttributeValuesAsync(combination, productAttributeService);

      if (IsFieldRequested(requestedFields, "images")) {
        data["images"] = await BuildCombinationImagesAsync(combination, productAttributeService);
      }

      return data;
    }

    private static async Task<List<object>> BuildCombinationImagesAsync(
        ProductAttributeCombination combination,
        IProductAttributeService productAttributeService
    )
    {
      var pictureService = EngineContext.Current.Resolve<IPictureService>();
      var combinationPictures = await productAttributeService
        .GetProductAttributeCombinationPicturesAsync(combination.Id);
      var imageList = new List<object>();
      var position = 0;

      foreach (var picMapping in combinationPictures) {
        var picture = await pictureService.GetPictureByIdAsync(picMapping.PictureId);

        if (picture == null) {
          continue;
        }

        var (url, _) = await pictureService.GetPictureUrlAsync(picture);

        imageList.Add(new Dictionary<string, object?>
        {
          ["id"] = picture.Id,
          ["url"] = url,
          ["position"] = position++,
        });
      }

      return imageList;
    }

    private static async Task<List<Dictionary<string, object?>>> ParseCombinationAttributeValuesAsync(
        ProductAttributeCombination combination,
        IProductAttributeService productAttributeService
    )
    {
      var result = new List<Dictionary<string, object?>>();

      if (string.IsNullOrEmpty(combination.AttributesXml)) {
        return result;
      }

      var mappings = await productAttributeService
        .GetProductAttributeMappingsByProductIdAsync(combination.ProductId);

      var attributesById = new Dictionary<int, ProductAttribute>();

      foreach (var attributeId in mappings.Select(m => m.ProductAttributeId).Distinct()) {
        var attr = await productAttributeService.GetProductAttributeByIdAsync(attributeId);

        if (attr != null) {
          attributesById[attributeId] = attr;
        }
      }

      foreach (var mapping in mappings) {
        attributesById.TryGetValue(mapping.ProductAttributeId, out var productAttribute);
        var values = await productAttributeService.GetProductAttributeValuesAsync(mapping.Id);

        foreach (var value in values) {
          if (combination.AttributesXml.Contains(
            $"<Value>{value.Id}</Value>", StringComparison.OrdinalIgnoreCase)) {
            result.Add(new Dictionary<string, object?>
            {
              ["attribute_id"] = mapping.Id,
              ["attribute_name"] = productAttribute?.Name ?? string.Empty,
              ["value_id"] = value.Id,
              ["value_name"] = value.Name,
            });
          }
        }
      }

      return result;
    }

    #endregion

    #region Batch Loaders

    private static Task<Dictionary<int, int[]>> BatchLoadCategoriesAsync(int[] productIds)
    {
      var repository = EngineContext.Current.Resolve<IRepository<ProductCategory>>();
      var mappings = repository.Table
        .Where(pc => productIds.Contains(pc.ProductId))
        .Select(pc => new { pc.ProductId, pc.CategoryId })
        .ToList();

      var result = mappings
        .GroupBy(pc => pc.ProductId)
        .ToDictionary(g => g.Key, g => g.Select(pc => pc.CategoryId).ToArray());

      foreach (var pid in productIds) {
        result.TryAdd(pid, Array.Empty<int>());
      }

      return Task.FromResult(result);
    }

    private static Task<Dictionary<int, int[]>> BatchLoadManufacturersAsync(int[] productIds)
    {
      var repository = EngineContext.Current.Resolve<IRepository<ProductManufacturer>>();
      var mappings = repository.Table
        .Where(pm => productIds.Contains(pm.ProductId))
        .Select(pm => new { pm.ProductId, pm.ManufacturerId })
        .ToList();

      var result = mappings
        .GroupBy(pm => pm.ProductId)
        .ToDictionary(g => g.Key, g => g.Select(pm => pm.ManufacturerId).ToArray());

      foreach (var pid in productIds) {
        result.TryAdd(pid, Array.Empty<int>());
      }

      return Task.FromResult(result);
    }

    private static Task<Dictionary<int, string>> BatchLoadManufacturerNamesAsync(int[] manufacturerIds)
    {
      if (manufacturerIds.Length == 0) {
        return Task.FromResult(new Dictionary<int, string>());
      }

      var repository = EngineContext.Current.Resolve<IRepository<Manufacturer>>();
      var result = repository.Table
        .Where(m => manufacturerIds.Contains(m.Id) && !m.Deleted)
        .Select(m => new { m.Id, m.Name })
        .ToDictionary(m => m.Id, m => m.Name);

      return Task.FromResult(result);
    }

    private static async Task<Dictionary<int, int[]>> BatchLoadStoreIdsAsync(
        int[] productIds,
        Product[] products
    )
    {
      var allStores = await EngineContext.Current.Resolve<IStoreService>().GetAllStoresAsync();
      var allStoreIds = allStores.Select(s => s.Id).ToArray();
      var result = new Dictionary<int, int[]>();

      var limitedProductIds = products
        .Where(p => p.LimitedToStores)
        .Select(p => p.Id)
        .ToArray();

      if (limitedProductIds.Length > 0) {
        var storeMappingRepo = EngineContext.Current
          .Resolve<IRepository<Nop.Core.Domain.Stores.StoreMapping>>();
        var mappings = storeMappingRepo.Table
          .Where(sm => sm.EntityName == "Product" && limitedProductIds.Contains(sm.EntityId))
          .Select(sm => new { sm.EntityId, sm.StoreId })
          .ToList();

        foreach (var group in mappings.GroupBy(m => m.EntityId)) {
          result[group.Key] = group.Select(m => m.StoreId).ToArray();
        }
      }

      foreach (var product in products) {
        if (!result.ContainsKey(product.Id)) {
          result[product.Id] = allStoreIds;
        }
      }

      return result;
    }

    #endregion

    #region Product Category Assignment

    [HttpPost("product-category-assign")]
    public async Task<IActionResult> ProductCategoryAssign(
        [FromQuery] string? product_id = null,
        [FromQuery] string? category_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var productId)) {
          return ParamError("Parameter 'product_id' is required.");
        }

        if (!TryParsePositiveInt(category_id, out var categoryId)) {
          return ParamError("Parameter 'category_id' is required.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(productId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {productId} not found.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {categoryId} not found.");
        }

        var existing = await categoryService.GetProductCategoriesByProductIdAsync(productId, true);

        if (existing.Any(pc => pc.CategoryId == categoryId)) {
          return ExistsError($"Product {productId} is already assigned to category {categoryId}.");
        }

        await categoryService.InsertProductCategoryAsync(new ProductCategory
        {
          ProductId = productId,
          CategoryId = categoryId,
          IsFeaturedProduct = false,
          DisplayOrder = 0,
        });

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {assigned = true},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("product-category-unassign")]
    public async Task<IActionResult> ProductCategoryUnassign(
        [FromQuery] string? product_id = null,
        [FromQuery] string? category_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var productId)) {
          return ParamError("Parameter 'product_id' is required.");
        }

        if (!TryParsePositiveInt(category_id, out var categoryId)) {
          return ParamError("Parameter 'category_id' is required.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var mapping = (await categoryService.GetProductCategoriesByProductIdAsync(productId, true))
          .FirstOrDefault(pc => pc.CategoryId == categoryId);

        if (mapping == null) {
          return NotFoundError($"Product {productId} is not assigned to category {categoryId}.");
        }

        await categoryService.DeleteProductCategoryAsync(mapping);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {unassigned = true},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-add")]
    public async Task<IActionResult> ProductsAdd(
        [FromQuery] string? name = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? full_description = null,
        [FromQuery] string? short_description = null,
        [FromQuery] string? price = null,
        [FromQuery] string? old_price = null,
        [FromQuery] string? cost_price = null,

        [FromQuery] string? published = null,
        [FromQuery] string? visible_individually = null,
        [FromQuery] string? manage_stock = null,
        [FromQuery] string? stock_quantity = null,
        [FromQuery] string? warehouse_id = null,
        [FromQuery] string? backorder_status = null,
        [FromQuery] string? low_stock_threshold = null,
        [FromQuery] string? min_order_quantity = null,
        [FromQuery] string? max_order_quantity = null,
        [FromQuery] string? weight = null,
        [FromQuery] string? width = null,
        [FromQuery] string? height = null,
        [FromQuery] string? length = null,
        [FromQuery] string? is_virtual = null,
        [FromQuery] string? downloadable = null,
        [FromQuery] string? manufacturer_id = null,
        [FromQuery] string? mpn = null,
        [FromQuery] string? gtin = null,
        [FromQuery] string? tax_category_id = null,
        [FromQuery] string? meta_title = null,
        [FromQuery] string? meta_keywords = null,
        [FromQuery] string? meta_description = null,
        [FromQuery] string? available_start_date_utc = null,
        [FromQuery] string? categories_ids = null,
        [FromQuery] string? related_products_ids = null,
        [FromQuery] string? cross_sell_products_ids = null,
        [FromQuery] string? stores_ids = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? tier_prices = null,
        [FromQuery] string? is_free_shipping = null,
        [FromQuery] string? slug = null,
        [FromQuery] string? vendor_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var rawName = Request.Query.ContainsKey("name") ? Request.Query["name"].ToString() : (name ?? string.Empty);

        if (string.IsNullOrWhiteSpace(rawName)) {
          return ParamError("Parameter 'name' is required.");
        }

        var (storeIds, storeError) = await ResolveProductStoreIdsAsync(null, stores_ids);

        if (storeError != null) {
          return storeError;
        }

        var (parsedWarehouseId, warehouseError) = ResolveWarehouseId(warehouse_id);

        if (warehouseError != null) {
          return warehouseError;
        }

        var (parsedBackorderModeId, backorderError) = ResolveBackorderModeId(backorder_status, (int)BackorderMode.NoBackorders);

        if (backorderError != null) {
          return backorderError;
        }

        var (parsedLowStockThreshold, lowStockError) = ParseOptionalInt(low_stock_threshold, "low_stock_threshold");

        if (lowStockError != null) {
          return lowStockError;
        }

        var (parsedMinOrderQuantity, minOrderError) = ParseOptionalInt(min_order_quantity, "min_order_quantity");

        if (minOrderError != null) {
          return minOrderError;
        }

        var (parsedMaxOrderQuantity, maxOrderError) = ParseOptionalInt(max_order_quantity, "max_order_quantity");

        if (maxOrderError != null) {
          return maxOrderError;
        }

        var effectiveMinOrderQuantity = parsedMinOrderQuantity ?? 1;
        var effectiveMaxOrderQuantity = parsedMaxOrderQuantity ?? 10000;

        if (effectiveMaxOrderQuantity < effectiveMinOrderQuantity) {
          return ParamError("Parameter 'max_order_quantity' is not valid.");
        }

        var (parsedManageStock, manageStockError) = ParseOptionalBool(manage_stock, "manage_stock");

        if (manageStockError != null) {
          return manageStockError;
        }

        var (parsedIsVirtual, isVirtualError) = ParseOptionalBool(is_virtual, "is_virtual");

        if (isVirtualError != null) {
          return isVirtualError;
        }

        var (parsedDownloadable, downloadableError) = ParseOptionalBool(downloadable, "downloadable");

        if (downloadableError != null) {
          return downloadableError;
        }

        var (parsedIsFreeShipping, isFreeShippingError) = ParseOptionalBool(is_free_shipping, "is_free_shipping");

        if (isFreeShippingError != null) {
          return isFreeShippingError;
        }

        var product = new Product
        {
          Name = rawName,
          Sku = Request.Query.ContainsKey("sku") ? Request.Query["sku"].ToString() : (sku ?? string.Empty),
          ShortDescription = short_description,
          FullDescription = full_description,
          Price = ParseDecimal(price, 0),
          OldPrice = ParseDecimal(old_price, 0),
          ProductCost = ParseDecimal(cost_price, 0),
          Published = ParseBool(published, fallback: true),
          VisibleIndividually = ParseBool(visible_individually, fallback: true),
          StockQuantity = ParseInt(stock_quantity, 0),
          Weight = ParseDecimal(weight, 0),
          Width = ParseDecimal(width, 0),
          Height = ParseDecimal(height, 0),
          Length = ParseDecimal(length, 0),
          Gtin = gtin,
          MetaTitle = meta_title,
          MetaKeywords = meta_keywords,
          MetaDescription = meta_description,
          ManageInventoryMethodId = parsedManageStock == false
            ? (int)ManageInventoryMethod.DontManageStock
            : (int)ManageInventoryMethod.ManageStock,
          WarehouseId = parsedWarehouseId ?? 0,
          BackorderModeId = parsedBackorderModeId,
          MinStockQuantity = parsedLowStockThreshold ?? 0,
          OrderMinimumQuantity = parsedMinOrderQuantity ?? 1,
          OrderMaximumQuantity = parsedMaxOrderQuantity ?? 10000,
          ManufacturerPartNumber = mpn ?? string.Empty,
          CreatedOnUtc = DateTime.UtcNow,
          UpdatedOnUtc = DateTime.UtcNow,
          ProductTypeId = (int)ProductType.SimpleProduct,
          LimitedToStores = storeIds?.Length > 0,
        };

        if (parsedIsVirtual.HasValue) {
          product.IsShipEnabled = !parsedIsVirtual.Value;
        }

        if (parsedDownloadable.HasValue) {
          product.IsDownload = parsedDownloadable.Value;
        }

        if (parsedIsFreeShipping.HasValue) {
          product.IsFreeShipping = parsedIsFreeShipping.Value;
        }

        if (vendor_id != null && TryParsePositiveInt(vendor_id, out var vendorIdVal)) {
          product.VendorId = vendorIdVal;
        }

        if (tax_category_id != null && TryParsePositiveInt(tax_category_id, out var taxCatId)) {
          var taxCategoryService = EngineContext.Current.Resolve<Nop.Services.Tax.ITaxCategoryService>();
          if (await taxCategoryService.GetTaxCategoryByIdAsync(taxCatId) == null) {
            return NotFoundError($"Tax category with id {taxCatId} not found.");
          }
          product.TaxCategoryId = taxCatId;
        }

        if (available_start_date_utc != null
          && DateTime.TryParse(
            available_start_date_utc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var startDate
          )) {
          product.AvailableStartDateTimeUtc = startDate.ToUniversalTime();
        }

        int? resolvedManufacturerId = TryParsePositiveInt(manufacturer_id, out var parsedMfgId) ? parsedMfgId : null;
        var (parsedTierPrices, tierPriceError) = ParseTierPrices(tier_prices);

        if (tierPriceError != null) {
          return tierPriceError;
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        await productService.InsertProductAsync(product);

        if (categories_ids != null) {
          var categoryService = EngineContext.Current.Resolve<ICategoryService>();
          var parsedCatIds = (ParseIntIds(categories_ids) ?? Array.Empty<int>()).Where(cid => cid > 0).ToArray();

          foreach (var cid in parsedCatIds) {
            if (await categoryService.GetCategoryByIdAsync(cid) == null) {
              return NotFoundError($"Category with id {cid} not found.");
            }
          }

          foreach (var cid in parsedCatIds) {
            var productCategory = new ProductCategory
            {
              ProductId = product.Id,
              CategoryId = cid,
              DisplayOrder = 0,
            };
            await categoryService.InsertProductCategoryAsync(productCategory);
          }
        }

        if (resolvedManufacturerId.HasValue) {
          var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
          var productManufacturer = new ProductManufacturer
          {
            ProductId = product.Id,
            ManufacturerId = resolvedManufacturerId.Value,
          };
          await manufacturerService.InsertProductManufacturerAsync(productManufacturer);
        }

        if (stores_ids != null) {
          var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

          foreach (var mappedStoreId in storeIds ?? Array.Empty<int>()) {
            await storeMappingService.InsertStoreMappingAsync(product, mappedStoreId);
          }
        }

        if (related_products_ids != null) {
          foreach (var relId in ParseIntIds(related_products_ids) ?? Array.Empty<int>()) {
            if (relId > 0) {
              var relatedProduct = new RelatedProduct
              {
                ProductId1 = product.Id,
                ProductId2 = relId,
              };
              await productService.InsertRelatedProductAsync(relatedProduct);
            }
          }
        }

        if (cross_sell_products_ids != null) {
          foreach (var crossId in ParseIntIds(cross_sell_products_ids) ?? Array.Empty<int>()) {
            if (crossId > 0) {
              var crossSellProduct = new CrossSellProduct
              {
                ProductId1 = product.Id,
                ProductId2 = crossId,
              };
              await productService.InsertCrossSellProductAsync(crossSellProduct);
            }
          }
        }

        if (tags != null) {
          var productTagService = EngineContext.Current.Resolve<IProductTagService>();
          var tagNames = tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
          await productTagService.UpdateProductTagsAsync(product, tagNames);
        }

        if (parsedTierPrices != null) {
          await ReplaceProductTierPricesAsync(product.Id, parsedTierPrices);
        }

        await SaveProductSlugAsync(product, slug);

        return JsonContent(new ConnectorResponse<object> { Result = new { id = product.Id } });
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-update")]
    public async Task<IActionResult> ProductsUpdate(
        [FromQuery] string? id = null,
        [FromQuery] string? name = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? full_description = null,
        [FromQuery] string? short_description = null,
        [FromQuery] string? price = null,
        [FromQuery] string? old_price = null,
        [FromQuery] string? cost_price = null,

        [FromQuery] string? published = null,
        [FromQuery] string? visible_individually = null,
        [FromQuery] string? manage_stock = null,
        [FromQuery] string? stock_quantity = null,
        [FromQuery] string? change_quantity_inc = null,
        [FromQuery] string? change_quantity_dec = null,
        [FromQuery] string? warehouse_id = null,
        [FromQuery] string? backorder_status = null,
        [FromQuery] string? low_stock_threshold = null,
        [FromQuery] string? min_order_quantity = null,
        [FromQuery] string? max_order_quantity = null,
        [FromQuery] string? weight = null,
        [FromQuery] string? width = null,
        [FromQuery] string? height = null,
        [FromQuery] string? length = null,
        [FromQuery] string? is_virtual = null,
        [FromQuery] string? downloadable = null,
        [FromQuery] string? manufacturer_id = null,
        [FromQuery] string? manufacturer_name = null,
        [FromQuery] string? mpn = null,
        [FromQuery] string? gtin = null,
        [FromQuery] string? tax_category_id = null,
        [FromQuery] string? meta_title = null,
        [FromQuery] string? meta_keywords = null,
        [FromQuery] string? meta_description = null,
        [FromQuery] string? available_start_date_utc = null,
        [FromQuery] string? categories_ids = null,
        [FromQuery] string? related_products_ids = null,
        [FromQuery] string? cross_sell_products_ids = null,
        [FromQuery] string? store_id = null,
        [FromQuery] string? stores_ids = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? tier_prices = null,
        [FromQuery] string? language_id = null,
        [FromQuery] string? is_free_shipping = null,
        [FromQuery] string? slug = null,
        [FromQuery] string? vendor_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var productId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(productId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {id} not found.");
        }

        int? localizedLanguageId = null;

        if (language_id != null) {
          var resolvedLanguageId = await ResolveLanguageIdAsync(language_id);

          if (resolvedLanguageId <= 0) {
            return NotFoundError($"Language with id {language_id} not found.");
          }

          localizedLanguageId = resolvedLanguageId;
        }

        var (parsedTierPrices, tierPriceError) = ParseTierPrices(tier_prices);

        if (tierPriceError != null) {
          return tierPriceError;
        }

        var (parsedWarehouseId, warehouseError) = ResolveWarehouseId(warehouse_id);

        if (warehouseError != null) {
          return warehouseError;
        }

        var (parsedBackorderModeId, backorderError) = ResolveBackorderModeId(backorder_status, product.BackorderModeId);

        if (backorderError != null) {
          return backorderError;
        }

        var (parsedLowStockThreshold, lowStockError) = ParseOptionalInt(low_stock_threshold, "low_stock_threshold");

        if (lowStockError != null) {
          return lowStockError;
        }

        var (parsedMinOrderQuantity, minOrderError) = ParseOptionalInt(min_order_quantity, "min_order_quantity");

        if (minOrderError != null) {
          return minOrderError;
        }

        var (parsedMaxOrderQuantity, maxOrderError) = ParseOptionalInt(max_order_quantity, "max_order_quantity");

        if (maxOrderError != null) {
          return maxOrderError;
        }

        var effectiveMinOrderQuantity = parsedMinOrderQuantity ?? product.OrderMinimumQuantity;
        var effectiveMaxOrderQuantity = parsedMaxOrderQuantity ?? product.OrderMaximumQuantity;

        if (effectiveMaxOrderQuantity < effectiveMinOrderQuantity) {
          return ParamError("Parameter 'max_order_quantity' is not valid.");
        }

        var (parsedManageStock, manageStockError) = ParseOptionalBool(manage_stock, "manage_stock");

        if (manageStockError != null) {
          return manageStockError;
        }

        var (parsedIsVirtual, isVirtualError) = ParseOptionalBool(is_virtual, "is_virtual");

        if (isVirtualError != null) {
          return isVirtualError;
        }

        var (parsedDownloadable, downloadableError) = ParseOptionalBool(downloadable, "downloadable");

        if (downloadableError != null) {
          return downloadableError;
        }

        var (parsedIsFreeShipping, isFreeShippingError) = ParseOptionalBool(is_free_shipping, "is_free_shipping");

        if (isFreeShippingError != null) {
          return isFreeShippingError;
        }

        if (name != null) {
          var rawName = Request.Query.ContainsKey("name") ? Request.Query["name"].ToString() : name;

          if (string.IsNullOrWhiteSpace(rawName)) {
            return ParamError("Parameter 'name' cannot be empty.");
          }

          name = rawName;

          if (!localizedLanguageId.HasValue) {
            product.Name = rawName;
          }
        }

        if (sku != null) {
          product.Sku = Request.Query.ContainsKey("sku") ? Request.Query["sku"].ToString() : sku;
        }

        if (full_description != null && !localizedLanguageId.HasValue) {
          product.FullDescription = full_description;
        }

        if (short_description != null && !localizedLanguageId.HasValue) {
          product.ShortDescription = short_description;
        }

        if (price != null
          && decimal.TryParse(
            price, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedPrice
          )) {
          product.Price = parsedPrice;
        }

        if (old_price != null
          && decimal.TryParse(
            old_price, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedOldPrice
          )) {
          product.OldPrice = parsedOldPrice;
        }

        if (cost_price != null
          && decimal.TryParse(
            cost_price, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedCostPrice
          )) {
          product.ProductCost = parsedCostPrice;
        }

        if (published != null) {
          product.Published = ParseBool(published, product.Published);
        }

        if (visible_individually != null) {
          product.VisibleIndividually = ParseBool(visible_individually, product.VisibleIndividually);
        }

        if (parsedManageStock.HasValue) {
          product.ManageInventoryMethodId = parsedManageStock.Value
            ? (int)ManageInventoryMethod.ManageStock
            : (int)ManageInventoryMethod.DontManageStock;
        }

        if (stock_quantity != null) {
          product.StockQuantity = ParseInt(stock_quantity, product.StockQuantity);
        }

        var stockDelta = 0;

        if (change_quantity_inc != null) {
          stockDelta += ParseInt(change_quantity_inc, 0);
        }

        if (change_quantity_dec != null) {
          stockDelta -= ParseInt(change_quantity_dec, 0);
        }

        if (weight != null
          && decimal.TryParse(
            weight, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedWeight
          )) {
          product.Weight = parsedWeight;
        }

        if (width != null
          && decimal.TryParse(
            width, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedWidth
          )) {
          product.Width = parsedWidth;
        }

        if (height != null
          && decimal.TryParse(
            height, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedHeight
          )) {
          product.Height = parsedHeight;
        }

        if (length != null
          && decimal.TryParse(
            length, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedLength
          )) {
          product.Length = parsedLength;
        }

        if (parsedWarehouseId.HasValue) {
          product.WarehouseId = parsedWarehouseId.Value;
        }

        if (backorder_status != null) {
          product.BackorderModeId = parsedBackorderModeId;
        }

        if (parsedLowStockThreshold.HasValue) {
          product.MinStockQuantity = parsedLowStockThreshold.Value;
        }

        if (parsedMinOrderQuantity.HasValue) {
          product.OrderMinimumQuantity = parsedMinOrderQuantity.Value;
        }

        if (parsedMaxOrderQuantity.HasValue) {
          product.OrderMaximumQuantity = parsedMaxOrderQuantity.Value;
        }

        if (parsedIsVirtual.HasValue) {
          product.IsShipEnabled = !parsedIsVirtual.Value;
        }

        if (parsedDownloadable.HasValue) {
          product.IsDownload = parsedDownloadable.Value;
        }

        if (gtin != null) {
          product.Gtin = gtin;
        }

        if (mpn != null) {
          product.ManufacturerPartNumber = mpn;
        }

        if (parsedIsFreeShipping.HasValue) {
          product.IsFreeShipping = parsedIsFreeShipping.Value;
        }

        if (vendor_id != null && TryParsePositiveInt(vendor_id, out var vendorIdVal)) {
          product.VendorId = vendorIdVal;
        }

        if (meta_title != null && !localizedLanguageId.HasValue) {
          product.MetaTitle = meta_title;
        }

        if (meta_keywords != null && !localizedLanguageId.HasValue) {
          product.MetaKeywords = meta_keywords;
        }

        if (meta_description != null && !localizedLanguageId.HasValue) {
          product.MetaDescription = meta_description;
        }

        if (tax_category_id != null && TryParsePositiveInt(tax_category_id, out var taxCatId)) {
          var taxCategoryService = EngineContext.Current.Resolve<Nop.Services.Tax.ITaxCategoryService>();
          if (await taxCategoryService.GetTaxCategoryByIdAsync(taxCatId) == null) {
            return NotFoundError($"Tax category with id {taxCatId} not found.");
          }
          product.TaxCategoryId = taxCatId;
        }

        if (available_start_date_utc != null
          && DateTime.TryParse(
            available_start_date_utc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var startDate
          )) {
          product.AvailableStartDateTimeUtc = startDate.ToUniversalTime();
        }

        int[]? storeIds = null;
        if (store_id != null || stores_ids != null) {
          var (resolvedStoreIds, storeError) = await ResolveProductStoreIdsAsync(store_id, stores_ids);

          if (storeError != null) {
            return storeError;
          }

          storeIds = resolvedStoreIds;
          product.LimitedToStores = storeIds?.Length > 0;
        }

        product.UpdatedOnUtc = DateTime.UtcNow;
        await productService.UpdateProductAsync(product);

        if (stockDelta != 0) {
          if (parsedWarehouseId.HasValue
            && product.ManageInventoryMethodId == (int)ManageInventoryMethod.ManageStock
            && product.UseMultipleWarehouses
          ) {
            await AdjustWarehouseInventoryAsync(productId, parsedWarehouseId.Value, stockDelta);
          } else {
            await AdjustProductStockAtomicallyAsync(productId, stockDelta);
          }
        }

        if (localizedLanguageId.HasValue) {
          if (name != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "Name", name);
          }

          if (short_description != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "ShortDescription", short_description);
          }

          if (full_description != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "FullDescription", full_description);
          }

          if (meta_title != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "MetaTitle", meta_title);
          }

          if (meta_keywords != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "MetaKeywords", meta_keywords);
          }

          if (meta_description != null) {
            await UpsertLocalizedProductPropertyAsync(productId, localizedLanguageId.Value, "MetaDescription", meta_description);
          }
        }

        if (categories_ids != null) {
          var categoryService = EngineContext.Current.Resolve<ICategoryService>();
          var newCatIds = new HashSet<int>((ParseIntIds(categories_ids) ?? Array.Empty<int>()).Where(i => i > 0));

          foreach (var cid in newCatIds) {
            if (await categoryService.GetCategoryByIdAsync(cid) == null) {
              return NotFoundError($"Category with id {cid} not found.");
            }
          }

          var existing = await categoryService.GetProductCategoriesByProductIdAsync(productId, true);
          var existingCatIds = new HashSet<int>(existing.Select(pc => pc.CategoryId));

          foreach (var pc in existing.Where(pc => !newCatIds.Contains(pc.CategoryId))) {
            await categoryService.DeleteProductCategoryAsync(pc);
          }

          foreach (var cid in newCatIds.Where(cid => !existingCatIds.Contains(cid))) {
            var productCategory = new ProductCategory
            {
              ProductId = productId,
              CategoryId = cid,
              DisplayOrder = 0,
            };
            await categoryService.InsertProductCategoryAsync(productCategory);
          }
        }

        if (manufacturer_id != null || manufacturer_name != null) {
          var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
          var existingManufacturers = await manufacturerService.GetProductManufacturersByProductIdAsync(productId, true);

          foreach (var pm in existingManufacturers) {
            await manufacturerService.DeleteProductManufacturerAsync(pm);
          }

          var resolvedManufacturerId = await ResolveManufacturerIdAsync(manufacturer_id, manufacturer_name);

          if (resolvedManufacturerId.HasValue) {
            var productManufacturer = new ProductManufacturer
            {
              ProductId = productId,
              ManufacturerId = resolvedManufacturerId.Value,
            };
            await manufacturerService.InsertProductManufacturerAsync(productManufacturer);
          }
        }

        if (store_id != null || stores_ids != null) {
          var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();
          var existingMappings = await storeMappingService.GetStoreMappingsAsync(product);

          foreach (var mapping in existingMappings) {
            await storeMappingService.DeleteStoreMappingAsync(mapping);
          }

          if (storeIds != null) {
            foreach (var storeId in storeIds) {
              await storeMappingService.InsertStoreMappingAsync(product, storeId);
            }
          }
        }

        if (related_products_ids != null) {
          var existingRelated = await productService.GetRelatedProductsByProductId1Async(productId, true);

          foreach (var rp in existingRelated) {
            await productService.DeleteRelatedProductAsync(rp);
          }

          foreach (var relId in (ParseIntIds(related_products_ids) ?? Array.Empty<int>()).Where(i => i > 0)) {
            var relatedProduct = new RelatedProduct
            {
              ProductId1 = productId,
              ProductId2 = relId,
            };
            await productService.InsertRelatedProductAsync(relatedProduct);
          }
        }

        if (cross_sell_products_ids != null) {
          var existingCrossSell = await productService.GetCrossSellProductsByProductId1Async(productId, true);

          foreach (var cs in existingCrossSell) {
            await productService.DeleteCrossSellProductAsync(cs);
          }

          foreach (var crossId in (ParseIntIds(cross_sell_products_ids) ?? Array.Empty<int>()).Where(i => i > 0)) {
            var crossSellProduct = new CrossSellProduct
            {
              ProductId1 = productId,
              ProductId2 = crossId,
            };
            await productService.InsertCrossSellProductAsync(crossSellProduct);
          }
        }

        if (tags != null) {
          var productTagService = EngineContext.Current.Resolve<IProductTagService>();
          var tagNames = tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
          await productTagService.UpdateProductTagsAsync(product, tagNames);
        }

        if (parsedTierPrices != null) {
          await ReplaceProductTierPricesAsync(productId, parsedTierPrices);
        }

        if (slug != null) {
          await SaveProductSlugAsync(product, slug, localizedLanguageId ?? 0);
        }

        return JsonContent(new ConnectorResponse<object> { Result = new { updated = true } });
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-delete")]
    public async Task<IActionResult> ProductsDelete([FromQuery] string? id = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var productId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(productId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {id} not found.");
        }

        await productService.DeleteProductAsync(product);

        return JsonContent(new ConnectorResponse<object> { Result = new { deleted = true } });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #region Product Variant Write Actions

    [HttpPost("products-child-add")]
    public async Task<IActionResult> ProductsChildAdd(
        [FromQuery] string? product_id = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? stock_quantity = null,
        [FromQuery] string? allow_out_of_stock_orders = null,
        [FromQuery] string? overridden_price = null,
        [FromQuery] string? gtin = null,
        [FromQuery] string? manufacturer_part_number = null,
        [FromQuery] string? min_stock_quantity = null,
        [FromQuery] string? notify_admin_for_quantity_below = null,
        [FromQuery] string? attributes_map = null,
        [FromQuery] string? price_modifiers = null,
        [FromQuery] string? weight_modifiers = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var productId)) {
          return NotFoundError($"Product with id {product_id} not found.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(productId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {product_id} not found.");
        }

        var pairs = ParseNameValueMap(attributes_map);

        if (pairs == null || pairs.Count == 0) {
          return ParamError("Parameter 'attributes_map' is required and must match 'name:value[,...]'.");
        }

        var priceMap = ParseNameValueDecimalMap(price_modifiers);
        var weightMap = ParseNameValueDecimalMap(weight_modifiers);

        if (priceMap == null || weightMap == null) {
          return ParamError("Parameter 'price_modifiers' / 'weight_modifiers' must match 'name:value:decimal[,...]'.");
        }

        var lengthError = ValidateVariantStringLengths(sku, gtin, manufacturer_part_number);

        if (lengthError != null) {
          return lengthError;
        }

        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var productAttributeParser = EngineContext.Current.Resolve<IProductAttributeParser>();
        var resolved = await ResolveOrCreateAttributeTreeAsync(productId, pairs, priceMap, weightMap, productAttributeService);
        var attributesXml = string.Empty;

        foreach (var (mapping, value) in resolved) {
          attributesXml = productAttributeParser.AddProductAttribute(attributesXml, mapping, value.Id.ToString());
        }

        var duplicateCombination = await productAttributeParser.FindProductAttributeCombinationAsync(product, attributesXml);

        if (duplicateCombination != null) {
          return ExistsError($"Variant with same attribute combination already exists (id {duplicateCombination.Id}).");
        }

        var combination = new ProductAttributeCombination {
          ProductId = productId,
          AttributesXml = attributesXml,
          Sku = Request.Query.ContainsKey("sku") ? Request.Query["sku"].ToString() : (sku ?? string.Empty),
          StockQuantity = ParseInt(stock_quantity, 0),
          AllowOutOfStockOrders = ParseBool(allow_out_of_stock_orders, false),
          OverriddenPrice = string.IsNullOrEmpty(overridden_price) ? (decimal?)null : ParseDecimal(overridden_price, 0m),
          Gtin = gtin ?? string.Empty,
          ManufacturerPartNumber = manufacturer_part_number ?? string.Empty,
          MinStockQuantity = ParseInt(min_stock_quantity, 0),
          NotifyAdminForQuantityBelow = ParseInt(notify_admin_for_quantity_below, 0),
        };

        await productAttributeService.InsertProductAttributeCombinationAsync(combination);

        return JsonContent(new ConnectorResponse<object> { Result = new { id = combination.Id } });
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-child-update")]
    public async Task<IActionResult> ProductsChildUpdate(
        [FromQuery] string? product_id = null,
        [FromQuery] string? id = null,
        [FromQuery] string? sku = null,
        [FromQuery] string? stock_quantity = null,
        [FromQuery] string? allow_out_of_stock_orders = null,
        [FromQuery] string? overridden_price = null,
        [FromQuery] string? gtin = null,
        [FromQuery] string? manufacturer_part_number = null,
        [FromQuery] string? min_stock_quantity = null,
        [FromQuery] string? notify_admin_for_quantity_below = null,
        [FromQuery] string? change_quantity_inc = null,
        [FromQuery] string? change_quantity_dec = null,
        [FromQuery] string? price_modifiers = null,
        [FromQuery] string? weight_modifiers = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var parsedProductId)
          || !TryParsePositiveInt(id, out var combinationId)) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var combination = await productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId);

        if (combination == null || combination.ProductId != parsedProductId) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        var lengthError = ValidateVariantStringLengths(sku, gtin, manufacturer_part_number);

        if (lengthError != null) {
          return lengthError;
        }

        var modified = false;

        var rawSku = Request.Query.ContainsKey("sku") ? Request.Query["sku"].ToString() : sku;

        if (rawSku != null && combination.Sku != rawSku) {
          combination.Sku = rawSku;
          modified = true;
        }

        if (stock_quantity != null) {
          var nq = ParseInt(stock_quantity, combination.StockQuantity);

          if (nq != combination.StockQuantity) {
            combination.StockQuantity = nq;
            modified = true;
          }
        }

        var inc = ParseInt(change_quantity_inc, 0);
        var dec = ParseInt(change_quantity_dec, 0);

        if (inc != 0 || dec != 0) {
          var newQty = combination.StockQuantity + inc - dec;

          if (newQty < 0) {
            return ParamError(
              $"Parameter 'change_quantity_dec' is not valid. Resulting stock_quantity would be {newQty} (current {combination.StockQuantity}).");
          }

          if (newQty != combination.StockQuantity) {
            combination.StockQuantity = newQty;
            modified = true;
          }
        }

        if (allow_out_of_stock_orders != null) {
          var nb = ParseBool(allow_out_of_stock_orders, combination.AllowOutOfStockOrders);

          if (nb != combination.AllowOutOfStockOrders) {
            combination.AllowOutOfStockOrders = nb;
            modified = true;
          }
        }

        if (overridden_price != null) {
          var np = string.IsNullOrEmpty(overridden_price) ? (decimal?)null : ParseDecimal(overridden_price, 0m);

          if (np != combination.OverriddenPrice) {
            combination.OverriddenPrice = np;
            modified = true;
          }
        }

        if (gtin != null && combination.Gtin != gtin) {
          combination.Gtin = gtin;
          modified = true;
        }

        if (manufacturer_part_number != null && combination.ManufacturerPartNumber != manufacturer_part_number) {
          combination.ManufacturerPartNumber = manufacturer_part_number;
          modified = true;
        }

        if (min_stock_quantity != null) {
          var nm = ParseInt(min_stock_quantity, combination.MinStockQuantity);

          if (nm != combination.MinStockQuantity) {
            combination.MinStockQuantity = nm;
            modified = true;
          }
        }

        if (notify_admin_for_quantity_below != null) {
          var nn = ParseInt(notify_admin_for_quantity_below, combination.NotifyAdminForQuantityBelow);

          if (nn != combination.NotifyAdminForQuantityBelow) {
            combination.NotifyAdminForQuantityBelow = nn;
            modified = true;
          }
        }

        var priceMap = ParseNameValueDecimalMap(price_modifiers);
        var weightMap = ParseNameValueDecimalMap(weight_modifiers);

        if (priceMap == null || weightMap == null) {
          return ParamError("Parameter 'price_modifiers' / 'weight_modifiers' must match 'name:value:decimal[,...]'.");
        }

        if (priceMap.Count > 0 || weightMap.Count > 0) {
          var parser = EngineContext.Current.Resolve<IProductAttributeParser>();
          var (applied, invalidValueName) = await ApplyModifiersToCombinationValuesAsync(
            combination,
            parser,
            productAttributeService,
            priceMap,
            weightMap
          );

          if (invalidValueName != null) {
            return ParamError($"Value '{invalidValueName}' is not part of combination {combination.Id}.");
          }

          if (applied) {
            modified = true;
          }
        }

        if (!modified) {
          return JsonContent(new ConnectorResponse<object> { Result = new { updated_items = 0 } });
        }

        await productAttributeService.UpdateProductAttributeCombinationAsync(combination);

        return JsonContent(new ConnectorResponse<object> { Result = new { updated_items = 1 } });
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("products-child-delete")]
    public async Task<IActionResult> ProductsChildDelete(
        [FromQuery] string? product_id = null,
        [FromQuery] string? id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var parsedProductId)
          || !TryParsePositiveInt(id, out var combinationId)) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        var productAttributeService = EngineContext.Current.Resolve<IProductAttributeService>();
        var combination = await productAttributeService.GetProductAttributeCombinationByIdAsync(combinationId);

        if (combination == null || combination.ProductId != parsedProductId) {
          return NotFoundError($"Combination with id {id} not found for product {product_id}.");
        }

        await productAttributeService.DeleteProductAttributeCombinationAsync(combination);

        return JsonContent(new ConnectorResponse<object> { Result = new { deleted = true } });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Variant Write Helpers

    private const int MaxVariantSkuLength = 400;
    private const int MaxVariantGtinLength = 400;
    private const int MaxVariantMpnLength = 400;

    private IActionResult? ValidateVariantStringLengths(string? sku, string? gtin, string? mpn)
    {
      if (sku != null && sku.Length > MaxVariantSkuLength) {
        return ParamError($"Parameter 'sku' is not valid. Maximum length is {MaxVariantSkuLength} characters.");
      }

      if (gtin != null && gtin.Length > MaxVariantGtinLength) {
        return ParamError($"Parameter 'gtin' is not valid. Maximum length is {MaxVariantGtinLength} characters.");
      }

      if (mpn != null && mpn.Length > MaxVariantMpnLength) {
        return ParamError($"Parameter 'manufacturer_part_number' is not valid. Maximum length is {MaxVariantMpnLength} characters.");
      }

      return null;
    }

    private static List<(string Name, string Value)>? ParseNameValueMap(string? raw)
    {
      if (string.IsNullOrWhiteSpace(raw)) {
        return null;
      }

      var result = new List<(string Name, string Value)>();

      foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
        var parts = token.Split(':', 2);

        if (parts.Length != 2) {
          return null;
        }

        var key = Uri.UnescapeDataString(parts[0]);
        var val = Uri.UnescapeDataString(parts[1]);

        if (key.Length == 0 || val.Length == 0) {
          return null;
        }

        result.Add((key, val));
      }

      return result.Count == 0 ? null : result;
    }

    private static Dictionary<string, decimal>? ParseNameValueDecimalMap(string? raw)
    {
      var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

      if (string.IsNullOrWhiteSpace(raw)) {
        return result;
      }

      foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
        var parts = token.Split(':', 3);

        if (parts.Length != 3) {
          return null;
        }

        var key = Uri.UnescapeDataString(parts[0]);
        var value = Uri.UnescapeDataString(parts[1]);

        if (key.Length == 0 || value.Length == 0) {
          return null;
        }

        if (!decimal.TryParse(
          parts[2],
          System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
          System.Globalization.CultureInfo.InvariantCulture,
          out var d
        )) {
          return null;
        }

        result[key + "::" + value] = d;
      }

      return result;
    }

    private static async Task<List<(ProductAttributeMapping Mapping, ProductAttributeValue Value)>>
      ResolveOrCreateAttributeTreeAsync(
        int productId,
        List<(string Name, string Value)> pairs,
        Dictionary<string, decimal> priceMap,
        Dictionary<string, decimal> weightMap,
        IProductAttributeService productAttributeService
    )
    {
      var result = new List<(ProductAttributeMapping Mapping, ProductAttributeValue Value)>();
      var allAttributes = await productAttributeService.GetAllProductAttributesAsync();
      var attributesByName = allAttributes
        .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
      var mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);
      var nextMappingDisplayOrder = mappings.Count > 0 ? mappings.Max(m => m.DisplayOrder) + 1 : 0;

      foreach (var (attrName, valueName) in pairs) {
        if (!attributesByName.TryGetValue(attrName, out var attribute)) {
          attribute = new ProductAttribute { Name = attrName, Description = string.Empty };
          await productAttributeService.InsertProductAttributeAsync(attribute);
          attributesByName[attrName] = attribute;
        }

        var mapping = mappings.FirstOrDefault(m => m.ProductAttributeId == attribute.Id);

        if (mapping == null) {
          mapping = new ProductAttributeMapping {
            ProductId = productId,
            ProductAttributeId = attribute.Id,
            TextPrompt = string.Empty,
            IsRequired = false,
            AttributeControlTypeId = 1,
            DisplayOrder = nextMappingDisplayOrder++,
          };
          await productAttributeService.InsertProductAttributeMappingAsync(mapping);
          mappings = await productAttributeService.GetProductAttributeMappingsByProductIdAsync(productId);
        }

        var values = await productAttributeService.GetProductAttributeValuesAsync(mapping.Id);
        var value = values.FirstOrDefault(v => string.Equals(v.Name, valueName, StringComparison.OrdinalIgnoreCase));

        var modifierKey = attrName + "::" + valueName;
        var priceAdjustment = priceMap.TryGetValue(modifierKey, out var p) ? p : 0m;
        var weightAdjustment = weightMap.TryGetValue(modifierKey, out var w) ? w : 0m;

        if (value == null) {
          value = new ProductAttributeValue {
            ProductAttributeMappingId = mapping.Id,
            AttributeValueTypeId = 1,
            Name = valueName,
            PriceAdjustment = priceAdjustment,
            PriceAdjustmentUsePercentage = false,
            WeightAdjustment = weightAdjustment,
            Cost = 0m,
            DisplayOrder = values.Count,
            IsPreSelected = false,
            AssociatedProductId = 0,
            Quantity = 1,
          };
          await productAttributeService.InsertProductAttributeValueAsync(value);
        } else {
          var valueModified = false;

          if (priceMap.ContainsKey(modifierKey) && value.PriceAdjustment != priceAdjustment) {
            value.PriceAdjustment = priceAdjustment;
            valueModified = true;
          }

          if (weightMap.ContainsKey(modifierKey) && value.WeightAdjustment != weightAdjustment) {
            value.WeightAdjustment = weightAdjustment;
            valueModified = true;
          }

          if (valueModified) {
            await productAttributeService.UpdateProductAttributeValueAsync(value);
          }
        }

        result.Add((mapping, value));
      }

      return result
        .OrderBy(r => r.Mapping.DisplayOrder)
        .ThenBy(r => r.Mapping.Id)
        .ThenBy(r => r.Value.DisplayOrder)
        .ThenBy(r => r.Value.Id)
        .ToList();
    }

    private static async Task<(bool Modified, string? InvalidValueName)> ApplyModifiersToCombinationValuesAsync(
        ProductAttributeCombination combination,
        IProductAttributeParser parser,
        IProductAttributeService productAttributeService,
        Dictionary<string, decimal> priceMap,
        Dictionary<string, decimal> weightMap
    )
    {
      var modified = false;
      var valuesByKey = new Dictionary<string, ProductAttributeValue>(StringComparer.OrdinalIgnoreCase);
      var parsedMappings = await parser.ParseProductAttributeMappingsAsync(combination.AttributesXml);

      foreach (var mapping in parsedMappings) {
        var attr = await productAttributeService.GetProductAttributeByIdAsync(mapping.ProductAttributeId);

        if (attr == null) {
          continue;
        }

        var values = await parser.ParseProductAttributeValuesAsync(combination.AttributesXml, mapping.Id);

        foreach (var v in values) {
          valuesByKey[attr.Name + "::" + v.Name] = v;
        }
      }

      foreach (var (key, newPrice) in priceMap) {
        if (!valuesByKey.TryGetValue(key, out var value)) {
          return (false, key.Split("::").Last());
        }

        if (value.PriceAdjustment != newPrice) {
          value.PriceAdjustment = newPrice;
          await productAttributeService.UpdateProductAttributeValueAsync(value);
          modified = true;
        }
      }

      foreach (var (key, newWeight) in weightMap) {
        if (!valuesByKey.TryGetValue(key, out var value)) {
          return (false, key.Split("::").Last());
        }

        if (value.WeightAdjustment != newWeight) {
          value.WeightAdjustment = newWeight;
          await productAttributeService.UpdateProductAttributeValueAsync(value);
          modified = true;
        }
      }

      return (modified, null);
    }

    #endregion

    private static decimal ParseDecimal(string? value, decimal fallback)
    {
      return decimal.TryParse(
        value,
        System.Globalization.NumberStyles.Any,
        System.Globalization.CultureInfo.InvariantCulture,
        out var result
      ) ? result : fallback;
    }

    private (List<(int Quantity, decimal Price)>? tierPrices, IActionResult? error) ParseTierPrices(string? tierPricesJson)
    {
      if (tierPricesJson == null) {
        return (null, null);
      }

      try {
        using var document = JsonDocument.Parse(tierPricesJson);

        if (document.RootElement.ValueKind != JsonValueKind.Array) {
          return (null, ParamError("Parameter 'tier_prices' is not valid."));
        }

        var tierPrices = new List<(int Quantity, decimal Price)>();
        var index = 0;

        foreach (var tierPriceElement in document.RootElement.EnumerateArray()) {
          if (!tierPriceElement.TryGetProperty("quantity", out var quantityElement)) {
            return (null, ParamError($"Property tier_prices[{index}]->quantity is required."));
          }

          if (!tierPriceElement.TryGetProperty("price", out var priceElement)) {
            return (null, ParamError($"Property tier_prices[{index}]->price is required."));
          }

          if (!int.TryParse(quantityElement.ToString(), out var quantity) || quantity <= 0) {
            return (null, ParamError("Parameter 'tier_prices' is not valid."));
          }

          if (!decimal.TryParse(
            priceElement.ToString(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var price
          )) {
            return (null, ParamError("Parameter 'tier_prices' is not valid."));
          }

          tierPrices.Add((quantity, price));
          index++;
        }

        return (tierPrices, null);
      } catch (JsonException) {
        return (null, ParamError("Parameter 'tier_prices' is not valid."));
      }
    }

    private (bool? value, IActionResult? error) ParseOptionalBool(string? rawValue, string paramName)
    {
      if (rawValue == null) {
        return (null, null);
      }

      if (!bool.TryParse(rawValue, out var parsedValue)) {
        return (null, ParamError($"Parameter '{paramName}' is not valid."));
      }

      return (parsedValue, null);
    }

    private static async Task<string> SaveProductSlugAsync(Product product, string? requestedSlug, int languageId = 0)
    {
      var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
      var source = string.IsNullOrWhiteSpace(requestedSlug) ? product.Name : requestedSlug;
      var seName = await urlRecordService.ValidateSeNameAsync(product, source, product.Name, true);
      await urlRecordService.SaveSlugAsync(product, seName, languageId);
      return seName;
    }

    private (int? value, IActionResult? error) ParseOptionalInt(string? rawValue, string paramName)
    {
      if (rawValue == null) {
        return (null, null);
      }

      if (!int.TryParse(rawValue, out var parsedValue)) {
        return (null, ParamError($"Parameter '{paramName}' is not valid."));
      }

      if (parsedValue < 0) {
        return (null, ParamError($"Parameter '{paramName}' is not valid."));
      }

      return (parsedValue, null);
    }

    private (int? warehouseId, IActionResult? error) ResolveWarehouseId(string? warehouseId)
    {
      if (warehouseId == null) {
        return (null, null);
      }

      if (warehouseId == "0") {
        return (0, null);
      }

      if (!TryParsePositiveInt(warehouseId, out var parsedWarehouseId)) {
        return (null, ParamError("Parameter 'warehouse_id' is not valid."));
      }

      var warehouseRepository = EngineContext.Current.Resolve<IRepository<Warehouse>>();

      if (!warehouseRepository.Table.Any(warehouse => warehouse.Id == parsedWarehouseId)) {
        return (null, NotFoundError($"Warehouse with id {warehouseId} not found."));
      }

      return (parsedWarehouseId, null);
    }

    private (int backorderModeId, IActionResult? error) ResolveBackorderModeId(string? backorderStatus, int fallbackModeId)
    {
      if (backorderStatus == null) {
        return (fallbackModeId, null);
      }

      return backorderStatus switch
      {
        "allow" => ((int)BackorderMode.AllowQtyBelow0, null),
        "deny" => ((int)BackorderMode.NoBackorders, null),
        "disable" => ((int)BackorderMode.NoBackorders, null),
        _ => (fallbackModeId, ParamError("Parameter 'backorder_status' is not valid.")),
      };
    }

    private static async Task ReplaceProductTierPricesAsync(int productId, IEnumerable<(int Quantity, decimal Price)> tierPrices)
    {
      var tierPriceRepository = EngineContext.Current.Resolve<IRepository<TierPrice>>();
      var existingTierPrices = tierPriceRepository.Table.Where(tp => tp.ProductId == productId).ToList();

      foreach (var tierPrice in existingTierPrices) {
        await tierPriceRepository.DeleteAsync(tierPrice);
      }

      foreach (var tierPrice in tierPrices) {
        var newTierPrice = new TierPrice
        {
          ProductId = productId,
          StoreId = 0,
          CustomerRoleId = null,
          Quantity = tierPrice.Quantity,
          Price = tierPrice.Price,
        };
        await tierPriceRepository.InsertAsync(newTierPrice);
      }
    }

    private static async Task UpsertLocalizedProductPropertyAsync(int productId, int languageId, string localeKey, string localeValue)
    {
      var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
      var localizedProperty = lpRepository.Table.FirstOrDefault(lp =>
        lp.EntityId == productId
        && lp.LanguageId == languageId
        && lp.LocaleKeyGroup == "Product"
        && lp.LocaleKey == localeKey);

      if (localizedProperty == null) {
        var entity = new Nop.Core.Domain.Localization.LocalizedProperty
        {
          EntityId = productId,
          LanguageId = languageId,
          LocaleKeyGroup = "Product",
          LocaleKey = localeKey,
          LocaleValue = localeValue,
        };
        await lpRepository.InsertAsync(entity);

        return;
      }

      localizedProperty.LocaleValue = localeValue;
      await lpRepository.UpdateAsync(localizedProperty);
    }

    private async Task<int?> ResolveManufacturerIdAsync(string? manufacturerId, string? manufacturerName)
    {
      if (TryParsePositiveInt(manufacturerId, out var parsedManufacturerId)) {
        return parsedManufacturerId;
      }

      if (string.IsNullOrWhiteSpace(manufacturerName)) {
        return null;
      }

      var manufacturerRepository = EngineContext.Current.Resolve<IRepository<Manufacturer>>();
      var existingManufacturer = manufacturerRepository.Table
        .FirstOrDefault(m => !m.Deleted && m.Name == manufacturerName);

      if (existingManufacturer != null) {
        return existingManufacturer.Id;
      }

      var manufacturer = new Manufacturer
      {
        Name = manufacturerName,
        Published = true,
        DisplayOrder = 0,
        CreatedOnUtc = DateTime.UtcNow,
        UpdatedOnUtc = DateTime.UtcNow,
      };

      var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
      await manufacturerService.InsertManufacturerAsync(manufacturer);

      var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
      var seName = await urlRecordService.ValidateSeNameAsync(manufacturer, null, manufacturer.Name, true);
      await urlRecordService.SaveSlugAsync(manufacturer, seName, 0);

      return manufacturer.Id;
    }

    private async Task<(int[]? storeIds, IActionResult? error)> ResolveProductStoreIdsAsync(string? storeId, string? storesIds)
    {
      if (storeId != null && storesIds != null) {
        return (null, ParamError("Parameters 'store_id' and 'stores_ids' cannot be used together."));
      }

      if (storeId != null) {
        if (!TryParsePositiveInt(storeId, out var parsedStoreId)) {
          return (null, ParamError("Parameter 'store_id' is not valid."));
        }

        var storeService = EngineContext.Current.Resolve<IStoreService>();
        var store = await storeService.GetStoreByIdAsync(parsedStoreId);

        if (store == null) {
          return (null, NotFoundError($"Store with id {parsedStoreId} not found."));
        }

        return (new[] {parsedStoreId}, null);
      }

      if (storesIds == null) {
        return (null, null);
      }

      var rawStoreIds = storesIds
        .Split(',')
        .Select(value => value.Trim())
        .Where(value => value.Length > 0)
        .ToArray();

      var hasZero = rawStoreIds.Contains("0");
      var hasRealStoreIds = rawStoreIds.Any(value => value != "0");

      if (hasZero && hasRealStoreIds) {
        return (null, ParamError("Parameter 'stores_ids' is not valid. Sentinel 0 cannot coexist with real store ids."));
      }

      if (hasZero) {
        return (Array.Empty<int>(), null);
      }

      var parsedStoreIds = ParseIntIds(storesIds)?.Where(id => id > 0).Distinct().ToArray() ?? Array.Empty<int>();
      var storeServiceForList = EngineContext.Current.Resolve<IStoreService>();

      foreach (var parsedStoreId in parsedStoreIds) {
        var store = await storeServiceForList.GetStoreByIdAsync(parsedStoreId);

        if (store == null) {
          return (null, NotFoundError($"Store with id {parsedStoreId} not found."));
        }
      }

      return (parsedStoreIds, null);
    }

    #endregion

    private class ProductBuildContext
    {
      public int LanguageId { get; init; }
      public ILocalizationService LocalizationService { get; init; } = null!;
      public IUrlRecordService UrlRecordService { get; init; } = null!;
      public Nop.Services.Discounts.IDiscountService DiscountService { get; init; } = null!;
      public string? WeightUnit { get; init; }
      public string? DimensionUnit { get; init; }
    }
  }
}
