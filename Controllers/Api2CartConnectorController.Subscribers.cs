using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Messages;
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
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Api2Cart.Connector.Controllers
{
  public partial class Api2CartConnectorController
  {
    #region Subscribers

    [HttpPost("subscribers-list")]
    public async Task<IActionResult> SubscribersList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] string? ids = null,
        [FromQuery] string? email = null,
        [FromQuery] bool? subscribed = null,
        [FromQuery] int store_id = 0,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);
        var createdFrom = ParseDateFilter(created_from);
        var createdTo = ParseDateFilter(created_to, isUpperBound: true);
        var filterIds = ParseIntIds(ids);

        var subscriptionRepo = EngineContext.Current
          .Resolve<IRepository<NewsLetterSubscription>>();

        var query = subscriptionRepo.Table;

        if (store_id > 0) {
          query = query.Where(s => s.StoreId == store_id);
        }

        if (!string.IsNullOrEmpty(email)) {
          query = query.Where(s => s.Email.Contains(email));
        }

        if (subscribed.HasValue) {
          query = query.Where(s => s.Active == subscribed.Value);
        }

        if (createdFrom.HasValue) {
          query = query.Where(s => s.CreatedOnUtc >= createdFrom.Value);
        }

        if (createdTo.HasValue) {
          query = query.Where(s => s.CreatedOnUtc <= createdTo.Value);
        }

        if (!string.IsNullOrEmpty(ids)) {
          if (filterIds == null || filterIds.Length == 0) {
            return JsonContent(
              new ConnectorResponse<object> {
                Result = new { subscribers = Array.Empty<object>(), total_count = 0 },
              }
            );
          }

          query = query.Where(s => filterIds.Contains(s.Id));
        }

        var totalCount = query.Count();

        var subscriptions = query
          .OrderBy(s => s.Id)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        var result = new List<object>();

        var emails = subscriptions.Select(s => s.Email).Distinct().ToList();
        var customerRepo = EngineContext.Current.Resolve<IRepository<Customer>>();
        var customersByEmail = customerRepo.Table
          .Where(c => emails.Contains(c.Email) && !c.Deleted)
          .ToList()
          .GroupBy(c => c.Email, StringComparer.OrdinalIgnoreCase)
          .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var sub in subscriptions) {
          var data = new Dictionary<string, object?>
          {
            ["id"] = sub.Id,
            ["email"] = sub.Email,
            ["subscribed"] = sub.Active,
            ["store_id"] = sub.StoreId,
            ["language_id"] = sub.LanguageId,
            ["created_at"] = sub.CreatedOnUtc.ToString("o"),
          };

          if (customersByEmail.TryGetValue(sub.Email, out var customer)) {
            data["customer_id"] = customer.Id;
            data["first_name"] = customer.FirstName;
            data["last_name"] = customer.LastName;
            data["gender"] = customer.Gender;
          }

          result.Add(data);
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new { subscribers = result, total_count = totalCount },
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    #endregion
  }
}
