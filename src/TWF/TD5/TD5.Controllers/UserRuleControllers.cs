using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using T5V3.Model;
using TD5.Model;

namespace TD5.Controllers
{
    public static class UserRuleControllers
    {
        public static DapQuery<UserRule> GetUserRule(string columns = "*")
        {
            return new DapQuery<UserRule>("SELECT " + columns + " FROM [UserRules] ", null, DapType.SELECT);
        }

        /// <summary>
        /// Fixed SQL copy-paste bug: legacy TD5 mistakenly inserted LeaveTypes columns into [UserRules].
        /// </summary>
        public static DapQuery<UserRule> InsertUserRule(UserRule UserRule)
        {
            return new DapQuery<UserRule>(
                "INSERT INTO [UserRules] ([UserRuleID],[BadgeNumber],[RuleType],[RuleID],[FromDate],[ToDate]) VALUES (@UserRuleID,@BadgeNumber,@RuleType,@RuleID,@FromDate,@ToDate)",
                UserRule,
                DapType.INSERT);
        }

        public static DapQuery<UserRule> UpdateUserRule()
        {
            return new DapQuery<UserRule>("UPDATE [UserRules] ", null, DapType.UPDATE);
        }

        public static DapQuery<UserRule> DeleteUserRule()
        {
            return new DapQuery<UserRule>("DELETE FROM [UserRules] ", null, DapType.DELETE);
        }

        public static DapQuery<UserRule> Where(this DapQuery<UserRule> query, string where, object @params = null)
        {
            return query.AddQuery(where, @params);
        }

        public static DapQuery<UserRule> OrderBy(this DapQuery<UserRule> query, string column, bool desc = false)
        {
            return query.AddOrder(column, desc);
        }

        public static DapQuery<UserRule> UserRuleId(this DapQuery<UserRule> query, int UserRuleID)
        {
            return query.AddQuery("[UserRuleID] = @UserRuleID", new
            {
                UserRuleID
            });
        }

        public static DapQuery<UserRule> BadgeNumber(this DapQuery<UserRule> query, long BadgeNumber)
        {
            return query.AddQuery("[BadgeNumber] = @BadgeNumber", new
            {
                BadgeNumber
            });
        }

        public static DapQuery<UserRule> RuleType(this DapQuery<UserRule> query, int RuleType)
        {
            return query.AddQuery("[RuleType] = @RuleType", new
            {
                RuleType
            });
        }

        public static DapQuery<UserRule> RuleId(this DapQuery<UserRule> query, int RuleID)
        {
            return query.AddQuery("[RuleID] = @RuleID", new
            {
                RuleID
            });
        }

        /// <summary>
        /// Fixed SQL column bug: legacy TD5 mistakenly wrote [ToTime] >= @FromDate instead of [FromDate] >= @FromDate.
        /// </summary>
        public static DapQuery<UserRule> FromDate(this DapQuery<UserRule> query, DateTime FromDate)
        {
            return query.AddQuery("[FromDate] >= @FromDate", new
            {
                FromDate
            });
        }

        public static DapQuery<UserRule> ToDate(this DapQuery<UserRule> query, DateTime ToDate)
        {
            return query.AddQuery("[ToDate] <= @ToDate", new
            {
                ToDate
            });
        }

        public static List<UserRule> Tolist(this DapQuery<UserRule> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Query<UserRule>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<UserRule>();
            }
        }

        public static UserRule First(this DapQuery<UserRule> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.QueryFirstOrDefault<UserRule>(query.Query, query.Params) ?? new UserRule();
                }
            }
            catch
            {
                return new UserRule();
            }
        }

        public static List<T> SelectMany<T>(this DapQuery<UserRule> query) where T : new()
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Query<T>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<T>();
            }
        }

        public static T Select<T>(this DapQuery<UserRule> query) where T : new()
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.QueryFirstOrDefault<T>(query.Query, query.Params);
                }
            }
            catch
            {
                return new T();
            }
        }

        public static int Execute(this DapQuery<UserRule> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Execute(query.Query, query.Params);
                }
            }
            catch
            {
                return 0;
            }
        }

        public static bool Any(this DapQuery<UserRule> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Query(query.Query, query.Params).Any();
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
