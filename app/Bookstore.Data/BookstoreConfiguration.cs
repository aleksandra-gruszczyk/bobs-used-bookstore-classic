using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace BobsBookstoreClassic.Data
{
    public sealed class BookstoreConfiguration
    {
        private static readonly Lazy<BookstoreConfiguration> Lazy = new Lazy<BookstoreConfiguration>(() => new BookstoreConfiguration());

        private static BookstoreConfiguration Instance => Lazy.Value;

        private readonly Dictionary<string, string> _appSettings = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _connectionStrings = new Dictionary<string, string>();

        private BookstoreConfiguration() { }

        /// <summary>
        /// Initializes the BookstoreConfiguration from ASP.NET Core IConfiguration.
        /// Call this once at application startup before using any static methods.
        /// </summary>
        public static void Initialize(IConfiguration configuration)
        {
            // Load appSettings section
            foreach (var kvp in configuration.AsEnumerable())
            {
                if (kvp.Value != null)
                {
                    Instance._appSettings[kvp.Key] = kvp.Value;
                }
            }

            // Load connection strings
            var connectionStringsSection = configuration.GetSection("ConnectionStrings");
            if (connectionStringsSection != null)
            {
                foreach (var child in connectionStringsSection.GetChildren())
                {
                    Instance._connectionStrings[child.Key] = child.Value;
                }
            }

            // Allow environment variable overrides (key format: key with / replaced by __)
            foreach (var key in new List<string>(Instance._appSettings.Keys))
            {
                var envKey = key.Replace("/", "__");
                var envValue = Environment.GetEnvironmentVariable(envKey);
                if (envValue != null)
                {
                    Instance._appSettings[key] = envValue;
                }
            }
        }

        public static void AddSetting(string key, string value)
        {
            Instance._appSettings[key] = value;
        }

        public static string GetSetting(string key)
        {
            return Instance._appSettings.TryGetValue(key, out var value) ? value : null;
        }

        public static T GetSetting<T>(string key)
        {
            var value = Instance._appSettings.TryGetValue(key, out var v) ? v : null;
            if (value == null) return default;
            return (T)Convert.ChangeType(value, typeof(T));
        }

        public static void AddConnectionString(string key, string value)
        {
            Instance._connectionStrings[key] = value;
        }

        public static string GetConnectionString(string key)
        {
            return Instance._connectionStrings.TryGetValue(key, out var value) ? value : null;
        }
    }
}
