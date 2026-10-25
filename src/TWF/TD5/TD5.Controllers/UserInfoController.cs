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
    public static class UserInfoController
    {
        private static readonly HashSet<string> AllowedComparisonOperators =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "=", ">", "<", ">=", "<=", "<>", "!="
            };

        private static string SanitizeComparisonOperator(string op)
        {
            if (string.IsNullOrWhiteSpace(op))
            {
                return "=";
            }
            string trimmed = op.Trim();
            return AllowedComparisonOperators.Contains(trimmed) ? trimmed : "=";
        }

        public static DapQuery<UserInfo> GetUserInfo(string columns = "*")
        {
            return new DapQuery<UserInfo>("SELECT " + columns + " FROM [UserInfo] ", null, DapType.SELECT);
        }

        /// <summary>
        /// Fixed SQL copy-paste bug: legacy TD5 mistakenly inserted LeaveTypes columns into [UserInfo].
        /// </summary>
        public static DapQuery<UserInfo> InsertUserInfo(UserInfo userInfo)
        {
            return new DapQuery<UserInfo>(
                "INSERT INTO [UserInfo] ([BadgeNumber],[DeptID],[EmpTypeID],[CertificateID],[EmployeeNumber],[FirstName],[LastName],[FatherName],[NationalNo],[Address],[Phone],[Email],[UserName],[Password],[ProgramUser],[WebUser],[DeviceName],[DeviceEnable],[DevicePass],[DevicePriv],[DeviceCardNo],[DviceGroup]) VALUES (@BadgeNumber,@DeptID,@EmpTypeID,@CertificateID,@EmployeeNumber,@FirstName,@LastName,@FatherName,@NationalNumber,@Address,@Tel,@Email,@UserName,@Password,@ProgramUser,@WebUser,@DeviceName,@DeviceEnable,@DevicePassword,@DevicePrivilage,@DeviceCardNumber,@DeviceGroupNumber)",
                userInfo,
                DapType.INSERT);
        }

        /// <summary>
        /// Preserved for backward binary signature compatibility with legacy TD5.dll.
        /// </summary>
        public static DapQuery<UserInfo> InsertUserInfo(UserRule UserRule)
        {
            return new DapQuery<UserInfo>(
                "INSERT INTO [UserRules] ([UserRuleID],[BadgeNumber],[RuleType],[RuleID],[FromDate],[ToDate]) VALUES (@UserRuleID,@BadgeNumber,@RuleType,@RuleID,@FromDate,@ToDate)",
                UserRule,
                DapType.INSERT);
        }

        public static DapQuery<UserInfo> UpdateUserInfo()
        {
            return new DapQuery<UserInfo>("UPDATE [UserInfo] ", null, DapType.UPDATE);
        }

        public static DapQuery<UserInfo> DeleteUserInfo()
        {
            return new DapQuery<UserInfo>("DELETE FROM [UserInfo] ", null, DapType.DELETE);
        }

        public static DapQuery<UserInfo> Where(this DapQuery<UserInfo> query, string where, object @params = null)
        {
            return query.AddQuery(where, @params);
        }

        public static DapQuery<UserInfo> OrderBy(this DapQuery<UserInfo> query, string column, bool desc = false)
        {
            return query.AddOrder(column, desc);
        }

        public static DapQuery<UserInfo> BadgeNumber(this DapQuery<UserInfo> query, long BadgeNumber)
        {
            return query.AddQuery("[BadgeNumber] = @BadgeNumber", new
            {
                BadgeNumber
            });
        }

        public static DapQuery<UserInfo> WorkflowType(this DapQuery<UserInfo> query, int DeptID)
        {
            return query.AddQuery("[DeptID] = @DeptID", new
            {
                DeptID
            });
        }

        public static DapQuery<UserInfo> DeptId(this DapQuery<UserInfo> query, int DeptID)
        {
            return query.AddQuery("[DeptID] = @DeptID", new
            {
                DeptID
            });
        }

        public static DapQuery<UserInfo> EmpTypeId(this DapQuery<UserInfo> query, int EmpTypeID)
        {
            return query.AddQuery("[EmpTypeID] = @EmpTypeID", new
            {
                EmpTypeID
            });
        }

        public static DapQuery<UserInfo> CertificateId(this DapQuery<UserInfo> query, int CertificateID)
        {
            return query.AddQuery("[CertificateID] = @CertificateID", new
            {
                CertificateID
            });
        }

        public static DapQuery<UserInfo> Janbaz(this DapQuery<UserInfo> query, int Janbaz)
        {
            return query.AddQuery("[Janbaz] = @Janbaz", new
            {
                Janbaz
            });
        }

        public static DapQuery<UserInfo> Gender(this DapQuery<UserInfo> query, bool Gender)
        {
            return query.AddQuery("[Gender] = @Gender", new
            {
                Gender
            });
        }

        public static DapQuery<UserInfo> EmployeeNumber(this DapQuery<UserInfo> query, string EmployeeNumber)
        {
            return query.AddQuery("[EmployeeNumber] = @EmployeeNumber", new
            {
                EmployeeNumber
            });
        }

        public static DapQuery<UserInfo> FirstName(this DapQuery<UserInfo> query, string FirstName)
        {
            return query.AddQuery("[FirstName] = @FirstName", new
            {
                FirstName
            });
        }

        public static DapQuery<UserInfo> LastName(this DapQuery<UserInfo> query, string LastName)
        {
            return query.AddQuery("[LastName] = @LastName", new
            {
                LastName
            });
        }

        public static DapQuery<UserInfo> FatherName(this DapQuery<UserInfo> query, string FatherName)
        {
            return query.AddQuery("[FatherName] = @FatherName", new
            {
                FatherName
            });
        }

        public static DapQuery<UserInfo> NationalNo(this DapQuery<UserInfo> query, string NationalNo)
        {
            return query.AddQuery("[NationalNo] = @NationalNo", new
            {
                NationalNo
            });
        }

        public static DapQuery<UserInfo> Address(this DapQuery<UserInfo> query, string Address)
        {
            return query.AddQuery("[Address] = @Address", new
            {
                Address
            });
        }

        public static DapQuery<UserInfo> Phone(this DapQuery<UserInfo> query, string Phone)
        {
            return query.AddQuery("[Phone] = @Phone", new
            {
                Phone
            });
        }

        public static DapQuery<UserInfo> Email(this DapQuery<UserInfo> query, string Email)
        {
            return query.AddQuery("[Email] = @Email", new
            {
                Email
            });
        }

        public static DapQuery<UserInfo> Birthday(this DapQuery<UserInfo> query, DateTime Birthday, string datetimeOperator = "=")
        {
            string safeOp = SanitizeComparisonOperator(datetimeOperator);
            return query.AddQuery("[Birthday] " + safeOp + " @Birthday", new
            {
                Birthday
            });
        }

        public static DapQuery<UserInfo> HiredDate(this DapQuery<UserInfo> query, DateTime HiredDate, string datetimeOperator = "=")
        {
            string safeOp = SanitizeComparisonOperator(datetimeOperator);
            return query.AddQuery("[HiredDate] " + safeOp + " @HiredDate", new
            {
                HiredDate
            });
        }

        public static DapQuery<UserInfo> FiredDate(this DapQuery<UserInfo> query, DateTime FiredDate, string datetimeOperator = "=")
        {
            string safeOp = SanitizeComparisonOperator(datetimeOperator);
            return query.AddQuery("[FiredDate] " + safeOp + " @FiredDate", new
            {
                FiredDate
            });
        }

        public static DapQuery<UserInfo> ValidFrom(this DapQuery<UserInfo> query, DateTime ValidFrom, string datetimeOperator = "=")
        {
            string safeOp = SanitizeComparisonOperator(datetimeOperator);
            return query.AddQuery("[ValidFrom] " + safeOp + " @ValidFrom", new
            {
                ValidFrom
            });
        }

        public static DapQuery<UserInfo> ValidTo(this DapQuery<UserInfo> query, DateTime ValidTo, string datetimeOperator = "=")
        {
            string safeOp = SanitizeComparisonOperator(datetimeOperator);
            return query.AddQuery("[ValidTo] " + safeOp + " @ValidTo", new
            {
                ValidTo
            });
        }

        public static DapQuery<UserInfo> Photo(this DapQuery<UserInfo> query, byte[] Photo)
        {
            return query.AddQuery("[Photo] = @Photo", new
            {
                Photo
            });
        }

        public static DapQuery<UserInfo> Descriptions(this DapQuery<UserInfo> query, string Descriptions)
        {
            return query.AddQuery("[Descriptions] = @Descriptions", new
            {
                Descriptions
            });
        }

        public static DapQuery<UserInfo> UserName(this DapQuery<UserInfo> query, string UserName)
        {
            return query.AddQuery("[UserName] = @UserName", new
            {
                UserName
            });
        }

        public static DapQuery<UserInfo> Password(this DapQuery<UserInfo> query, string Password)
        {
            return query.AddQuery("[Password] = @Password", new
            {
                Password
            });
        }

        public static DapQuery<UserInfo> ProgramUser(this DapQuery<UserInfo> query, bool ProgramUser)
        {
            return query.AddQuery("[ProgramUser] = @ProgramUser", new
            {
                ProgramUser
            });
        }

        public static DapQuery<UserInfo> WebUser(this DapQuery<UserInfo> query, bool WebUser)
        {
            return query.AddQuery("[WebUser] = @WebUser", new
            {
                WebUser
            });
        }

        public static DapQuery<UserInfo> DeviceName(this DapQuery<UserInfo> query, string DeviceName)
        {
            return query.AddQuery("[DeviceName] = @DeviceName", new
            {
                DeviceName
            });
        }

        public static DapQuery<UserInfo> DeviceEnable(this DapQuery<UserInfo> query, bool DeviceEnable)
        {
            return query.AddQuery("[DeviceEnable] = @DeviceEnable", new
            {
                DeviceEnable
            });
        }

        public static DapQuery<UserInfo> DevicePass(this DapQuery<UserInfo> query, string DevicePass)
        {
            return query.AddQuery("[DevicePass] = @DevicePass", new
            {
                DevicePass
            });
        }

        public static DapQuery<UserInfo> DevicePriv(this DapQuery<UserInfo> query, int DevicePriv)
        {
            return query.AddQuery("[DevicePriv] = @DevicePriv", new
            {
                DevicePriv
            });
        }

        public static DapQuery<UserInfo> DeviceCardNo(this DapQuery<UserInfo> query, string DeviceCardNo)
        {
            return query.AddQuery("[DeviceCardNo] = @DeviceCardNo", new
            {
                DeviceCardNo
            });
        }

        public static DapQuery<UserInfo> DviceGroup(this DapQuery<UserInfo> query, int DviceGroup)
        {
            return query.AddQuery("[DviceGroup] = @DviceGroup", new
            {
                DviceGroup
            });
        }

        public static List<UserInfo> Tolist(this DapQuery<UserInfo> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    return cnn.Query<UserInfo>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<UserInfo>();
            }
        }

        public static UserInfo First(this DapQuery<UserInfo> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    return cnn.QueryFirstOrDefault<UserInfo>(query.Query, query.Params) ?? new UserInfo();
                }
            }
            catch
            {
                return new UserInfo();
            }
        }

        public static List<T> SelectMany<T>(this DapQuery<UserInfo> query) where T : new()
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    return cnn.Query<T>(query.Query, query.Params).ToList();
                }
            }
            catch
            {
                return new List<T>();
            }
        }

        public static T Select<T>(this DapQuery<UserInfo> query) where T : new()
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    return cnn.QueryFirstOrDefault<T>(query.Query, query.Params);
                }
            }
            catch
            {
                return new T();
            }
        }

        public static int Execute(this DapQuery<UserInfo> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
                {
                    return cnn.Execute(query.Query, query.Params);
                }
            }
            catch
            {
                return 0;
            }
        }

        public static bool Any(this DapQuery<UserInfo> query)
        {
            try
            {
                using (IDbConnection cnn = new SqlConnection(Repo.PersonnelConnection))
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
