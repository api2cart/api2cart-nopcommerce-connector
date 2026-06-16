using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api2Cart.Connector.Helpers;
using Api2Cart.Connector.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Api2Cart.Connector.Filters
{
  /// <summary>
  /// IAsyncResourceFilter that handles transparent encryption/decryption of requests and responses.
  /// Resource filters execute BEFORE model binding, so decrypted parameters are written to
  /// QueryString and [FromQuery] binding works transparently — zero changes to action signatures.
  ///
  /// Response encryption uses stream wrapping: the response body stream is replaced with a buffer
  /// before the action executes. After execution, the buffered response is encrypted and written
  /// to the original stream.
  /// </summary>
  public class DecryptionFilter : IAsyncResourceFilter
  {
    private const string EncryptedHeader = "X-Encrypted";
    private const long RequestMaxAgeSeconds = 30;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
      DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
      if (!context.HttpContext.Request.Headers.TryGetValue(EncryptedHeader, out var encHeader)
        || encHeader.ToString() != "1")
      {
        await next();
        return;
      }

      try {
        context.HttpContext.Request.EnableBuffering();

        using var reader = new StreamReader(context.HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        if (string.IsNullOrEmpty(body)) {
          context.Result = BuildErrorResult("ERROR_DECRYPT", "Empty encrypted body.");
          return;
        }

        var decrypted = CryptoHelper.Decrypt(body);
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(decrypted);
        var parameters = raw?.ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

        if (parameters == null || !parameters.TryGetValue("_timestamp", out var tsValue)) {
          context.Result = BuildErrorResult("ERROR_REPLAY", "Missing _timestamp in encrypted payload.");
          return;
        }

        parameters.Remove("_timestamp");

        if (!long.TryParse(tsValue, out var timestamp)) {
          context.Result = BuildErrorResult("ERROR_REPLAY", "Invalid _timestamp value.");
          return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var drift = now - timestamp;

        if (drift < 0 || drift > RequestMaxAgeSeconds) {
          context.Result = BuildErrorResult("ERROR_REPLAY", $"Request expired. Age: {drift}s, max allowed: {RequestMaxAgeSeconds}s.");
          return;
        }

        if (parameters.Count > 0) {
          var queryBuilder = new QueryBuilder();

          foreach (var existing in context.HttpContext.Request.Query) {
            queryBuilder.Add(existing.Key, existing.Value.ToString());
          }

          foreach (var param in parameters) {
            queryBuilder.Add(param.Key, param.Value);
          }

          context.HttpContext.Request.QueryString = queryBuilder.ToQueryString();
        }
      } catch (CryptographicException ex) {
        context.Result = BuildErrorResult("ERROR_DECRYPT", $"Decryption failed: {ex.Message}");
        return;
      } catch (JsonException ex) {
        context.Result = BuildErrorResult("ERROR_DECRYPT", $"Invalid decrypted payload format: {ex.Message}");
        return;
      } catch (Exception ex) {
        context.Result = BuildErrorResult("ERROR_DECRYPT", $"Decryption error: {ex.Message}");
        return;
      }

      var originalBody = context.HttpContext.Response.Body;

      using var buffer = new MemoryStream();
      context.HttpContext.Response.Body = buffer;

      try {
        await next();

        buffer.Seek(0, SeekOrigin.Begin);

        string responseBody;

        using (var responseReader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true)) {
          responseBody = await responseReader.ReadToEndAsync();
        }

        var statusCode = context.HttpContext.Response.StatusCode;

        if (statusCode >= 400 && statusCode < 500) {
          var plainBytes = Encoding.UTF8.GetBytes(responseBody);

          context.HttpContext.Response.Body = originalBody;
          context.HttpContext.Response.ContentLength = plainBytes.Length;
          context.HttpContext.Response.ContentType = "application/json";

          await originalBody.WriteAsync(plainBytes);

          return;
        }

        try {
          var encrypted = CryptoHelper.Encrypt(responseBody);
          var encryptedBytes = Encoding.UTF8.GetBytes(encrypted);

          context.HttpContext.Response.Body = originalBody;
          context.HttpContext.Response.ContentLength = encryptedBytes.Length;
          context.HttpContext.Response.ContentType = "application/json";

          await originalBody.WriteAsync(encryptedBytes);
        } catch (Exception) {
          context.HttpContext.Response.Body = originalBody;
          context.HttpContext.Response.StatusCode = 500;

          var errorJson = JsonSerializer.Serialize(
            new ConnectorResponse<object>
            {
              ResponseCode = 0,
              Error = new ConnectorError
              {
                Code = "ERROR_ENCRYPT",
                Message = "Response encryption failed.",
              },
            },
            _jsonOptions
          );

          var errorBytes = Encoding.UTF8.GetBytes(errorJson);
          context.HttpContext.Response.ContentLength = errorBytes.Length;
          context.HttpContext.Response.ContentType = "application/json";

          await originalBody.WriteAsync(errorBytes);
        }
      } finally {
        context.HttpContext.Response.Body = originalBody;
      }
    }

    private static ContentResult BuildErrorResult(string code, string message)
    {
      var response = new ConnectorResponse<object>
      {
        ResponseCode = 0,
        Error = new ConnectorError
        {
          Code = code,
          Message = message,
        },
      };

      return new ContentResult
      {
        Content = JsonSerializer.Serialize(response, _jsonOptions),
        ContentType = "application/json",
        StatusCode = 400,
      };
    }
  }
}
