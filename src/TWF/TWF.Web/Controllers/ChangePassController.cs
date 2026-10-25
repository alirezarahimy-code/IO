using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using TD5;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten ChangePassController (Phase 3).
    /// - Fixes BUG-03: ChangePassCheck reads "ChangePassForce" instead of "ChangePassLimit".
    /// - Fixes BUG-07c: Normalizes connection string via TD5.Init.NormalizeAdoNetConnectionString and uses parameterized SQL.
    /// - Supports in-memory test mode via DBInitializer.TestDataRepository.
    /// </summary>
    public static class ChangePassController
    {
        public class PassCheckProps
        {
            public int ID { get; set; }

            public long Badge { get; set; }

            public DateTime LastChangeDate { get; set; }
        }

        public static bool ChangePassCheck(long badge, out int message)
        {
            try
            {
                bool changePassMonthly = (bool)SettingsController.LoadValue("ChangePassMonthly", typeof(bool), false);

                // Fixed BUG-03: Legacy code mistakenly read "ChangePassLimit" with typeof(bool) instead of "ChangePassForce"!
                bool changePassForce = (bool)SettingsController.LoadValue("ChangePassForce", typeof(bool), false);

                if (changePassForce && !CheckPassForce(badge))
                {
                    message = 1;
                    return false;
                }

                if (changePassMonthly && !CheckPassMonthly(badge))
                {
                    message = 2;
                    return false;
                }

                message = 0;
                return true;
            }
            catch
            {
                message = 0;
                return true;
            }
        }

        public static bool CheckPassMonthly(long badge)
        {
            int limitMonths = (int)SettingsController.LoadValue("ChangePassLimit", typeof(int), 3);
            if (limitMonths <= 0)
            {
                limitMonths = 3;
            }

            if (DBInitializer.IsTestMode && DBInitializer.TestDataRepository != null)
            {
                var history = DBInitializer.TestDataRepository.GetPasswordChangeHistory(badge);
                if (history == null || !history.HasChangedPassword)
                    return false;

                return DateTime.Now <= history.LastChangeDate.AddMonths(limitMonths);
            }

            // Password history lives in the T5 database as [PassCheckProps]([Badge], [LastChangeDate]),
            // exactly as the legacy TWF 4.28 recorded it (see db/README.md).
            string connectionString = ConnectionStringResolver.T5Required;
            using (IDbConnection cnn = new SqlConnection(connectionString))
            {
                const string sql = "SELECT TOP 1 [ID], [Badge], [LastChangeDate] FROM [PassCheckProps] " +
                                   "WHERE [Badge] = @Badge AND [LastChangeDate] >= @LimitDate";
                List<PassCheckProps> list = cnn.Query<PassCheckProps>(
                    sql,
                    new { Badge = badge, LimitDate = DateTime.Now.AddMonths(-limitMonths) }).ToList();
                return list.Count > 0;
            }
        }

        public static bool CheckPassForce(long badge)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestDataRepository != null)
            {
                var history = DBInitializer.TestDataRepository.GetPasswordChangeHistory(badge);
                return history != null && history.HasChangedPassword;
            }

            string connectionString = ConnectionStringResolver.T5Required;
            using (IDbConnection cnn = new SqlConnection(connectionString))
            {
                const string sql = "SELECT TOP 1 [ID], [Badge], [LastChangeDate] FROM [PassCheckProps] WHERE [Badge] = @Badge";
                List<PassCheckProps> list = cnn.Query<PassCheckProps>(sql, new { Badge = badge }).ToList();
                return list.Count > 0;
            }
        }

        public static void PassChanged(long badge)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestDataRepository != null)
            {
                DBInitializer.TestDataRepository.RecordPasswordChange(badge, DateTime.Now);
                return;
            }

            string connectionString = ConnectionStringResolver.T5Required;
            using (IDbConnection cnn = new SqlConnection(connectionString))
            {
                const string sql = @"
IF EXISTS (SELECT 1 FROM [PassCheckProps] WHERE [Badge] = @B1)
    UPDATE [PassCheckProps] SET [LastChangeDate] = @D1 WHERE [Badge] = @B1;
ELSE
    INSERT INTO [PassCheckProps] ([Badge], [LastChangeDate]) VALUES (@B1, @D1);";
                cnn.Execute(sql, new
                {
                    B1 = badge,
                    D1 = DateTime.Now
                });
            }
        }
    }
}
