using System.Text.Json.Serialization;

namespace Api2Cart.Connector.Models
{
  public class CartInfoModel
  {
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("stores_info")]
    public List<StoreInfoModel> StoresInfo { get; set; } = new();

    [JsonPropertyName("warehouses")]
    public List<WarehouseModel> Warehouses { get; set; } = new();

    [JsonPropertyName("shipping_methods")]
    public List<ShippingMethodModel> ShippingMethods { get; set; } = new();

    [JsonPropertyName("default_warehouse_id")]
    public string? DefaultWarehouseId { get; set; }

    [JsonPropertyName("ignore_store_limitations")]
    public bool IgnoreStoreLimitations { get; set; }
  }

  public class StoreInfoModel
  {
    [JsonPropertyName("store_id")]
    public string StoreId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("currency")]
    public CurrencyModel? Currency { get; set; }

    [JsonPropertyName("languages")]
    public List<LanguageModel> Languages { get; set; } = new();

    [JsonPropertyName("time_zone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("weight_unit")]
    public string? WeightUnit { get; set; }

    [JsonPropertyName("dimension_unit")]
    public string? DimensionUnit { get; set; }

    [JsonPropertyName("prices_include_tax")]
    public bool? PricesIncludeTax { get; set; }

    [JsonPropertyName("owner_info")]
    public OwnerInfoModel? OwnerInfo { get; set; }

    [JsonPropertyName("currencies")]
    public List<CurrencyModel> Currencies { get; set; } = new();

    [JsonPropertyName("ignore_store_limitations")]
    public bool IgnoreStoreLimitations { get; set; }
  }

  public class CurrencyModel
  {
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("rate")]
    public decimal Rate { get; set; } = 1;

    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }
  }

  public class LanguageModel
  {
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }
  }

  public class OwnerInfoModel
  {
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip_code")]
    public string? ZipCode { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
  }

  public class WarehouseModel
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("address")]
    public WarehouseAddressModel? Address { get; set; }
  }

  public class WarehouseAddressModel
  {
    [JsonPropertyName("address1")]
    public string? Address1 { get; set; }

    [JsonPropertyName("address2")]
    public string? Address2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip_code")]
    public string? ZipCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }
  }

  public class ShippingMethodModel
  {
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
  }
}
