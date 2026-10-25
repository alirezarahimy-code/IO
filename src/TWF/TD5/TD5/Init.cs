using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using Dapper;

namespace TD5
{
    public class Init
    {
        public static bool Connected { get; private set; }

        /// <summary>
        /// Parses and normalizes Personnel and T5 connection strings (supporting both standard ADO.NET
        /// and Entity Framework metadata=res://*/...;provider connection string="..." formats)
        /// and verifies both databases on their respective SQL Server connections (fixing BUG-07a).
        /// </summary>
        public static bool Start(string personnel, string t5)
        {
            if (!ConfigureConnectionsOnly(personnel, t5))
            {
                Connected = false;
                return false;
            }

            try
            {
                string personnelDbName = GetInitialCatalog(Repo.PersonnelConnection);
                string t5DbName = GetInitialCatalog(Repo.T5Connection);

                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    if (!cnn.Query("SELECT [name] FROM [sys].[databases] WHERE [name] = @DBNAME", new
                    {
                        DBNAME = personnelDbName
                    }).Any())
                    {
                        Connected = false;
                        return false;
                    }
                }

                // Fixed BUG-07a: Legacy TD5 mistakenly opened Repo.PersonnelConnection a second time
                // instead of Repo.T5Connection when checking the T5 database.
                using (IDbConnection cnn2 = new SqlConnection(Repo.T5Connection))
                {
                    if (!cnn2.Query("SELECT [name] FROM [sys].[databases] WHERE [name] = @DBNAME", new
                    {
                        DBNAME = t5DbName
                    }).Any())
                    {
                        Connected = false;
                        return false;
                    }
                }

                Connected = true;
                return true;
            }
            catch
            {
                Connected = false;
                return false;
            }
        }

        public static bool ConfigureConnectionsOnly(string personnel, string t5)
        {
            if (string.IsNullOrWhiteSpace(personnel) || string.IsNullOrWhiteSpace(t5))
            {
                return false;
            }

            Repo.PersonnelConnection = NormalizeAdoNetConnectionString(personnel);
            Repo.T5Connection = NormalizeAdoNetConnectionString(t5);

            return !string.IsNullOrWhiteSpace(Repo.PersonnelConnection) &&
                   !string.IsNullOrWhiteSpace(Repo.T5Connection);
        }

        public static string NormalizeAdoNetConnectionString(string rawConnectionString)
        {
            if (string.IsNullOrWhiteSpace(rawConnectionString))
            {
                return string.Empty;
            }

            string working = rawConnectionString.Trim();
            const string marker = "provider connection string=";
            int idx = working.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                working = working.Substring(idx + marker.Length).Trim();
                working = working.Replace("&quot;", "\"").Trim();
                if (working.StartsWith("\"", StringComparison.Ordinal))
                {
                    int closingQuote = working.LastIndexOf('"');
                    if (closingQuote > 0)
                    {
                        working = working.Substring(1, closingQuote - 1);
                    }
                    else
                    {
                        working = working.Substring(1);
                    }
                }
            }

            string[] parts = working.Split(new[] { ';', '&', '?', '~' }, StringSplitOptions.RemoveEmptyEntries);
            var allowedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Data Source", "Server",
                "Initial Catalog", "Database",
                "User ID", "UID",
                "Password", "PWD",
                "Integrated Security", "Trusted_Connection",
                "MultipleActiveResultSets",
                "Connect Timeout", "Connection Timeout",
                "Encrypt", "TrustServerCertificate",
                "Application Name"
            };

            var sb = new StringBuilder();
            foreach (string rawPart in parts)
            {
                string part = rawPart.Replace("\"", "").Trim();
                int eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = part.Substring(0, eq).Trim();
                string val = part.Substring(eq + 1).Trim();
                if (allowedKeys.Contains(key))
                {
                    sb.Append(key).Append('=').Append(val).Append(';');
                }
            }

            return sb.ToString();
        }

        public static string GetInitialCatalog(string buffer)
        {
            if (string.IsNullOrWhiteSpace(buffer))
            {
                return null;
            }

            string normalized = NormalizeAdoNetConnectionString(buffer);
            string[] parts = normalized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string text in parts)
            {
                int eq = text.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = text.Substring(0, eq).Trim();
                if (string.Equals(key, "Initial Catalog", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "Database", StringComparison.OrdinalIgnoreCase))
                {
                    return text.Substring(eq + 1).Trim();
                }
            }
            return null;
        }
    }
}
