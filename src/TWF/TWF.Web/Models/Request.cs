using System;
using MyTime3.Content;
using MyTime3.Controllers;
using T5V3.Misc;
using T5V3.Model;

namespace MyTime3.Models
{
    public class Request
    {
        public T5V3.Model.Request RequestT5 { get; set; }

        public string Name => RequestT5?.RequesterName ?? string.Empty;

        public long RequestID => RequestT5?.RequestID ?? 0L;

        public long BadgeNumber => RequestT5?.BadgeNumber ?? 0L;

        public string Descriptions => RequestT5?.Descriptions ?? string.Empty;

        public string RequestTime => RequestT5 != null ? DateConverter.ToHijriDateWithTimeMins(RequestT5.CreateTime) : string.Empty;

        public bool ForDeputy { get; set; }

        public string DeputyTitleName { get; set; }

        public string AcceptedTitles { get; set; }

        public string CanceledTitle { get; set; }

        public string WaitForTitle { get; set; }

        public bool IsCanceled { get; set; }

        public double Longitude => (RequestT5 != null && RequestT5.Longitude.HasValue) ? RequestT5.Longitude.Value : 0.0;

        public double Latitude => (RequestT5 != null && RequestT5.Latitude.HasValue) ? RequestT5.Latitude.Value : 0.0;

        public string ImageData
        {
            get
            {
                try
                {
                    if (RequestT5?.ImageData == null || RequestT5.ImageData.Length == 0)
                        return string.Empty;
                    return Convert.ToBase64String(RequestT5.ImageData);
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        public string WorkflowName
        {
            get
            {
                if (RequestT5 == null)
                    return string.Empty;

                if (RequestT5.WorkflowType == WorkflowOperations.Leave)
                    return PersianString.Leave;
                if (RequestT5.WorkflowType == WorkflowOperations.Duty)
                    return PersianString.Duty;
                if (RequestT5.WorkflowType == WorkflowOperations.InsertRecord)
                    return PersianString.InsertRecord;
                if (RequestT5.WorkflowType == WorkflowOperations.LateEarly)
                    return PersianString.LateEarly;
                if (RequestT5.WorkflowType == WorkflowOperations.EditOverTimes)
                    return PersianString.EditOverTime;
                if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
                    return PersianString.OverTimeAccess;
                if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
                    return PersianString.RewardAndPenalty;
                return string.Empty;
            }
        }

        public string TypeName
        {
            get
            {
                if (RequestT5 == null)
                    return string.Empty;

                if (RequestT5.WorkflowType == WorkflowOperations.Leave || RequestT5.WorkflowType == WorkflowOperations.Duty)
                {
                    if (DBInitializer.IsTestMode && DBInitializer.TestBackendAdapter != null)
                    {
                        return DBInitializer.TestBackendAdapter.GetLeaveTypeById(RequestT5.LeaveID)?.LeaveName ?? string.Empty;
                    }
                    return DBInitializer.GetT5DBInstance()?.LeaveTypeDA?.GetByID(RequestT5.LeaveID)?.LeaveName ?? string.Empty;
                }
                if (RequestT5.WorkflowType == WorkflowOperations.EditOverTimes)
                {
                    if (RequestT5.OverTimeAllowType == AllowType.OverTimeAfter)
                        return PersianString.OverTimeAfter;
                    if (RequestT5.OverTimeAllowType == AllowType.OverTimeBefore)
                        return PersianString.OverTimeBefore;
                }
                else if (RequestT5.WorkflowType == WorkflowOperations.LateEarly)
                {
                    if (RequestT5.OverTimeAllowType == AllowType.Late)
                        return PersianString.Late;
                    if (RequestT5.OverTimeAllowType == AllowType.Early)
                        return PersianString.Early;
                }
                else if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
                {
                    if (RequestT5.OverTimeAllowType == AllowType.FreeOverTime)
                        return PersianString.FreeOverTime;
                    if (RequestT5.OverTimeAllowType == AllowType.OverTimeAfter)
                        return PersianString.OverTimeAfter;
                    if (RequestT5.OverTimeAllowType == AllowType.OverTimeBefore)
                        return PersianString.OverTimeBefore;
                }
                else if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
                {
                    if (RequestT5.OverTimeAllowType == AllowType.Early)
                        return PersianString.Penalty;
                    if (RequestT5.OverTimeAllowType == AllowType.Late)
                        return PersianString.Reward;
                }
                return string.Empty;
            }
        }

        public string From
        {
            get
            {
                if (RequestT5 == null)
                    return string.Empty;

                if (RequestT5.WorkflowType != WorkflowOperations.Leave && RequestT5.WorkflowType != WorkflowOperations.Duty)
                {
                    if (RequestT5.WorkflowType != WorkflowOperations.InsertRecord)
                    {
                        return DateConverter.ToHijriDate(RequestT5.ForDate);
                    }
                    return DateConverter.ToHijriDateWithTimeMins(RequestT5.RecordTime);
                }
                if (!RequestT5.IsDaily)
                {
                    DateTime dateTime = RequestT5.ForDate.Date;
                    return DateConverter.ToHijriDateWithTimeMins(dateTime.Add(RequestT5.FromTime.TimeOfDay));
                }
                return DateConverter.ToHijriDate(RequestT5.ForDate);
            }
        }

        public string To
        {
            get
            {
                if (RequestT5 == null)
                    return string.Empty;

                string result = string.Empty;
                if (RequestT5.WorkflowType == WorkflowOperations.Leave || RequestT5.WorkflowType == WorkflowOperations.Duty)
                {
                    if (RequestT5.IsDaily)
                    {
                        result = DateConverter.ToHijriDate(RequestT5.ToTime);
                    }
                    else
                    {
                        DateTime dateTime = RequestT5.ForDate.Date;
                        result = DateConverter.ToHijriDateWithTimeMins(dateTime.Add(RequestT5.ToTime.TimeOfDay));
                    }
                }
                if (RequestT5.WorkflowType == WorkflowOperations.AllowOverTimes)
                {
                    result = RequestT5.AllowValue.ToString();
                }
                if (RequestT5.WorkflowType == WorkflowOperations.RewardAndPenalty)
                {
                    result = TimeSpanFuncs.TimeSpanToString(RequestT5.AllowValue, "hhh\\:mm");
                }
                return result;
            }
        }
    }
}
