using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory attendance store for the tests.
    ///
    /// It reproduces the two rules that matter operationally: a punch is unique by
    /// (badge, time, type), and a request step can be decided only once - the failure
    /// the legacy screen allowed and the reason T20 exists.
    /// </summary>
    public sealed class InMemoryAttendanceStore : IAttendanceStore
    {
        private readonly List<RecordRow> _records = new List<RecordRow>();
        private readonly List<RequestRow> _requests = new List<RequestRow>();
        private readonly Dictionary<long, List<string>> _decisions =
            new Dictionary<long, List<string>>();

        public InMemoryAttendanceStore Seed(params RecordRow[] records)
        {
            if (records != null)
            {
                _records.AddRange(records);
            }
            return this;
        }

        public InMemoryAttendanceStore SeedRequest(RequestRow request, params string[] decidedTitles)
        {
            if (request != null)
            {
                _requests.Add(request);
            }
            if (decidedTitles != null)
            {
                _decisions[request.RequestId] = decidedTitles.ToList();
            }
            return this;
        }

        public int RecordCount
        {
            get { return _records.Count; }
        }

        public IReadOnlyList<RecordRow> QueryRecords(RecordQuery query)
        {
            RecordQuery q = (query ?? new RecordQuery()).Clamped();
            IEnumerable<RecordRow> rows = _records;

            if (q.BadgeNumber.HasValue)
            {
                rows = rows.Where(r => r.BadgeNumber == q.BadgeNumber.Value);
            }
            if (q.MachineNumber.HasValue)
            {
                rows = rows.Where(r => r.MachineNumber == q.MachineNumber.Value);
            }
            if (q.FromDate.HasValue)
            {
                rows = rows.Where(r => r.CheckTime >= q.FromDate.Value.Date);
            }
            if (q.ToDate.HasValue)
            {
                rows = rows.Where(r => r.CheckTime < q.ToDate.Value.Date.AddDays(1));
            }
            if (q.OnlyInvalid)
            {
                rows = rows.Where(r => !r.IsValid);
            }
            if (q.OnlyManual)
            {
                rows = rows.Where(r => r.IsManual);
            }

            return rows.OrderByDescending(r => r.CheckTime).Take(q.Take).ToList();
        }

        public OperationResult SaveRecord(RecordRow row, bool isNew)
        {
            if (row == null || row.BadgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (row.CheckType < 0 || row.CheckType > 5)
            {
                return OperationResult.Fail("نوع تردد باید بین ۰ تا ۵ باشد.");
            }

            if (isNew)
            {
                bool duplicate = _records.Any(r => r.BadgeNumber == row.BadgeNumber &&
                                                   r.CheckTime == row.CheckTime &&
                                                   r.CheckType == row.CheckType);
                if (duplicate)
                {
                    return OperationResult.Fail("این تردد از قبل ثبت شده است.");
                }
                _records.Add(row);
                return OperationResult.Ok();
            }

            RecordRow current = _records.FirstOrDefault(r => r.BadgeNumber == row.BadgeNumber &&
                                                             r.CheckTime == row.CheckTime &&
                                                             r.CheckType == row.OriginalCheckType);
            if (current == null)
            {
                return OperationResult.Fail("تردد مورد نظر یافت نشد.");
            }
            current.IsValid = row.IsValid;
            current.CheckType = row.CheckType;
            return OperationResult.Ok();
        }

        public IReadOnlyList<RequestRow> QueryRequests(bool pendingOnly, long? badgeNumber, int take)
        {
            IEnumerable<RequestRow> rows = _requests;
            if (badgeNumber.HasValue)
            {
                rows = rows.Where(r => r.BadgeNumber == badgeNumber.Value);
            }
            if (pendingOnly)
            {
                rows = rows.Where(r => r.IsPending);
            }
            return rows.OrderByDescending(r => r.RequestId).Take(take <= 0 ? 100 : take).ToList();
        }

        public RequestRow GetRequest(long requestId)
        {
            return _requests.FirstOrDefault(r => r.RequestId == requestId);
        }

        public OperationResult DecideRequest(long requestId, long accepterBadgeNumber, int accepterTitleId,
                                             bool cancel, bool isDeputy, out string detail)
        {
            detail = string.Empty;
            RequestRow request = GetRequest(requestId);
            if (request == null)
            {
                return OperationResult.Fail("درخواست یافت نشد.");
            }
            if (request.IsSuspended)
            {
                return OperationResult.Fail("این درخواست قبلاً لغو شده است.");
            }

            List<string> decisions;
            if (!_decisions.TryGetValue(requestId, out decisions))
            {
                decisions = new List<string>();
                _decisions[requestId] = decisions;
            }
            if (decisions.Contains(accepterTitleId.ToString()))
            {
                return OperationResult.Fail("این مرحله قبلاً توسط همین عنوان تأیید شده است.");
            }

            decisions.Add(accepterTitleId.ToString());
            request.AcceptedCount = decisions.Count;
            if (cancel)
            {
                request.IsSuspended = true;
            }
            detail = "request=" + requestId + "; acceptor=" + accepterBadgeNumber +
                     "; title=" + accepterTitleId + "; accepted_before=" + (decisions.Count - 1) +
                     (cancel ? "; cancelled" : string.Empty);
            return OperationResult.Ok();
        }

        public IReadOnlyList<AttendanceSummaryRow> GetMonthlySummary(int year, int month, long? badgeNumber, int take)
        {
            DateTime from = new DateTime(year, month, 1);
            DateTime to = from.AddMonths(1);

            var groups = _records
                .Where(r => r.CheckTime >= from && r.CheckTime < to)
                .Where(r => !badgeNumber.HasValue || r.BadgeNumber == badgeNumber.Value)
                .GroupBy(r => r.BadgeNumber)
                .Take(take <= 0 ? 200 : take);

            var summary = new List<AttendanceSummaryRow>();
            foreach (var group in groups)
            {
                List<RecordRow> rows = group.ToList();
                var byDay = rows.GroupBy(r => r.CheckTime.Date);
                summary.Add(new AttendanceSummaryRow
                {
                    BadgeNumber = group.Key,
                    PersonName = rows.First().PersonName,
                    PresentDays = rows.Where(r => r.IsValid).Select(r => r.CheckTime.Date).Distinct().Count(),
                    LateCount = rows.Count(r => !r.IsValid && r.CheckType == 0),
                    EarlyCount = rows.Count(r => !r.IsValid && r.CheckType == 1),
                    MissingPunchDays = byDay.Count(day => day.Count() % 2 == 1)
                });
            }
            return summary;
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetCheckTypes()
        {
            return new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, "ورود"),
                new KeyValuePair<int, string>(1, "خروج"),
                new KeyValuePair<int, string>(2, "شروع استراحت"),
                new KeyValuePair<int, string>(3, "پایان استراحت"),
                new KeyValuePair<int, string>(4, "اضافه‌کاری ورود"),
                new KeyValuePair<int, string>(5, "اضافه‌کاری خروج")
            };
        }
    }
}
