namespace Api2Cart.Connector
{
  public static class ScopeRegistry
  {
    public static readonly string[] AllScopeKeys = [
      "Products.Read", "Products.Write",
      "Categories.Read", "Categories.Write",
      "Orders.Read", "Orders.Write",
      "Customers.Read", "Customers.Write",
      "Shipments.Read", "Shipments.Write",
      "Returns.Read", "Returns.Write",
      "Reviews.Read", "Reviews.Write",
      "Brands.Read", "Brands.Write",
      "Tax.Read", "Tax.Write",
      "Attributes.Read", "Attributes.Write",
      "Subscribers.Read", "Subscribers.Write",
      "Wishlist.Read", "Wishlist.Write",
      "Basket.Read", "Basket.Write",
      "Webhooks.Read", "Webhooks.Write",
    ];

    public static readonly string[] EntityNames = [
      "Products", "Categories", "Orders", "Customers", "Shipments",
      "Returns", "Reviews", "Brands", "Tax", "Attributes",
      "Subscribers", "Wishlist", "Basket", "Webhooks",
    ];

    private static readonly Dictionary<string, string> ActionToScope = new() {
      ["products-list"]       = "Products.Read",
      ["products-count"]      = "Products.Read",
      ["products-info"]       = "Products.Read",
      ["products-child-list"] = "Products.Read",
      ["products-child-info"] = "Products.Read",

      ["categories-list"]  = "Categories.Read",
      ["categories-count"] = "Categories.Read",
      ["categories-info"]  = "Categories.Read",

      ["categories-add"]           = "Categories.Write",
      ["categories-update"]        = "Categories.Write",
      ["categories-delete"]        = "Categories.Write",
      ["categories-image-upsert"]  = "Categories.Write",
      ["categories-image-delete"]  = "Categories.Write",

      ["product-category-assign"]   = "Products.Write",
      ["product-category-unassign"] = "Products.Write",

      ["products-add"]    = "Products.Write",
      ["products-update"] = "Products.Write",
      ["products-delete"] = "Products.Write",
      ["products-child-add"]    = "Products.Write",
      ["products-child-update"] = "Products.Write",
      ["products-child-delete"] = "Products.Write",

      ["orders-list"]            = "Orders.Read",
      ["order-statuses-list"]    = "Orders.Read",
      ["payment-statuses-list"]  = "Orders.Read",
      ["shipping-statuses-list"] = "Orders.Read",
      ["order-calculate"]        = "Orders.Read",

      ["orders-add"]    = "Orders.Write",
      ["orders-update"] = "Orders.Write",

      ["customers-list"]      = "Customers.Read",
      ["customers-count"]     = "Customers.Read",
      ["customers-info"]      = "Customers.Read",
      ["customer-roles-list"] = "Customers.Read",

      ["customers-add"]            = "Customers.Write",
      ["customers-update"]         = "Customers.Write",
      ["customers-delete"]         = "Customers.Write",
      ["customer-address-add"]     = "Customers.Write",
      ["customer-roles-add"]       = "Customers.Write",

      ["shipments-list"]  = "Shipments.Read",
      ["shipment-add"]    = "Shipments.Write",
      ["shipment-update"] = "Shipments.Write",
      ["shipment-delete"] = "Shipments.Write",

      ["returns-list"]         = "Returns.Read",
      ["return-statuses-list"] = "Returns.Read",
      ["return-reasons-list"]  = "Returns.Read",
      ["return-actions-list"]  = "Returns.Read",
      ["return-create"]        = "Returns.Write",
      ["return-update"]        = "Returns.Write",
      ["return-delete"]        = "Returns.Write",
      ["refund-create"]        = "Orders.Write",

      ["reviews-list"]  = "Reviews.Read",
      ["reviews-count"] = "Reviews.Read",

      ["manufacturers-list"]  = "Brands.Read",
      ["manufacturers-count"] = "Brands.Read",
      ["manufacturers-info"]  = "Brands.Read",
      ["vendors-list"]        = "Brands.Read",

      ["manufacturers-add"]         = "Brands.Write",
      ["product-manufacturer-link"] = "Brands.Write",

      ["tax-classes-list"] = "Tax.Read",
      ["tax-classes-info"] = "Tax.Read",

      ["specification-attributes-list"]         = "Attributes.Read",
      ["specification-attributes-count"]        = "Attributes.Read",
      ["specification-attributes-info"]         = "Attributes.Read",
      ["product-specification-attributes-list"] = "Attributes.Read",

      ["subscribers-list"] = "Subscribers.Read",

      ["wishlist-list"] = "Wishlist.Read",

      ["basket-info"] = "Basket.Read",

      ["webhooks-list"]    = "Webhooks.Read",
      ["webhooks-version"] = "Webhooks.Read",
      ["webhooks-add"]     = "Webhooks.Write",
      ["webhooks-update"]  = "Webhooks.Write",
      ["webhooks-delete"]  = "Webhooks.Write",
    };

    private static readonly Dictionary<string, string> WebhookEntityToReadScope = new() {
      ["order"]              = "Orders.Read",
      ["order.shipment"]     = "Shipments.Read",
      ["subscriber"]         = "Subscribers.Read",
      ["productReview"]      = "Reviews.Read",
      ["product"]            = "Products.Read",
      ["category"]           = "Categories.Read",
      ["customer"]           = "Customers.Read",
      ["product.child_item"] = "Products.Read",
    };

    public static string? GetScopeForAction(string actionName)
    {
      return ActionToScope.TryGetValue(actionName, out var scope) ? scope : null;
    }

    public static string? GetReadScopeForWebhookEntity(string entity)
    {
      return WebhookEntityToReadScope.TryGetValue(entity, out var scope) ? scope : null;
    }

    public static HashSet<string> GetEntitiesWithWriteActions()
    {
      return ActionToScope.Values
        .Where(s => s.EndsWith(".Write"))
        .Select(s => s.Substring(0, s.Length - ".Write".Length))
        .ToHashSet();
    }

    public static string[] GetAllowedScopes(HashSet<string> disabledScopes)
    {
      if (disabledScopes.Count == 0) {
        return AllScopeKeys;
      }

      return AllScopeKeys.Where(s => !disabledScopes.Contains(s)).ToArray();
    }

    public static HashSet<string> ParseDisabledScopes(string? json)
    {
      if (string.IsNullOrEmpty(json)) {
        return [];
      }

      try {
        var list = System.Text.Json.JsonSerializer.Deserialize<string[]>(json);

        return list != null ? new HashSet<string>(list) : [];
      } catch {
        return [];
      }
    }

    public static string SerializeDisabledScopes(HashSet<string> scopes)
    {
      if (scopes.Count == 0) {
        return string.Empty;
      }

      return System.Text.Json.JsonSerializer.Serialize(scopes.Order().ToArray());
    }
  }
}
