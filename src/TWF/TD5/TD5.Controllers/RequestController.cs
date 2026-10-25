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
    public static class RequestController
    {
        public static DapQuery<TD5.Model.Request> GetRequest(string columns = "*")
        {
            return new DapQuery<TD5.Model.Request>("SELECT " + columns + " FROM [Request] ", null, DapType.SELECT);
        }

        public static DapQuery<TD5.Model.Request> Where(this DapQuery<TD5.Model.Request> query, string where, object @params = null)
        {
            return query.AddQuery(where, @params);
        }

        public static DapQuery<TD5.Model.Request> OrderBy(this DapQuery<TD5.Model.Request> query, string column, bool desc = false)
        {
            return query.AddOrder(column, desc);
        }

        public static DapQuery<TD5.Model.Request> Id(this DapQuery<TD5.Model.Request> query, long id)
        {
            return query.AddQuery("[RequestID] = @ID", new
            {
                ID = id
            });
        }

        public static DapQuery<TD5.Model.Request> BadgeNumber(this DapQuery<TD5.Model.Request> query, long badgeNumber)
        {
            return query.AddQuery("[BadgeNumber] = @BadgeNumber", new
            {
                BadgeNumber = badgeNumber
            });
        }

        public static DapQuery<TD5.Model.Request> WorkflowType(this DapQuery<TD5.Model.Request> query, int wofkflowType)
        {
            return query.AddQuery("[WofkflowType] = @WofkflowType", new
            {
                WofkflowType = wofkflowType
            });
        }

        public static DapQuery<TD5.Model.Request> DateTime(this DapQuery<TD5.Model.Request> query, DateTime createDate)
        {
            return query.AddQuery("[CreateDate] = @CreateDate", new
            {
                CreateDate = createDate
            });
        }

        public static DapQuery<TD5.Model.Request> ForDate(this DapQuery<TD5.Model.Request> query, DateTime forDate)
        {
            return query.AddQuery("[ForDate] = @ForDate", new
            {
                ForDate = forDate.Date
            });
        }

        public static DapQuery<TD5.Model.Request> LeaveId(this DapQuery<TD5.Model.Request> query, int leaveId)
        {
            return query.AddQuery("[LeaveID] = @LeaveID", new
            {
                LeaveID = leaveId
            });
        }

        public static DapQuery<TD5.Model.Request> LeaveDescriptionId(this DapQuery<TD5.Model.Request> query, int leaveDescriptionId)
        {
            return query.AddQuery("[LeaveDescriptionID] = @LeaveDescriptionID", new
            {
                LeaveDescriptionID = leaveDescriptionId
            });
        }

        /// <summary>
        /// Fixed BUG-07b: Legacy TD5 mutated FromTime by calling fromTime.AddDays(1.0),
        /// causing same-day requests to be excluded from queries.
        /// </summary>
        public static DapQuery<TD5.Model.Request> FromTime(this DapQuery<TD5.Model.Request> query, DateTime fromTime)
        {
            return query.AddQuery("[FromTime] >= @FromTime", new
            {
                FromTime = fromTime
            });
        }

        public static DapQuery<TD5.Model.Request> ToTime(this DapQuery<TD5.Model.Request> query, DateTime toTime)
        {
            return query.AddQuery("[ToTime] <= @ToTime", new
            {
                ToTime = toTime
            });
        }

        public static DapQuery<TD5.Model.Request> RecordTime(this DapQuery<TD5.Model.Request> query, DateTime recordTime)
        {
            return query.AddQuery("[RecordTime] = @RecordTime", new
            {
                RecordTime = recordTime
            });
        }

        public static DapQuery<TD5.Model.Request> OverTimeType(this DapQuery<TD5.Model.Request> query, int overTimeType)
        {
            return query.AddQuery("[OverTimeType] = @OverTimeType", new
            {
                OverTimeType = overTimeType
            });
        }

        public static DapQuery<TD5.Model.Request> OverTimeAllowValue(this DapQuery<TD5.Model.Request> query, double overTimeAllowValue)
        {
            return query.AddQuery("[OverTimeAllowValue] = @OverTimeAllowValue", new
            {
                OverTimeAllowValue = overTimeAllowValue
            });
        }

        public static DapQuery<TD5.Model.Request> IsSuspend(this DapQuery<TD5.Model.Request> query, bool isSuspend = true)
        {
            return query.AddQuery("[IsSuspend] = @IsSuspend", new
            {
                IsSuspend = isSuspend
            });
        }

        public static DapQuery<TD5.Model.Request> Descriptions(this DapQuery<TD5.Model.Request> query, string descriptions)
        {
            return query.AddQuery("[Descriptions] = @Descriptions", new
            {
                Descriptions = descriptions
            });
        }

        public static DapQuery<TD5.Model.Request> IsDaily(this DapQuery<TD5.Model.Request> query, bool isDaily)
        {
            return query.AddQuery("[IsDaily] = @IsDaily", new
            {
                IsDaily = isDaily
            });
        }

        public static List<TD5.Model.Request> Tolist(this DapQuery<TD5.Model.Request> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.Query<TD5.Model.Request>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<TD5.Model.Request>();
            }
        }

        public static TD5.Model.Request First(this DapQuery<TD5.Model.Request> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.T5Connection))
                {
                    return cnn.QueryFirstOrDefault<TD5.Model.Request>(query.Query, query.Params) ?? new TD5.Model.Request();
                }
            }
            catch
            {
                return new TD5.Model.Request();
            }
        }

        public static List<T> SelectMany<T>(this DapQuery<TD5.Model.Request> query) where T : new()
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

        public static T Select<T>(this DapQuery<TD5.Model.Request> query) where T : new()
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

        public static T5V3.Model.Request GetT5Model(this TD5.Model.Request req)
        {
            return new T5V3.Model.Request
            {
                RequestID = req.RequestID,
                BadgeNumber = req.BadgeNumber,
                CreateTime = req.CreateDate,
                ImageData = req.ImageData,
                FromTime = req.FromTime,
                ToTime = req.ToTime,
                RecordTime = req.RecordTime,
                IsSuspend = req.IsSuspend,
                WorkflowType = (WorkflowOperations)req.WofkflowType,
                OverTimeAllowType = (AllowType)req.OverTimeType,
                Descriptions = req.Descriptions,
                ForDate = req.ForDate,
                IsDaily = req.IsDaily,
                Latitude = req.Latitude,
                LeaveDescriptionID = req.LeaveDescriptionID,
                LeaveID = req.LeaveID,
                Longitude = req.Longitude,
                AllowValue = TimeSpan.FromHours(req.OverTimeAllowValue)
            };
        }
    }
}
