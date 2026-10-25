using System;
using System.Collections.Specialized;

namespace System.Configuration
{
    public class ConfigurationErrorsException : Exception
    {
        public ConfigurationErrorsException() { }
        public ConfigurationErrorsException(string message) : base(message) { }
    }

    public class KeyValueConfigurationElement
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public KeyValueConfigurationElement(string key, string value) { Key = key; Value = value; }
    }

    public class KeyValueConfigurationCollection
    {
        private readonly System.Collections.Generic.Dictionary<string, KeyValueConfigurationElement> _items =
            new System.Collections.Generic.Dictionary<string, KeyValueConfigurationElement>(StringComparer.OrdinalIgnoreCase);

        public KeyValueConfigurationElement this[string key]
        {
            get { return _items.TryGetValue(key, out var v) ? v : null; }
        }

        public void Add(string key, string value)
        {
            _items[key] = new KeyValueConfigurationElement(key, value);
            if (ConfigurationManager.AppSettings != null)
                ConfigurationManager.AppSettings[key] = value;
        }

        public void Remove(string key)
        {
            _items.Remove(key);
        }
    }

    public class AppSettingsSection
    {
        public KeyValueConfigurationCollection Settings { get; } = new KeyValueConfigurationCollection();
    }

    public class Configuration
    {
        public AppSettingsSection AppSettings { get; } = new AppSettingsSection();
        public void Save() { }
    }

    public class ConnectionStringSettings
    {
        public string Name { get; set; }
        public string ConnectionString { get; set; }
        public string ProviderName { get; set; }
        public ConnectionStringSettings() { }
        public ConnectionStringSettings(string name, string connectionString)
        {
            Name = name;
            ConnectionString = connectionString;
        }
    }

    public class ConnectionStringSettingsCollection
    {
        private readonly System.Collections.Generic.Dictionary<string, ConnectionStringSettings> _map =
            new System.Collections.Generic.Dictionary<string, ConnectionStringSettings>(StringComparer.OrdinalIgnoreCase)
            {
                { "Personnel", new ConnectionStringSettings("Personnel", "Data Source=.;Initial Catalog=PersonnelDemo2;Integrated Security=True") }
            };

        public ConnectionStringSettings this[string name]
        {
            get
            {
                if (_map.TryGetValue(name, out var s)) return s;
                return new ConnectionStringSettings(name, "");
            }
            set { _map[name] = value; }
        }
    }

    public static class ConfigurationManager
    {
        public static NameValueCollection AppSettings { get; } = new NameValueCollection();
        public static ConnectionStringSettingsCollection ConnectionStrings { get; } = new ConnectionStringSettingsCollection();
    }
}
