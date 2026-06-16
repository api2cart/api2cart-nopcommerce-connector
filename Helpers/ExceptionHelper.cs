using System.Diagnostics;

namespace Api2Cart.Connector.Helpers
{
  public static class ExceptionHelper
  {
    private const string PluginNamespace = "Api2Cart.Connector";
    private const int MaxTraceFrames = 15;

    public static List<string>? GetFilteredTrace(Exception ex)
    {
      var stackTrace = new StackTrace(ex, true);
      var frames = stackTrace.GetFrames();

      if (frames == null || frames.Length == 0) {
        return null;
      }

      var filtered = new List<string>();

      foreach (var frame in frames) {
        var method = frame.GetMethod();

        if (method?.DeclaringType == null) {
          continue;
        }

        var fullName = method.DeclaringType.FullName ?? string.Empty;

        if (!fullName.StartsWith(PluginNamespace, StringComparison.Ordinal)) {
          continue;
        }

        var fileName = frame.GetFileName();
        var lineNumber = frame.GetFileLineNumber();

        var entry = $"{fullName}.{method.Name}()";

        if (!string.IsNullOrEmpty(fileName)) {
          entry += $" in {Path.GetFileName(fileName)}:line {lineNumber}";
        }

        filtered.Add(entry);

        if (filtered.Count >= MaxTraceFrames) {
          break;
        }
      }

      return filtered.Count > 0 ? filtered : null;
    }
  }
}
