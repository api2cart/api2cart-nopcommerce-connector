using System.Net.Http;
using Api2Cart.Connector.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Http;
using Nop.Core.Infrastructure;
using Nop.Services.Customers;

namespace Api2Cart.Connector.Infrastructure
{
  /// <summary>
  /// Prevents NopCommerce from creating a ghost guest customer on every API request.
  /// NopCommerce middleware calls WebWorkContext.GetCurrentCustomerAsync() which creates
  /// a new guest when no .Nop.Customer cookie is present. Connector requests are stateless,
  /// so each request would create a ghost. Fix: inject a reusable guest cookie.
  /// </summary>
  public class Api2CartApiStartup : INopStartup
  {
    private static readonly string CookieName = $"{NopCookieDefaults.Prefix}{NopCookieDefaults.CustomerCookie}";

    private static string? _connectorCookie;
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public int Order => 499; // Before AuthenticationStartup (500)

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
      services.AddScoped<IWebhookService, WebhookService>();
      services.AddScoped<IEnqueueService, EnqueueService>();
      services.AddScoped<ICustomerChangeDetector, CustomerChangeDetector>();

      // Named HttpClient for webhook delivery. Bumps timeout to 30s (vs 10s default)
      // because .NET HttpClient cold-start in Docker can take 5-15s on first TLS handshake
      // after long idle (DNS + cert chain validation). Pool reuse via PooledConnectionLifetime
      // keeps subsequent deliveries fast. Verified empirically: cold = 10s+ timeout, warm = 1.2s.
      services
        .AddHttpClient(
          "Api2Cart.Webhook",
          client => {
            client.Timeout = TimeSpan.FromSeconds(30);
          }
        )
        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
        .ConfigurePrimaryHttpMessageHandler(
          () => new SocketsHttpHandler {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
          }
        );
    }

    public void Configure(IApplicationBuilder application)
    {
      application.Use(
        async (context, next) => {
          var path = context.Request.Path;

          if (path.StartsWithSegments("/api/api2cart")
            && !path.Value!.Contains("/configure", StringComparison.OrdinalIgnoreCase)
            && !context.Request.Cookies.ContainsKey(CookieName)) {
            var cookie = _connectorCookie ?? await InitConnectorCookie(context.RequestServices);
            var existing = context.Request.Headers.Cookie.ToString();

            context.Request.Headers.Cookie = string.IsNullOrEmpty(existing) ? cookie : $"{existing}; {cookie}";
          }

          await next();
        }
      );
    }

    private static async Task<string> InitConnectorCookie(IServiceProvider services)
    {
      await _lock.WaitAsync();

      try {
        if (_connectorCookie != null) {
          return _connectorCookie;
        }

        var customerService = services.GetRequiredService<ICustomerService>();
        var customer = await customerService.InsertGuestCustomerAsync();

        _connectorCookie = $"{CookieName}={customer.CustomerGuid}";

        return _connectorCookie;
      } finally {
        _lock.Release();
      }
    }
  }
}
