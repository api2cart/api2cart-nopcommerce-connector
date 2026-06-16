using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Data;
using Nop.Services.Directory;
using Nop.Services.Plugins;
using Nop.Services.Tax;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Tax Classes

    [HttpPost("tax-classes-list")]
    public async Task<IActionResult> TaxClassesList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var taxCategoryService = EngineContext.Current.Resolve<ITaxCategoryService>();
        var allCategories = await taxCategoryService.GetAllTaxCategoriesAsync();

        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);
        var totalCount = allCategories.Count;

        var items = allCategories
          .OrderBy(t => t.DisplayOrder)
          .ThenBy(t => t.Id)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .Select(t => new { id = t.Id, name = t.Name, display_order = t.DisplayOrder })
          .ToList();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {tax_classes = items, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("tax-classes-info")]
    public async Task<IActionResult> TaxClassesInfo(
        [FromQuery] string? id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var taxId)) {
          return NotFoundError($"Tax class with id {id} not found.");
        }

        var taxCategoryService = EngineContext.Current.Resolve<ITaxCategoryService>();
        var taxCategory = await taxCategoryService.GetTaxCategoryByIdAsync(taxId);

        if (taxCategory == null) {
          return NotFoundError($"Tax class with id {id} not found.");
        }

        var store = await _storeContext.GetCurrentStoreAsync();
        var taxSettings = await _settingService.LoadSettingAsync<TaxSettings>(store.Id);

        var taxBasedOn = taxSettings.TaxBasedOn switch
        {
          TaxBasedOn.BillingAddress => "billing",
          TaxBasedOn.ShippingAddress => "shipping",
          _ => "shipping",
        };

        var rates = await LoadTaxRatesAsync(taxId);

        var data = new Dictionary<string, object?>
        {
          ["id"] = taxCategory.Id,
          ["name"] = taxCategory.Name,
          ["display_order"] = taxCategory.DisplayOrder,
          ["tax_based_on"] = taxBasedOn,
          ["tax_rates"] = rates,
        };

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Tax Helpers

    private async Task<List<Dictionary<string, object?>>> LoadTaxRatesAsync(int taxCategoryId)
    {
      var result = new List<Dictionary<string, object?>>();

      var pluginService = EngineContext.Current.Resolve<IPluginService>();
      var descriptor = await pluginService
        .GetPluginDescriptorBySystemNameAsync<ITaxProvider>("Tax.FixedOrByCountryStateZip");

      if (descriptor?.Installed != true) {
        return result;
      }

      var taxRateRepo = EngineContext.Current.Resolve<IRepository<TaxRateRecord>>();

      var rates = taxRateRepo.Table
        .Where(r => r.TaxCategoryId == taxCategoryId)
        .ToList();

      if (!rates.Any()) {
        return result;
      }

      var countryService = EngineContext.Current.Resolve<ICountryService>();
      var stateService = EngineContext.Current.Resolve<IStateProvinceService>();

      foreach (var rate in rates) {
        var rateData = new Dictionary<string, object?>
        {
          ["id"] = rate.Id,
          ["percentage"] = rate.Percentage,
          ["country_id"] = rate.CountryId,
          ["country_name"] = null as string,
          ["country_code"] = null as string,
          ["state_id"] = rate.StateProvinceId,
          ["state_name"] = null as string,
          ["state_code"] = null as string,
          ["zip"] = rate.Zip,
        };

        if (rate.CountryId > 0) {
          var country = await countryService.GetCountryByIdAsync(rate.CountryId);

          if (country != null) {
            rateData["country_name"] = country.Name;
            rateData["country_code"] = country.TwoLetterIsoCode;
          }
        }

        if (rate.StateProvinceId > 0) {
          var state = await stateService.GetStateProvinceByIdAsync(rate.StateProvinceId);

          if (state != null) {
            rateData["state_name"] = state.Name;
            rateData["state_code"] = state.Abbreviation;
          }
        }

        result.Add(rateData);
      }

      return result;
    }

    #endregion
  }
}
