using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web.Configuration;
using TWF.Infrastructure.Data;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten DescriptionClass (Phase 4).
    /// - Fixes BUG-07a: Replaces fragile Entity Framework metadata string stripping (.Remove(len - 1))
    ///   with <see cref="SqlConnectionStringHelper.NormalizeToSqlClient"/> and connectionStrings fallback.
    /// - Supports <see cref="DBInitializer.IsTestMode"/> for offline in-memory testing.
    /// </summary>
    public class DescriptionClass
    {
        public long BadgeNumber { get; set; }

        public DateTime FromTime { get; set; }

        public string Descriptions { get; set; }

        private static string ResolveT5SqlConnectionString()
        {
            string raw = ConfigurationManager.ConnectionStrings["T5CnnStr"]?.ConnectionString;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = WebConfigurationManager.AppSettings["T5CnnStr"];
            }
            return SqlConnectionStringHelper.ExtractProviderConnectionString(raw ?? string.Empty);
        }

        public static string GetDescriptionInDate(long bg, DateTime fromDate)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var matched = DBInitializer.TestBackendAdapter
                    .GetEmployeeRequests(bg, suspendedOnly: false)
                    .FirstOrDefault(r => r.FromTime == fromDate || r.ForDate.Date == fromDate.Date);
                return matched?.Descriptions ?? "NoData";
            }

            string connectionString = ResolveT5SqlConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "NoData";
            }

            const string sql = "SELECT Descriptions FROM [Request] WHERE [BadgeNumber]=@BG AND [FromTime]=@FT";
            try
            {
                using (var conn = new SqlConnection(connectionString))
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@BG", SqlDbType.BigInt).Value = bg;
                    cmd.Parameters.Add("@FT", SqlDbType.DateTime).Value = fromDate;
                    conn.Open();
                    object result = cmd.ExecuteScalar();
                    return result as string ?? "NoData";
                }
            }
            catch
            {
                return "NoData";
            }
        }

        public static string GetDescriptionInOld(long bg, DateTime fromDate)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var matched = DBInitializer.TestBackendAdapter
                    .GetEmployeeRequests(bg, suspendedOnly: false)
                    .FirstOrDefault(r => r.IsDaily && (r.FromTime == fromDate || r.ForDate.Date == fromDate.Date));
                return matched?.Descriptions ?? "NoData";
            }

            string connectionString = ResolveT5SqlConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "NoData";
            }

            const string sql = "SELECT Descriptions FROM [Request] WHERE [BadgeNumber]=@BG AND [FromTime]=@FT AND [IsDaily] = 1";
            try
            {
                using (var conn = new SqlConnection(connectionString))
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.Add("@BG", SqlDbType.BigInt).Value = bg;
                    cmd.Parameters.Add("@FT", SqlDbType.DateTime).Value = fromDate;
                    conn.Open();
                    object result = cmd.ExecuteScalar();
                    return result as string ?? "NoData";
                }
            }
            catch
            {
                return "NoData";
            }
        }
    }
}
