using System.Reflection;
using System.Text.Json;

namespace Api2Cart.Connector.Helpers
{
  public static class ConnectorConfig
  {
    private const string ConfigFileName = "connector.config.json";
    private const string DefaultSlug = "api2cart";
    private const string DefaultFriendlyName = "Plugin";

    private static readonly object _lock = new();
    private static Dictionary<string, string>? _values;

    public static string Slug => GetValue("ConnectorUrlSlug", DefaultSlug);

    public static string FriendlyName => GetValue("PluginFriendlyName", DefaultFriendlyName);

    public static string GetValue(string key, string fallback = "")
    {
      if (_values == null) {
        Load();
      }

      if (_values != null && _values.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)) {
        return value;
      }

      return fallback;
    }

    private static void Load()
    {
      lock (_lock) {
        if (_values != null) {
          return;
        }

        try {
          var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

          if (!string.IsNullOrEmpty(assemblyDir)) {
            var configPath = Path.Combine(assemblyDir, ConfigFileName);

            if (File.Exists(configPath)) {
              var json = File.ReadAllText(configPath);
              _values = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
              return;
            }
          }
        } catch {
          // fall through to empty
        }

        _values = new Dictionary<string, string>();
      }
    }
  }
}
