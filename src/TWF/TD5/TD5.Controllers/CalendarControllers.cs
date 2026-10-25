using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Dapper;
using T5V3.Model;
using TD5.Model;

namespace TD5.Controllers
{
    public static class CalendarControllers
    {
        public static DapQuery<Calendar> GetCalendar(string columns = "*")
        {
            return new DapQuery<Calendar>("SELECT " + columns + " FROM [Calendar] ", null, DapType.SELECT);
        }

        /// <summary>
        /// Fixed SQL copy-paste bug: legacy TD5 mistakenly inserted LeaveTypes columns into [Calendar].
        /// </summary>
        public static DapQuery<Calendar> InsertCalendar(Calendar Calendar)
        {
            return new DapQuery<Calendar>(
                "INSERT INTO [Calendar] ([CalendarID],[CalendarName]) VALUES (@CalendarID,@CalendarName)",
                Calendar,
                DapType.INSERT);
        }

        public static DapQuery<Calendar> UpdateCalendar()
        {
            return new DapQuery<Calendar>("UPDATE [Calendar] ", null, DapType.UPDATE);
        }

        public static DapQuery<Calendar> DeleteCalendar()
        {
            return new DapQuery<Calendar>("DELETE FROM [Calendar] ", null, DapType.DELETE);
        }

        public static DapQuery<Calendar> Where(this DapQuery<Calendar> query, string where, object @params = null)
        {
            return query.AddQuery(where, @params);
        }

        public static DapQuery<Calendar> OrderBy(this DapQuery<Calendar> query, string column, bool desc = false)
        {
            return query.AddOrder(column, desc);
        }

        public static DapQuery<Calendar> CalendarId(this DapQuery<Calendar> query, int CalendarId)
        {
            return query.AddQuery("[CalendarID] = @CalendarID", new
            {
                CalendarID = CalendarId,
                CalendarId
            });
        }

        public static DapQuery<Calendar> CalendarName(this DapQuery<Calendar> query, string CalendarName)
        {
            return query.AddQuery("[CalendarName] = @CalendarName", new
            {
                CalendarName
            });
        }

        public static List<Calendar> Tolist(this DapQuery<Calendar> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Query<Calendar>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<Calendar>();
            }
        }

        public static Calendar First(this DapQuery<Calendar> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.QueryFirstOrDefault<Calendar>(query.Query, query.Params) ?? new Calendar();
                }
            }
            catch
            {
                return new Calendar();
            }
        }

        public static List<T> SelectMany<T>(this DapQuery<Calendar> query) where T : new()
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

        public static T Select<T>(this DapQuery<Calendar> query) where T : new()
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

        public static int Execute(this DapQuery<Calendar> query)
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

        public static bool Any(this DapQuery<Calendar> query)
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
