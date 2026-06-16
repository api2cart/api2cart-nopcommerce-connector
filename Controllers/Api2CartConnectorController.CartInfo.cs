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
    #region Cart / Store Info

    [HttpPost("validate")]
    public async Task<IActionResult> Validate()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      string keyId;

      try {
        keyId = CryptoHelper.KeyId;
      } catch (InvalidOperationException ex) {
        return ErrorResponse("CONNECTOR_NOT_CONFIGURED", ex);
      }

      var disabled = ScopeRegistry.ParseDisabledScopes(_resolvedSettings!.DisabledScopes);

      return JsonContent(
        new ConnectorResponse<object> {
          Result = new {
            status = "CONNECTOR_OK",
            key_id = keyId,
            plugin_version = PluginVersion,
            allowed_scopes = ScopeRegistry.GetAllowedScopes(disabled),
          },
        }
      );
    }

    [HttpPost("store-info")]
    public async Task<IActionResult> CartInfo()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var cartInfo = await GetCartInfoAsync();

        return JsonContent(new ConnectorResponse<CartInfoModel> { Result = cartInfo });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Cart Info Builders

    private async Task<CartInfoModel> GetCartInfoAsync()
    {
      var currentStore = await _storeContext.GetCurrentStoreAsync();

      var result = new CartInfoModel
      {
        Name                   = currentStore.Name,
        Url                    = currentStore.Url?.TrimEnd('/') + "/",
        Version                = NopVersion.FULL_VERSION,
        IgnoreStoreLimitations = false,
      };

      var stores = (await _storeService.GetAllStoresAsync()).OrderBy(s => s.Id);

      foreach (var store in stores) {
        result.StoresInfo.Add(await BuildStoreInfoAsync(store));
      }

      await BuildWarehousesAsync(result);
      await BuildShippingMethodsAsync(result);

      return result;
    }

    private async Task<StoreInfoModel> BuildStoreInfoAsync(Nop.Core.Domain.Stores.Store store)
    {
      var storeInfo = new StoreInfoModel
      {
        StoreId = store.Id.ToString(),
        Name    = store.Name,
        Url     = store.Url?.TrimEnd('/') + "/",
        Active  = true,
        IgnoreStoreLimitations = false,
      };

      var currencies = await _currencyService.GetAllCurrenciesAsync(storeId: store.Id);
      var primaryCurrencyId = _currencySettings.PrimaryStoreCurrencyId;

      if (currencies.Count > 0) {
        var primary = currencies.FirstOrDefault(c => c.Id == primaryCurrencyId) ?? currencies[0];

        storeInfo.Currency = new CurrencyModel
        {
          Code      = primary.CurrencyCode,
          Name      = primary.Name,
          IsDefault = true,
          Rate      = primary.Rate,
          Symbol    = primary.DisplayLocale,
        };
      }

      var languages = await _languageService.GetAllLanguagesAsync(storeId: store.Id);

      foreach (var lang in languages) {
        storeInfo.Languages.Add(new LanguageModel
        {
          Code      = lang.LanguageCulture,
          Name      = lang.Name,
          IsDefault = lang.Id == (languages.FirstOrDefault()?.Id ?? 0),
        });
      }

      var defaultLang = languages.FirstOrDefault();

      if (defaultLang != null) {
        var parts = defaultLang.LanguageCulture?.Split('-');

        if (parts?.Length == 2) {
          storeInfo.Country = parts[1];
        }
      }

      var measureSettings = await _settingService.LoadSettingAsync<MeasureSettings>(store.Id);

      if (measureSettings != null) {
        var measureService = EngineContext.Current.Resolve<IMeasureService>();
        var weight = await measureService.GetMeasureWeightByIdAsync(measureSettings.BaseWeightId);

        if (weight != null) {
          storeInfo.WeightUnit = weight.SystemKeyword;
        }

        var dimension = await measureService.GetMeasureDimensionByIdAsync(measureSettings.BaseDimensionId);

        if (dimension != null) {
          storeInfo.DimensionUnit = dimension.SystemKeyword;
        }
      }

      var dateTimeSettings = await _settingService.LoadSettingAsync<DateTimeSettings>(store.Id);

      if (!string.IsNullOrEmpty(dateTimeSettings?.DefaultStoreTimeZoneId)) {
        storeInfo.TimeZone = dateTimeSettings.DefaultStoreTimeZoneId;
      }

      var taxSettings = await _settingService.LoadSettingAsync<TaxSettings>(store.Id);

      if (taxSettings != null) {
        storeInfo.PricesIncludeTax = taxSettings.PricesIncludeTax;
      }

      var ownerInfo = new OwnerInfoModel();
      var hasOwnerData = false;

      if (!string.IsNullOrEmpty(store.CompanyAddress)) {
        ownerInfo.Address = store.CompanyAddress;
        hasOwnerData = true;
      }

      if (!string.IsNullOrEmpty(store.CompanyPhoneNumber)) {
        ownerInfo.Phone = store.CompanyPhoneNumber;
        hasOwnerData = true;
      }

      if (hasOwnerData) {
        storeInfo.OwnerInfo = ownerInfo;
      }

      foreach (var cur in currencies) {
        storeInfo.Currencies.Add(new CurrencyModel
        {
          Code      = cur.CurrencyCode,
          Name      = cur.Name,
          IsDefault = cur.Id == primaryCurrencyId,
          Rate      = cur.Rate,
          Symbol    = cur.DisplayLocale,
        });
      }

      return storeInfo;
    }

    private async Task BuildWarehousesAsync(CartInfoModel result)
    {
      var warehouseService = EngineContext.Current.Resolve<IWarehouseService>();
      var warehouses = await warehouseService.GetAllWarehousesAsync();

      if (warehouses.Count == 0) {
        return;
      }

      result.DefaultWarehouseId = warehouses[0].Id.ToString();

      var addressService = EngineContext.Current.Resolve<Nop.Services.Common.IAddressService>();
      var countryService = EngineContext.Current.Resolve<ICountryService>();
      var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();

      foreach (var wh in warehouses) {
        var warehouseModel = new WarehouseModel
        {
          Id   = wh.Id.ToString(),
          Name = wh.Name,
        };

        if (wh.AddressId > 0) {
          var addr = await addressService.GetAddressByIdAsync(wh.AddressId);

          if (addr != null) {
            warehouseModel.Address = await BuildWarehouseAddressAsync(addr, countryService, stateService);
          }
        }

        result.Warehouses.Add(warehouseModel);
      }
    }

    private async Task BuildShippingMethodsAsync(CartInfoModel result)
    {
      var shippingMethodsService = EngineContext.Current.Resolve<IShippingMethodsService>();
      var shippingMethods = await shippingMethodsService.GetAllShippingMethodsAsync();

      foreach (var method in shippingMethods) {
        result.ShippingMethods.Add(new ShippingMethodModel
        {
          Id      = method.Id.ToString(),
          Name    = method.Name,
          Enabled = true,
        });
      }
    }

    private static async Task<WarehouseAddressModel> BuildWarehouseAddressAsync(
        Nop.Core.Domain.Common.Address addr,
        ICountryService countryService,
        Nop.Services.Directory.IStateProvinceService stateService
    )
    {
      var model = new WarehouseAddressModel
      {
        Address1 = addr.Address1,
        Address2 = addr.Address2,
        City     = addr.City,
        ZipCode  = addr.ZipPostalCode,
        Phone    = addr.PhoneNumber,
      };

      if (addr.CountryId.HasValue && addr.CountryId.Value > 0) {
        var country = await countryService.GetCountryByIdAsync(addr.CountryId.Value);

        if (country != null) {
          model.Country = country.TwoLetterIsoCode;
        }
      }

      if (addr.StateProvinceId.HasValue && addr.StateProvinceId.Value > 0) {
        var state = await stateService.GetStateProvinceByIdAsync(addr.StateProvinceId.Value);

        if (state != null) {
          model.State = state.Abbreviation ?? state.Name;
        }
      }

      return model;
    }

    #endregion
  }
}
