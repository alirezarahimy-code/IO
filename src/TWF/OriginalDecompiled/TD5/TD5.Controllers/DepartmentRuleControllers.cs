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
	public static class DepartmentRuleControllers
	{
		public static DapQuery<DepartmentRule> GetDepartmentRule(string columns = "*")
		{
			return new DapQuery<DepartmentRule>("SELECT " + columns + " FROM [DepartmentRules] ", null, DapType.SELECT);
		}

		public static DapQuery<DepartmentRule> InsertDepartmentRule(DepartmentRule DepartmentRule)
		{
			return new DapQuery<DepartmentRule>("INSERT INTO [DepartmentRules] ([LeaveID],[LeaveName],[Color],[DutyOrLeave],[Hourly],[StateKey],[MaxHoursInDay],[MaxHoursInMonth],[MaxDailyInYear],[MaxDailyInMonth],[EachXHoursADay],[GetInType],[ForNextYear],[ForNextYearSum],[MaxMinus],[GetInHolidays],[Payment],[Symbol],[AutoInHolidays],[OnlyOne],[DecreaseFromLeaveID],[ParameterID],[ValidFreeOT]) VALUES (@LeaveID,@LeaveName,@Color,@DutyOrLeave,@Hourly,@StateKey,@MaxHoursInDay,@MaxHoursInMonth,@MaxDailyInYear,@MaxDailyInMonth,@EachXHoursADay,@GetInType,@ForNextYear,@ForNextYearSum,@MaxMinus,@GetInHolidays,@Payment,@Symbol,@AutoInHolidays,@OnlyOne,@DecreaseFromLeaveID,@ParameterID,@ValidFreeOT)", DepartmentRule, DapType.INSERT);
		}

		public static DapQuery<DepartmentRule> UpdateDepartmentRule()
		{
			return new DapQuery<DepartmentRule>("UPDATE [DepartmentRules] ", null, DapType.UPDATE);
		}

		public static DapQuery<DepartmentRule> DeleteDepartmentRule()
		{
			return new DapQuery<DepartmentRule>("DELETE FROM [DepartmentRules] ", null, DapType.DELETE);
		}

		public static DapQuery<DepartmentRule> Where(this DapQuery<DepartmentRule> query, string where, object @params = null)
		{
			return query.AddQuery(where, @params);
		}

		public static DapQuery<DepartmentRule> OrderBy(this DapQuery<DepartmentRule> query, string column, bool desc = false)
		{
			return query.AddOrder(column, desc);
		}

		public static DapQuery<DepartmentRule> DepartmentRuleId(this DapQuery<DepartmentRule> query, int DeptRuleID)
		{
			return query.AddQuery("[DeptRuleID] = @DeptRuleID", new
			{
				DeptRuleID
			});
		}

		public static DapQuery<DepartmentRule> DepartmentID(this DapQuery<DepartmentRule> query, int DepartmentID)
		{
			return query.AddQuery("[DepartmentID] = @DepartmentID", new
			{
				DepartmentID
			});
		}

		public static DapQuery<DepartmentRule> RuleId(this DapQuery<DepartmentRule> query, int RuleID)
		{
			return query.AddQuery("[RuleID] = @RuleID", new
			{
				RuleID
			});
		}

		public static DapQuery<DepartmentRule> FromDate(this DapQuery<DepartmentRule> query, DateTime FromDate)
		{
			return query.AddQuery("[ToTime] >= @FromDate", new
			{
				FromDate
			});
		}

		public static DapQuery<DepartmentRule> ToDate(this DapQuery<DepartmentRule> query, DateTime ToDate)
		{
			return query.AddQuery("[ToDate] <= @ToDate", new
			{
				ToDate
			});
		}

		public static List<DepartmentRule> Tolist(this DapQuery<DepartmentRule> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Query<DepartmentRule>(query.Query, query.Params, null, true, null, null).ToList();
				}
			}
			catch
			{
				return new List<DepartmentRule>();
			}
		}

		public static DepartmentRule First(this DapQuery<DepartmentRule> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.QueryFirstOrDefault<DepartmentRule>(query.Query, query.Params, null, null, null);
				}
			}
			catch
			{
				return new DepartmentRule();
			}
		}

		public static List<T> SelectMany<T>(this DapQuery<DepartmentRule> query) where T : new()
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

		public static T Select<T>(this DapQuery<DepartmentRule> query) where T : new()
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

		public static int Execute(this DapQuery<DepartmentRule> query)
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

		public static bool Any(this DapQuery<DepartmentRule> query)
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
