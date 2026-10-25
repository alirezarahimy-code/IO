using System;
using System.Collections.Generic;
using TWF.Domain.ValueObjects;

namespace TWF.Domain.Models
{
    /// <summary>
    /// Represents a single approval or rejection step recorded in T5 (RequestAcceptMdl).
    /// </summary>
    public sealed class RequestAcceptStepDto
    {
        public int Id { get; set; }
        public long RequestId { get; set; }
        public long AccepterBadgeNumber { get; set; }
        public string AccepterFullName { get; set; } = string.Empty;
        public int AccepterTitleId { get; set; }
        public string AccepterTitleName { get; set; } = string.Empty;
        public DateTime AcceptDate { get; set; }
        public bool IsCancel { get; set; }
        public bool IsDeputy { get; set; }

        public DateTime AcceptedAt { get => AcceptDate; set => AcceptDate = value; }
        public bool IsCanceled { get => IsCancel; set => IsCancel = value; }

        public string AcceptDateJalali => JalaliDateHelper.ToJalaliDateTimeString(AcceptDate);
    }

    /// <summary>
    /// Represents a workflow request and its multi-step approval state in T5.
    /// </summary>
    public sealed class WorkflowRequestItemDto
    {
        public long RequestId { get; set; }
        public long BadgeNumber { get; set; }
        public string RequesterFullName { get; set; } = string.Empty;
        public int RequesterTitleId { get; set; }
        public string RequesterTitleName { get; set; } = string.Empty;
        public TwfWorkflowType WorkflowType { get; set; }
        public bool IsDaily { get; set; }
        public int LeaveTypeId { get; set; }
        public string LeaveTypeName { get; set; } = string.Empty;
        public int LeaveDescriptionId { get; set; }
        public int OverTimeAllowType { get; set; }
        public DateTime RequestDate { get; set; } = DateTime.Now;
        public DateTime ForDate { get; set; }
        public DateTime FromTime { get; set; }
        public DateTime ToTime { get; set; }
        public DateTime RecordTime { get; set; }
        public int RecordCheckType { get; set; }
        public TimeSpan AllowValueDuration { get; set; }
        public string Descriptions { get; set; } = string.Empty;
        public bool IsSuspend { get; set; } = true;
        public TwfRequestState RequestState { get; set; } = TwfRequestState.Waiting;
        public long SenderBadgeNumber { get; set; }
        public int SenderTitleId { get; set; }
        public byte[] ImageData { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public DateTime CreatedAt { get => RequestDate; set => RequestDate = value; }
        public bool IsSuspended { get => IsSuspend; set => IsSuspend = value; }
        public int AllowDurationMinutes
        {
            get => (int)Math.Round(AllowValueDuration.TotalMinutes);
            set => AllowValueDuration = TimeSpan.FromMinutes(value);
        }

        // Deputy context when viewed by an active deputy
        public bool ForDeputy { get; set; }
        public int DeputyPrincipalTitleId { get; set; }
        public string DeputyPrincipalTitleName { get; set; } = string.Empty;

        // Multi-step approval audit trail
        public List<RequestAcceptStepDto> AcceptedSteps { get; set; } = new List<RequestAcceptStepDto>();
        public List<KeyValuePair<int, int>> RemainingAccepters { get; set; } = new List<KeyValuePair<int, int>>();

        public string ForDateJalali => JalaliDateHelper.ToJalaliString(ForDate);
        public string FromDateJalali => JalaliDateHelper.ToJalaliString(FromTime);
        public string ToDateJalali => JalaliDateHelper.ToJalaliString(ToTime);
    }

    /// <summary>
    /// Represents the outcome of a workflow action (Submit, ApproveStep, FinalApprove, Reject, Undo, Edit, Delete).
    /// </summary>
    public sealed class WorkflowActionResult
    {
        public bool Succeeded { get; }
        public string Message { get; }
        public long RequestId { get; }
        public TwfRequestState NewState { get; }
        public bool IsFinalized { get; }
        public bool OperationalRecordInserted { get; }

        public WorkflowActionResult(
            bool succeeded,
            string message,
            long requestId,
            TwfRequestState newState,
            bool isFinalized,
            bool operationalRecordInserted)
        {
            Succeeded = succeeded;
            Message = message ?? string.Empty;
            RequestId = requestId;
            NewState = newState;
            IsFinalized = isFinalized;
            OperationalRecordInserted = operationalRecordInserted;
        }

        public static WorkflowActionResult Success(
            long requestId,
            TwfRequestState newState,
            bool isFinalized,
            bool operationalRecordInserted,
            string message = "")
        {
            return new WorkflowActionResult(true, message, requestId, newState, isFinalized, operationalRecordInserted);
        }

        public static WorkflowActionResult Failure(string errorMessage, long requestId = 0)
        {
            return new WorkflowActionResult(false, errorMessage, requestId, TwfRequestState.UnKnown, false, false);
        }
    }

    /// <summary>
    /// Represents the validation outcome of CheckRequestTime (MaxRequestAgeDays, LastDayForRequest, RequestsLockOADate).
    /// </summary>
    public sealed class RequestTimeValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        public RequestTimeValidationResult(bool isValid, string errorMessage = "")
        {
            IsValid = isValid;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public static RequestTimeValidationResult Valid() => new RequestTimeValidationResult(true, string.Empty);
        public static RequestTimeValidationResult Invalid(string message) => new RequestTimeValidationResult(false, message);
    }
}
