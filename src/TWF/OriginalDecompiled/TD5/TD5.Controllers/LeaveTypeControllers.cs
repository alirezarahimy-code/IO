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
	public static class LeaveTypeControllers
	{
		public static DapQuery<LeaveType> GetLeaveType(string columns = "*")
		{
			return new DapQuery<LeaveType>("SELECT " + columns + " FROM [LeaveTypes] ", null, DapType.SELECT);
		}

		public static DapQuery<LeaveType> InsertLeaveType(LeaveType LeaveType)
		{
			return new DapQuery<LeaveType>("INSERT INTO [LeaveTypes] ([LeaveID],[LeaveName],[Color],[DutyOrLeave],[Hourly],[StateKey],[MaxHoursInDay],[MaxHoursInMonth],[MaxDailyInYear],[MaxDailyInMonth],[EachXHoursADay],[GetInType],[ForNextYear],[ForNextYearSum],[MaxMinus],[GetInHolidays],[Payment],[Symbol],[AutoInHolidays],[OnlyOne],[DecreaseFromLeaveID],[ParameterID],[ValidFreeOT]) VALUES (@LeaveID,@LeaveName,@Color,@DutyOrLeave,@Hourly,@StateKey,@MaxHoursInDay,@MaxHoursInMonth,@MaxDailyInYear,@MaxDailyInMonth,@EachXHoursADay,@GetInType,@ForNextYear,@ForNextYearSum,@MaxMinus,@GetInHolidays,@Payment,@Symbol,@AutoInHolidays,@OnlyOne,@DecreaseFromLeaveID,@ParameterID,@ValidFreeOT)", LeaveType, DapType.INSERT);
		}

		public static DapQuery<LeaveType> UpdateLeaveType()
		{
			return new DapQuery<LeaveType>("UPDATE [LeaveTypes] ", null, DapType.UPDATE);
		}

		public static DapQuery<LeaveType> DeleteLeaveType()
		{
			return new DapQuery<LeaveType>("DELETE FROM [LeaveTypes] ", null, DapType.DELETE);
		}

		public static DapQuery<LeaveType> Where(this DapQuery<LeaveType> query, string where, object @params = null)
		{
			return query.AddQuery(where, @params);
		}

		public static DapQuery<LeaveType> OrderBy(this DapQuery<LeaveType> query, string column, bool desc = false)
		{
			return query.AddOrder(column, desc);
		}

		public static DapQuery<LeaveType> Id(this DapQuery<LeaveType> query, long id)
		{
			return query.AddQuery("[LeaveID] = @LeaveID", new
			{
				ID = id
			});
		}

		public static DapQuery<LeaveType> LeaveName(this DapQuery<LeaveType> query, string LeaveName)
		{
			return query.AddQuery("[LeaveName] = @LeaveName", new
			{
				LeaveName
			});
		}

		public static DapQuery<LeaveType> Color(this DapQuery<LeaveType> query, int Color)
		{
			return query.AddQuery("[Color] = @Color", new
			{
				Color
			});
		}

		public static DapQuery<LeaveType> DutyOrLeave(this DapQuery<LeaveType> query, int DutyOrLeave)
		{
			return query.AddQuery("[DutyOrLeave] = @DutyOrLeave", new
			{
				DutyOrLeave
			});
		}

		public static DapQuery<LeaveType> Hourly(this DapQuery<LeaveType> query, bool Hourly)
		{
			return query.AddQuery("[Hourly] = @Hourly", new
			{
				Hourly
			});
		}

		public static DapQuery<LeaveType> StateKey(this DapQuery<LeaveType> query, int StateKey)
		{
			return query.AddQuery("[StateKey] = @StateKey", new
			{
				StateKey
			});
		}

		public static DapQuery<LeaveType> MaxHoursInDay(this DapQuery<LeaveType> query, double MaxHoursInDay)
		{
			return query.AddQuery("[MaxHoursInDay] = @MaxHoursInDay", new
			{
				MaxHoursInDay
			});
		}

		public static DapQuery<LeaveType> MaxHoursInMonth(this DapQuery<LeaveType> query, double MaxHoursInMonth)
		{
			return query.AddQuery("[MaxHoursInMonth] = @MaxHoursInMonth", new
			{
				MaxHoursInMonth
			});
		}

		public static DapQuery<LeaveType> MaxDailyInYear(this DapQuery<LeaveType> query, double MaxDailyInYear)
		{
			return query.AddQuery("[MaxDailyInYear] = @MaxDailyInYear", new
			{
				MaxDailyInYear
			});
		}

		public static DapQuery<LeaveType> MaxDailyInMonth(this DapQuery<LeaveType> query, double MaxDailyInMonth)
		{
			return query.AddQuery("[MaxDailyInMonth] = @MaxDailyInMonth", new
			{
				MaxDailyInMonth
			});
		}

		public static DapQuery<LeaveType> EachXHoursADay(this DapQuery<LeaveType> query, double EachXHoursADay)
		{
			return query.AddQuery("[EachXHoursADay] = @EachXHoursADay", new
			{
				EachXHoursADay
			});
		}

		public static DapQuery<LeaveType> GetInType(this DapQuery<LeaveType> query, int GetInType)
		{
			return query.AddQuery("[GetInType] = @GetInType", new
			{
				GetInType
			});
		}

		public static DapQuery<LeaveType> ForNextYear(this DapQuery<LeaveType> query, double ForNextYear)
		{
			return query.AddQuery("[ForNextYear] = @ForNextYear", new
			{
				ForNextYear
			});
		}

		public static DapQuery<LeaveType> ForNextYearSum(this DapQuery<LeaveType> query, bool ForNextYearSum)
		{
			return query.AddQuery("[ForNextYearSum] = @ForNextYearSum", new
			{
				ForNextYearSum
			});
		}

		public static DapQuery<LeaveType> MaxMinus(this DapQuery<LeaveType> query, double MaxMinus)
		{
			return query.AddQuery("[MaxMinus] = @MaxMinus", new
			{
				MaxMinus
			});
		}

		public static DapQuery<LeaveType> GetInHolidays(this DapQuery<LeaveType> query, bool GetInHolidays)
		{
			return query.AddQuery("[GetInHolidays] = @GetInHolidays", new
			{
				GetInHolidays
			});
		}

		public static DapQuery<LeaveType> Payment(this DapQuery<LeaveType> query, bool Payment)
		{
			return query.AddQuery("[Payment] = @Payment", new
			{
				Payment
			});
		}

		public static DapQuery<LeaveType> Symbol(this DapQuery<LeaveType> query, string Symbol)
		{
			return query.AddQuery("[Symbol] = @Symbol", new
			{
				Symbol
			});
		}

		public static DapQuery<LeaveType> AutoInHolidays(this DapQuery<LeaveType> query, bool AutoInHolidays)
		{
			return query.AddQuery("[AutoInHolidays] = @AutoInHolidays", new
			{
				AutoInHolidays
			});
		}

		public static DapQuery<LeaveType> OnlyOne(this DapQuery<LeaveType> query, bool OnlyOne)
		{
			return query.AddQuery("[OnlyOne] = @OnlyOne", new
			{
				OnlyOne
			});
		}

		public static DapQuery<LeaveType> ParameterID(this DapQuery<LeaveType> query, int ParameterID)
		{
			return query.AddQuery("[ParameterID] = @ParameterID", new
			{
				ParameterID
			});
		}

		public static DapQuery<LeaveType> DecreaseFromLeaveID(this DapQuery<LeaveType> query, int DecreaseFromLeaveID)
		{
			return query.AddQuery("[DecreaseFromLeaveID] = @DecreaseFromLeaveID", new
			{
				DecreaseFromLeaveID
			});
		}

		public static DapQuery<LeaveType> ValidFreeOT(this DapQuery<LeaveType> query, bool ValidFreeOT)
		{
			return query.AddQuery("[ValidFreeOT] = @ValidFreeOT", new
			{
				ValidFreeOT
			});
		}

		public static List<LeaveType> Tolist(this DapQuery<LeaveType> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.Query<LeaveType>(query.Query, query.Params, null, true, null, null).ToList();
				}
			}
			catch
			{
				return new List<LeaveType>();
			}
		}

		public static LeaveType First(this DapQuery<LeaveType> query)
		{
			//IL_0005: Unknown result type (might be due to invalid IL or missing references)
			//IL_000a: Expected O, but got Unknown
			try
			{
				using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
				{
					return cnn.QueryFirstOrDefault<LeaveType>(query.Query, query.Params, null, null, null);
				}
			}
			catch
			{
				return new LeaveType();
			}
		}

		public static List<T> SelectMany<T>(this DapQuery<LeaveType> query) where T : new()
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

		public static T Select<T>(this DapQuery<LeaveType> query) where T : new()
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

		public static int Execute(this DapQuery<LeaveType> query)
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

		public static bool Any(this DapQuery<LeaveType> query)
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

		public static LeaveType GetByID(int leaveTypeID)
		{
			return GetLeaveType("*").Id(leaveTypeID).First();
		}

		public static List<LeaveType> GetAll()
		{
			return GetLeaveType("*").Tolist();
		}

		public static bool AddNew(LeaveType leaveType, bool generateKey, out string err, out int newID)
		{
			newID = 1;
			err = "";
			try
			{
				if (generateKey)
				{
					leaveType.LeaveTypeID = GetLeaveType("[LeaveID]").OrderBy("[LeaveID]", false).Select<int>();
				}
				if (leaveType.LeaveName.Trim() == string.Empty || leaveType.Symbol.Trim() == string.Empty)
				{
					throw new Exception("Invalid Name.");
				}
				return InsertLeaveType(leaveType).Execute() > 0;
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				err = ex.Message;
				while (ex2 != null)
				{
					err = err + "\r\n" + ex2.Message;
					ex2 = ex2.InnerException;
				}
			}
			return false;
		}
	}
}
