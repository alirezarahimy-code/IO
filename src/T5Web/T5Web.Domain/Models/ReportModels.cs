using System;
using System.Collections.Generic;
using T5Web.Domain.Models;

namespace T5Web.Domain.Models
{
    /// <summary>One report the portal offers.</summary>
    public sealed class ReportDescriptor
    {
        public string Code { get; set; } = string.Empty;
        public string TitleFa { get; set; } = string.Empty;

        /// <summary>The legacy controller this report replaces (kept for the coverage audit).</summary>
        public string LegacyController { get; set; } = string.Empty;

        /// <summary>"query" = computed by the portal, "engine" = rendered by the pinned T5V3.Report.</summary>
        public string Source { get; set; } = "query";

        public AdminFeature Feature { get; set; } = AdminFeature.ReportView;
        public string NoteFa { get; set; } = string.Empty;

        /// <summary>Largest accepted window, in days.</summary>
        public int MaxDays { get; set; } = 366;

        public string SourceFa
        {
            get { return Source == "engine" ? "موتور حضور" : "پرس‌وجوی پورتال"; }
        }

        public bool FromEngine
        {
            get { return Source == "engine"; }
        }
    }

    /// <summary>A rendered report: fixed columns plus rows of strings.</summary>
    public sealed class ReportResult
    {
        public string Code { get; set; } = string.Empty;
        public string TitleFa { get; set; } = string.Empty;
        public IReadOnlyList<string> Columns { get; set; } = new List<string>();
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = new List<IReadOnlyList<string>>();
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>How long the run took, so a slow report is visible instead of mysterious.</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>True when the row limit was hit and the output is truncated.</summary>
        public bool Truncated { get; set; }

        /// <summary>Honest label shown above the grid (e.g. "raw punches, not recalculated").</summary>
        public string NoteFa { get; set; } = string.Empty;

        public int RowCount
        {
            get { return Rows == null ? 0 : Rows.Count; }
        }

        public string RangeFa
        {
            get { return JalaliText.Format(From) + " تا " + JalaliText.Format(To); }
        }
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Executes a report. Implementations never reach the network and never stream a
    /// remote font or image: the export is built in memory and returned to the browser.
    /// </summary>
    public interface IReportStore
    {
        /// <summary>Runs one report for a window. A bad window must be rejected by the caller.</summary>
        ReportResult Run(string code, DateTime from, DateTime to, long? badgeNumber, int? departmentId);
    }
}
