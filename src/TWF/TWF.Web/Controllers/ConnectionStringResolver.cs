using System.Configuration;
using TWF.Infrastructure.Data;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Single source of truth for resolving the two SQL Server connection strings
    /// used by the rewritten system (T5 core + Personnel / org-chart).
    ///
    /// Context: the legacy TWF 4.28 stored both values ONLY under &lt;appSettings&gt; as
    /// Entity Framework metadata strings ("metadata=res://...;provider connection string=..."),
    /// while the rewrite publishes plain ADO.NET strings under &lt;connectionStrings&gt;.
    /// Several call sites still read AppSettings directly, which returns null on a fresh
    /// deployment and aborts application start (Init.Start) or login password checks.
    ///
    /// Resolution order (first non-empty wins):
    ///   1. &lt;connectionStrings&gt; entry with the requested name
    ///   2. &lt;connectionStrings&gt; entry "Personnel" (Personnel fallback name)
    ///   3. &lt;appSettings&gt; entry with the requested name (legacy EF format supported)
    /// Every result is normalized to a plain SQL Server connection string.
    /// </summary>
    public static class ConnectionStringResolver
    {
        /// <summary>Connection string of the T5 core database (catalog: ReportT5V3).</summary>
        public static string T5
        {
            get { return Resolve("T5CnnStr", null); }
        }

        /// <summary>Connection string of the Personnel database (org chart, users, titles).</summary>
        public static string Personnel
        {
            get { return Resolve("PersonnelCnnStr", "Personnel"); }
        }

        /// <summary>
        /// Resolves and normalizes a connection string by logical name.
        /// </summary>
        /// <param name="name">Logical name, e.g. "T5CnnStr" or "PersonnelCnnStr".</param>
        /// <param name="connectionStringAlias">Optional alias inside &lt;connectionStrings&gt;.</param>
        public static string Resolve(string name, string connectionStringAlias)
        {
            string raw = null;

            ConnectionStringSettings byName = ConfigurationManager.ConnectionStrings[name];
            if (byName != null && !string.IsNullOrWhiteSpace(byName.ConnectionString))
            {
                raw = byName.ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(raw) && !string.IsNullOrWhiteSpace(connectionStringAlias))
            {
                ConnectionStringSettings byAlias = ConfigurationManager.ConnectionStrings[connectionStringAlias];
                if (byAlias != null && !string.IsNullOrWhiteSpace(byAlias.ConnectionString))
                {
                    raw = byAlias.ConnectionString;
                }
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                string fromAppSettings = ConfigurationManager.AppSettings[name];
                if (!string.IsNullOrWhiteSpace(fromAppSettings))
                {
                    raw = fromAppSettings;
                }
            }

            // Normalizes legacy Entity Framework metadata strings to a plain provider string.
            return SqlConnectionStringHelper.ExtractProviderConnectionString(raw);
        }

        /// <summary>
        /// Throws a descriptive configuration error instead of failing later inside the
        /// data layer with an opaque "null connection string" stack trace.
        /// </summary>
        public static string T5Required
        {
            get
            {
                string value = T5;
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ConfigurationErrorsException(
                        "Connection string 'T5CnnStr' is missing. Add it to Web.config under <connectionStrings> " +
                        "(Data Source=...;Initial Catalog=ReportT5V3;...) or <appSettings>.");
                }
                return value;
            }
        }

        /// <summary>Same as <see cref="T5Required"/> for the Personnel database.</summary>
        public static string PersonnelRequired
        {
            get
            {
                string value = Personnel;
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ConfigurationErrorsException(
                        "Connection string 'PersonnelCnnStr' is missing. Add it to Web.config under <connectionStrings> " +
                        "(Data Source=...;Initial Catalog=Personnel;...) or <appSettings>.");
                }
                return value;
            }
        }
    }
}
