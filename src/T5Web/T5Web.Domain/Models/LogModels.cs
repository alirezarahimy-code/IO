using System;
using System.Collections.Generic;
using System.Globalization;
using T5Web.Domain.Models;

namespace T5Web.Domain.Models
{
    /// <summary>
    /// One line of a system log the portal may read.
    ///
    /// The three logs this portal can show are the ones that live in the two catalogs it
    /// is allowed to open (docs/09):
    ///   * <c>[SystemLog]</c>        - T5V3 desktop: who changed what (engine catalog);
    ///   * <c>[OperationLog]</c>     - device operations per person (Personnel catalog);
    ///   * <c>[DownloadRecordsLog]</c>- one line per download round (engine catalog).
    ///
    /// The device service's own logs (<c>RecordsLog*</c>, <c>CommandLog</c>,
    /// <c>EvenetsLog</c>) are deliberately *not* here: they belong to a catalog the
    /// portal never connects to (docs/11 §2).
    /// </summary>
    public sealed class LogEntryRow
    {
        /// <summary>Stable code of the source ("system", "operation", "download").</summary>
        public string Source { get; set; } = string.Empty;

        public DateTime? Time { get; set; }

        /// <summary>Badge number when the line names a person, 0 otherwise.</summary>
        public long BadgeNumber { get; set; }

        public string PersonName { get; set; } = string.Empty;

        public int MachineNumber { get; set; }

        public string MachineName { get; set; } = string.Empty;

        /// <summary>What happened, in one short phrase (already Persian).</summary>
        public string KindFa { get; set; } = string.Empty;

        /// <summary>Free detail: description, parameter list, error text.</summary>
        public string Detail { get; set; } = string.Empty;

        public string TimeFa
        {
            get { return Time.HasValue ? JalaliText.FormatDateTime(Time) : "—"; }
        }
    }

    /// <summary>A log the portal can show, with the reason it exists.</summary>
    public sealed class LogSourceDescriptor
    {
        public string Code { get; set; } = string.Empty;
        public string TitleFa { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Catalog { get; set; } = "t5";
        public string NoteFa { get; set; } = string.Empty;
    }

    /// <summary>
    /// One referential-integrity check across the schema the portal does not own.
    ///
    /// The engine keeps logical relations instead of foreign keys (and 31 of them cross
    /// two catalogs, so SQL Server *cannot* enforce them - docs/09). This is the only
    /// place an operator can see that a row points at a person, leave type or title that
    /// no longer exists.
    /// </summary>
    public sealed class IntegrityCheckRow
    {
        public string Code { get; set; } = string.Empty;
        public string TitleFa { get; set; } = string.Empty;

        /// <summary>How many orphan rows were found.</summary>
        public int Count { get; set; }

        /// <summary>Where the orphans live, e.g. "LeavesAdded.LeaveTypeID".</summary>
        public string Reference { get; set; } = string.Empty;

        /// <summary>A few example keys, so the operator can go and look.</summary>
        public IReadOnlyList<string> Samples { get; set; } = new List<string>();

        public string NoteFa { get; set; } = string.Empty;

        public bool Passed
        {
            get { return Count == 0; }
        }

        public string StatusFa
        {
            get { return Count == 0 ? "سالم" : "یافت شد"; }
        }

        public string SamplesFa
        {
            get
            {
                if (Samples == null || Samples.Count == 0)
                {
                    return "—";
                }
                string joined = string.Join(" ، ", Samples);
                return Count > Samples.Count ? joined + " …" : joined;
            }
        }
    }
}

namespace T5Web.Domain.Interfaces
{
    /// <summary>
    /// Read-only access to the system logs that live in the portal's two catalogs.
    ///
    /// There is no write path on purpose: these tables are written by the engine and by
    /// the device service. A web console that could edit an audit trail would defeat the
    /// point of having one.
    /// </summary>
    public interface ILogStore
    {
        IReadOnlyList<LogSourceDescriptor> Sources();

        /// <summary>Newest first, capped by <paramref name="take"/>.</summary>
        IReadOnlyList<LogEntryRow> Search(string source, DateTime from, DateTime to,
                                          long? badgeNumber, string term, int take);
    }

    public interface IIntegrityStore
    {
        IReadOnlyList<IntegrityCheckRow> Run();
    }
}
