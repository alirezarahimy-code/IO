using System;
using System.IO;
using System.Xml.Linq;

namespace TWF.Infrastructure.Data
{
    /// <summary>
    /// Helper for extracting clean SQL Server provider connection strings from Entity Framework
    /// or standard ADO.NET connection strings in Web.config (both &lt;connectionStrings&gt; and &lt;appSettings&gt;).
    /// Fixes BUG-07a where TD5.Init.Start used hardcoded Substring(168)/Substring(189) offsets
    /// and checked PersonnelConnection twice instead of checking T5Connection.
    /// </summary>
    public static class SqlConnectionStringHelper
    {
        private const string ProviderConnectionPrefix = "provider connection string=";

        /// <summary>
        /// Extracts the underlying SQL Server ADO.NET connection string from either an EntityClient
        /// connection string (containing 'metadata=res://...;provider connection string="..."')
        /// or a plain SQL connection string.
        /// </summary>
        public static string ExtractProviderConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
                return string.Empty;

            string trimmed = rawConnectionString.Trim();
            int idx = trimmed.IndexOf(ProviderConnectionPrefix, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return trimmed.Trim('"', '\'');
            }

            string remainder = trimmed.Substring(idx + ProviderConnectionPrefix.Length).Trim();
            if (remainder.StartsWith("\"", StringComparison.Ordinal))
            {
                int endQuote = remainder.LastIndexOf('"');
                if (endQuote > 0)
                {
                    return remainder.Substring(1, endQuote - 1).Trim();
                }
            }
            else if (remainder.StartsWith("'", StringComparison.Ordinal))
            {
                int endQuote = remainder.LastIndexOf('\'');
                if (endQuote > 0)
                {
                    return remainder.Substring(1, endQuote - 1).Trim();
                }
            }

            return remainder.Trim('"', '\'', ';');
        }

        /// <summary>
        /// Reads Personnel and T5 SQL Server connection strings from a Web.config file,
        /// checking both &lt;appSettings&gt; (T5CnnStr, PersonnelCnnStr) and &lt;connectionStrings&gt; (Personnel, PersonnelCnnStr, T5CnnStr).
        /// </summary>
        public static (string PersonnelConnectionString, string T5ConnectionString) ReadFromWebConfig(string webConfigPath)
        {
            if (string.IsNullOrWhiteSpace(webConfigPath) || !File.Exists(webConfigPath))
                return (string.Empty, string.Empty);

            XDocument doc = XDocument.Load(webConfigPath);
            string personnelEf = null;
            string personnelPlain = null;
            string t5Ef = null;

            XElement connStrings = doc.Root?.Element("connectionStrings");
            if (connStrings != null)
            {
                foreach (XElement add in connStrings.Elements("add"))
                {
                    string name = add.Attribute("name")?.Value?.Trim();
                    string connStr = add.Attribute("connectionString")?.Value;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(connStr))
                        continue;

                    if (string.Equals(name, "PersonnelCnnStr", StringComparison.OrdinalIgnoreCase))
                        personnelEf = ExtractProviderConnectionString(connStr);
                    else if (string.Equals(name, "Personnel", StringComparison.OrdinalIgnoreCase))
                        personnelPlain = ExtractProviderConnectionString(connStr);
                    else if (string.Equals(name, "T5CnnStr", StringComparison.OrdinalIgnoreCase))
                        t5Ef = ExtractProviderConnectionString(connStr);
                }
            }

            XElement appSettings = doc.Root?.Element("appSettings");
            if (appSettings != null)
            {
                foreach (XElement add in appSettings.Elements("add"))
                {
                    string key = add.Attribute("key")?.Value?.Trim();
                    string val = add.Attribute("value")?.Value;
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val))
                        continue;

                    if (string.Equals(key, "PersonnelCnnStr", StringComparison.OrdinalIgnoreCase))
                        personnelEf = ExtractProviderConnectionString(val);
                    else if (string.Equals(key, "T5CnnStr", StringComparison.OrdinalIgnoreCase))
                        t5Ef = ExtractProviderConnectionString(val);
                }
            }

            string finalPersonnel = !string.IsNullOrWhiteSpace(personnelEf)
                ? personnelEf
                : (personnelPlain ?? string.Empty);

            return (finalPersonnel, t5Ef ?? string.Empty);
        }
    }
}
