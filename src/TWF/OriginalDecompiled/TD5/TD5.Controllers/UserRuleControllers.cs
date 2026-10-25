using Dapper;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
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

		public static DapQuery<UserRule> InsertUserRule(UserRule UserRule)
		{
			return new DapQuery<UserRule>("INSERT INTO [UserRules] ([LeaveID],[LeaveName],[Color],[DutyOrLeave],[Hourly],[StateKey],[MaxHoursInDay],[MaxHoursInMonth],[MaxDailyInYear],[MaxDailyInMonth],[EachXHoursADay],[GetInType],[ForNextYear],[ForNextYearSum],[MaxMinus],[GetInHolidays],[Payment],[Symbol],[AutoInHolidays],[OnlyOne],[DecreaseFromLeaveID],[ParameterID],[ValidFreeOT]) VALUES (@LeaveID,@LeaveName,@Color,@DutyOrLeave,@Hourly,@StateKey,@MaxHoursInDay,@MaxHoursInMonth,@MaxDailyInYear,@MaxDailyInMonth,@EachXHoursADay,@GetInType,@ForNextYear,@ForNextYearSum,@MaxMinus,@GetInHolidays,@Payment,@Symbol,@AutoInHolidays,@OnlyOne,@DecreaseFromLeaveID,@ParameterID,@ValidFreeOT)", UserRule, DapType.INSERT);
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

		public static DapQuery<UserRule> FromDate(this DapQuery<UserRule> query, DateTime FromDate)
		{
			return query.AddQuery("[ToTime] >= @FromDate", new
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
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Query<UserRule>(query.Query, query.Params, null, true, null, null).ToList();
				}
			}
			catch
			{
				return new List<UserRule>();
			}
		}

		public static UserRule First(this DapQuery<UserRule> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.QueryFirstOrDefault<UserRule>(query.Query, query.Params, null, null, null);
				}
			}
			catch
			{
				return new UserRule();
			}
		}

		public static List<T> SelectMany<T>(this DapQuery<UserRule> query) where T : new()
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Query<T>(query.Query, query.Params, null, true, null, null).ToList();
				}
			}
			catch
			{
				return new List<T>();
			}
		}

		public static T Select<T>(this DapQuery<UserRule> query) where T : new()
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.QueryFirstOrDefault<T>(query.Query, query.Params, null, null, null);
				}
			}
			catch
			{
				return new T();
			}
		}

		public static int Execute(this DapQuery<UserRule> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Execute(query.Query, query.Params, null, null, null);
				}
			}
			catch
			{
				return 0;
			}
		}

		public static bool Any(this DapQuery<UserRule> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Query(query.Query, query.Params, null, true, null, null).Any();
				}
			}
			catch
			{
				return false;
			}
		}
	}
}
