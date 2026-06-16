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
    #region Manufacturers

    [HttpPost("manufacturers-list")]
    public async Task<IActionResult> ManufacturersList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] int store_id = 0,
        [FromQuery] string? published = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] string? find_value = null,
        [FromQuery] string? find_where = null,
        [FromQuery] string? brand_ids = null,
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
        var parsedBrandIds = ParseIntIds(brand_ids);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var langId = await ResolveLanguageIdAsync(language_id);

        var query = BuildFilteredManufacturerQuery(
          createdFrom,
          createdTo,
          modifiedFrom,
          modifiedTo,
          published,
          find_value,
          find_where,
          store_id,
          parsedBrandIds,
          langId
        );

        var totalCount = query.Count();
        var manufacturers = query
          .OrderBy(m => m.DisplayOrder)
          .ThenBy(m => m.Id)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();
        var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

        var result = new List<Dictionary<string, object?>>();

        foreach (var manufacturer in manufacturers) {
          result.Add(await BuildManufacturerDataAsync(
            manufacturer,
            langId,
            localizationService,
            urlRecordService,
            pictureService,
            storeMappingService,
            requestedFields
          ));
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {manufacturers = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("manufacturers-count")]
    public async Task<IActionResult> ManufacturersCount(
        [FromQuery] int store_id = 0,
        [FromQuery] string? published = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] string? find_value = null,
        [FromQuery] string? find_where = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var langId = await ResolveLanguageIdAsync(language_id);

        var query = BuildFilteredManufacturerQuery(
          ParseDateFilter(created_from),
          ParseDateFilter(created_to, isUpperBound: true),
          ParseDateFilter(modified_from),
          ParseDateFilter(modified_to, isUpperBound: true),
          published,
          find_value,
          find_where,
          store_id,
          null,
          langId
        );

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {count = query.Count()},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("manufacturers-info")]
    public async Task<IActionResult> ManufacturersInfo(
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
        if (!TryParsePositiveInt(id, out var manufacturerId)) {
          return NotFoundError($"Manufacturer with id {id} not found.");
        }

        var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
        var manufacturer = await manufacturerService.GetManufacturerByIdAsync(manufacturerId);

        if (manufacturer == null || manufacturer.Deleted) {
          return NotFoundError($"Manufacturer with id {id} not found.");
        }

        var langId = await ResolveLanguageIdAsync(language_id);
        var requestedFields = ParseFields(fields);
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();
        var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

        var data = await BuildManufacturerDataAsync(
          manufacturer,
          langId,
          localizationService,
          urlRecordService,
          pictureService,
          storeMappingService,
          requestedFields
        );

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Manufacturer Write Actions

    [HttpPost("manufacturers-add")]
    public async Task<IActionResult> ManufacturersAdd(
        [FromQuery] string? name = null,
        [FromQuery] string? product_id = null,
        [FromQuery] string? description = null,
        [FromQuery] string? meta_title = null,
        [FromQuery] string? meta_keywords = null,
        [FromQuery] string? meta_description = null,
        [FromQuery] string? image_url = null
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

        if (rawName.Length > 400) {
          return ParamError("Parameter 'name' is not valid. The required type of parameter is string with maximum length of 400 characters.");
        }

        if (meta_title != null && meta_title.Length > 400) {
          return ParamError("Parameter 'meta_title' is not valid. The required type of parameter is string with maximum length of 400 characters.");
        }

        if (meta_keywords != null && meta_keywords.Length > 400) {
          return ParamError(
            "Parameter 'meta_keywords' is not valid. "
            + "The required type of parameter is string with maximum length of 400 characters."
          );
        }

        int? parsedProductId = null;

        if (!string.IsNullOrEmpty(product_id)) {
          if (!TryParsePositiveInt(product_id, out var pid)) {
            return NotFoundError($"Product with id {product_id} not found.");
          }

          var productService = EngineContext.Current.Resolve<IProductService>();
          var product = await productService.GetProductByIdAsync(pid);

          if (product == null || product.Deleted) {
            return NotFoundError($"Product with id {product_id} not found.");
          }

          parsedProductId = pid;
        }

        var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
        var manufacturerRepository = EngineContext.Current.Resolve<IRepository<Manufacturer>>();
        var existingManufacturer = manufacturerRepository.Table.FirstOrDefault(m => !m.Deleted && m.Name == rawName);

        if (existingManufacturer != null) {
          return ExistsError($"Manufacturer with name '{rawName}' already exists (id {existingManufacturer.Id}).");
        }

        var newPictureId = 0;

        if (!string.IsNullOrEmpty(image_url)) {
          var pictureService = EngineContext.Current.Resolve<IPictureService>();
          var (pictureBytes, resolvedMime, resolveError) = await ResolveImageContentAsync(null, image_url, null);

          if (resolveError != null) {
            return resolveError;
          }

          if (pictureBytes == null || pictureBytes.Length == 0) {
            return ParamError("Image content is empty.");
          }

          var seoFilename = BuildSeoFilename(null, rawName);
          var newPicture = await pictureService.InsertPictureAsync(
            pictureBytes,
            resolvedMime,
            seoFilename,
            string.Empty,
            string.Empty,
            true,
            true
          );
          newPictureId = newPicture.Id;
        }

        var manufacturer = new Manufacturer {
          Name = rawName,
          Description = description ?? string.Empty,
          ManufacturerTemplateId = 1,
          MetaKeywords = meta_keywords ?? string.Empty,
          MetaDescription = meta_description ?? string.Empty,
          MetaTitle = meta_title ?? string.Empty,
          PictureId = newPictureId,
          PageSize = 6,
          AllowCustomersToSelectPageSize = true,
          PageSizeOptions = "6, 3, 9",
          Published = true,
          Deleted = false,
          DisplayOrder = 0,
          LimitedToStores = false,
          SubjectToAcl = false,
          CreatedOnUtc = DateTime.UtcNow,
          UpdatedOnUtc = DateTime.UtcNow,
        };

        await manufacturerService.InsertManufacturerAsync(manufacturer);

        var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
        var seName = await urlRecordService.ValidateSeNameAsync(manufacturer, null, manufacturer.Name, true);
        await urlRecordService.SaveSlugAsync(manufacturer, seName, 0);

        int? productManufacturerId = null;

        if (parsedProductId.HasValue) {
          var productManufacturer = new ProductManufacturer {
            ProductId = parsedProductId.Value,
            ManufacturerId = manufacturer.Id,
            IsFeaturedProduct = false,
            DisplayOrder = 0,
          };

          await manufacturerService.InsertProductManufacturerAsync(productManufacturer);
          productManufacturerId = productManufacturer.Id;
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {id = manufacturer.Id, product_manufacturer_id = productManufacturerId},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("product-manufacturer-link")]
    public async Task<IActionResult> ProductManufacturerLink(
        [FromQuery] string? product_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] string? manufacturer_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(product_id, out var parsedProductId)) {
          return NotFoundError($"Product with id {product_id} not found.");
        }

        if (!TryParsePositiveInt(manufacturer_id, out var parsedManufacturerId)) {
          return NotFoundError($"Manufacturer with id {manufacturer_id} not found.");
        }

        var productService = EngineContext.Current.Resolve<IProductService>();
        var product = await productService.GetProductByIdAsync(parsedProductId);

        if (product == null || product.Deleted) {
          return NotFoundError($"Product with id {product_id} not found.");
        }

        var manufacturerService = EngineContext.Current.Resolve<IManufacturerService>();
        var manufacturer = await manufacturerService.GetManufacturerByIdAsync(parsedManufacturerId);

        if (manufacturer == null || manufacturer.Deleted) {
          return NotFoundError($"Manufacturer with id {manufacturer_id} not found.");
        }

        var existingLinks = await manufacturerService.GetProductManufacturersByProductIdAsync(parsedProductId, true);

        if (existingLinks.Any(pm => pm.ManufacturerId == parsedManufacturerId)) {
          return ExistsError($"Manufacturer {parsedManufacturerId} is already linked to product {parsedProductId}.");
        }

        var link = new ProductManufacturer {
          ProductId = parsedProductId,
          ManufacturerId = parsedManufacturerId,
          IsFeaturedProduct = false,
          DisplayOrder = 0,
        };

        await manufacturerService.InsertProductManufacturerAsync(link);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {id = link.Id, linked = true},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Vendors

    [HttpPost("vendors-list")]
    public async Task<IActionResult> VendorsList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var vendorService = EngineContext.Current.Resolve<IVendorService>();
        var vendors = await vendorService.GetAllVendorsAsync();
        var result = vendors
          .Where(v => !v.Deleted)
          .Select(v => new { id = v.Id, name = v.Name, active = v.Active })
          .ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {vendors = result},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Manufacturer Query Builder

    private static IQueryable<Manufacturer> BuildFilteredManufacturerQuery(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo,
        string? published,
        string? findValue,
        string? findWhere = null,
        int storeId = 0,
        int[]? brandIds = null,
        int langId = 0
    )
    {
      var manufacturerRepository = EngineContext.Current.Resolve<IRepository<Manufacturer>>();
      var query = manufacturerRepository.Table.Where(m => !m.Deleted);

      if (brandIds != null && brandIds.Length == 0) {
        query = query.Where(m => false);
      } else if (brandIds != null && brandIds.Length > 0) {
        query = query.Where(m => brandIds.Contains(m.Id));
      }

      if (createdFrom.HasValue) {
        query = query.Where(m => m.CreatedOnUtc >= createdFrom.Value);
      }

      if (createdTo.HasValue) {
        query = query.Where(m => m.CreatedOnUtc <= createdTo.Value);
      }

      if (modifiedFrom.HasValue) {
        query = query.Where(m => m.UpdatedOnUtc >= modifiedFrom.Value);
      }

      if (modifiedTo.HasValue) {
        query = query.Where(m => m.UpdatedOnUtc <= modifiedTo.Value);
      }

      if (!string.IsNullOrEmpty(published)) {
        if (bool.TryParse(published, out var parsedBool)) {
          query = query.Where(m => m.Published == parsedBool);
        }
      }

      if (!string.IsNullOrEmpty(findValue)) {
        var searchInName = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("name", StringComparison.OrdinalIgnoreCase);
        var searchInDesc = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("description", StringComparison.OrdinalIgnoreCase);

        var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
        var lpQuery = lpRepository.Table.Where(lp => lp.LocaleKeyGroup == "Manufacturer");

        if (langId > 0) {
          lpQuery = lpQuery.Where(lp => lp.LanguageId == langId);
        }

        var localizedNameIds = lpQuery
          .Where(lp => lp.LocaleKey == "Name" && lp.LocaleValue.Contains(findValue))
          .Select(lp => lp.EntityId);

        var localizedDescIds = lpQuery
          .Where(lp => lp.LocaleKey == "Description" && lp.LocaleValue.Contains(findValue))
          .Select(lp => lp.EntityId);

        if (searchInName && searchInDesc) {
          query = query.Where(m =>
            m.Name.Contains(findValue) || m.Description.Contains(findValue)
            || localizedNameIds.Contains(m.Id) || localizedDescIds.Contains(m.Id));
        } else if (searchInName) {
          query = query.Where(m =>
            m.Name.Contains(findValue) || localizedNameIds.Contains(m.Id));
        } else if (searchInDesc) {
          query = query.Where(m =>
            m.Description.Contains(findValue) || localizedDescIds.Contains(m.Id));
        }
      }

      if (storeId > 0) {
        var storeMappingRepository = EngineContext.Current
          .Resolve<IRepository<Nop.Core.Domain.Stores.StoreMapping>>();
        var mappedManufacturerIds = storeMappingRepository.Table
          .Where(sm => sm.EntityName == "Manufacturer" && sm.StoreId == storeId)
          .Select(sm => sm.EntityId);

        query = query.Where(m => !m.LimitedToStores || mappedManufacturerIds.Contains(m.Id));
      }

      return query;
    }

    #endregion

    #region Manufacturer Data Builder

    private async Task<Dictionary<string, object?>> BuildManufacturerDataAsync(
        Manufacturer manufacturer,
        int langId,
        ILocalizationService localizationService,
        IUrlRecordService urlRecordService,
        IPictureService pictureService,
        IStoreMappingService storeMappingService,
        HashSet<string>? requestedFields = null
    )
    {
      var data = new Dictionary<string, object?>
      {
        ["id"] = manufacturer.Id,
        ["name"] = await localizationService.GetLocalizedAsync(manufacturer, m => m.Name, langId),
        ["description"] = await localizationService.GetLocalizedAsync(manufacturer, m => m.Description, langId),
        ["published"] = manufacturer.Published,
        ["display_order"] = manufacturer.DisplayOrder,
        ["created_at"] = manufacturer.CreatedOnUtc.ToString("o"),
        ["updated_at"] = manufacturer.UpdatedOnUtc.ToString("o"),
        ["meta_title"] = await localizationService.GetLocalizedAsync(manufacturer, m => m.MetaTitle, langId),
        ["meta_keywords"] = await localizationService.GetLocalizedAsync(manufacturer, m => m.MetaKeywords, langId),
        ["meta_description"] = await localizationService.GetLocalizedAsync(manufacturer, m => m.MetaDescription, langId),
      };

      if (IsFieldRequested(requestedFields, "seo_url")) {
        data["seo_url"] = await urlRecordService.GetSeNameAsync(manufacturer, langId);
      }

      if (IsFieldRequested(requestedFields, "image") && manufacturer.PictureId > 0) {
        var picture = await pictureService.GetPictureByIdAsync(manufacturer.PictureId);

        if (picture != null) {
          var (url, _) = await pictureService.GetPictureUrlAsync(picture);

          var fileName = url != null
            ? System.IO.Path.GetFileName(new Uri(url).LocalPath)
            : null;

          data["image"] = new Dictionary<string, object?>
          {
            ["id"] = picture.Id,
            ["url"] = url,
            ["mime_type"] = picture.MimeType,
            ["alt"] = picture.AltAttribute,
            ["file_name"] = fileName,
          };
        }
      }

      if (IsFieldRequested(requestedFields, "store_ids")) {
        if (manufacturer.LimitedToStores) {
          var storeIds = await storeMappingService.GetStoresIdsWithAccessAsync(manufacturer);

          data["store_ids"] = storeIds.ToArray();
        } else {
          var allStores = await EngineContext.Current.Resolve<IStoreService>().GetAllStoresAsync();

          data["store_ids"] = allStores.Select(s => s.Id).ToArray();
        }
      }

      return data;
    }

    #endregion
  }
}
