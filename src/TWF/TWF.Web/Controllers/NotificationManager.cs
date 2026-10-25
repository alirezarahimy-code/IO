using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MyTime3.Content;
using Personnel;
using T5V3.DB;
using T5V3.Misc;
using T5V3.Model;
using TWF.Domain.Models;
using TWF.Domain.ValueObjects;

namespace MyTime3.Controllers
{
    /// <summary>
    /// Rewritten NotificationManager (Phase 4).
    /// - Fixes legacy bug where Notification instances were constructed in NewRequestAdded() and immediately discarded.
    /// - Maintains a thread-safe in-memory notification store for Noti5 / web polling and supports DBInitializer.IsTestMode.
    /// </summary>
    public class NotificationManager
    {
        private static readonly ConcurrentQueue<Notification> _recentNotifications =
            new ConcurrentQueue<Notification>();

        public static IReadOnlyList<Notification> GetQueuedNotifications(long? forBadgeNumber = null)
        {
            var items = _recentNotifications.ToArray();
            if (forBadgeNumber.HasValue)
            {
                return items.Where(n => n.ForBadgeNumber == forBadgeNumber.Value).ToList();
            }
            return items;
        }

        public static void ClearQueuedNotifications()
        {
            while (_recentNotifications.TryDequeue(out _))
            {
            }
        }

        public static void NewRequestAdded(Request request)
        {
            if (request == null)
                return;

            if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
            {
                var emp = DBInitializer.TestBackendAdapter.GetEmployeeByBadge(request.BadgeNumber);
                if (emp != null)
                {
                    request.RequesterName = emp.FullName;
                }

                var pending = DBInitializer.TestBackendAdapter.GetPendingRequestAccepterTitles();
                var targetTitles = pending
                    .Where(kv => kv.Key == request.RequestID)
                    .Select(kv => kv.Value)
                    .Distinct()
                    .ToList();

                foreach (int titleId in targetTitles)
                {
                    foreach (var accepter in DBInitializer.TestBackendAdapter.GetEmployeesByTitleIds(new[] { titleId }))
                    {
                        var notif = BuildNotification(request, accepter.BadgeNumber, leaveNameResolver: id =>
                            DBInitializer.TestBackendAdapter.GetLeaveTypeById(id)?.LeaveName ?? string.Empty);
                        EnqueueNotification(notif);
                    }
                }
                return;
            }

            try
            {
                T5V3DBManager t5DBInstance = DBInitializer.GetT5DBInstance();
                PersonnelSystem personnelSystem = DBInitializer.GetPersonnelSystem();
                var userInfoTitles = personnelSystem.DB.UserInfos.GetAllUserInfoTitles();
                var chartUppers = personnelSystem.DB.TitlesCharts.GetAllWithUppersOnly();

                var matchingAccepters = t5DBInstance.RequestDA
                    .GetRequestAccepters(userInfoTitles, chartUppers)
                    .Where(r => r.Key == request.RequestID)
                    .ToList();

                if (matchingAccepters.Count == 0)
                    return;

                int titlesID = matchingAccepters[0].Value;
                var byId = personnelSystem.DB.UserInfos.GetByID(request.BadgeNumber);
                if (byId != null)
                {
                    request.RequesterName = byId.ToString();
                }

                foreach (long accepterBadge in userInfoTitles.Where(u => u.Value == titlesID).Select(u => u.Key))
                {
                    Notification notification = BuildNotification(
                        request,
                        accepterBadge,
                        id => t5DBInstance.LeaveTypeDA.GetByID(id)?.ToString() ?? string.Empty);
                    EnqueueNotification(notification);
                }
            }
            catch
            {
            }
        }

        private static Notification BuildNotification(
            Request request,
            long forBadgeNumber,
            Func<int, string> leaveNameResolver)
        {
            var notification = new Notification
            {
                IsView = false,
                ForBadgeNumber = forBadgeNumber
            };

            string createHijri = JalaliDate.FromDateTime(request.CreateTime).ToString() + " " + request.CreateTime.ToString("HH:mm");
            string forDateHijri = JalaliDate.FromDateTime(request.ForDate).ToString();

            notification.Descriptions =
                $"{PersianString.BadgeNumber}:{request.BadgeNumber}\r\n" +
                $"{PersianString.Name}:{request.RequesterName}\r\n" +
                $"{PersianString.RequestTime}:{createHijri}\r\n" +
                $"{PersianString.ForDate}:{forDateHijri}";

            string timePart = string.Empty;
            string typePart = string.Empty;

            if (request.WorkflowType == WorkflowOperations.AllowOverTimes)
            {
                notification.Title = PersianString.RequestOverTime;
            }
            else if (request.WorkflowType == WorkflowOperations.Duty)
            {
                string leaveName = leaveNameResolver?.Invoke(request.LeaveID) ?? string.Empty;
                if (!string.IsNullOrEmpty(leaveName))
                {
                    typePart = $"\r\n{PersianString.Type}:{leaveName}";
                }
                if (request.IsDaily)
                {
                    notification.Title = PersianString.RequestDailyDuty;
                }
                else
                {
                    notification.Title = PersianString.RequestHourlyDuty;
                    timePart = $"\r\n{PersianString.From}:{request.FromTime:HH:mm}\r\n{PersianString.To}:{request.ToTime:HH:mm}";
                }
            }
            else if (request.WorkflowType == WorkflowOperations.Leave)
            {
                string leaveName = leaveNameResolver?.Invoke(request.LeaveID) ?? string.Empty;
                if (!string.IsNullOrEmpty(leaveName))
                {
                    typePart = $"\r\n{PersianString.Type}:{leaveName}";
                }
                if (request.IsDaily)
                {
                    notification.Title = PersianString.RequestDailyLeave;
                }
                else
                {
                    notification.Title = PersianString.RequestHourlyLeave;
                    timePart = $"\r\n{PersianString.From}:{request.FromTime:HH:mm}\r\n{PersianString.To}:{request.ToTime:HH:mm}";
                }
            }
            else if (request.WorkflowType == WorkflowOperations.InsertRecord)
            {
                notification.Title = PersianString.RequestRecord;
            }
            else if (request.WorkflowType == WorkflowOperations.LateEarly)
            {
                notification.Title = request.OverTimeAllowType == AllowType.Late
                    ? PersianString.RequestLate
                    : PersianString.RequestEarly;
            }

            notification.Descriptions += typePart + timePart;
            return notification;
        }

        private static void EnqueueNotification(Notification notification)
        {
            if (notification == null)
                return;

            _recentNotifications.Enqueue(notification);
            while (_recentNotifications.Count > 500 && _recentNotifications.TryDequeue(out _))
            {
            }
        }
    }
}
