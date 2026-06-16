using System.Linq;
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
using Nop.Services.Security;
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
    #region Customers

    [HttpPost("customers-list")]
    public async Task<IActionResult> CustomersList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] string? email = null,
        [FromQuery] string? first_name = null,
        [FromQuery] string? last_name = null,
        [FromQuery] string? company = null,
        [FromQuery] string? phone = null,
        [FromQuery] string? ip_address = null,
        [FromQuery] int? day_of_birth = null,
        [FromQuery] int? month_of_birth = null,
        [FromQuery] string? customer_role_ids = null,
        [FromQuery] string? fields = null
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
        var roleIds = ParseIntIds(customer_role_ids) ?? await GetNonGuestRoleIdsAsync();
        var requestedFields = ParseFields(fields);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var customers = await GetCustomersPageAsync(
          createdFrom,
          createdTo,
          modifiedFrom,
          modifiedTo,
          roleIds,
          email,
          first_name,
          last_name,
          company,
          phone,
          ip_address,
          day_of_birth ?? 0,
          month_of_birth ?? 0,
          pageIndex,
          pageSize
        );

        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var newsLetterService = EngineContext.Current.Resolve<INewsLetterSubscriptionService>();
        var countryService = EngineContext.Current.Resolve<ICountryService>();
        var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();
        var guestRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.GuestsRoleName);
        var currentStore = await _storeContext.GetCurrentStoreAsync();

        var result = new List<object>();

        foreach (var customer in customers) {
          result.Add(await BuildCustomerDataAsync(
            customer,
            orderService,
            newsLetterService,
            countryService,
            stateService,
            guestRole,
            currentStore,
            requestedFields
          ));
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {customers = result},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customers-count")]
    public async Task<IActionResult> CustomersCount(
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null,
        [FromQuery] string? modified_from = null,
        [FromQuery] string? modified_to = null,
        [FromQuery] string? email = null,
        [FromQuery] string? first_name = null,
        [FromQuery] string? last_name = null,
        [FromQuery] string? company = null,
        [FromQuery] string? phone = null,
        [FromQuery] string? ip_address = null,
        [FromQuery] int? day_of_birth = null,
        [FromQuery] int? month_of_birth = null,
        [FromQuery] string? customer_role_ids = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var roleIds = ParseIntIds(customer_role_ids) ?? await GetNonGuestRoleIdsAsync();

        var customers = await _customerService.GetAllCustomersAsync(
          createdFromUtc: ParseDateFilter(created_from),
          createdToUtc: ParseDateFilter(created_to, isUpperBound: true),
          lastActivityFromUtc: ParseDateFilter(modified_from),
          lastActivityToUtc: ParseDateFilter(modified_to, isUpperBound: true),
          customerRoleIds: roleIds,
          email: email,
          firstName: first_name,
          lastName: last_name,
          company: company,
          phone: phone,
          ipAddress: ip_address,
          dayOfBirth: day_of_birth ?? 0,
          monthOfBirth: month_of_birth ?? 0,
          pageIndex: 0,
          pageSize: 1
        );

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {count = customers.TotalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customers-info")]
    public async Task<IActionResult> CustomersInfo(
        [FromQuery] string? id = null,
        [FromQuery] string? fields = null
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

        var requestedFields = ParseFields(fields);
        var orderService = EngineContext.Current.Resolve<IOrderService>();
        var newsLetterService = EngineContext.Current.Resolve<INewsLetterSubscriptionService>();
        var countryService = EngineContext.Current.Resolve<ICountryService>();
        var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();
        var guestRole = await _customerService.GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.GuestsRoleName);
        var currentStore = await _storeContext.GetCurrentStoreAsync();

        var data = await BuildCustomerDataAsync(
          customer,
          orderService,
          newsLetterService,
          countryService,
          stateService,
          guestRole,
          currentStore,
          requestedFields
        );

        return JsonContent(new ConnectorResponse<object> { Result = data });
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion

    #region Customer Data Builder

    private async Task<object> BuildCustomerDataAsync(
        Nop.Core.Domain.Customers.Customer customer,
        IOrderService orderService,
        INewsLetterSubscriptionService newsLetterService,
        ICountryService countryService,
        Nop.Services.Directory.IStateProvinceService stateService,
        Nop.Core.Domain.Customers.CustomerRole? guestRole,
        Nop.Core.Domain.Stores.Store currentStore,
        HashSet<string>? requestedFields = null
    )
    {
      var roles = await _customerService.GetCustomerRolesAsync(customer);
      var isGuest = guestRole != null && roles.Any(r => r.Id == guestRole.Id);

      var genderStr = customer.Gender switch
      {
        "M" => "M",
        "F" => "F",
        _ => (string?)null
      };

      var data = new Dictionary<string, object?> {
        ["id"] = customer.Id,
        ["email"] = customer.Email,
        ["first_name"] = customer.FirstName,
        ["last_name"] = customer.LastName,
        ["company"] = customer.Company,
        ["phone"] = customer.Phone,
        ["fax"] = customer.Fax,
        ["admin_comment"] = customer.AdminComment,
        ["last_ip_address"] = customer.LastIpAddress,
        ["created_at"] = customer.CreatedOnUtc.ToString("o"),
        ["updated_at"] = customer.LastActivityDateUtc.ToString("o"),
        ["created_on_utc"] = customer.CreatedOnUtc.ToString("o"),
        ["updated_on_utc"] = customer.LastActivityDateUtc.ToString("o"),
        ["active"] = customer.Active ? 1 : 0,
        ["is_guest"] = isGuest,
        ["store_ids"] = new[] { currentStore.Id },
        ["gender"] = genderStr,
        ["date_of_birth"] = customer.DateOfBirth?.ToString("yyyy-MM-dd"),
        ["language_id"] = customer.LanguageId > 0 ? customer.LanguageId : (int?)null,
        ["currency_id"] = customer.CurrencyId > 0 ? customer.CurrencyId : (int?)null,
        ["vat_number"] = customer.VatNumber,
        ["vendor_id"] = customer.VendorId > 0 ? customer.VendorId : (int?)null,
        ["is_tax_exempt"] = customer.IsTaxExempt,
        ["last_login"] = customer.LastLoginDateUtc?.ToString("o"),
        ["roles"] = roles.Select(r => new {id = r.Id, name = r.Name}).ToArray(),
      };

      if (IsFieldRequested(requestedFields, "newsletter_subscription")) {
        var subscriptions = await newsLetterService
          .GetNewsLetterSubscriptionsByEmailAsync(customer.Email ?? string.Empty);
        var subscription = subscriptions.FirstOrDefault(s => s.StoreId == currentStore.Id);

        data["newsletter_subscription"] = subscription?.Active == true;
      }

      if (IsFieldRequested(requestedFields, "orders_count") || IsFieldRequested(requestedFields, "last_order_id")) {
        var orders = await orderService.SearchOrdersAsync(customerId: customer.Id, pageSize: 1);

        data["orders_count"] = orders.TotalCount;
        data["last_order_id"] = orders.Count > 0 ? orders[0].Id : (int?)null;
      }

      if (IsFieldRequested(requestedFields, "addresses")) {
        data["addresses"] = await BuildCustomerAddressesAsync(customer, countryService, stateService);
      }

      return data;
    }

    private async Task<List<object>> BuildCustomerAddressesAsync(
        Nop.Core.Domain.Customers.Customer customer,
        ICountryService countryService,
        Nop.Services.Directory.IStateProvinceService stateService
    )
    {
      var addresses = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);
      var addressList = new List<object>();
      var billingId = customer.BillingAddressId;
      var shippingId = customer.ShippingAddressId;

      foreach (var addr in addresses) {
        string type = "additional";

        if (addr.Id == billingId) {
          type = "billing";
        } else if (addr.Id == shippingId) {
          type = "shipping";
        }

        string? countryIso = null;
        string? stateAbbr = null;

        if (addr.CountryId.HasValue && addr.CountryId.Value > 0) {
          var country = await countryService.GetCountryByIdAsync(addr.CountryId.Value);

          if (country != null) {
            countryIso = country.TwoLetterIsoCode;
          }
        }

        if (addr.StateProvinceId.HasValue && addr.StateProvinceId.Value > 0) {
          var state = await stateService.GetStateProvinceByIdAsync(addr.StateProvinceId.Value);

          if (state != null) {
            stateAbbr = state.Abbreviation ?? state.Name;
          }
        }

        addressList.Add(
          new Dictionary<string, object?> {
            ["id"] = addr.Id,
            ["first_name"] = addr.FirstName,
            ["last_name"] = addr.LastName,
            ["company"] = addr.Company,
            ["address1"] = addr.Address1,
            ["address2"] = addr.Address2,
            ["city"] = addr.City,
            ["zip_code"] = addr.ZipPostalCode,
            ["country"] = countryIso,
            ["state"] = stateAbbr,
            ["phone"] = addr.PhoneNumber,
            ["fax"] = addr.FaxNumber,
            ["county"] = addr.County,
            ["vat_number"] = customer.VatNumber,
            ["type"] = type,
            ["is_default"] = addr.Id == billingId,
          }
        );
      }

      return addressList;
    }

    #endregion

    #region Customers Write

    private record AddressInput
    {
      public int? id { get; init; }
      public string? type { get; init; }
      public bool? is_default { get; init; }
      public string? first_name { get; init; }
      public string? last_name { get; init; }
      public string? company { get; init; }
      public string? fax { get; init; }
      public string? phone { get; init; }
      public string? address1 { get; init; }
      public string? address2 { get; init; }
      public string? city { get; init; }
      public string? country_iso { get; init; }
      public string? state { get; init; }
      public string? postcode { get; init; }
      public string? county { get; init; }
      public string? email { get; init; }
    }

    [HttpPost("customer-roles-list")]
    public async Task<IActionResult> CustomerRolesList()
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var roles = await _customerService.GetAllCustomerRolesAsync(showHidden: true);
        var result = roles.Select(
          r => new {
            id = r.Id,
            name = r.Name,
            system_name = r.SystemName,
            is_system_role = r.IsSystemRole,
            active = r.Active,
          }
        ).ToArray();

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {roles = result, total_count = result.Length},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customers-add")]
    public async Task<IActionResult> CustomersAdd(
        [FromQuery] string? email = null,
        [FromQuery] string? first_name = null,
        [FromQuery] string? last_name = null,
        [FromQuery] string? password = null,
        [FromQuery] string? group_ids = null,
        [FromQuery] string? active = null,
        [FromQuery] string? date_of_birth = null,
        [FromQuery] string? subscribe_newsletter = null,
        [FromQuery] string? gender = null,
        [FromQuery] string? fax = null,
        [FromQuery] string? company = null,
        [FromQuery] string? phone = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] string? country_iso = null,
        [FromQuery] string? vat_number = null,
        [FromQuery] string? registered_in_store_id = null,
        [FromQuery] string? currency_id = null,
        [FromQuery] string? is_tax_exempt = null,
        [FromQuery] string? vendor_id = null,
        [FromQuery] string? addresses = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(email, first_name, last_name, gender, fax, company, phone, admin_comment, vat_number, addresses);

        if (charsError != null) {
          return charsError;
        }

        var trimmedEmail = (email ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmedEmail)) {
          return ParamError("Parameter 'email' is required.");
        }

        var trimmedFirstName = (first_name ?? string.Empty).Trim();
        var trimmedLastName = (last_name ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmedFirstName)) {
          return ParamError("Parameter 'first_name' is required.");
        }

        if (string.IsNullOrEmpty(trimmedLastName)) {
          return ParamError("Parameter 'last_name' is required.");
        }

        var customerSettings = EngineContext.Current.Resolve<CustomerSettings>();

        if (!string.IsNullOrEmpty(password)
          && (password.Length < customerSettings.PasswordMinLength
            || password.Length > customerSettings.PasswordMaxLength)
        ) {
          return ParamError(
            $"Parameter 'password' must be {customerSettings.PasswordMinLength}..{customerSettings.PasswordMaxLength} chars."
          );
        }

        await using var emailLock = await AcquireEmailLockAsync(trimmedEmail);

        var existing = await _customerService.GetCustomerByEmailAsync(trimmedEmail);

        if (existing != null && !existing.Deleted) {
          return ExistsError($"Customer with email '{trimmedEmail}' already exists.");
        }

        int[] roleIds;
        var parsedRoleIds = ParseIntIds(group_ids);

        if (parsedRoleIds == null || parsedRoleIds.Length == 0) {
          var registeredRole = await _customerService
            .GetCustomerRoleBySystemNameAsync(NopCustomerDefaults.RegisteredRoleName);

          if (registeredRole == null) {
            return ErrorResponse("INTERNAL_ERROR", new InvalidOperationException("Registered role missing."));
          }

          roleIds = new[] { registeredRole.Id };
        } else {
          foreach (var roleId in parsedRoleIds) {
            var role = await _customerService.GetCustomerRoleByIdAsync(roleId);

            if (role == null) {
              return NotFoundError($"Customer role with id {roleId} not found.");
            }
          }

          roleIds = parsedRoleIds;
        }

        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var storeId = currentStore.Id;

        if (TryParsePositiveInt(registered_in_store_id, out var parsedStoreId)) {
          var store = await _storeService.GetStoreByIdAsync(parsedStoreId);

          if (store == null) {
            return NotFoundError($"Store with id {parsedStoreId} not found.");
          }

          storeId = parsedStoreId;
        }

        int customerCountryId = 0;

        if (!string.IsNullOrEmpty(country_iso)) {
          var country = await ResolveCountryByIsoAsync(country_iso);

          if (country == null) {
            return NotFoundError($"Country with ISO '{country_iso}' not found.");
          }

          customerCountryId = country.Id;
        }

        int? customerCurrencyId = null;

        if (!string.IsNullOrEmpty(currency_id)) {
          if (!TryParsePositiveInt(currency_id, out var parsedCurrencyId)) {
            return ParamError("Parameter 'currency_id' must be a positive integer.");
          }

          var currencyService = EngineContext.Current.Resolve<ICurrencyService>();

          if (await currencyService.GetCurrencyByIdAsync(parsedCurrencyId) == null) {
            return NotFoundError($"Currency with id {parsedCurrencyId} not found.");
          }

          customerCurrencyId = parsedCurrencyId;
        }

        int customerVendorId = 0;

        if (!string.IsNullOrEmpty(vendor_id)) {
          if (!TryParsePositiveInt(vendor_id, out var parsedVendorId)) {
            return ParamError("Parameter 'vendor_id' must be a positive integer.");
          }

          var vendorService = EngineContext.Current.Resolve<Nop.Services.Vendors.IVendorService>();
          var vendor = await vendorService.GetVendorByIdAsync(parsedVendorId);

          if (vendor == null || vendor.Deleted) {
            return NotFoundError($"Vendor with id {parsedVendorId} not found.");
          }

          customerVendorId = parsedVendorId;
        }

        bool customerIsTaxExempt = false;

        if (!string.IsNullOrEmpty(is_tax_exempt)) {
          customerIsTaxExempt = ParseBool(is_tax_exempt, fallback: false);
        }

        var addressList = new List<AddressInput>();

        if (!string.IsNullOrEmpty(addresses)) {
          try {
            addressList = JsonSerializer.Deserialize<List<AddressInput>>(addresses, _jsonOptions)
              ?? new List<AddressInput>();
          } catch (JsonException ex) {
            return ParamError($"Parameter 'addresses' is not valid JSON: {ex.Message}");
          }
        }

        var addressService = EngineContext.Current.Resolve<IAddressService>();
        var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();
        var insertedAddresses = new List<(int Id, string Type, bool IsDefault)>();

        foreach (var input in addressList) {
          int? countryId = null;
          int? stateId = null;

          if (!string.IsNullOrEmpty(input.country_iso)) {
            var country = await ResolveCountryByIsoAsync(input.country_iso);

            if (country == null) {
              return NotFoundError($"Country with ISO '{input.country_iso}' not found.");
            }

            countryId = country.Id;

            if (!string.IsNullOrEmpty(input.state)) {
              var state = await ResolveStateAsync(stateService, country.Id, input.state);

              if (state == null) {
                var states = (await stateService.GetStateProvincesByCountryIdAsync(country.Id)).ToList();

                if (states.Any()) {
                  var prefix = string.Equals(input.type, "billing", StringComparison.OrdinalIgnoreCase) ? "bill" : "shipp";
                  var allowed = string.Join(", ", states.Select(s => s.Name));
                  return ParamError($"Parameter '{prefix}_state' value '{input.state}' is not supported. Allowed only: {allowed}");
                }

                return NotFoundError($"State '{input.state}' not found in country '{input.country_iso}'.");
              }

              stateId = state.Id;
            }
          }

          var addr = new Nop.Core.Domain.Common.Address {
            FirstName = input.first_name ?? trimmedFirstName,
            LastName = input.last_name ?? trimmedLastName,
            Email = input.email ?? trimmedEmail,
            Company = input.company,
            Address1 = input.address1,
            Address2 = input.address2,
            City = input.city,
            County = input.county,
            ZipPostalCode = input.postcode,
            PhoneNumber = input.phone,
            FaxNumber = input.fax,
            CountryId = countryId,
            StateProvinceId = stateId,
            CreatedOnUtc = DateTime.UtcNow,
          };

          await addressService.InsertAddressAsync(addr);

          var type = (input.type ?? "additional").ToLowerInvariant();
          insertedAddresses.Add((addr.Id, type, input.is_default ?? false));
        }

        var customer = new Nop.Core.Domain.Customers.Customer {
          CustomerGuid = Guid.NewGuid(),
          Email = trimmedEmail,
          FirstName = trimmedFirstName,
          LastName = trimmedLastName,
          Phone = phone,
          Fax = fax,
          Company = company,
          Gender = gender,
          DateOfBirth = ParseDateFilter(date_of_birth),
          AdminComment = admin_comment,
          CountryId = customerCountryId,
          VatNumber = vat_number,
          CurrencyId = customerCurrencyId,
          VendorId = customerVendorId,
          IsTaxExempt = customerIsTaxExempt,
          CreatedOnUtc = DateTime.UtcNow,
          LastActivityDateUtc = DateTime.UtcNow,
          Active = ParseBool(active, fallback: true),
          Deleted = false,
          IsSystemAccount = false,
          RegisteredInStoreId = storeId,
        };

        await _customerService.InsertCustomerAsync(customer);

        foreach (var roleId in roleIds) {
          await _customerService.AddCustomerRoleMappingAsync(
            new CustomerCustomerRoleMapping {
              CustomerId = customer.Id,
              CustomerRoleId = roleId,
            }
          );
        }

        var warnings = new List<string>();
        int? billingAddressId = null;
        int? shippingAddressId = null;

        foreach (var (addrId, type, isDefault) in insertedAddresses) {
          var addrEntity = await addressService.GetAddressByIdAsync(addrId);

          if (addrEntity != null) {
            await _customerService.InsertCustomerAddressAsync(customer, addrEntity);
          }

          if (!isDefault) {
            continue;
          }

          if (type == "billing") {
            if (billingAddressId.HasValue) {
              warnings.Add("multiple_billing_default_last_wins");
            }

            billingAddressId = addrId;
          } else if (type == "shipping") {
            if (shippingAddressId.HasValue) {
              warnings.Add("multiple_shipping_default_last_wins");
            }

            shippingAddressId = addrId;
          }
        }

        if (billingAddressId.HasValue || shippingAddressId.HasValue) {
          customer.BillingAddressId = billingAddressId;
          customer.ShippingAddressId = shippingAddressId;
          await _customerService.UpdateCustomerAsync(customer);
        }

        if (!string.IsNullOrEmpty(password)) {
          var encryptionService = EngineContext.Current.Resolve<IEncryptionService>();
          var saltBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(5);
          var salt = Convert.ToBase64String(saltBytes);
          var hashedPassword = encryptionService.CreatePasswordHash(password, salt, customerSettings.HashedPasswordFormat);

          await _customerService.InsertCustomerPasswordAsync(
            new CustomerPassword {
              CustomerId = customer.Id,
              Password = hashedPassword,
              PasswordFormat = PasswordFormat.Hashed,
              PasswordSalt = salt,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
        }

        if (ParseBool(subscribe_newsletter, fallback: false)) {
          var newsLetterService = EngineContext.Current.Resolve<INewsLetterSubscriptionService>();
          var existingSubs = await newsLetterService.GetNewsLetterSubscriptionsByEmailAsync(trimmedEmail);
          var existingSubForStore = existingSubs.FirstOrDefault(s => s.StoreId == storeId && s.TypeId == 1);

          if (existingSubForStore != null) {
            warnings.Add("newsletter_subscription_already_exists");
          } else {
            await newsLetterService.InsertNewsLetterSubscriptionAsync(
              new Nop.Core.Domain.Messages.NewsLetterSubscription {
                Email = trimmedEmail,
                StoreId = storeId,
                Active = true,
                NewsLetterSubscriptionGuid = Guid.NewGuid(),
                LanguageId = customer.LanguageId ?? 0,
                CreatedOnUtc = DateTime.UtcNow,
                TypeId = 1,
              }
            );
          }
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {
              customer_id = customer.Id,
              customer_guid = customer.CustomerGuid.ToString(),
              warnings = warnings.ToArray(),
            },
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customers-update")]
    public async Task<IActionResult> CustomersUpdate(
        [FromQuery] string? id = null,
        [FromQuery] string? email = null,
        [FromQuery] string? first_name = null,
        [FromQuery] string? last_name = null,
        [FromQuery] string? password = null,
        [FromQuery] string? group_ids = null,
        [FromQuery] string? active = null,
        [FromQuery] string? date_of_birth = null,
        [FromQuery] string? subscribe_newsletter = null,
        [FromQuery] string? gender = null,
        [FromQuery] string? fax = null,
        [FromQuery] string? company = null,
        [FromQuery] string? phone = null,
        [FromQuery] string? admin_comment = null,
        [FromQuery] string? country_iso = null,
        [FromQuery] string? registered_in_store_id = null,
        [FromQuery] string? vat_number = null,
        [FromQuery] string? currency_id = null,
        [FromQuery] string? is_tax_exempt = null,
        [FromQuery] string? vendor_id = null,
        [FromQuery] string? addresses = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(email, first_name, last_name, gender, fax, company, phone, admin_comment, vat_number, addresses);

        if (charsError != null) {
          return charsError;
        }

        if (!TryParsePositiveInt(id, out var customerId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var customer = await _customerService.GetCustomerByIdAsync(customerId);

        if (customer == null || customer.Deleted) {
          return NotFoundError($"Customer with id {customerId} not found.");
        }

        var customerSettings = EngineContext.Current.Resolve<CustomerSettings>();
        var changed = false;

        if (email != null) {
          var trimmedEmail = email.Trim();

          if (string.IsNullOrEmpty(trimmedEmail)) {
            return ParamError("Parameter 'email' can not be empty.");
          }

          if (!string.Equals(customer.Email, trimmedEmail, StringComparison.OrdinalIgnoreCase)) {
            var conflict = await _customerService.GetCustomerByEmailAsync(trimmedEmail);

            if (conflict != null && !conflict.Deleted && conflict.Id != customer.Id) {
              return ExistsError($"Customer with email '{trimmedEmail}' already exists.");
            }

            customer.Email = trimmedEmail;
            changed = true;
          }
        }

        if (first_name != null && !string.Equals(customer.FirstName, first_name, StringComparison.Ordinal)) {
          customer.FirstName = first_name;
          changed = true;
        }

        if (last_name != null && !string.Equals(customer.LastName, last_name, StringComparison.Ordinal)) {
          customer.LastName = last_name;
          changed = true;
        }

        if (phone != null && !string.Equals(customer.Phone, phone, StringComparison.Ordinal)) {
          customer.Phone = phone;
          changed = true;
        }

        if (fax != null && !string.Equals(customer.Fax, fax, StringComparison.Ordinal)) {
          customer.Fax = fax;
          changed = true;
        }

        if (company != null && !string.Equals(customer.Company, company, StringComparison.Ordinal)) {
          customer.Company = company;
          changed = true;
        }

        if (gender != null && !string.Equals(customer.Gender, gender, StringComparison.Ordinal)) {
          customer.Gender = gender;
          changed = true;
        }

        if (admin_comment != null && !string.Equals(customer.AdminComment, admin_comment, StringComparison.Ordinal)) {
          customer.AdminComment = admin_comment;
          changed = true;
        }

        if (active != null) {
          var activeVal = ParseBool(active, fallback: customer.Active);

          if (customer.Active != activeVal) {
            customer.Active = activeVal;
            changed = true;
          }
        }

        if (date_of_birth != null) {
          var parsedDob = ParseDateFilter(date_of_birth);

          if (customer.DateOfBirth != parsedDob) {
            customer.DateOfBirth = parsedDob;
            changed = true;
          }
        }

        if (country_iso != null) {
          var trimmedIso = country_iso.Trim();
          int newCountryId = 0;

          if (!string.IsNullOrEmpty(trimmedIso)) {
            var country = await ResolveCountryByIsoAsync(trimmedIso);

            if (country == null) {
              return NotFoundError($"Country with ISO '{trimmedIso}' not found.");
            }

            newCountryId = country.Id;
          }

          if (customer.CountryId != newCountryId) {
            customer.CountryId = newCountryId;
            changed = true;
          }
        }

        var storeId = customer.RegisteredInStoreId;

        if (registered_in_store_id != null) {
          if (!TryParsePositiveInt(registered_in_store_id, out var parsedStoreId)) {
            return NotFoundError($"Store with id {registered_in_store_id} not found.");
          }

          var store = await _storeService.GetStoreByIdAsync(parsedStoreId);

          if (store == null) {
            return NotFoundError($"Store with id {parsedStoreId} not found.");
          }

          if (customer.RegisteredInStoreId != parsedStoreId) {
            customer.RegisteredInStoreId = parsedStoreId;
            changed = true;
          }

          storeId = parsedStoreId;
        }

        if (vat_number != null && !string.Equals(customer.VatNumber, vat_number, StringComparison.Ordinal)) {
          customer.VatNumber = vat_number;
          changed = true;
        }

        if (currency_id != null) {
          int? newCurrencyId = null;

          if (!string.IsNullOrEmpty(currency_id)) {
            if (!TryParsePositiveInt(currency_id, out var parsedCurrencyId)) {
              return ParamError("Parameter 'currency_id' must be a positive integer.");
            }

            var currencyService = EngineContext.Current.Resolve<ICurrencyService>();

            if (await currencyService.GetCurrencyByIdAsync(parsedCurrencyId) == null) {
              return NotFoundError($"Currency with id {parsedCurrencyId} not found.");
            }

            newCurrencyId = parsedCurrencyId;
          }

          if (customer.CurrencyId != newCurrencyId) {
            customer.CurrencyId = newCurrencyId;
            changed = true;
          }
        }

        if (vendor_id != null) {
          int newVendorId = 0;

          if (!string.IsNullOrEmpty(vendor_id)) {
            if (!TryParsePositiveInt(vendor_id, out var parsedVendorId)) {
              return ParamError("Parameter 'vendor_id' must be a positive integer.");
            }

            var vendorService = EngineContext.Current.Resolve<Nop.Services.Vendors.IVendorService>();
            var vendor = await vendorService.GetVendorByIdAsync(parsedVendorId);

            if (vendor == null || vendor.Deleted) {
              return NotFoundError($"Vendor with id {parsedVendorId} not found.");
            }

            newVendorId = parsedVendorId;
          }

          if (customer.VendorId != newVendorId) {
            customer.VendorId = newVendorId;
            changed = true;
          }
        }

        if (is_tax_exempt != null) {
          var newIsTaxExempt = ParseBool(is_tax_exempt, fallback: customer.IsTaxExempt);

          if (customer.IsTaxExempt != newIsTaxExempt) {
            customer.IsTaxExempt = newIsTaxExempt;
            changed = true;
          }
        }

        if (group_ids != null) {
          var parsedRoleIds = ParseIntIds(group_ids) ?? Array.Empty<int>();

          foreach (var roleId in parsedRoleIds) {
            var role = await _customerService.GetCustomerRoleByIdAsync(roleId);

            if (role == null) {
              return NotFoundError($"Customer role with id {roleId} not found.");
            }
          }

          var currentRoles = await _customerService.GetCustomerRolesAsync(customer);
          var currentIds = currentRoles.Select(r => r.Id).ToHashSet();
          var targetIds = parsedRoleIds.ToHashSet();

          foreach (var roleId in targetIds) {
            if (currentIds.Contains(roleId)) {
              continue;
            }

            await _customerService.AddCustomerRoleMappingAsync(
              new CustomerCustomerRoleMapping {
                CustomerId = customer.Id,
                CustomerRoleId = roleId,
              }
            );
            changed = true;
          }

          foreach (var role in currentRoles) {
            if (targetIds.Contains(role.Id)) {
              continue;
            }

            await _customerService.RemoveCustomerRoleMappingAsync(customer, role);
            changed = true;
          }
        }

        if (!string.IsNullOrEmpty(password)) {
          if (password.Length < customerSettings.PasswordMinLength
            || password.Length > customerSettings.PasswordMaxLength
          ) {
            return ParamError(
              $"Parameter 'password' must be {customerSettings.PasswordMinLength}..{customerSettings.PasswordMaxLength} chars."
            );
          }

          var encryptionService = EngineContext.Current.Resolve<IEncryptionService>();
          var saltBytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(5);
          var salt = Convert.ToBase64String(saltBytes);
          var hashedPassword = encryptionService.CreatePasswordHash(password, salt, customerSettings.HashedPasswordFormat);

          await _customerService.InsertCustomerPasswordAsync(
            new CustomerPassword {
              CustomerId = customer.Id,
              Password = hashedPassword,
              PasswordFormat = PasswordFormat.Hashed,
              PasswordSalt = salt,
              CreatedOnUtc = DateTime.UtcNow,
            }
          );
          changed = true;
        }

        if (subscribe_newsletter != null) {
          var shouldSubscribe = ParseBool(subscribe_newsletter, fallback: false);
          var newsLetterService = EngineContext.Current.Resolve<INewsLetterSubscriptionService>();
          var existingSubs = await newsLetterService
            .GetNewsLetterSubscriptionsByEmailAsync(customer.Email ?? string.Empty);
          var existingSubForStore = existingSubs.FirstOrDefault(s => s.StoreId == storeId && s.TypeId == 1);

          if (existingSubForStore == null && shouldSubscribe) {
            await newsLetterService.InsertNewsLetterSubscriptionAsync(
              new Nop.Core.Domain.Messages.NewsLetterSubscription {
                Email = customer.Email ?? string.Empty,
                StoreId = storeId,
                Active = true,
                NewsLetterSubscriptionGuid = Guid.NewGuid(),
                LanguageId = customer.LanguageId ?? 0,
                CreatedOnUtc = DateTime.UtcNow,
                TypeId = 1,
              }
            );
            changed = true;
          } else if (existingSubForStore != null && existingSubForStore.Active != shouldSubscribe) {
            existingSubForStore.Active = shouldSubscribe;
            await newsLetterService.UpdateNewsLetterSubscriptionAsync(existingSubForStore);
            changed = true;
          }
        }

        if (!string.IsNullOrEmpty(addresses)) {
          var addressList = new List<AddressInput>();

          try {
            addressList = JsonSerializer.Deserialize<List<AddressInput>>(addresses, _jsonOptions)
              ?? new List<AddressInput>();
          } catch (JsonException ex) {
            return ParamError($"Parameter 'addresses' is not valid JSON: {ex.Message}");
          }

          var addressService = EngineContext.Current.Resolve<IAddressService>();
          var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();

          foreach (var input in addressList) {
            int? countryId = null;
            int? stateIdAddr = null;

            if (!string.IsNullOrEmpty(input.country_iso)) {
              var country = await ResolveCountryByIsoAsync(input.country_iso);

              if (country == null) {
                return NotFoundError($"Country with ISO '{input.country_iso}' not found.");
              }

              countryId = country.Id;

              if (!string.IsNullOrEmpty(input.state)) {
                var state = await ResolveStateAsync(stateService, country.Id, input.state);

                if (state == null) {
                  return NotFoundError($"State '{input.state}' not found in country '{input.country_iso}'.");
                }

                stateIdAddr = state.Id;
              }
            }

            Nop.Core.Domain.Common.Address? addr = null;
            if (input.id > 0) {
              var customerAddresses = await _customerService.GetAddressesByCustomerIdAsync(customer.Id);
              addr = customerAddresses.FirstOrDefault(a => a.Id == input.id.Value);
            }
            if (addr == null) {
              addr = new Nop.Core.Domain.Common.Address { CreatedOnUtc = DateTime.UtcNow };
            }
            addr.FirstName = input.first_name ?? customer.FirstName;
            addr.LastName = input.last_name ?? customer.LastName;
            addr.Email = input.email ?? customer.Email;
            addr.Company = input.company;
            addr.Address1 = input.address1;
            addr.Address2 = input.address2;
            addr.City = input.city;
            addr.County = input.county;
            addr.ZipPostalCode = input.postcode;
            addr.PhoneNumber = input.phone;
            addr.FaxNumber = input.fax;
            addr.CountryId = countryId;
            addr.StateProvinceId = stateIdAddr;
            if (addr.Id > 0) {
              await addressService.UpdateAddressAsync(addr);
            } else {
              await addressService.InsertAddressAsync(addr);
              await _customerService.InsertCustomerAddressAsync(customer, addr);
            }
            changed = true;

            var type = (input.type ?? "").ToLowerInvariant();

            if (type == "billing" && customer.BillingAddressId != addr.Id) {
              customer.BillingAddressId = addr.Id;
              changed = true;
            } else if (type == "shipping" && customer.ShippingAddressId != addr.Id) {
              customer.ShippingAddressId = addr.Id;
              changed = true;
            }

            if (type == "shipping" && customer.BillingAddressId == addr.Id) {
              customer.BillingAddressId = null;
              changed = true;
            } else if (type == "billing" && customer.ShippingAddressId == addr.Id) {
              customer.ShippingAddressId = null;
              changed = true;
            }
          }
        }

        if (changed) {
          await _customerService.UpdateCustomerAsync(customer);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {updated_items = changed ? 1 : 0},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customers-delete")]
    public async Task<IActionResult> CustomersDelete(
        [FromQuery] string? id = null,
        [FromQuery] string? store_id = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        if (!TryParsePositiveInt(id, out var customerId)) {
          return ParamError("Parameter 'id' is required.");
        }

        var customer = await _customerService.GetCustomerByIdAsync(customerId);

        if (customer == null || customer.Deleted) {
          return NotFoundError($"Customer with id {customerId} not found.");
        }

        await _customerService.DeleteCustomerAsync(customer);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {delete_items = 1},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customer-address-add")]
    public async Task<IActionResult> CustomerAddressAdd(
        [FromQuery] string? customer_id = null,
        [FromQuery] string? first_name = null,
        [FromQuery] string? last_name = null,
        [FromQuery] string? company = null,
        [FromQuery] string? fax = null,
        [FromQuery] string? phone = null,
        [FromQuery] string? address1 = null,
        [FromQuery] string? address2 = null,
        [FromQuery] string? city = null,
        [FromQuery] string? country_iso = null,
        [FromQuery] string? state = null,
        [FromQuery] string? postcode = null,
        [FromQuery] string? county = null,
        [FromQuery] string? vat_number = null,
        [FromQuery] string? email = null,
        [FromQuery] string? types = null,
        [FromQuery] string? is_default = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(first_name, last_name, company, fax, phone, address1, address2, city, county, vat_number, email);

        if (charsError != null) {
          return charsError;
        }

        if (!TryParsePositiveInt(customer_id, out var parsedCustomerId)) {
          return ParamError("Parameter 'customer_id' is required.");
        }

        var customer = await _customerService.GetCustomerByIdAsync(parsedCustomerId);

        if (customer == null || customer.Deleted) {
          return NotFoundError($"Customer with id {parsedCustomerId} not found.");
        }

        int? countryId = null;
        int? stateId = null;

        if (!string.IsNullOrEmpty(country_iso)) {
          var country = await ResolveCountryByIsoAsync(country_iso);

          if (country == null) {
            return NotFoundError($"Country with ISO '{country_iso}' not found.");
          }

          countryId = country.Id;

          if (!string.IsNullOrEmpty(state)) {
            var stateService = EngineContext.Current.Resolve<Nop.Services.Directory.IStateProvinceService>();
            var stateEntity = await ResolveStateAsync(stateService, country.Id, state);

            if (stateEntity == null) {
              return NotFoundError($"State '{state}' not found in country '{country_iso}'.");
            }

            stateId = stateEntity.Id;
          }
        }

        var vatUpdated = false;

        if (!string.IsNullOrEmpty(vat_number)
          && !string.Equals(customer.VatNumber, vat_number, StringComparison.Ordinal)
        ) {
          customer.VatNumber = vat_number;
          vatUpdated = true;
        }

        var addr = new Nop.Core.Domain.Common.Address {
          FirstName = first_name ?? customer.FirstName,
          LastName = last_name ?? customer.LastName,
          Email = email ?? customer.Email,
          Company = company,
          Address1 = address1,
          Address2 = address2,
          City = city,
          County = county,
          ZipPostalCode = postcode,
          PhoneNumber = phone,
          FaxNumber = fax,
          CountryId = countryId,
          StateProvinceId = stateId,
          CreatedOnUtc = DateTime.UtcNow,
        };

        var addressService = EngineContext.Current.Resolve<IAddressService>();
        await addressService.InsertAddressAsync(addr);
        await _customerService.InsertCustomerAddressAsync(customer, addr);

        var pointersChanged = false;

        if (ParseBool(is_default, fallback: false) && !string.IsNullOrEmpty(types)) {
          var typeList = types
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

          if (typeList.Contains("billing")) {
            customer.BillingAddressId = addr.Id;
            pointersChanged = true;
          }

          if (typeList.Contains("shipping")) {
            customer.ShippingAddressId = addr.Id;
            pointersChanged = true;
          }
        }

        if (pointersChanged || vatUpdated) {
          await _customerService.UpdateCustomerAsync(customer);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {id = addr.Id},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("customer-roles-add")]
    public async Task<IActionResult> CustomerRolesAdd([FromQuery] string? name = null)
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var charsError = ValidateSupportedChars(name);

        if (charsError != null) {
          return charsError;
        }

        var trimmedName = (name ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmedName)) {
          return ParamError("Parameter 'name' is required.");
        }

        if (trimmedName.Length > 255) {
          return ParamError("Parameter 'name' must be a string with maximum length of 255 characters.");
        }

        var sb = new System.Text.StringBuilder();

        foreach (var ch in trimmedName.ToLowerInvariant()) {
          if (char.IsLetterOrDigit(ch)) {
            sb.Append(ch);
          } else if (sb.Length > 0 && sb[sb.Length - 1] != '_') {
            sb.Append('_');
          }
        }

        var systemName = sb.ToString().Trim('_');

        if (string.IsNullOrEmpty(systemName)) {
          return ParamError("Parameter 'name' must contain alphanumeric characters.");
        }

        var existing = await _customerService.GetCustomerRoleBySystemNameAsync(systemName);

        if (existing != null) {
          return ExistsError($"Customer role with name '{trimmedName}' already exists.");
        }

        var role = new CustomerRole {
          Name = trimmedName,
          SystemName = systemName,
          IsSystemRole = false,
          Active = true,
        };

        await _customerService.InsertCustomerRoleAsync(role);

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {group_id = role.Id},
          }
        );
      } catch (Exception ex) when (IsUnsupportedCharsError(ex)) {
        return UnsupportedCharsError();
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    private static async Task<IPagedList<Nop.Core.Domain.Customers.Customer>> GetCustomersPageAsync(
        DateTime? createdFromUtc,
        DateTime? createdToUtc,
        DateTime? lastActivityFromUtc,
        DateTime? lastActivityToUtc,
        int[] customerRoleIds,
        string? email,
        string? firstName,
        string? lastName,
        string? company,
        string? phone,
        string? ipAddress,
        int dayOfBirth,
        int monthOfBirth,
        int pageIndex,
        int pageSize
    )
    {
      var customerRepository = EngineContext.Current.Resolve<IRepository<Nop.Core.Domain.Customers.Customer>>();
      var mappingRepository = EngineContext.Current.Resolve<IRepository<CustomerCustomerRoleMapping>>();

      return await customerRepository.GetAllPagedAsync(
        query => {
          if (createdFromUtc.HasValue) {
            query = query.Where(c => createdFromUtc.Value <= c.CreatedOnUtc);
          }

          if (createdToUtc.HasValue) {
            query = query.Where(c => createdToUtc.Value >= c.CreatedOnUtc);
          }

          if (lastActivityFromUtc.HasValue) {
            query = query.Where(c => lastActivityFromUtc.Value <= c.LastActivityDateUtc);
          }

          if (lastActivityToUtc.HasValue) {
            query = query.Where(c => lastActivityToUtc.Value >= c.LastActivityDateUtc);
          }

          query = query.Where(c => !c.Deleted);

          if (customerRoleIds != null && customerRoleIds.Length > 0) {
            query = query
              .Join(mappingRepository.Table, x => x.Id, y => y.CustomerId, (x, y) => new { Customer = x, Mapping = y })
              .Where(z => customerRoleIds.Contains(z.Mapping.CustomerRoleId))
              .Select(z => z.Customer)
              .Distinct();
          }

          if (!string.IsNullOrWhiteSpace(email)) {
            query = query.Where(c => c.Email.Contains(email));
          }

          if (!string.IsNullOrWhiteSpace(firstName)) {
            query = query.Where(c => c.FirstName.Contains(firstName));
          }

          if (!string.IsNullOrWhiteSpace(lastName)) {
            query = query.Where(c => c.LastName.Contains(lastName));
          }

          if (!string.IsNullOrWhiteSpace(company)) {
            query = query.Where(c => c.Company.Contains(company));
          }

          if (!string.IsNullOrWhiteSpace(phone)) {
            query = query.Where(c => c.Phone.Contains(phone));
          }

          if (dayOfBirth > 0 && monthOfBirth > 0) {
            query = query.Where(c => c.DateOfBirth.HasValue && c.DateOfBirth.Value.Day == dayOfBirth && c.DateOfBirth.Value.Month == monthOfBirth);
          } else if (dayOfBirth > 0) {
            query = query.Where(c => c.DateOfBirth.HasValue && c.DateOfBirth.Value.Day == dayOfBirth);
          } else if (monthOfBirth > 0) {
            query = query.Where(c => c.DateOfBirth.HasValue && c.DateOfBirth.Value.Month == monthOfBirth);
          }

          if (!string.IsNullOrWhiteSpace(ipAddress) && CommonHelper.IsValidIpAddress(ipAddress)) {
            query = query.Where(c => c.LastIpAddress == ipAddress);
          }

          return query.OrderByDescending(c => c.CreatedOnUtc).ThenByDescending(c => c.Id);
        },
        pageIndex,
        pageSize
      );
    }

    private static async Task<Nop.Core.Domain.Directory.StateProvince?> ResolveStateAsync(
        Nop.Services.Directory.IStateProvinceService stateService,
        int countryId,
        string stateInput
    )
    {
      return await stateService.GetStateProvinceByAbbreviationAsync(stateInput, countryId)
        ?? (await stateService.GetStateProvincesByCountryIdAsync(countryId))
          .FirstOrDefault(s => string.Equals(s.Name, stateInput, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<Nop.Core.Domain.Directory.Country?> ResolveCountryByIsoAsync(string iso)
    {
      var countryService = EngineContext.Current.Resolve<ICountryService>();
      var trimmed = iso.Trim();

      if (trimmed.Length == 2) {
        return await countryService.GetCountryByTwoLetterIsoCodeAsync(trimmed);
      }

      if (trimmed.Length == 3) {
        return await countryService.GetCountryByThreeLetterIsoCodeAsync(trimmed);
      }

      return await countryService.GetCountryByTwoLetterIsoCodeAsync(trimmed)
        ?? await countryService.GetCountryByThreeLetterIsoCodeAsync(trimmed);
    }

    #endregion
  }
}
