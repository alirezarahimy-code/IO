using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Mvc;
using System.Xml.Serialization;
using MyTime3.Content;
using Personnel;
using Personnel.Model;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    public class NotifyController : Controller
    {
        [HttpGet]
        public string Index(string userName, string password)
        {
            string result = "";
            if (userName != null && password != null)
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    var adapter = DBInitializer.TestBackendAdapter;
                    long? badge = adapter.AuthenticateEmployee(userName, password);
                    if (badge.HasValue && badge.Value > 0)
                    {
                        var badgeList = new List<long> { badge.Value };
                        badgeList.AddRange(adapter.GetPrincipalsWhereEmployeeIsDeputy(badge.Value));

                        var titleIDs = badgeList
                            .Select(b => adapter.GetCurrentTitleIdForEmployee(b))
                            .Where(t => t > 0)
                            .ToList();

                        var pending = adapter.GetPendingRequestAccepterTitles();
                        var notifications = new List<Notification>();

                        foreach (long reqId in pending
                            .Where(r => titleIDs.Contains(r.Value))
                            .Select(r => r.Key)
                            .OrderByDescending(r => r))
                        {
                            var dto = adapter.GetRequestById(reqId);
                            if (dto == null)
                                continue;

                            var notification = new Notification
                            {
                                IsView = false,
                                ForBadgeNumber = badge.Value,
                                Descriptions =
                                    $"{PersianString.BadgeNumber}:{dto.BadgeNumber}  {PersianString.Name}:{dto.RequesterFullName}\r\n" +
                                    $"{PersianString.RequestTime}:{JalaliDate.FromDateTime(dto.CreatedAt)} {dto.CreatedAt:HH:mm}\r\n" +
                                    $"{PersianString.ForDate}:{JalaliDate.FromDateTime(dto.ForDate)}"
                            };

                            if (dto.WorkflowType == TwfWorkflowType.OverTime)
                            {
                                notification.Title = PersianString.RequestOverTime;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.DailyDuty)
                            {
                                notification.Title = PersianString.RequestDailyDuty;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.HourlyDuty)
                            {
                                notification.Title = PersianString.RequestHourlyDuty;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.DailyLeave)
                            {
                                notification.Title = PersianString.RequestDailyLeave;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.HourlyLeave)
                            {
                                notification.Title = PersianString.RequestHourlyLeave;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.Record)
                            {
                                notification.Title = PersianString.RequestRecord;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.Late)
                            {
                                notification.Title = PersianString.RequestLate;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.Early)
                            {
                                notification.Title = PersianString.RequestEarly;
                            }
                            else if (dto.WorkflowType == TwfWorkflowType.RewardAndPenalty)
                            {
                                notification.Title = dto.OverTimeAllowType == (int)AllowType.Late
                                    ? PersianString.Reward
                                    : PersianString.Penalty;
                            }

                            notifications.Add(notification);
                        }

                        XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Notification>));
                        using (MemoryStream memoryStream = new MemoryStream())
                        {
                            xmlSerializer.Serialize(memoryStream, notifications);
                            memoryStream.Position = 0L;
                            using (StreamReader reader = new StreamReader(memoryStream))
                            {
                                result = reader.ReadToEnd();
                            }
                        }
                    }
                    return result;
                }

                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(userName, password);
                if (badgeNumber > 0)
                {
                    List<long> badgeList = new List<long> { badgeNumber };
                    badgeList.AddRange(t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber));

                    List<int> titleIDs = new List<int>();
                    foreach (long item in badgeList)
                    {
                        titleIDs.Add(personnelSystem.DB.UserInfos.GetUserCurrentTitleID(item));
                    }

                    List<KeyValuePair<long, int>> requestAccepters = t5DBInstance.RequestDA.GetRequestAccepters(
                        personnelSystem.DB.UserInfos.GetAllUserInfoTitles(),
                        personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly());

                    List<Notification> notifications = new List<Notification>();
                    foreach (long reqId in requestAccepters
                        .Where(r => titleIDs.Contains(r.Value))
                        .Select(r => r.Key)
                        .OrderByDescending(r => r))
                    {
                        T5V3.Model.Request byID = t5DBInstance.RequestDA.GetByID(reqId);
                        if (byID == null)
                        {
                            continue;
                        }

                        Notification notification = new Notification
                        {
                            IsView = false
                        };

                        Personnel.Model.UserInfo requester = personnelSystem.DB.UserInfos.GetByID(byID.BadgeNumber);
                        if (requester != null)
                        {
                            byID.RequesterName = requester.ToString();
                        }

                        notification.Descriptions =
                            $"{PersianString.BadgeNumber}:{byID.BadgeNumber}  {PersianString.Name}:{byID.RequesterName}\r\n" +
                            $"{PersianString.RequestTime}:{DateConverter.ToHijriDateWithTimeMins(byID.CreateTime)}\r\n" +
                            $"{PersianString.ForDate}:{DateConverter.ToHijriDate(byID.ForDate)}";

                        string timeRangeText = "";
                        string typeText = "";

                        if (byID.WorkflowType == WorkflowOperations.AllowOverTimes)
                        {
                            notification.Title = PersianString.RequestOverTime;
                        }
                        else if (byID.WorkflowType == WorkflowOperations.Duty)
                        {
                            LeaveType leaveType = t5DBInstance.LeaveTypeDA.GetByID(byID.LeaveID);
                            if (leaveType != null)
                            {
                                typeText = $"\r\n{PersianString.Type}:{leaveType}";
                            }
                            if (byID.IsDaily)
                            {
                                notification.Title = PersianString.RequestDailyDuty;
                            }
                            else
                            {
                                notification.Title = PersianString.RequestHourlyDuty;
                                timeRangeText = $"\r\n{PersianString.From}:{byID.FromTime:HH:mm}\r\n{PersianString.To}:{byID.ToTime:HH:mm}";
                            }
                        }
                        else if (byID.WorkflowType == WorkflowOperations.Leave)
                        {
                            LeaveType leaveType = t5DBInstance.LeaveTypeDA.GetByID(byID.LeaveID);
                            if (leaveType != null)
                            {
                                typeText = $"\r\n{PersianString.Type}:{leaveType}";
                            }
                            if (byID.IsDaily)
                            {
                                notification.Title = PersianString.RequestDailyLeave;
                            }
                            else
                            {
                                notification.Title = PersianString.RequestHourlyLeave;
                                timeRangeText = $"\r\n{PersianString.From}:{byID.FromTime:HH:mm}\r\n{PersianString.To}:{byID.ToTime:HH:mm}";
                            }
                        }
                        else if (byID.WorkflowType == WorkflowOperations.InsertRecord)
                        {
                            notification.Title = PersianString.RequestRecord;
                        }
                        else if (byID.WorkflowType == WorkflowOperations.LateEarly)
                        {
                            notification.Title = (byID.OverTimeAllowType == AllowType.Late)
                                ? PersianString.RequestLate
                                : PersianString.RequestEarly;
                        }
                        else if (byID.WorkflowType == WorkflowOperations.RewardAndPenalty)
                        {
                            // Fixed legacy bug: handle RewardAndPenalty notifications
                            notification.Title = (byID.OverTimeAllowType == AllowType.Late)
                                ? PersianString.Reward
                                : PersianString.Penalty;
                        }

                        notification.Descriptions += typeText + timeRangeText;
                        notification.ForBadgeNumber = badgeNumber;
                        notifications.Add(notification);
                    }

                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Notification>));
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        xmlSerializer.Serialize(memoryStream, notifications);
                        memoryStream.Position = 0L;
                        using (StreamReader reader = new StreamReader(memoryStream))
                        {
                            result = reader.ReadToEnd();
                        }
                    }
                }
            }
            return result;
        }

        [HttpGet]
        public void SetViewed(string userName, string password, int notifyID)
        {
            if (userName != null && password != null)
            {
                if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                {
                    long? badge = DBInitializer.TestBackendAdapter.AuthenticateEmployee(userName, password);
                    if (badge.HasValue && badge.Value > 0)
                    {
                        NotificationManager.ClearQueuedNotifications();
                    }
                    return;
                }

                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                long badgeNumber = personnelSystem.DB.UserInfos.CheckUserPass(userName, password);
                if (badgeNumber > 0)
                {
                    List<long> badgeList = new List<long> { badgeNumber };
                    badgeList.AddRange(t5DBInstance.PersonnelDeputyDA.IsDeputy(badgeNumber));
                }
            }
        }
    }
}
