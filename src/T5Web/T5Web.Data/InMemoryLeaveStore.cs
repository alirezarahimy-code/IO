using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Data
{
    /// <summary>
    /// In-memory leave store for the tests and the offline demo.
    ///
    /// It implements the same arithmetic as the SQL store so T21 can assert the
    /// invariant "opening + added - used = closing" and the all-or-nothing transfer
    /// without a database.
    /// </summary>
    public sealed class InMemoryLeaveStore : ILeaveStore
    {
        private readonly List<LeaveBalanceRow> _balances = new List<LeaveBalanceRow>();
        private readonly List<LeaveGrantRow> _grants = new List<LeaveGrantRow>();
        private readonly HashSet<int> _carryTypes = new HashSet<int>();
        private readonly Dictionary<int, string> _typeNames = new Dictionary<int, string>();
        private long _nextGrantId = 1;

        public InMemoryLeaveStore Type(int id, string name, bool carry)
        {
            _typeNames[id] = name;
            if (carry)
            {
                _carryTypes.Add(id);
            }
            return this;
        }

        public InMemoryLeaveStore Balance(long badge, int leaveTypeId, int year, double opening,
                                          double added = 0, double used = 0)
        {
            _balances.Add(new LeaveBalanceRow
            {
                BadgeNumber = badge,
                PersonName = "پرسنل " + badge,
                LeaveTypeId = leaveTypeId,
                LeaveTypeName = NameOf(leaveTypeId),
                Year = year,
                OpeningDays = opening,
                AddedDays = added,
                UsedDays = used
            });
            return this;
        }

        public IReadOnlyList<LeaveBalanceRow> GetBalances(int year, int? leaveTypeId, string term, int take)
        {
            return _balances
                .Where(b => b.Year == year)
                .Where(b => !leaveTypeId.HasValue || leaveTypeId.Value <= 0 || b.LeaveTypeId == leaveTypeId.Value)
                .Where(b => string.IsNullOrWhiteSpace(term) ||
                            b.PersonName.IndexOf(term.Trim(), StringComparison.OrdinalIgnoreCase) >= 0 ||
                            b.BadgeNumber.ToString().Contains(term.Trim()))
                .Take(take <= 0 ? 200 : take)
                .ToList();
        }

        public IReadOnlyList<LeaveGrantRow> GetGrants(long? badgeNumber, int? leaveTypeId, int take)
        {
            return _grants
                .Where(g => !badgeNumber.HasValue || badgeNumber.Value <= 0 || g.BadgeNumber == badgeNumber.Value)
                .Where(g => !leaveTypeId.HasValue || leaveTypeId.Value <= 0 || g.LeaveTypeId == leaveTypeId.Value)
                .OrderByDescending(g => g.AssignDate)
                .Take(take <= 0 ? 200 : take)
                .ToList();
        }

        public OperationResult AddGrant(LeaveGrantRow grant)
        {
            if (grant == null || grant.BadgeNumber <= 0)
            {
                return OperationResult.Fail("شماره پرسنلی معتبر نیست.");
            }
            if (grant.LeaveTypeId <= 0)
            {
                return OperationResult.Fail("نوع مرخصی انتخاب نشده است.");
            }
            if (grant.Days <= 0 || grant.Days > 366)
            {
                return OperationResult.Fail("مقدار مرخصی باید بین ۰ و ۳۶۶ روز باشد.");
            }
            if (grant.Days * 2 != Math.Floor(grant.Days * 2))
            {
                return OperationResult.Fail("مقدار مرخصی حداکثر دو رقم اعشار دارد (مثلاً ۰٫۵ روز).");
            }

            grant.Id = _nextGrantId++;
            grant.LeaveTypeName = NameOf(grant.LeaveTypeId);
            _grants.Add(grant);

            // The grant lands in the target balance immediately, exactly like the
            // SQL store's single INSERT would appear to the next read.
            LeaveBalanceRow balance = _balances.FirstOrDefault(b =>
                b.BadgeNumber == grant.BadgeNumber &&
                b.LeaveTypeId == grant.LeaveTypeId &&
                b.Year == grant.AssignDate.Year);
            if (balance != null)
            {
                balance.AddedDays += grant.Days;
            }
            else
            {
                _balances.Add(new LeaveBalanceRow
                {
                    BadgeNumber = grant.BadgeNumber,
                    PersonName = "پرسنل " + grant.BadgeNumber,
                    LeaveTypeId = grant.LeaveTypeId,
                    LeaveTypeName = NameOf(grant.LeaveTypeId),
                    Year = grant.AssignDate.Year,
                    AddedDays = grant.Days
                });
            }
            return OperationResult.Ok();
        }

        public IReadOnlyList<LeaveTransferRow> PreviewTransfer(int fromYear, int toYear, double carryCap,
                                                               long? badgeNumber)
        {
            var rows = new List<LeaveTransferRow>();
            foreach (LeaveBalanceRow balance in GetBalances(fromYear, null, null, 500))
            {
                if (badgeNumber.HasValue && badgeNumber.Value > 0 && balance.BadgeNumber != badgeNumber.Value)
                {
                    continue;
                }

                double closing = balance.ClosingDays;
                if (closing <= 0)
                {
                    continue;
                }
                if (!_carryTypes.Contains(balance.LeaveTypeId))
                {
                    rows.Add(new LeaveTransferRow
                    {
                        BadgeNumber = balance.BadgeNumber,
                        PersonName = balance.PersonName,
                        LeaveTypeId = balance.LeaveTypeId,
                        LeaveTypeName = balance.LeaveTypeName,
                        FromDays = balance.OpeningDays,
                        TransferDays = 0,
                        CarryCap = carryCap,
                        Note = "این نوع مرخصی قابل انتقال نیست."
                    });
                    continue;
                }

                double allowed = carryCap > 0 ? Math.Min(closing, carryCap) : closing;
                rows.Add(new LeaveTransferRow
                {
                    BadgeNumber = balance.BadgeNumber,
                    PersonName = balance.PersonName,
                    LeaveTypeId = balance.LeaveTypeId,
                    LeaveTypeName = balance.LeaveTypeName,
                    FromDays = balance.OpeningDays,
                    TransferDays = Math.Round(allowed, 2),
                    CarryCap = carryCap,
                    Capped = carryCap > 0 && closing > carryCap,
                    Note = carryCap > 0 && closing > carryCap ? "بیش از سقف انتقال؛ مازاد حذف می‌شود." : string.Empty
                });
            }
            return rows;
        }

        public OperationResult CommitTransfer(IReadOnlyList<LeaveTransferRow> rows, int fromYear, int toYear,
                                              string reason, out string detail)
        {
            detail = string.Empty;
            if (rows == null || rows.Count == 0)
            {
                return OperationResult.Fail("سطرهای انتقال خالی است.");
            }
            if (toYear != fromYear + 1)
            {
                return OperationResult.Fail("انتقال فقط به سال بعد مجاز است.");
            }
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 4)
            {
                return OperationResult.Fail("دلیل انتقال (حداقل ۴ نویسه) الزامی است.");
            }

            string marker = "انتقال مانده " + fromYear + " به " + toYear + " — " + reason.Trim();
            bool alreadyDone = _grants.Any(g => g.AssignDate.Year == toYear &&
                                                g.Descriptions.StartsWith("انتقال مانده " + fromYear + " به",
                                                                          StringComparison.Ordinal));
            if (alreadyDone)
            {
                return OperationResult.Fail("انتقال این سال قبلاً انجام شده است.");
            }

            List<LeaveTransferRow> moving = rows.Where(r => r.TransferDays > 0).ToList();
            if (moving.Count == 0)
            {
                return OperationResult.Fail("هیچ مانده‌ای قابل انتقال نیست.");
            }

            // Validate every source row before mutating anything: all-or-nothing.
            foreach (LeaveTransferRow row in moving)
            {
                bool exists = _balances.Any(b => b.BadgeNumber == row.BadgeNumber &&
                                                 b.LeaveTypeId == row.LeaveTypeId &&
                                                 b.Year == fromYear);
                if (!exists)
                {
                    return OperationResult.Fail("سطر مانده برای پرسنل " + row.BadgeNumber +
                                                " در سال مبدأ یافت نشد؛ هیچ تغییری ثبت نشد.");
                }
            }

            double total = 0;
            foreach (LeaveTransferRow row in moving)
            {
                LeaveBalanceRow source = _balances.First(b => b.BadgeNumber == row.BadgeNumber &&
                                                              b.LeaveTypeId == row.LeaveTypeId &&
                                                              b.Year == fromYear);
                source.OpeningDays -= row.TransferDays;
                total += row.TransferDays;

                _grants.Add(new LeaveGrantRow
                {
                    Id = _nextGrantId++,
                    BadgeNumber = row.BadgeNumber,
                    LeaveTypeId = row.LeaveTypeId,
                    LeaveTypeName = NameOf(row.LeaveTypeId),
                    AssignDate = new DateTime(toYear, 1, 1),
                    Days = row.TransferDays,
                    Descriptions = marker
                });

                LeaveBalanceRow target = _balances.FirstOrDefault(b => b.BadgeNumber == row.BadgeNumber &&
                                                                       b.LeaveTypeId == row.LeaveTypeId &&
                                                                       b.Year == toYear);
                if (target == null)
                {
                    _balances.Add(new LeaveBalanceRow
                    {
                        BadgeNumber = row.BadgeNumber,
                        PersonName = row.PersonName,
                        LeaveTypeId = row.LeaveTypeId,
                        LeaveTypeName = NameOf(row.LeaveTypeId),
                        Year = toYear,
                        OpeningDays = row.TransferDays
                    });
                }
                else
                {
                    target.OpeningDays += row.TransferDays;
                }
            }

            detail = "from=" + fromYear + "; to=" + toYear + "; rows=" + moving.Count +
                     "; days=" + total.ToString("0.##") + "; reason=" + reason.Trim();
            return OperationResult.Ok();
        }

        public IReadOnlyList<KeyValuePair<int, string>> GetLeaveTypes()
        {
            return _typeNames.Select(pair => new KeyValuePair<int, string>(pair.Key, pair.Value)).ToList();
        }

        private string NameOf(int leaveTypeId)
        {
            string name;
            return _typeNames.TryGetValue(leaveTypeId, out name) ? name : "نوع " + leaveTypeId;
        }
    }
}
