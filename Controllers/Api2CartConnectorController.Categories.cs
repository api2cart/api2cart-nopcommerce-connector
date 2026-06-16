using System.Net;
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
    #region Categories

    [HttpPost("categories-list")]
    public async Task<IActionResult> CategoriesList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] int? parent_id = null,
        [FromQuery] int store_id = 0,
        [FromQuery] string? ids = null,
        [FromQuery] string? published = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] string? find_value = null,
        [FromQuery] string? find_where = null,
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

        var langId = await ResolveLanguageIdAsync(language_id);

        var query = BuildFilteredCategoryQuery(
          createdFrom,
          createdTo,
          modifiedFrom,
          modifiedTo,
          parent_id,
          published,
          find_value,
          find_where,
          store_id,
          langId
        );

        if (!string.IsNullOrEmpty(ids)) {
          var filterIds = ParseIntIds(ids);

          if (filterIds == null || filterIds.Length == 0) {
            return JsonContent(
              new ConnectorResponse<object> {
                Result = new {categories = Array.Empty<object>(), total_count = 0},
              }
            );
          }

          query = query.Where(c => filterIds.Contains(c.Id));
        }

        var totalCount = query.Count();
        var categories = query
          .OrderBy(c => c.DisplayOrder)
          .ThenBy(c => c.Id)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();
        var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

        var result = new List<Dictionary<string, object?>>();

        foreach (var category in categories) {
          result.Add(await BuildCategoryDataAsync(
            category,
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
            Result = new {categories = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-count")]
    public async Task<IActionResult> CategoriesCount(
        [FromQuery] int? parent_id = null,
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

        var query = BuildFilteredCategoryQuery(
          ParseDateFilter(created_from),
          ParseDateFilter(created_to, isUpperBound: true),
          ParseDateFilter(modified_from),
          ParseDateFilter(modified_to, isUpperBound: true),
          parent_id,
          published,
          find_value,
          find_where,
          store_id,
          langId
        );

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {count = query.Count()},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-info")]
    public async Task<IActionResult> CategoriesInfo(
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
        if (!TryParsePositiveInt(id, out var categoryId)) {
          return NotFoundError($"Category with id {id} not found.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {id} not found.");
        }

        var langId = await ResolveLanguageIdAsync(language_id);
        var requestedFields = ParseFields(fields);
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();
        var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

        var data = await BuildCategoryDataAsync(
          category,
          langId,
          localizationService,
          urlRecordService,
          pictureService,
          storeMappingService,
          requestedFields
        );

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Category Query Builder

    private static IQueryable<Category> BuildFilteredCategoryQuery(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo,
        int? parentId,
        string? published,
        string? findValue,
        string? findWhere = null,
        int storeId = 0,
        int langId = 0
    )
    {
      var categoryRepository = EngineContext.Current.Resolve<IRepository<Category>>();
      var query = categoryRepository.Table.Where(c => !c.Deleted);

      if (createdFrom.HasValue) {
        query = query.Where(c => c.CreatedOnUtc >= createdFrom.Value);
      }

      if (createdTo.HasValue) {
        query = query.Where(c => c.CreatedOnUtc <= createdTo.Value);
      }

      if (modifiedFrom.HasValue) {
        query = query.Where(c => c.UpdatedOnUtc >= modifiedFrom.Value);
      }

      if (modifiedTo.HasValue) {
        query = query.Where(c => c.UpdatedOnUtc <= modifiedTo.Value);
      }

      if (parentId.HasValue) {
        query = query.Where(c => c.ParentCategoryId == parentId.Value);
      }

      if (!string.IsNullOrEmpty(published)) {
        if (bool.TryParse(published, out var parsedBool)) {
          query = query.Where(c => c.Published == parsedBool);
        }
      }

      if (!string.IsNullOrEmpty(findValue)) {
        var searchInName = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("name", StringComparison.OrdinalIgnoreCase);
        var searchInDesc = string.IsNullOrEmpty(findWhere)
          || findWhere.Contains("description", StringComparison.OrdinalIgnoreCase);

        var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
        var lpQuery = lpRepository.Table.Where(lp => lp.LocaleKeyGroup == "Category");

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
          query = query.Where(c =>
            c.Name.Contains(findValue) || c.Description.Contains(findValue)
            || localizedNameIds.Contains(c.Id) || localizedDescIds.Contains(c.Id));
        } else if (searchInName) {
          query = query.Where(c =>
            c.Name.Contains(findValue) || localizedNameIds.Contains(c.Id));
        } else if (searchInDesc) {
          query = query.Where(c =>
            c.Description.Contains(findValue) || localizedDescIds.Contains(c.Id));
        }
      }

      if (storeId > 0) {
        var storeMappingRepository = EngineContext.Current
          .Resolve<IRepository<Nop.Core.Domain.Stores.StoreMapping>>();
        var mappedCategoryIds = storeMappingRepository.Table
          .Where(sm => sm.EntityName == "Category" && sm.StoreId == storeId)
          .Select(sm => sm.EntityId);

        query = query.Where(c => !c.LimitedToStores || mappedCategoryIds.Contains(c.Id));
      }

      return query;
    }

    #endregion

    #region Category Data Builder

    private async Task<Dictionary<string, object?>> BuildCategoryDataAsync(
        Category category,
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
        ["id"] = category.Id,
        ["name"] = await localizationService.GetLocalizedAsync(category, c => c.Name, langId),
        ["description"] = await localizationService.GetLocalizedAsync(category, c => c.Description, langId),
        ["parent_category_id"] = category.ParentCategoryId,
        ["published"] = category.Published,
        ["display_order"] = category.DisplayOrder,
        ["created_at"] = category.CreatedOnUtc.ToString("o"),
        ["updated_at"] = category.UpdatedOnUtc.ToString("o"),
        ["meta_title"] = await localizationService.GetLocalizedAsync(category, c => c.MetaTitle, langId),
        ["meta_keywords"] = await localizationService.GetLocalizedAsync(category, c => c.MetaKeywords, langId),
        ["meta_description"] = await localizationService.GetLocalizedAsync(category, c => c.MetaDescription, langId),
      };

      if (IsFieldRequested(requestedFields, "seo_url")) {
        data["seo_url"] = await urlRecordService.GetSeNameAsync(category, langId);
      }

      if (IsFieldRequested(requestedFields, "image") && category.PictureId > 0) {
        var picture = await pictureService.GetPictureByIdAsync(category.PictureId);

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
        if (category.LimitedToStores) {
          var storeIds = await storeMappingService.GetStoresIdsWithAccessAsync(category);

          data["store_ids"] = storeIds.ToArray();
        } else {
          var allStores = await EngineContext.Current.Resolve<IStoreService>().GetAllStoresAsync();

          data["store_ids"] = allStores.Select(s => s.Id).ToArray();
        }
      }

      return data;
    }

    #endregion

    #region Category Write Actions

    [HttpPost("categories-add")]
    public async Task<IActionResult> CategoriesAdd(
        [FromQuery] string? name = null,
        [FromQuery] string? parent_id = null,
        [FromQuery] string? published = null,
        [FromQuery] string? display_order = null,
        [FromQuery] string? description = null,
        [FromQuery] string? meta_title = null,
        [FromQuery] string? meta_keywords = null,
        [FromQuery] string? meta_description = null,
        [FromQuery] string? seo_url = null,
        [FromQuery] string? stores_ids = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var trimmedName = (name ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmedName)) {
          return ParamError("Parameter 'name' is required.");
        }

        var parentCategoryId = TryParsePositiveInt(parent_id, out var parsedParentId) ? parsedParentId : 0;
        var categoryService = EngineContext.Current.Resolve<ICategoryService>();

        if (parentCategoryId > 0) {
          var parent = await categoryService.GetCategoryByIdAsync(parentCategoryId);

          if (parent == null || parent.Deleted) {
            return NotFoundError($"Category with id {parentCategoryId} not found.");
          }
        }

        var storeIds = ParseIntIds(stores_ids);

        var category = new Category
        {
          Name = trimmedName,
          ParentCategoryId = parentCategoryId,
          CategoryTemplateId = 1,
          PageSize = 6,
          AllowCustomersToSelectPageSize = true,
          PageSizeOptions = "6, 3, 9",
          Published = ParseBool(published, fallback: true),
          DisplayOrder = ParseInt(display_order, 0),
          Description = description,
          MetaTitle = meta_title,
          MetaKeywords = meta_keywords,
          MetaDescription = meta_description,
          LimitedToStores = storeIds != null && storeIds.Length > 0,
          CreatedOnUtc = DateTime.UtcNow,
          UpdatedOnUtc = DateTime.UtcNow,
        };

        await categoryService.InsertCategoryAsync(category);
        await ApplyStoreMappingsAsync(category, storeIds);
        var slug = await SaveCategorySlugAsync(category, seo_url);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {id = category.Id, seo_url = slug},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-update")]
    public async Task<IActionResult> CategoriesUpdate(
        [FromQuery] string? id = null,
        [FromQuery] string? name = null,
        [FromQuery] string? parent_id = null,
        [FromQuery] string? published = null,
        [FromQuery] string? display_order = null,
        [FromQuery] string? description = null,
        [FromQuery] string? meta_title = null,
        [FromQuery] string? meta_keywords = null,
        [FromQuery] string? meta_description = null,
        [FromQuery] string? seo_url = null,
        [FromQuery] string? store_id = null,
        [FromQuery] string? stores_ids = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var categoryId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {categoryId} not found.");
        }

        var parentIdProvided = !string.IsNullOrEmpty(parent_id);
        var parentCategoryId = 0;

        if (parentIdProvided) {
          if (!int.TryParse(parent_id, out parentCategoryId) || parentCategoryId < 0) {
            return ParamError($"Invalid parent_id value: {parent_id}");
          }

          if (parentCategoryId == categoryId) {
            return ParamError("Category can't be a parent for itself.");
          }

          if (parentCategoryId > 0) {
            var parent = await categoryService.GetCategoryByIdAsync(parentCategoryId);

            if (parent == null || parent.Deleted) {
              return NotFoundError($"Category with id {parentCategoryId} not found.");
            }

            if (await IsDescendantAsync(categoryService, parentCategoryId, categoryId)) {
              return ParamError($"Category {parentCategoryId} is a descendant of {categoryId} and can't be set as parent.");
            }
          }
        }

        var langId = await ResolveLanguageIdAsync(language_id);

        if (!string.IsNullOrEmpty(language_id) && langId == 0) {
          return NotFoundError($"Language with id {language_id} not found.");
        }

        if (name != null) {
          var trimmedName = name.Trim();

          if (trimmedName.Length == 0) {
            return ParamError("The value of parameter 'name' can not be empty.");
          }

          if (langId > 0) {
            await UpsertLocalizedCategoryPropertyAsync(categoryId, langId, "Name", trimmedName);
          } else {
            category.Name = trimmedName;
          }
        }

        if (parentIdProvided) {
          category.ParentCategoryId = parentCategoryId;
        }

        if (published != null) {
          category.Published = ParseBool(published, fallback: true);
        }

        if (display_order != null && int.TryParse(display_order, out var parsedDisplayOrder)) {
          category.DisplayOrder = parsedDisplayOrder;
        }

        if (description != null) {
          if (langId > 0) {
            await UpsertLocalizedCategoryPropertyAsync(categoryId, langId, "Description", description);
          } else {
            category.Description = description;
          }
        }

        if (meta_title != null) {
          if (langId > 0) {
            await UpsertLocalizedCategoryPropertyAsync(categoryId, langId, "MetaTitle", meta_title);
          } else {
            category.MetaTitle = meta_title;
          }
        }

        if (meta_keywords != null) {
          if (langId > 0) {
            await UpsertLocalizedCategoryPropertyAsync(categoryId, langId, "MetaKeywords", meta_keywords);
          } else {
            category.MetaKeywords = meta_keywords;
          }
        }

        if (meta_description != null) {
          if (langId > 0) {
            await UpsertLocalizedCategoryPropertyAsync(categoryId, langId, "MetaDescription", meta_description);
          } else {
            category.MetaDescription = meta_description;
          }
        }

        var storesIdsProvided = stores_ids != null || store_id != null;
        int[]? storeIds = null;

        if (store_id != null) {
          if (!TryParsePositiveInt(store_id, out var parsedStoreId)) {
            return NotFoundError($"Store with id {store_id} not found.");
          }

          var storeService = EngineContext.Current.Resolve<IStoreService>();
          var store = await storeService.GetStoreByIdAsync(parsedStoreId);

          if (store == null) {
            return NotFoundError($"Store with id {parsedStoreId} not found.");
          }

          storeIds = new[] {parsedStoreId};
          category.LimitedToStores = true;
        } else if (stores_ids != null) {
          storeIds = ParseIntIds(stores_ids);
          category.LimitedToStores = storeIds != null && storeIds.Length > 0;
        }

        category.UpdatedOnUtc = DateTime.UtcNow;
        await categoryService.UpdateCategoryAsync(category);

        if (storesIdsProvided) {
          await ReplaceStoreMappingsAsync(category, storeIds);
        }

        var slug = await SaveCategorySlugAsync(category, seo_url, langId);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {updated = true, seo_url = slug},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-delete")]
    public async Task<IActionResult> CategoriesDelete([FromQuery] string? id = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var categoryId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {categoryId} not found.");
        }

        if (category.PictureId > 0) {
          var pictureService = EngineContext.Current.Resolve<IPictureService>();
          var picture = await pictureService.GetPictureByIdAsync(category.PictureId);

          if (picture != null) {
            await pictureService.DeletePictureAsync(picture);
          }
        }

        await categoryService.DeleteCategoryAsync(category);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {deleted = true},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-image-upsert")]
    public async Task<IActionResult> CategoriesImageUpsert(
        [FromQuery] string? category_id = null,
        [FromQuery] string? base64_content = null,
        [FromQuery] string? url = null,
        [FromQuery] string? mime_type = null,
        [FromQuery] string? file_name = null,
        [FromQuery] string? alt = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(category_id, out var categoryId)) {
          return ParamError("Parameter 'category_id' is required.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {categoryId} not found.");
        }

        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var (pictureBytes, resolvedMime, resolveError) = await ResolveImageContentAsync(base64_content, url, mime_type);

        if (resolveError != null) {
          return resolveError;
        }

        if (pictureBytes == null || pictureBytes.Length == 0) {
          return ParamError("Image content is empty.");
        }

        var seoFilename = BuildSeoFilename(file_name, category.Name);
        var altText = alt ?? string.Empty;

        if (category.PictureId > 0) {
          var oldPicture = await pictureService.GetPictureByIdAsync(category.PictureId);

          if (oldPicture != null) {
            await pictureService.DeletePictureAsync(oldPicture);
          }
        }

        var newPicture = await pictureService.InsertPictureAsync(
          pictureBytes,
          resolvedMime,
          seoFilename,
          altText,
          string.Empty,
          true,
          true
        );

        category.PictureId = newPicture.Id;
        category.UpdatedOnUtc = DateTime.UtcNow;
        await categoryService.UpdateCategoryAsync(category);

        var (pictureUrl, _) = await pictureService.GetPictureUrlAsync(newPicture);
        var resolvedFileName = pictureUrl != null
          ? System.IO.Path.GetFileName(new Uri(pictureUrl).LocalPath)
          : seoFilename;

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              id = newPicture.Id,
              url = pictureUrl,
              file_name = resolvedFileName,
            },
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("categories-image-delete")]
    public async Task<IActionResult> CategoriesImageDelete([FromQuery] string? category_id = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(category_id, out var categoryId)) {
          return ParamError("Parameter 'category_id' is required.");
        }

        var categoryService = EngineContext.Current.Resolve<ICategoryService>();
        var category = await categoryService.GetCategoryByIdAsync(categoryId);

        if (category == null || category.Deleted) {
          return NotFoundError($"Category with id {categoryId} not found.");
        }

        if (category.PictureId == 0) {
          return NotFoundError("The category has no image.");
        }

        var pictureService = EngineContext.Current.Resolve<IPictureService>();
        var picture = await pictureService.GetPictureByIdAsync(category.PictureId);

        if (picture != null) {
          await pictureService.DeletePictureAsync(picture);
        }

        category.PictureId = 0;
        category.UpdatedOnUtc = DateTime.UtcNow;
        await categoryService.UpdateCategoryAsync(category);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {deleted = true},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Category Write Helpers

    private static async Task<string> SaveCategorySlugAsync(Category category, string? requestedSlug, int languageId = 0)
    {
      var urlRecordService = EngineContext.Current.Resolve<IUrlRecordService>();
      string? localizedName = null;

      if (languageId > 0) {
        var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
        localizedName = lpRepository.Table.FirstOrDefault(lp =>
          lp.EntityId == category.Id
          && lp.LanguageId == languageId
          && lp.LocaleKeyGroup == "Category"
          && lp.LocaleKey == "Name")?.LocaleValue;
      }

      var defaultSource = !string.IsNullOrWhiteSpace(localizedName) ? localizedName! : category.Name;
      var source = string.IsNullOrWhiteSpace(requestedSlug) ? defaultSource : requestedSlug;
      var seName = await urlRecordService.ValidateSeNameAsync(category, source, category.Name, true);
      await urlRecordService.SaveSlugAsync(category, seName, languageId);

      return seName;
    }

    private static async Task UpsertLocalizedCategoryPropertyAsync(int categoryId, int languageId, string localeKey, string localeValue)
    {
      var lpRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Localization.LocalizedProperty>>();
      var localizedProperty = lpRepository.Table.FirstOrDefault(lp =>
        lp.EntityId == categoryId
        && lp.LanguageId == languageId
        && lp.LocaleKeyGroup == "Category"
        && lp.LocaleKey == localeKey);

      if (localizedProperty == null) {
        var entity = new Nop.Core.Domain.Localization.LocalizedProperty
        {
          EntityId = categoryId,
          LanguageId = languageId,
          LocaleKeyGroup = "Category",
          LocaleKey = localeKey,
          LocaleValue = localeValue,
        };
        await lpRepository.InsertAsync(entity);

        return;
      }

      localizedProperty.LocaleValue = localeValue;
      await lpRepository.UpdateAsync(localizedProperty);
    }

    private static async Task ApplyStoreMappingsAsync(Category category, int[]? storeIds)
    {
      if (storeIds == null || storeIds.Length == 0) {
        return;
      }

      var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();

      foreach (var storeId in storeIds) {
        await storeMappingService.InsertStoreMappingAsync(category, storeId);
      }
    }

    private static async Task ReplaceStoreMappingsAsync(Category category, int[]? storeIds)
    {
      var storeMappingService = EngineContext.Current.Resolve<IStoreMappingService>();
      var existing = await storeMappingService.GetStoreMappingsAsync(category);

      foreach (var mapping in existing) {
        await storeMappingService.DeleteStoreMappingAsync(mapping);
      }

      if (storeIds == null || storeIds.Length == 0) {
        return;
      }

      foreach (var storeId in storeIds) {
        await storeMappingService.InsertStoreMappingAsync(category, storeId);
      }
    }

    private static async Task<bool> IsDescendantAsync(ICategoryService categoryService, int candidateParentId, int categoryId)
    {
      var current = candidateParentId;
      var visited = new HashSet<int>();

      while (current > 0 && visited.Add(current)) {
        var node = await categoryService.GetCategoryByIdAsync(current);

        if (node == null || node.Deleted) {
          return false;
        }

        if (node.ParentCategoryId == categoryId) {
          return true;
        }

        current = node.ParentCategoryId;
      }

      return false;
    }

    private const long MaxImageSizeBytes = 10L * 1024 * 1024;

    private async Task<(byte[]? bytes, string mime, IActionResult? error)> ResolveImageContentAsync(
        string? base64Content, string? url, string? mimeType)
    {
      if (!string.IsNullOrEmpty(base64Content)) {
        try {
          var bytes = Convert.FromBase64String(base64Content);

          if (bytes.Length > MaxImageSizeBytes) {
            return (null, string.Empty, ParamError($"Image too large: {bytes.Length} bytes (max {MaxImageSizeBytes})."));
          }

          var resolvedMime = string.IsNullOrEmpty(mimeType) ? DetectMimeType(bytes) : mimeType;

          if (!IsAllowedImageMime(resolvedMime)) {
            return (null, string.Empty, ParamError($"Unsupported image mime type: {resolvedMime}"));
          }

          return (bytes, resolvedMime, null);
        } catch (FormatException ex) {
          return (null, string.Empty, ParamError($"Invalid base64_content: {ex.Message}"));
        }
      }

      if (string.IsNullOrEmpty(url)) {
        return (null, string.Empty, ParamError("Either 'url' or 'base64_content' is required."));
      }

      var urlError = await ValidateDownloadUrlAsync(url);

      if (urlError != null) {
        return (null, string.Empty, ParamError(urlError));
      }

      try {
        using var http = new HttpClient {
          Timeout = TimeSpan.FromSeconds(30),
          MaxResponseContentBufferSize = MaxImageSizeBytes,
        };
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode) {
          return (null, string.Empty, ParamError($"Failed to download image. HTTP {(int)response.StatusCode}"));
        }

        if (response.Content.Headers.ContentLength is long declaredLength && declaredLength > MaxImageSizeBytes) {
          return (null, string.Empty, ParamError($"Image too large: {declaredLength} bytes (max {MaxImageSizeBytes})."));
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();

        if (bytes.Length > MaxImageSizeBytes) {
          return (null, string.Empty, ParamError($"Image too large: {bytes.Length} bytes (max {MaxImageSizeBytes})."));
        }

        var resolvedMime = response.Content.Headers.ContentType?.MediaType;

        if (string.IsNullOrEmpty(resolvedMime)) {
          resolvedMime = DetectMimeType(bytes);
        }

        if (!IsAllowedImageMime(resolvedMime)) {
          return (null, string.Empty, ParamError($"Unsupported image mime type: {resolvedMime}"));
        }

        return (bytes, resolvedMime, null);
      } catch (Exception) {
        return (null, string.Empty, ParamError("Image download failed."));
      }
    }

    private static async Task<string?> ValidateDownloadUrlAsync(string url)
    {
      if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
        return "Invalid URL.";
      }

      if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) {
        return $"URL scheme '{uri.Scheme}' is not allowed. Use http or https.";
      }

      IPAddress[] addresses;

      if (IPAddress.TryParse(uri.Host, out var directIp)) {
        addresses = new[] {directIp};
      } else {
        try {
          addresses = await Dns.GetHostAddressesAsync(uri.Host);
        } catch {
          return $"Failed to resolve host: {uri.Host}.";
        }
      }

      foreach (var addr in addresses) {
        if (IsPrivateOrLoopback(addr)) {
          return "URL points to a private, loopback, or link-local address.";
        }
      }

      return null;
    }

    private static bool IsPrivateOrLoopback(IPAddress ip)
    {
      if (IPAddress.IsLoopback(ip)) {
        return true;
      }

      var bytes = ip.GetAddressBytes();

      if (bytes.Length == 4) {
        if (bytes[0] == 10) {
          return true;
        }

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) {
          return true;
        }

        if (bytes[0] == 192 && bytes[1] == 168) {
          return true;
        }

        if (bytes[0] == 169 && bytes[1] == 254) {
          return true;
        }

        if (bytes[0] == 127) {
          return true;
        }
      }

      if (bytes.Length == 16) {
        if ((bytes[0] & 0xFE) == 0xFC) {
          return true;
        }

        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) {
          return true;
        }
      }

      return false;
    }

    private static string DetectMimeType(byte[] bytes)
    {
      if (bytes.Length < 4) {
        return "application/octet-stream";
      }

      if (bytes[0] == 0xFF && bytes[1] == 0xD8) {
        return "image/jpeg";
      }

      if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) {
        return "image/png";
      }

      if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) {
        return "image/gif";
      }

      if (bytes.Length >= 12
        && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
        && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
      {
        return "image/webp";
      }

      return "application/octet-stream";
    }

    private static bool IsAllowedImageMime(string? mimeType)
    {
      return mimeType is "image/jpeg" or "image/jpg" or "image/png" or "image/gif" or "image/webp";
    }

    private static string BuildSeoFilename(string? requestedName, string fallbackName)
    {
      var baseName = !string.IsNullOrWhiteSpace(requestedName)
        ? System.IO.Path.GetFileNameWithoutExtension(requestedName)
        : fallbackName;

      if (string.IsNullOrWhiteSpace(baseName)) {
        baseName = "image";
      }

      return new string(baseName.ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-')
        .ToArray()).Trim('-');
    }

    private static bool ParseBool(string? value, bool fallback)
    {
      if (string.IsNullOrEmpty(value)) {
        return fallback;
      }

      return bool.TryParse(value, out var result) ? result : fallback;
    }

    private static int ParseInt(string? value, int fallback)
    {
      return int.TryParse(value, out var result) ? result : fallback;
    }

    #endregion
  }
}
