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
    #region Reviews

    [HttpPost("reviews-list")]
    public async Task<IActionResult> ReviewsList(
        [FromQuery] int page = 0,
        [FromQuery] int size = 10,
        [FromQuery] int? product_id = null,
        [FromQuery] string? ids = null,
        [FromQuery] string? status = null,
        [FromQuery] int store_id = 0,
        [FromQuery] int? customer_id = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var createdFrom = ParseDateFilter(created_from);
        var createdTo = ParseDateFilter(created_to, isUpperBound: true);
        var parsedIds = ParseIntIds(ids);
        var pageSize = Math.Max(1, Math.Min(size, 250));
        var pageIndex = Math.Max(0, page);

        var query = BuildFilteredReviewQuery(
          createdFrom,
          createdTo,
          product_id,
          parsedIds,
          status,
          store_id,
          customer_id
        );

        var totalCount = query.Count();
        var reviews = query
          .OrderByDescending(r => r.CreatedOnUtc)
          .Skip(pageIndex * pageSize)
          .Take(pageSize)
          .ToList();

        var reviewIds = reviews.Select(r => r.Id).ToArray();
        var helpfulMap = BatchLoadHelpfulCounts(reviewIds);

        var customerIds = reviews.Select(r => r.CustomerId).Distinct().ToArray();
        var customerMap = new Dictionary<int, (string? Name, string? Email)>();

        if (customerIds.Length > 0) {
          var customers = await _customerService.GetCustomersByIdsAsync(customerIds);

          foreach (var customer in customers) {
            if (customer == null) {
              continue;
            }

            var name = string.IsNullOrEmpty(customer.FirstName)
              ? null
              : $"{customer.FirstName} {customer.LastName}".Trim();
            customerMap[customer.Id] = (name, customer.Email);
          }
        }

        var result = new List<Dictionary<string, object?>>();

        foreach (var review in reviews) {
          var helpfulYes = 0;
          var helpfulNo = 0;

          if (helpfulMap.TryGetValue(review.Id, out var counts)) {
            helpfulYes = counts.Yes;
            helpfulNo = counts.No;
          }

          string? customerName = null;
          string? customerEmail = null;

          if (customerMap.TryGetValue(review.CustomerId, out var custData)) {
            customerName = custData.Name;
            customerEmail = custData.Email;
          }

          result.Add(BuildReviewData(review, helpfulYes, helpfulNo, customerName, customerEmail));
        }

        return JsonContent(
          new ConnectorResponse<object> {
            Result = new {reviews = result, total_count = totalCount},
          }
        );
      } catch (Exception ex) {
        return ErrorResponse("INTERNAL_ERROR", ex);
      }
    }

    [HttpPost("reviews-count")]
    public async Task<IActionResult> ReviewsCount(
        [FromQuery] int? product_id = null,
        [FromQuery] string? ids = null,
        [FromQuery] string? status = null,
        [FromQuery] int store_id = 0,
        [FromQuery] int? customer_id = null,
        [FromQuery] string? created_from = null,
        [FromQuery] string? created_to = null
    )
    {
      var authError = await AuthorizeRequest();

      if (authError != null) {
        return authError;
      }

      try {
        var query = BuildFilteredReviewQuery(
          ParseDateFilter(created_from),
          ParseDateFilter(created_to, isUpperBound: true),
          product_id,
          ParseIntIds(ids),
          status,
          store_id,
          customer_id
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

    #endregion

    #region Review Query Builder

    private static IQueryable<ProductReview> BuildFilteredReviewQuery(
        DateTime? createdFrom,
        DateTime? createdTo,
        int? productId,
        int[]? ids,
        string? status,
        int storeId = 0,
        int? customerId = null
    )
    {
      var reviewRepository = EngineContext.Current.Resolve<IRepository<ProductReview>>();
      var query = reviewRepository.Table.AsQueryable();

      if (productId.HasValue) {
        query = query.Where(r => r.ProductId == productId.Value);
      }

      if (ids != null && ids.Length == 0) {
        query = query.Where(r => false);
      } else if (ids != null && ids.Length > 0) {
        query = query.Where(r => ids.Contains(r.Id));
      }

      if (!string.IsNullOrEmpty(status)) {
        if (status.Equals("approved", StringComparison.OrdinalIgnoreCase)) {
          query = query.Where(r => r.IsApproved);
        } else if (status.Equals("pending", StringComparison.OrdinalIgnoreCase)
          || status.Equals("disapproved", StringComparison.OrdinalIgnoreCase)) {
          query = query.Where(r => !r.IsApproved);
        }
      }

      if (storeId > 0) {
        query = query.Where(r => r.StoreId == storeId);
      }

      if (customerId.HasValue) {
        query = query.Where(r => r.CustomerId == customerId.Value);
      }

      if (createdFrom.HasValue) {
        query = query.Where(r => r.CreatedOnUtc >= createdFrom.Value);
      }

      if (createdTo.HasValue) {
        query = query.Where(r => r.CreatedOnUtc <= createdTo.Value);
      }

      return query;
    }

    #endregion

    #region Review Data Builder

    private static Dictionary<string, object?> BuildReviewData(
        ProductReview review,
        int helpfulYes,
        int helpfulNo,
        string? customerName,
        string? customerEmail
    )
    {
      return new Dictionary<string, object?>
      {
        ["id"] = review.Id,
        ["product_id"] = review.ProductId,
        ["customer_id"] = review.CustomerId,
        ["customer_name"] = customerName,
        ["customer_email"] = customerEmail,
        ["store_id"] = review.StoreId,
        ["is_approved"] = review.IsApproved,
        ["title"] = review.Title,
        ["review_text"] = review.ReviewText,
        ["reply_text"] = review.ReplyText,
        ["rating"] = review.Rating,
        ["created_at"] = review.CreatedOnUtc.ToString("o"),
        ["helpful_yes_total"] = helpfulYes,
        ["helpful_no_total"] = helpfulNo,
      };
    }

    private static Dictionary<int, (int Yes, int No)> BatchLoadHelpfulCounts(int[] reviewIds)
    {
      if (reviewIds.Length == 0) {
        return new Dictionary<int, (int, int)>();
      }

      var helpfulnessRepo = EngineContext.Current
        .Resolve<IRepository<ProductReviewHelpfulness>>();

      var grouped = helpfulnessRepo.Table
        .Where(h => reviewIds.Contains(h.ProductReviewId))
        .GroupBy(h => new { h.ProductReviewId, h.WasHelpful })
        .Select(g => new
        {
          g.Key.ProductReviewId,
          g.Key.WasHelpful,
          Count = g.Count(),
        })
        .ToList();

      var result = new Dictionary<int, (int Yes, int No)>();

      foreach (var entry in grouped) {
        if (!result.ContainsKey(entry.ProductReviewId)) {
          result[entry.ProductReviewId] = (0, 0);
        }

        var current = result[entry.ProductReviewId];

        if (entry.WasHelpful) {
          result[entry.ProductReviewId] = (current.Yes + entry.Count, current.No);
        } else {
          result[entry.ProductReviewId] = (current.Yes, current.No + entry.Count);
        }
      }

      return result;
    }

    #endregion
  }
}
