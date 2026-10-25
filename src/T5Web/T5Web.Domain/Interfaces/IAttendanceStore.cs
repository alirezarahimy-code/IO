using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Attendance punches, leave requests and the monthly summary.
    ///
    /// Kept apart from <see cref="IAdminRepository"/> because these are *operational*
    /// data (hundreds of thousands of rows, always filtered by date) and because the
    /// records screen needs its own validation rules. Kept apart from
    /// <see cref="IModuleStore"/> because a punch has no identity column and a request
    /// has an approval chain - neither fits a generic key/column editor.
    /// </summary>
    public interface IAttendanceStore
    {
        IReadOnlyList<RecordRow> QueryRecords(RecordQuery query);

        /// <summary>Inserts or corrects one punch. The triple (badge, time, type) is the key.</summary>
        OperationResult SaveRecord(RecordRow row, bool isNew);

        IReadOnlyList<RequestRow> QueryRequests(bool pendingOnly, long? badgeNumber, int take);

        RequestRow GetRequest(long requestId);

        /// <summary>
        /// Records one approval step (or a cancellation) for a request.
        ///
        /// The store refuses a duplicate approval of the same step by the same title,
        /// and refuses any decision on a request that is already fully accepted or
        /// cancelled - the legacy screen happily wrote a second approval.
        /// </summary>
        OperationResult DecideRequest(long requestId, long accepterBadgeNumber, int accepterTitleId,
                                     bool cancel, bool isDeputy, out string detail);

        IReadOnlyList<AttendanceSummaryRow> GetMonthlySummary(int year, int month, long? badgeNumber, int take);

        IReadOnlyList<KeyValuePair<int, string>> GetCheckTypes();
    }
}
