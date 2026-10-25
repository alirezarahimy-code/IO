using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>Which rule applies to a person on a date, and why.</summary>
    public sealed class RuleResolution
    {
        public long BadgeNumber { get; set; }
        public DateTime Date { get; set; }
        public int DepartmentId { get; set; }

        /// <summary>The winning rule, or 0 when nothing applies.</summary>
        public int AppliedRuleId { get; set; }

        /// <summary>"user" | "department" | "global" | "none"</summary>
        public string Layer { get; set; } = "none";

        /// <summary>Every layer that matched, strongest first: user, department, global.</summary>
        public IReadOnlyList<string> MatchedLayers { get; set; } = new List<string>();

        /// <summary>Two rules on the same layer matched, or an override silently replaced a rule.</summary>
        public IReadOnlyList<RuleConflict> Conflicts { get; set; } = new List<RuleConflict>();

        public bool HasConflict
        {
            get { return Conflicts.Count > 0; }
        }

        public string LayerFa
        {
            get
            {
                switch (Layer)
                {
                    case "user": return "استثنای پرسنل";
                    case "department": return "استثنای واحد";
                    case "global": return "قاعده عمومی";
                    default: return "بدون قاعده";
                }
            }
        }
    }

    /// <summary>One detected ambiguity in the three-layer rule set.</summary>
    public sealed class RuleConflict
    {
        /// <summary>"ambiguous" (two rules, same layer) or "override" (upper layer wins).</summary>
        public string Kind { get; set; } = "ambiguous";
        public string MessageFa { get; set; } = string.Empty;

        public bool IsBlocking
        {
            get { return Kind == "ambiguous"; }
        }
    }

    /// <summary>
    /// Resolves the legacy three-layer rule set: global rule, department exception,
    /// personnel exception ([Rule], [DepartmentRule], [UserRule]).
    ///
    /// The old screens let all three coexist with no stated precedence, so the same
    /// person could be judged by different rules depending on which screen you opened.
    /// The precedence is now explicit - personnel over department over global - and a
    /// same-layer overlap is reported as a blocking conflict instead of being resolved
    /// by row order.
    /// </summary>
    public sealed class RuleResolutionService
    {
        private readonly IReadOnlyList<RuleLayerRow> _userRules;
        private readonly IReadOnlyList<RuleLayerRow> _departmentRules;
        private readonly int _globalRuleId;

        /// <summary>
        /// Builds the resolver from the three layers. <paramref name="globalRuleId"/> is
        /// the portal-wide default rule (0 when none is configured).
        /// </summary>
        public RuleResolutionService(IReadOnlyList<RuleLayerRow> userRules,
                                     IReadOnlyList<RuleLayerRow> departmentRules,
                                     int globalRuleId)
        {
            _userRules = userRules ?? new List<RuleLayerRow>();
            _departmentRules = departmentRules ?? new List<RuleLayerRow>();
            _globalRuleId = globalRuleId;
        }

        public RuleResolution Resolve(long badgeNumber, int departmentId, DateTime date)
        {
            var result = new RuleResolution
            {
                BadgeNumber = badgeNumber,
                DepartmentId = departmentId,
                Date = date.Date
            };

            var layers = new List<string>();
            List<RuleLayerRow> userMatches = _userRules
                .Where(r => r.BadgeNumber == badgeNumber && Covers(r, date))
                .ToList();
            List<RuleLayerRow> deptMatches = _departmentRules
                .Where(r => r.DepartmentId == departmentId && departmentId > 0 && Covers(r, date))
                .ToList();

            var conflicts = new List<RuleConflict>();
            if (userMatches.Select(r => r.RuleId).Distinct().Count() > 1)
            {
                conflicts.Add(new RuleConflict
                {
                    Kind = "ambiguous",
                    MessageFa = "برای این پرسنل در این تاریخ بیش از یک قاعده ثبت شده است (" +
                                string.Join("، ", userMatches.Select(r => r.RuleId).Distinct()) +
                                ")؛ باید یکی حذف یا بازه‌اش اصلاح شود."
                });
            }
            if (deptMatches.Select(r => r.RuleId).Distinct().Count() > 1)
            {
                conflicts.Add(new RuleConflict
                {
                    Kind = "ambiguous",
                    MessageFa = "برای این واحد در این تاریخ بیش از یک قاعده ثبت شده است (" +
                                string.Join("، ", deptMatches.Select(r => r.RuleId).Distinct()) +
                                ")؛ باید یکی حذف یا بازه‌اش اصلاح شود."
                });
            }

            if (userMatches.Count > 0)
            {
                layers.Add("user");
            }
            if (deptMatches.Count > 0)
            {
                layers.Add("department");
            }
            if (_globalRuleId > 0)
            {
                layers.Add("global");
            }

            string winningLayer;
            if (userMatches.Count > 0)
            {
                result.AppliedRuleId = userMatches[0].RuleId;
                winningLayer = "user";
            }
            else if (deptMatches.Count > 0)
            {
                result.AppliedRuleId = deptMatches[0].RuleId;
                winningLayer = "department";
            }
            else
            {
                result.AppliedRuleId = _globalRuleId;
                winningLayer = _globalRuleId > 0 ? "global" : "none";
            }

            // An override is legal, but it is recorded so the screen can show it as a
            // deliberate decision rather than a silent one.
            if (winningLayer == "user" && deptMatches.Count > 0)
            {
                conflicts.Add(new RuleConflict
                {
                    Kind = "override",
                    MessageFa = "قاعده پرسنل (" + result.AppliedRuleId + ") قاعده واحد (" +
                                deptMatches[0].RuleId + ") را بازنویسی می‌کند."
                });
            }
            else if (winningLayer != "global" && _globalRuleId > 0 && result.AppliedRuleId != _globalRuleId)
            {
                conflicts.Add(new RuleConflict
                {
                    Kind = "override",
                    MessageFa = "قاعده عمومی (" + _globalRuleId + ") با استثنای «" +
                                LayerFa(winningLayer) + "» (" + result.AppliedRuleId + ") بازنویسی می‌شود."
                });
            }

            result.Layer = winningLayer;
            result.MatchedLayers = layers;
            result.Conflicts = conflicts;
            return result;
        }

        /// <summary>
        /// Checks a candidate window before it is saved: overlapping windows for the same
        /// badge (user rules) or the same department are the only blocking case.
        /// </summary>
        public OperationResult ValidateWindow(RuleLayerRow candidate, bool isUserLayer)
        {
            if (candidate == null)
            {
                return OperationResult.Fail("داده‌ای برای بررسی وجود ندارد.");
            }
            if (candidate.FromDate.HasValue && candidate.ToDate.HasValue &&
                candidate.ToDate.Value.Date < candidate.FromDate.Value.Date)
            {
                return OperationResult.Fail("تاریخ پایان نمی‌تواند پیش از تاریخ شروع باشد.");
            }
            if (candidate.RuleId <= 0)
            {
                return OperationResult.Fail("قاعده انتخاب نشده است.");
            }

            IReadOnlyList<RuleLayerRow> pool = isUserLayer ? _userRules : _departmentRules;
            foreach (RuleLayerRow existing in pool)
            {
                if (existing.RowId == candidate.RowId)
                {
                    continue;
                }
                bool sameOwner = isUserLayer
                    ? existing.BadgeNumber == candidate.BadgeNumber
                    : existing.DepartmentId == candidate.DepartmentId;
                if (!sameOwner)
                {
                    continue;
                }
                if (Overlaps(existing, candidate))
                {
                    return OperationResult.Fail(
                        "بازه " + Describe(existing) + " با این بازه هم‌پوشانی دارد؛ ابتدا آن بازه را اصلاح یا حذف کنید.");
                }
            }
            return OperationResult.Ok();
        }

        private static bool Covers(RuleLayerRow row, DateTime date)
        {
            DateTime day = date.Date;
            if (row.FromDate.HasValue && day < row.FromDate.Value.Date)
            {
                return false;
            }
            if (row.ToDate.HasValue && day > row.ToDate.Value.Date)
            {
                return false;
            }
            return true;
        }

        private static bool Overlaps(RuleLayerRow left, RuleLayerRow right)
        {
            DateTime leftStart = left.FromDate.HasValue ? left.FromDate.Value.Date : DateTime.MinValue;
            DateTime leftEnd = left.ToDate.HasValue ? left.ToDate.Value.Date : DateTime.MaxValue;
            DateTime rightStart = right.FromDate.HasValue ? right.FromDate.Value.Date : DateTime.MinValue;
            DateTime rightEnd = right.ToDate.HasValue ? right.ToDate.Value.Date : DateTime.MaxValue;
            return leftStart <= rightEnd && rightStart <= leftEnd;
        }

        private static string Describe(RuleLayerRow row)
        {
            return JalaliText.Format(row.FromDate) + " تا " + JalaliText.Format(row.ToDate);
        }

        private static string LayerFa(string layer)
        {
            switch (layer)
            {
                case "user": return "پرسنل";
                case "department": return "واحد";
                default: return "عمومی";
            }
        }
    }

    /// <summary>
    /// One row of a rule layer, flattened from [UserRule] / [DepartmentRule] (the same
    /// shape for both, so the resolver has no table knowledge).
    /// </summary>
    public sealed class RuleLayerRow
    {
        public long RowId { get; set; }
        public long BadgeNumber { get; set; }
        public int DepartmentId { get; set; }
        public int RuleId { get; set; }
        public int RuleType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
    }
}
