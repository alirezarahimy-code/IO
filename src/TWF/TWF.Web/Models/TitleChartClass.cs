using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using MyTime3.Controllers;
using TD5;

namespace MyTime3.Models
{
    /// <summary>
    /// Rewritten TitleChartClass (fixing BUG-01 & BUG-07a).
    /// - Eliminates cross-request static field corruption on <see cref="user"/> and <see cref="Users"/>
    ///   using [ThreadStatic] per-thread isolation.
    /// - Normalizes the Personnel connection string so Entity Framework metadata strings do not break SqlConnection.
    /// - Supports both live SQL Server Dapper queries and in-memory test mode via DBInitializer.
    /// </summary>
    public class TitleChartClass
    {
        [ThreadStatic]
        private static List<UserInfoModel> _threadUsers;

        [ThreadStatic]
        private static UserInfoModel _threadUser;

        public static List<UserInfoModel> Users
        {
            get => _threadUsers ?? (_threadUsers = new List<UserInfoModel>());
            set => _threadUsers = value;
        }

        public static UserInfoModel user
        {
            get => _threadUser ?? (_threadUser = new UserInfoModel());
            set => _threadUser = value;
        }

        public static string Cnn
        {
            get
            {
                return Init.NormalizeAdoNetConnectionString(ConnectionStringResolver.Personnel);
            }
            set
            {
                if (ConfigurationManager.ConnectionStrings["Personnel"] != null)
                {
                    ConfigurationManager.ConnectionStrings["Personnel"].ConnectionString = value;
                }
            }
        }

        public static List<UserInfoModel> GetUserListInfoById(long id)
        {
            var result = new List<UserInfoModel>();
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(id);
                if (emp != null)
                {
                    result.Add(new UserInfoModel
                    {
                        BadgeNumber = emp.BadgeNumber,
                        EmployeeNumber = emp.EmployeeNumber,
                        FirstName = emp.FirstName,
                        LastName = emp.LastName,
                        DeptID = emp.DepartmentId,
                        EmpTypeID = emp.EmployeeTypeId,
                        UserName = emp.UserName,
                        WebUser = emp.WebUserEnabled
                    });
                }
                return result;
            }

            try
            {
                using (IDbConnection cnn = new SqlConnection(Cnn))
                {
                    const string sql = "SELECT * FROM [UserInfo] WHERE BadgeNumber = @BadgeNumber";
                    return cnn.Query<UserInfoModel>(sql, new { BadgeNumber = id }).ToList();
                }
            }
            catch
            {
                return result;
            }
        }

        public static List<UserInfoTitlesModel> GetUserInfoTitlesByBg(long bg)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                int titleId = DBInitializer.TestBackendAdapter.GetEmployeeCurrentTitleId(bg);
                if (titleId <= 0)
                    return new List<UserInfoTitlesModel>();

                return new List<UserInfoTitlesModel>
                {
                    new UserInfoTitlesModel
                    {
                        ID = 1,
                        BadgeNumber = bg,
                        TitleChartID = titleId,
                        FromDate = DateTime.Today.AddYears(-1),
                        ToDate = DateTime.Today.AddYears(5)
                    }
                };
            }

            try
            {
                using (IDbConnection cnn = new SqlConnection(Cnn))
                {
                    const string sql = "SELECT * FROM [UserInfoTitles] WHERE BadgeNumber = @BG";
                    return cnn.Query<UserInfoTitlesModel>(sql, new { BG = bg }).ToList();
                }
            }
            catch
            {
                return new List<UserInfoTitlesModel>();
            }
        }

        public static TitleChartModel GetTitleChartById(int id)
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var node = DBInitializer.TestBackendAdapter.GetTitleNodeById(id);
                if (node == null)
                    return null;

                return new TitleChartModel
                {
                    TitleID = node.TitleId,
                    TitleName = node.TitleName,
                    DisplayName = node.DisplayName,
                    UpperTitleID = node.UpperTitleId
                };
            }

            try
            {
                using (IDbConnection cnn = new SqlConnection(Cnn))
                {
                    const string sql = "SELECT * FROM [TitlesChart] WHERE TitleID = @TitleID";
                    return cnn.Query<TitleChartModel>(sql, new { TitleID = id }).FirstOrDefault();
                }
            }
            catch
            {
                return null;
            }
        }

        public static List<UserInfoModel> GetAllUserInfo()
        {
            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var allTitleIds = DBInitializer.TestBackendAdapter.GetAllTitleNodes().Select(t => t.TitleId);
                return DBInitializer.TestBackendAdapter.GetEmployeesByTitleIds(allTitleIds)
                    .Select(emp => new UserInfoModel
                    {
                        BadgeNumber = emp.BadgeNumber,
                        EmployeeNumber = emp.EmployeeNumber,
                        FirstName = emp.FirstName,
                        LastName = emp.LastName,
                        DeptID = emp.DepartmentId,
                        EmpTypeID = emp.EmployeeTypeId,
                        UserName = emp.UserName,
                        WebUser = emp.WebUserEnabled
                    })
                    .ToList();
            }

            try
            {
                using (IDbConnection cnn = new SqlConnection(Cnn))
                {
                    const string sql = "SELECT * FROM [UserInfo]";
                    return cnn.Query<UserInfoModel>(sql).ToList();
                }
            }
            catch
            {
                return new List<UserInfoModel>();
            }
        }
    }
}
