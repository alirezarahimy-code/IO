using System;

namespace MyTime3.Models
{
    /// <summary>
    /// Form model for the unified "new / edit request" screen (Views/Request/New.cshtml).
    /// Kept flat and DTO-shaped so the view stays strongly typed and Razor-compilable,
    /// while the controller maps it onto GeneralRequest for the workflow engine.
    /// </summary>
    public class NewRequestForm
    {
        public bool IsNew { get; set; }

        public long RequestID { get; set; }

        public long FromUserID { get; set; }

        /// <summary>0=DailyLeave, 1=HourlyLeave, 2=DailyDuty, 3=HourlyDuty, 4=InsertRecord, 5=Late, 6=Early, 7=OverTime.</summary>
        public int RequestType { get; set; }

        public int TypeID { get; set; }

        public string ForDate { get; set; }

        public string ToDate { get; set; }

        public string FromTime { get; set; }

        public string ToTime { get; set; }

        public int LeaveDescriptionTypeID { get; set; }

        public string Description { get; set; }

        /// <summary>Validation / database message rendered at the top of the form.</summary>
        public string Error { get; set; }

        public NewRequestForm()
        {
            IsNew = true;
            RequestType = 0;
            TypeID = -1;
            LeaveDescriptionTypeID = -1;
            ForDate = string.Empty;
            ToDate = string.Empty;
            FromTime = string.Empty;
            ToTime = string.Empty;
            Description = string.Empty;
            Error = string.Empty;
            RequestID = 0L;
            FromUserID = 0L;
        }
    }
}
