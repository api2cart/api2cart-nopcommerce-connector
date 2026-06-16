using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Catalog;
using Nop.Services.Localization;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Specification Attributes

    [HttpPost("specification-attributes-list")]
    public async Task<IActionResult> SpecificationAttributesList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? attribute_ids = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var langId = await ResolveLanguageIdAsync(language_id);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);
        var parsedIds = ParseIntIds(attribute_ids);

        var specAttrRepository = EngineContext.Current.Resolve<IRepository<SpecificationAttribute>>();
        var specAttrService = EngineContext.Current.Resolve<ISpecificationAttributeService>();
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();

        var query = specAttrRepository.Table.AsQueryable();

        if (!string.IsNullOrEmpty(attribute_ids)) {
          if (parsedIds == null || parsedIds.Length == 0) {
            return JsonContent(
              new ConnectorResponse<object> {
                Result = new { specification_attributes = Array.Empty<object>(), total_count = 0 },
              }
            );
          }

          query = query.Where(a => parsedIds.Contains(a.Id));
        }

        var totalCount = query.Count();
        var items = query
          .OrderBy(a => a.DisplayOrder)
          .ThenBy(a => a.Id)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        var result = new List<Dictionary<string, object?>>();

        foreach (var attr in items) {
          result.Add(await BuildSpecificationAttributeDataAsync(
            attr,
            langId,
            specAttrService,
            localizationService
            )
          );
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {specification_attributes = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("specification-attributes-count")]
    public async Task<IActionResult> SpecificationAttributesCount(
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var specAttrRepository = EngineContext.Current.Resolve<IRepository<SpecificationAttribute>>();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {count = specAttrRepository.Table.Count()},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("specification-attributes-info")]
    public async Task<IActionResult> SpecificationAttributesInfo(
        [FromQuery] string? id = null,
        [FromQuery] string? language_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var attrId)) {
          return NotFoundError($"Specification attribute with id {id} not found.");
        }

        var langId = await ResolveLanguageIdAsync(language_id);
        var specAttrService = EngineContext.Current.Resolve<ISpecificationAttributeService>();
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();

        var attr = await specAttrService.GetSpecificationAttributeByIdAsync(attrId);

        if (attr == null) {
          return NotFoundError($"Specification attribute with id {id} not found.");
        }

        var data = await BuildSpecificationAttributeDataAsync(
          attr,
          langId,
          specAttrService,
          localizationService
        );

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("product-specification-attributes-list")]
    public async Task<IActionResult> ProductSpecificationAttributesList(
        [FromQuery] string? product_id = null,
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? language_id = null,
        [FromQuery] int attribute_id = 0
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

        var langId = await ResolveLanguageIdAsync(language_id);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var specAttrService = EngineContext.Current.Resolve<ISpecificationAttributeService>();
        var localizationService = EngineContext.Current.Resolve<ILocalizationService>();

        var mappings = await specAttrService.GetProductSpecificationAttributesAsync(parsedProductId);

        if (attribute_id > 0) {
          var options = await specAttrService
            .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attribute_id);
          var optionIds = options.Select(o => o.Id).ToHashSet();

          mappings = mappings.Where(m => optionIds.Contains(m.SpecificationAttributeOptionId)).ToList();
        }

        var totalCount = mappings.Count;
        var pagedMappings = mappings
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        SpecificationAttribute? preloadedAttr = null;

        if (attribute_id > 0) {
          preloadedAttr = await specAttrService.GetSpecificationAttributeByIdAsync(attribute_id);
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var mapping in pagedMappings) {
          var option = await specAttrService.GetSpecificationAttributeOptionByIdAsync(
            mapping.SpecificationAttributeOptionId);

          SpecificationAttribute? specAttr = preloadedAttr;

          if (specAttr == null && option != null) {
            specAttr = await specAttrService.GetSpecificationAttributeByIdAsync(
              option.SpecificationAttributeId);
          }

          if (specAttr == null) {
            continue;
          }

          var attrName = await localizationService.GetLocalizedAsync(specAttr, a => a.Name, langId);
          string? value;

          if (mapping.AttributeTypeId == 0 && option != null) {
            value = await localizationService.GetLocalizedAsync(option, o => o.Name, langId);
          } else {
            value = mapping.CustomValue;
          }

          string? groupName = null;
          int? groupId = specAttr.SpecificationAttributeGroupId;

          if (groupId.HasValue && groupId.Value > 0) {
            var group = await specAttrService.GetSpecificationAttributeGroupByIdAsync(groupId.Value);

            if (group != null) {
              groupName = await localizationService.GetLocalizedAsync(group, g => g.Name, langId);
            }
          }

          result.Add(new Dictionary<string, object?>
          {
            ["id"] = mapping.Id,
            ["product_id"] = product_id,
            ["specification_attribute_id"] = specAttr.Id,
            ["specification_attribute_name"] = attrName,
            ["specification_attribute_option_id"] = option?.Id,
            ["value"] = value,
            ["attribute_type_id"] = mapping.AttributeTypeId,
            ["custom_value"] = mapping.CustomValue,
            ["allow_filtering"] = mapping.AllowFiltering,
            ["show_on_product_page"] = mapping.ShowOnProductPage,
            ["display_order"] = mapping.DisplayOrder,
            ["group_id"] = groupId,
            ["group_name"] = groupName,
          });
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {product_specification_attributes = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Specification Attribute Data Builder

    private static async Task<Dictionary<string, object?>> BuildSpecificationAttributeDataAsync(
        SpecificationAttribute attr,
        int langId,
        ISpecificationAttributeService specAttrService,
        ILocalizationService localizationService
    )
    {
      var attrName = await localizationService.GetLocalizedAsync(attr, a => a.Name, langId);
      var options = await specAttrService
        .GetSpecificationAttributeOptionsBySpecificationAttributeAsync(attr.Id);

      var optionList = new List<Dictionary<string, object?>>();

      foreach (var option in options.OrderBy(o => o.DisplayOrder).ThenBy(o => o.Id)) {
        var optionName = await localizationService.GetLocalizedAsync(option, o => o.Name, langId);

        optionList.Add(new Dictionary<string, object?>
        {
          ["id"] = option.Id,
          ["name"] = optionName,
          ["display_order"] = option.DisplayOrder,
          ["color_square_rgb"] = option.ColorSquaresRgb,
        });
      }

      string? groupName = null;
      int? groupId = attr.SpecificationAttributeGroupId;

      if (groupId.HasValue && groupId.Value > 0) {
        var group = await specAttrService.GetSpecificationAttributeGroupByIdAsync(groupId.Value);

        if (group != null) {
          groupName = await localizationService.GetLocalizedAsync(group, g => g.Name, langId);
        }
      }

      return new Dictionary<string, object?>
      {
        ["id"] = attr.Id,
        ["name"] = attrName,
        ["display_order"] = attr.DisplayOrder,
        ["group_id"] = groupId,
        ["group_name"] = groupName,
        ["options"] = optionList,
      };
    }

    #endregion
  }
}
