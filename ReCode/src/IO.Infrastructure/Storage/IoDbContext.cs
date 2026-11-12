using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Storage;

/// <summary>Initial SQL Server schema only; no automatic migration at application startup.</summary>
public sealed class IoDbContext(DbContextOptions<IoDbContext> options) : DbContext(options)
{
    public DbSet<DevicePollScheduleRecord> DevicePollSchedules => Set<DevicePollScheduleRecord>();
    public DbSet<DevicePollRunRecord> DevicePollRuns => Set<DevicePollRunRecord>();
    public DbSet<DeviceAttendanceDownloadRecord> DeviceAttendanceDownloads => Set<DeviceAttendanceDownloadRecord>();
    public DbSet<DeviceAttendanceRowRecord> DeviceAttendanceRows => Set<DeviceAttendanceRowRecord>();
    public DbSet<DeviceFileImportRecord> DeviceFileImports => Set<DeviceFileImportRecord>();
    public DbSet<DeviceFileImportLineRecord> DeviceFileImportLines => Set<DeviceFileImportLineRecord>();
    public DbSet<DeviceUserUploadRecord> DeviceUserUploads => Set<DeviceUserUploadRecord>();
    public DbSet<DeviceUserDownloadRecord> DeviceUserDownloads => Set<DeviceUserDownloadRecord>();
    public DbSet<DeviceDownloadedUserRecord> DeviceDownloadedUsers => Set<DeviceDownloadedUserRecord>();
    public DbSet<DeviceOperationRecord> DeviceOperations => Set<DeviceOperationRecord>();
    public DbSet<DeviceConnectionRecord> DeviceConnections => Set<DeviceConnectionRecord>();
    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<CompanyRecord> Companies => Set<CompanyRecord>();
    public DbSet<SiteRecord> Sites => Set<SiteRecord>();
    public DbSet<OrgUnitRecord> OrgUnits => Set<OrgUnitRecord>();
    public DbSet<EmployeeRecord> Employees => Set<EmployeeRecord>();
    public DbSet<EmployeeAssignmentRecord> EmployeeAssignments => Set<EmployeeAssignmentRecord>();
    public DbSet<EmploymentProfileRecord> EmploymentProfiles => Set<EmploymentProfileRecord>();
    public DbSet<EmployeeSiteAssignmentRecord> EmployeeSiteAssignments => Set<EmployeeSiteAssignmentRecord>();
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<PermissionRecord> Permissions => Set<PermissionRecord>();
    public DbSet<UserRoleRecord> UserRoles => Set<UserRoleRecord>();
    public DbSet<RolePermissionRecord> RolePermissions => Set<RolePermissionRecord>();
    public DbSet<UserOrgScopeRecord> UserOrgScopes => Set<UserOrgScopeRecord>();
    public DbSet<CalendarRecord> Calendars => Set<CalendarRecord>();
    public DbSet<CalendarDayRecord> CalendarDays => Set<CalendarDayRecord>();
    public DbSet<ShiftDefinitionRecord> ShiftDefinitions => Set<ShiftDefinitionRecord>();
    public DbSet<WorkScheduleAssignmentRecord> WorkScheduleAssignments => Set<WorkScheduleAssignmentRecord>();
    public DbSet<EventSourceRecord> EventSources => Set<EventSourceRecord>();
    public DbSet<RawAttendanceEventRecord> RawAttendanceEvents => Set<RawAttendanceEventRecord>();
    public DbSet<SourceEmployeeMappingRecord> SourceEmployeeMappings => Set<SourceEmployeeMappingRecord>();
    public DbSet<SourceMappingChangeRecord> SourceMappingChanges => Set<SourceMappingChangeRecord>();
    public DbSet<RawEventConflictRecord> RawEventConflicts => Set<RawEventConflictRecord>();
    public DbSet<RawConflictNoteRecord> RawConflictNotes => Set<RawConflictNoteRecord>();
    public DbSet<ObservedRuleVersionRecord> ObservedRuleVersions => Set<ObservedRuleVersionRecord>();
    public DbSet<DailyCalculationRunRecord> DailyCalculationRuns => Set<DailyCalculationRunRecord>();
    public DbSet<CalculationInputRevisionRecord> CalculationInputRevisions => Set<CalculationInputRevisionRecord>();
    public DbSet<CorrectionPolicyRecord> CorrectionPolicies => Set<CorrectionPolicyRecord>();
    public DbSet<CorrectionRequestRecord> CorrectionRequests => Set<CorrectionRequestRecord>();
    public DbSet<CorrectionDecisionRecord> CorrectionDecisions => Set<CorrectionDecisionRecord>();
    public DbSet<PeriodDraftRecord> PeriodDrafts => Set<PeriodDraftRecord>();
    public DbSet<PeriodDiagnosticRecord> PeriodDiagnostics => Set<PeriodDiagnosticRecord>();
    public DbSet<LeaveTypeRecord> LeaveTypes => Set<LeaveTypeRecord>();
    public DbSet<LeaveLedgerEntryRecord> LeaveLedgerEntries => Set<LeaveLedgerEntryRecord>();
    public DbSet<LeaveRequestRecord> LeaveRequests => Set<LeaveRequestRecord>();
    public DbSet<LeaveDecisionRecord> LeaveDecisions => Set<LeaveDecisionRecord>();
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    public DbSet<DelegationRecord> Delegations => Set<DelegationRecord>();
    public DbSet<DelegationRevocationRecord> DelegationRevocations => Set<DelegationRevocationRecord>();

    public DbSet<CorrectionSettingsRecord> CorrectionSettings => Set<CorrectionSettingsRecord>();
    public DbSet<CorrectionDelegationGrantRecord> CorrectionDelegationGrants => Set<CorrectionDelegationGrantRecord>();
    public DbSet<DelegatedCorrectionDecisionRecord> DelegatedCorrectionDecisions => Set<DelegatedCorrectionDecisionRecord>();

    public DbSet<CorrectionDecisionReversalRecord> CorrectionDecisionReversals => Set<CorrectionDecisionReversalRecord>();
    public DbSet<LeaveDecisionReversalRecord> LeaveDecisionReversals => Set<LeaveDecisionReversalRecord>();

    public DbSet<DeviceGroupRecord> DeviceGroups => Set<DeviceGroupRecord>();
    public DbSet<DeviceRecord> Devices => Set<DeviceRecord>();
    public DbSet<DeviceSecretRecord> DeviceSecrets => Set<DeviceSecretRecord>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DeviceFileImportRecord>(b =>
        {
            b.ToTable("DeviceFileImports",t=>
            {
                t.HasTrigger("TR_DeviceFileImports_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceFileImports_File","DATALENGTH([OriginalFile]) BETWEEN 1 AND 1048576 AND DATALENGTH([ExpectedDeviceVersion])=8 AND LEN([FileHash])=64 AND [LineCount] BETWEEN 1 AND 10000");
                t.HasCheckConstraint("CK_DeviceFileImports_Interpretation","[Calendar] COLLATE Latin1_General_100_BIN2 IN(N'gregorian',N'persian') AND [MachineNumber]>0 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode]");
                t.HasCheckConstraint("CK_DeviceFileImports_Utc","DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[LastReviewedAtUtc])=0 AND ([LastReviewedAtUtc] IS NULL OR [LastReviewedAtUtc]>=[CreatedAtUtc])");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SiteId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CompanyId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CreatedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CreatedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired().HasColumnType("varbinary(8)");
            b.Property(x=>x.OriginalFile).IsRequired().HasColumnType("varbinary(max)");
            b.Property(x=>x.FileHash).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.Property(x=>x.Calendar).HasMaxLength(16).IsRequired().HasColumnType("nvarchar(16)");
            b.Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired().HasColumnType("nvarchar(100)");
            b.Property(x=>x.MachineNumber).HasColumnType("int");
            b.Property(x=>x.InCode).HasColumnType("int");
            b.Property(x=>x.OutCode).HasColumnType("int");
            b.Property(x=>x.LineCount).HasColumnType("int");
            b.Property(x=>x.LastReviewedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.Version).IsRowVersion().IsRequired().HasColumnType("rowversion");
            b.HasIndex(x=>new{x.DeviceId,x.FileHash}).IsUnique();
            b.HasIndex(x=>x.DeviceId);
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SourceId);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SiteId);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x=>x.SiteId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.CompanyId);
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.CreatedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceFileImportLineRecord>(b =>
        {
            b.ToTable("DeviceFileImportLines",t=>
            {
                t.HasTrigger("TR_DeviceFileImportLines_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceFileImportLines_Receipt","[LineNumber] BETWEEN 1 AND 10000 AND LEN([Status])>0 AND DATEPART(TZOFFSET,[ReviewedAtUtc])=0 AND ([ConflictId] IS NULL OR [RawEventId] IS NOT NULL) AND (([Status] IN(N'imported',N'already-recorded') AND [RawEventId] IS NOT NULL AND [ConflictId] IS NULL) OR ([Status]=N'ingestion-conflict' AND [RawEventId] IS NOT NULL AND [ConflictId] IS NOT NULL) OR ([Status] NOT IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NULL AND [ConflictId] IS NULL))");
            });
            b.HasKey(x=>new{x.ImportId,x.LineNumber});b.Property(x=>x.LineNumber).ValueGeneratedNever();
            b.Property(x=>x.ImportId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.LineNumber).HasColumnType("int");
            b.Property(x=>x.Status).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.Property(x=>x.RawEventId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ConflictId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ReviewedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ReviewedAtUtc).HasColumnType("datetimeoffset");
            b.HasOne<DeviceFileImportRecord>().WithMany().HasForeignKey(x=>x.ImportId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.RawEventId);
            b.HasOne<RawAttendanceEventRecord>().WithMany().HasForeignKey(x=>x.RawEventId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ConflictId);
            b.HasOne<RawEventConflictRecord>().WithMany().HasForeignKey(x=>x.ConflictId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ReviewedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DevicePollScheduleRecord>(b=>
        {
            b.ToTable("DevicePollSchedules",t=>
            {
                t.HasTrigger("TR_DevicePollSchedules_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DevicePollSchedules_Config","DATALENGTH([ExpectedDeviceVersion])=8 AND [EverySeconds] BETWEEN 15 AND 86400 AND [WindowSeconds] BETWEEN 60 AND 86400 AND [LagSeconds] BETWEEN 0 AND 86400 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode] AND [Failures]>=0 AND [CursorUtc]>=[StartUtc] AND ([Enabled]=1 OR [ActiveRunId] IS NULL)");
                t.HasCheckConstraint("CK_DevicePollSchedules_Utc","DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[StartUtc])=0 AND DATEPART(TZOFFSET,[CursorUtc])=0 AND DATEPART(TZOFFSET,[NextDueUtc])=0");
            });
            b.HasKey(x=>x.Id);
            b.Property(x=>x.Id).ValueGeneratedNever().HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SiteId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CompanyId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired().HasColumnType("nvarchar(100)");
            b.Property(x=>x.CreatedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CreatedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired().HasColumnType("varbinary(8)");
            b.Property(x=>x.StartUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.EverySeconds).HasColumnType("int");
            b.Property(x=>x.WindowSeconds).HasColumnType("int");
            b.Property(x=>x.LagSeconds).HasColumnType("int");
            b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
            b.Property(x=>x.InCode).HasColumnType("int");
            b.Property(x=>x.OutCode).HasColumnType("int");
            b.Property(x=>x.AutoCommit).HasColumnType("bit");
            b.Property(x=>x.Enabled).HasColumnType("bit");
            b.Property(x=>x.CursorUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.NextDueUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ActiveRunId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.Failures).HasColumnType("int");
            b.Property(x=>x.LastOutcome).HasMaxLength(32).IsRequired().HasColumnType("nvarchar(32)");
            b.Property(x=>x.Version).IsRequired().IsRowVersion().HasColumnType("rowversion");
            b.HasIndex(x=>x.DeviceId).IsUnique().HasFilter("[Enabled] = 1");
            b.HasIndex(x=>new{x.NextDueUtc,x.Id});
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SourceId);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SiteId);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x=>x.SiteId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.CompanyId);
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.CreatedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ActiveRunId);
            b.HasOne<DevicePollRunRecord>().WithMany().HasForeignKey(x=>x.ActiveRunId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DevicePollRunRecord>(b=>
        {
            b.ToTable("DevicePollRuns",t=>
            {
                t.HasTrigger("TR_DevicePollRuns_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DevicePollRuns_Data","[FromUtc]<[UntilUtcExclusive] AND [NextAfter]>=0 AND DATEPART(TZOFFSET,[CreatedAtUtc])=0 AND DATEPART(TZOFFSET,[FromUtc])=0 AND DATEPART(TZOFFSET,[UntilUtcExclusive])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND (([Outcome] IN(N'queued',N'collecting',N'reviewing') AND [FinishedAtUtc] IS NULL) OR ([Outcome] NOT IN(N'queued',N'collecting',N'reviewing') AND [FinishedAtUtc] IS NOT NULL AND [FinishedAtUtc]>=[CreatedAtUtc]))");
            });
            b.HasKey(x=>x.Id);
            b.Property(x=>x.Id).ValueGeneratedNever().HasColumnType("uniqueidentifier");
            b.Property(x=>x.ScheduleId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DownloadId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.FromUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.UntilUtcExclusive).HasColumnType("datetimeoffset");
            b.Property(x=>x.CreatedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.NextAfter).HasColumnType("bigint");
            b.Property(x=>x.Outcome).HasMaxLength(32).IsRequired().HasColumnType("nvarchar(32)");
            b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
            b.HasIndex(x=>x.DownloadId).IsUnique();
            b.HasIndex(x=>new{x.ScheduleId,x.CreatedAtUtc,x.Id});
            b.HasOne<DevicePollScheduleRecord>().WithMany().HasForeignKey(x=>x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceAttendanceDownloadRecord>().WithMany().HasForeignKey(x=>x.DownloadId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceAttendanceDownloadRecord>(b=>
        {
            b.ToTable("DeviceAttendanceDownloads",t=>
            {
                t.HasTrigger("TR_DeviceAttendanceDownloads_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceAttendanceDownloads_Request","DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [ReceivedCount]>=0 AND [FromUtc]<[UntilUtcExclusive] AND DATEPART(TZOFFSET,[FromUtc])=0 AND DATEPART(TZOFFSET,[UntilUtcExclusive])=0 AND [InCode] BETWEEN 0 AND 255 AND [OutCode] BETWEEN 0 AND 255 AND [InCode]<>[OutCode]");
                t.HasCheckConstraint("CK_DeviceAttendanceDownloads_State","[State] COLLATE Latin1_General_100_BIN2 IN(N'pending',N'running',N'succeeded',N'incomplete',N'failed',N'interrupted',N'timed-out',N'unsupported',N'identity-mismatch',N'credential-unavailable',N'endpoint-denied',N'bridge-failed',N'cancelled',N'revoked',N'device-changed')");
                t.HasCheckConstraint("CK_DeviceAttendanceDownloads_Lifecycle","(([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NULL AND [ReceivedCount]=0) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL AND [ReceivedCount]=0) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[LeaseAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND DATEPART(TZOFFSET,[LastReviewedAtUtc])=0 AND ([LeaseAtUtc] IS NULL OR [LeaseAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([LeaseAtUtc],[RequestedAtUtc])) AND ([LastReviewedAtUtc] IS NULL OR ([State]=N'succeeded' AND [LastReviewedAtUtc]>=[FinishedAtUtc]))");
            });
            b.HasKey(x=>x.Id);
            b.Property(x=>x.Id).ValueGeneratedNever().HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SiteId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CompanyId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired().HasColumnType("varbinary(8)");
            b.Property(x=>x.FromUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.UntilUtcExclusive).HasColumnType("datetimeoffset");
            b.Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired().HasColumnType("nvarchar(100)");
            b.Property(x=>x.InCode).HasColumnType("int");
            b.Property(x=>x.OutCode).HasColumnType("int");
            b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
            b.Property(x=>x.State).HasMaxLength(32).IsRequired().HasColumnType("nvarchar(32)");
            b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.LeaseAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ReceivedCount).HasColumnType("bigint");
            b.Property(x=>x.LastReviewedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.Version).IsRequired().IsRowVersion().HasColumnType("rowversion");
            b.HasIndex(x=>x.DeviceId).IsUnique().HasFilter("[State] IN (N'pending', N'running')");
            b.HasIndex(x=>new{x.RequestedAtUtc,x.Id});
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SourceId);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SiteId);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x=>x.SiteId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.CompanyId);
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.RequestedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ServiceUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceAttendanceRowRecord>(b=>
        {
            b.ToTable("DeviceAttendanceRows",t=>
            {
                t.HasTrigger("TR_DeviceAttendanceRows_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceAttendanceRows_Data","[Sequence]>0 AND [OccurrenceOrdinal]>0 AND ISJSON([PayloadJson])=1 AND LEN([PayloadHash])=64");
                t.HasCheckConstraint("CK_DeviceAttendanceRows_Receipt","(([ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [Status]=N'received' AND [RawEventId] IS NULL AND [ConflictId] IS NULL) OR ([ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ReviewedAtUtc])=0)) AND ([ConflictId] IS NULL OR [RawEventId] IS NOT NULL) AND (([Status] IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NOT NULL) OR ([Status] NOT IN(N'imported',N'already-recorded',N'ingestion-conflict') AND [RawEventId] IS NULL AND [ConflictId] IS NULL))");
            });
            b.HasKey(x=>new{x.DownloadId,x.Sequence});
            b.Property(x=>x.DownloadId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.Sequence).HasColumnType("bigint");
            b.Property(x=>x.PayloadJson).HasMaxLength(2000).IsRequired().HasColumnType("nvarchar(2000)");
            b.Property(x=>x.PayloadHash).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.Property(x=>x.OccurrenceOrdinal).HasColumnType("bigint");
            b.Property(x=>x.Status).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.Property(x=>x.RawEventId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ConflictId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ReviewedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ReviewedAtUtc).HasColumnType("datetimeoffset");
            b.HasIndex(x=>new{x.DownloadId,x.PayloadHash,x.OccurrenceOrdinal}).IsUnique();
            b.HasOne<DeviceAttendanceDownloadRecord>().WithMany().HasForeignKey(x=>x.DownloadId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.RawEventId);
            b.HasOne<RawAttendanceEventRecord>().WithMany().HasForeignKey(x=>x.RawEventId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ConflictId);
            b.HasOne<RawEventConflictRecord>().WithMany().HasForeignKey(x=>x.ConflictId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ReviewedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceUserUploadRecord>(b =>
        {
            b.ToTable("DeviceUserUploads",t=>
            {
                t.HasTrigger("TR_DeviceUserUploads_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceUserUploads_Request","DATALENGTH([ExpectedDeviceVersion])=8 AND LEN([RequestHash])=64 AND DATALENGTH([PersonIdentifier]) BETWEEN 2 AND 80 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND ISJSON([DesiredJson])=1 AND ([ExpectedJson] IS NULL OR ISJSON([ExpectedJson])=1)");
                t.HasCheckConstraint("CK_DeviceUserUploads_State","[State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'unchanged',N'conflict',N'unsupported',N'identity-mismatch',N'failed-before-write',N'unknown',N'cancelled',N'revoked',N'device-changed')");
                t.HasCheckConstraint("CK_DeviceUserUploads_Lifecycle","(([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[ClaimedAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([ClaimedAtUtc],[RequestedAtUtc]))");
                t.HasCheckConstraint("CK_DeviceUserUploads_Resolution","([ResolutionSnapshotId] IS NULL AND [ResolvedByUserId] IS NULL AND [ResolvedAtUtc] IS NULL) OR ([State]=N'unknown' AND [ResolutionSnapshotId] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedAtUtc]>=[FinishedAtUtc] AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0)");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.BatchId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestHash).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.EmployeeId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SnapshotId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired().HasColumnType("varbinary(8)");
            b.Property(x=>x.PersonIdentifier).HasMaxLength(40).IsRequired().UseCollation("Latin1_General_100_BIN2").HasColumnType("nvarchar(40)");
            b.Property(x=>x.ExpectedJson).HasMaxLength(2000).HasColumnType("nvarchar(2000)");
            b.Property(x=>x.DesiredJson).HasMaxLength(2000).IsRequired().HasColumnType("nvarchar(2000)");
            b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
            b.Property(x=>x.State).HasMaxLength(32).IsRequired().HasColumnType("nvarchar(32)");
            b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ResolutionSnapshotId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ResolvedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ResolvedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.Version).IsRequired().IsRowVersion().HasColumnType("rowversion");
            b.HasIndex(x=>new{x.BatchId,x.Id});b.HasIndex(x=>new{x.RequestedAtUtc,x.Id});
            b.HasIndex(x=>x.DeviceId).IsUnique().HasFilter("[State] = N'running'");
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.EmployeeId);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x=>x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.SnapshotId);
            b.HasOne<DeviceUserDownloadRecord>().WithMany().HasForeignKey(x=>x.SnapshotId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.RequestedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ServiceUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ResolutionSnapshotId);
            b.HasOne<DeviceUserDownloadRecord>().WithMany().HasForeignKey(x=>x.ResolutionSnapshotId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x=>x.ResolvedByUserId);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceUserDownloadRecord>(b =>
        {
            b.ToTable("DeviceUserDownloads",t=>
            {
                t.HasTrigger("TR_DeviceUserDownloads_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceUserDownloads_Request","DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 600 AND [ReceivedCount]>=0 AND DATEPART(TZOFFSET,[RequestedAtUtc])=0");
                t.HasCheckConstraint("CK_DeviceUserDownloads_State","[State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'incomplete',N'failed',N'interrupted',N'timed-out',N'unsupported',N'identity-mismatch',N'credential-unavailable',N'endpoint-denied',N'bridge-failed',N'cancelled',N'revoked',N'device-changed')");
                t.HasCheckConstraint("CK_DeviceUserDownloads_Lifecycle","(([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NULL AND [ReceivedCount]=0) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN(N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [LeaseAtUtc] IS NULL AND [FinishedAtUtc] IS NOT NULL AND [ReceivedCount]=0) OR ([State] NOT IN(N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [LeaseAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NOT NULL)) AND DATEPART(TZOFFSET,[LeaseAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([LeaseAtUtc] IS NULL OR [LeaseAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=COALESCE([LeaseAtUtc],[RequestedAtUtc]))");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.CompanyId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired().HasColumnType("varbinary(8)");
            b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
            b.Property(x=>x.State).HasMaxLength(32).IsRequired().HasColumnType("nvarchar(32)");
            b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.LeaseAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ReceivedCount).HasColumnType("bigint");
            b.Property(x=>x.Version).IsRequired().IsRowVersion().HasColumnType("rowversion");
            b.HasIndex(x=>x.DeviceId).IsUnique().HasFilter("[State] IN (N'pending', N'running')");
            b.HasIndex(x=>new{x.RequestedAtUtc,x.Id});
            b.HasIndex(x=>x.SourceId);b.HasIndex(x=>x.CompanyId);b.HasIndex(x=>x.RequestedByUserId);b.HasIndex(x=>x.ServiceUserId);
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceDownloadedUserRecord>(b =>
        {
            b.ToTable("DeviceDownloadedUsers",t=>
            {
                t.HasTrigger("TR_DeviceDownloadedUsers_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceDownloadedUsers_Data","[Sequence]>0 AND LEN([PersonIdentifier]) BETWEEN 1 AND 40 AND [Privilege] BETWEEN 0 AND 3 AND LEN([Fingerprint])=64");
            });
            b.HasKey(x=>new{x.DownloadId,x.Sequence});b.Property(x=>x.Sequence).ValueGeneratedNever();
            b.Property(x=>x.DownloadId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.Sequence).HasColumnType("bigint");
            b.Property(x=>x.PersonIdentifier).HasMaxLength(40).IsRequired().UseCollation("Latin1_General_100_BIN2").HasColumnType("nvarchar(40)");
            b.Property(x=>x.Name).HasMaxLength(200).IsRequired().HasColumnType("nvarchar(200)");
            b.Property(x=>x.Card).HasMaxLength(40).IsRequired(false).HasColumnType("nvarchar(40)");
            b.Property(x=>x.Privilege).HasColumnType("int");
            b.Property(x=>x.Enabled).HasColumnType("bit");
            b.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired().HasColumnType("nvarchar(64)");
            b.HasIndex(x=>new{x.DownloadId,x.PersonIdentifier});
            b.HasOne<DeviceUserDownloadRecord>().WithMany().HasForeignKey(x=>x.DownloadId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceOperationRecord>(b =>
        {
            b.ToTable("DeviceOperations",t=>
            {
                t.HasTrigger("TR_DeviceOperations_History");t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceOperations_Request","DATALENGTH([ExpectedDeviceVersion])=8 AND [TimeoutSeconds] BETWEEN 1 AND 60 AND (([Kind]=N'information.read' AND [TargetUtc] IS NULL) OR ([Kind]=N'clock.set' AND [TargetUtc] IS NOT NULL AND DATEPART(TZOFFSET,[TargetUtc])=0 AND DATEPART(NANOSECOND,[TargetUtc])=0))");
                t.HasCheckConstraint("CK_DeviceOperations_State","[State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'succeeded',N'failed',N'unsupported',N'unknown',N'interrupted',N'timed-out',N'identity-mismatch',N'credential-unavailable',N'bridge-failed',N'endpoint-denied',N'cancelled',N'revoked',N'device-changed',N'expired')");
                t.HasCheckConstraint("CK_DeviceOperations_Lifecycle","(([State]=N'pending' AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State]=N'running' AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN (N'cancelled',N'revoked',N'device-changed',N'expired') AND [FinishedAtUtc] IS NOT NULL AND [ClaimId] IS NULL AND [ServiceUserId] IS NULL AND [ClaimedAtUtc] IS NULL) OR ([State] NOT IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'expired') AND [FinishedAtUtc] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc]>=[ClaimedAtUtc])) AND DATEPART(TZOFFSET,[RequestedAtUtc])=0 AND DATEPART(TZOFFSET,[ClaimedAtUtc])=0 AND DATEPART(TZOFFSET,[FinishedAtUtc])=0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc]>=[RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc]>=[RequestedAtUtc])");
                t.HasCheckConstraint("CK_DeviceOperations_Result","([ResultJson] IS NULL OR ([Kind]=N'information.read' AND [State]=N'succeeded' AND ISJSON([ResultJson])=1 AND DATALENGTH([ResultJson])<=8000)) AND ([Kind]<>N'information.read' OR [State]<>N'succeeded' OR [ResultJson] IS NOT NULL)");
                t.HasCheckConstraint("CK_DeviceOperations_Resolution","([ResolutionInfoId] IS NULL AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL) OR ([Kind]=N'clock.set' AND [State]=N'unknown' AND [ResolutionInfoId] IS NOT NULL AND [ResolutionInfoId]<>[Id] AND [ResolvedByUserId] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL AND DATEPART(TZOFFSET,[ResolvedAtUtc])=0 AND [ResolvedAtUtc]>=[FinishedAtUtc])");
            });
            b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.Property(x=>x.Version).IsRowVersion();
            b.Property(x=>x.Kind).HasMaxLength(40).IsRequired();b.Property(x=>x.State).HasMaxLength(32).IsRequired();b.Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired();
            b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired();b.Property(x=>x.ResultJson).HasMaxLength(4000);
            b.HasIndex(x=>x.DeviceId).IsUnique().HasFilter("[State] IN (N'pending', N'running')");b.HasIndex(x=>new{x.DeviceId,x.RequestedAtUtc});
            b.HasIndex(x=>x.RequestedByUserId);b.HasIndex(x=>x.ServiceUserId);b.HasIndex(x=>x.ResolvedByUserId);b.HasIndex(x=>x.ResolutionInfoId);
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceOperationRecord>().WithMany().HasForeignKey(x=>x.ResolutionInfoId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceConnectionRecord>(b =>
        {
            b.ToTable("DeviceConnections", t =>
            {
                t.HasTrigger("TR_DeviceConnections_History"); t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceConnections_Timeout", "[TimeoutSeconds] BETWEEN 1 AND 60 AND DATALENGTH([ExpectedDeviceVersion]) = 8");
                t.HasCheckConstraint("CK_DeviceConnections_State", "[State] COLLATE Latin1_General_100_BIN2 IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed',N'tcp-reachable',N'unreachable',N'timed-out',N'unsupported',N'endpoint-denied',N'interrupted',N'sdk-serial-matched',N'sdk-identity-mismatch',N'sdk-unavailable',N'credential-unavailable',N'bridge-failed')");
                t.HasCheckConstraint("CK_DeviceConnections_Lifecycle", "(([State] = N'pending' AND [ServiceUserId] IS NULL AND [ClaimId] IS NULL AND [ClaimedAtUtc] IS NULL AND [FinishedAtUtc] IS NULL) OR ([State] = N'running' AND [ServiceUserId] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] IS NULL) OR ([State] IN (N'cancelled',N'revoked',N'device-changed') AND [FinishedAtUtc] IS NOT NULL AND [ServiceUserId] IS NULL AND [ClaimId] IS NULL AND [ClaimedAtUtc] IS NULL) OR ([State] NOT IN (N'pending',N'running',N'cancelled',N'revoked',N'device-changed') AND [FinishedAtUtc] IS NOT NULL AND [ServiceUserId] IS NOT NULL AND [ClaimId] IS NOT NULL AND [ClaimedAtUtc] IS NOT NULL AND [FinishedAtUtc] >= [ClaimedAtUtc])) AND DATEPART(TZOFFSET,[RequestedAtUtc]) = 0 AND DATEPART(TZOFFSET,[ClaimedAtUtc]) = 0 AND DATEPART(TZOFFSET,[FinishedAtUtc]) = 0 AND ([ClaimedAtUtc] IS NULL OR [ClaimedAtUtc] >= [RequestedAtUtc]) AND ([FinishedAtUtc] IS NULL OR [FinishedAtUtc] >= [RequestedAtUtc])");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired();
            b.Property(x => x.State).HasMaxLength(32).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.DeviceId, x.RequestedAtUtc });
            b.HasIndex(x => x.DeviceId).IsUnique().HasFilter("[State] IN (N'pending', N'running')");
            b.HasIndex(x => x.RequestedByUserId); b.HasIndex(x => x.ServiceUserId);
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceGroupRecord>(b =>
        {
            b.ToTable("DeviceGroups", t =>
            {
                t.HasTrigger("TR_DeviceGroups_Integrity"); t.UseSqlOutputClause(false);
                t.HasCheckConstraint("CK_DeviceGroups_Parent", "[ParentId] IS NULL OR [ParentId] <> [Id]");
                t.HasCheckConstraint("CK_DeviceGroups_Deletion", "([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL AND [DeletedFromVersion] IS NULL) OR ([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL AND [DeletedFromVersion] IS NOT NULL AND DATALENGTH([DeletedFromVersion]) = 8)");
                t.HasCheckConstraint("CK_DeviceGroups_Utc", "DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[DeletedAtUtc]) = 0 AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ([DeletedAtUtc] IS NULL OR [DeletedAtUtc] >= [CreatedAtUtc])");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.CreationFingerprint).HasMaxLength(64).IsRequired();
            b.Property(x => x.DeletedFromVersion).HasMaxLength(8);
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.CompanyId, x.ParentId, x.Name }).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");
            b.HasIndex(x => x.ParentId); b.HasIndex(x => x.CreatedByUserId); b.HasIndex(x => x.DeletedByUserId);
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<DeviceGroupRecord>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.DeletedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceRecord>(b =>
        {
            b.Property(x => x.GroupId);
            b.HasIndex(x => x.GroupId);
            b.HasOne<DeviceGroupRecord>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("Devices", t => { t.HasTrigger("TR_Devices_GroupIntegrity"); t.UseSqlOutputClause(false); });
        });
        model.Entity<DeviceRecord>(b =>
        {
            b.ToTable("Devices", t =>
            {
                t.HasCheckConstraint("CK_Devices_Number", "LEN([Number]) BETWEEN 1 AND 40 AND REPLACE([Number], N'-', N'') COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^A-Z0-9_]%' AND DATALENGTH([Number]) = LEN([Number]) * 2 AND LEFT([Number],1) COLLATE Latin1_General_100_BIN2 LIKE '[A-Z0-9]'");
                t.HasCheckConstraint("CK_Devices_Profile", "([Profile] = N'zk-standalone' AND [Host] IS NOT NULL AND [Port] IS NOT NULL AND [Port] BETWEEN 1 AND 65535) OR ([Profile] IN (N'zk-push', N'offline-file') AND [Host] IS NULL AND [Port] IS NULL)");
                t.HasCheckConstraint("CK_Devices_Utc", "DATEPART(TZOFFSET,[CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET,[UpdatedAtUtc]) = 0 AND [UpdatedAtUtc] >= [CreatedAtUtc]");
                t.HasTrigger("TR_Devices_Identity"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Number).HasMaxLength(40).IsRequired();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Model).HasMaxLength(120).IsRequired();
            b.Property(x => x.Profile).HasMaxLength(32).IsRequired();
            b.Property(x => x.Host).HasMaxLength(253);
            b.Property(x => x.CreationFingerprint).HasMaxLength(64).IsRequired();
            b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Number).IsUnique(); b.HasIndex(x => x.SourceId).IsUnique();
            b.HasIndex(x => x.SiteId); b.HasIndex(x => x.CreatedByUserId);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DeviceSecretRecord>(b =>
        {
            b.ToTable("DeviceSecrets"); b.HasKey(x => x.DeviceId); b.Property(x => x.DeviceId).ValueGeneratedNever();
            b.Property(x => x.ProtectedValue).HasMaxLength(2048).IsRequired();
            b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionDelegationGrantRecord>(b =>
        {
            b.ToTable("CorrectionDelegationGrants", t =>
            {
                t.HasCheckConstraint("CK_CorrectionDelegationGrants_Utc", "DATEPART(TZOFFSET, [GrantedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionDelegationGrants_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DelegationId); b.Property(x => x.DelegationId).ValueGeneratedNever();
            b.HasOne<DelegationRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegatedCorrectionDecisionRecord>(b =>
        {
            b.ToTable("DelegatedCorrectionDecisions", t =>
            {
                t.HasTrigger("TR_DelegatedCorrectionDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DecisionId); b.Property(x => x.DecisionId).ValueGeneratedNever();
            b.HasOne<CorrectionDecisionRecord>().WithMany().HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CorrectionDelegationGrantRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionSettingsRecord>(b =>
        {
            b.ToTable("CorrectionSettings", t =>
            {
                t.HasCheckConstraint("CK_CorrectionSettings_Revision", "[Revision] > 0");
                t.HasCheckConstraint("CK_CorrectionSettings_Age", "[MaxRequestAgeDays] BETWEEN 0 AND 3650");
                t.HasCheckConstraint("CK_CorrectionSettings_Limits", "[DailyRequestLimit] BETWEEN 0 AND 1000 AND [MonthlyRequestLimit] BETWEEN 0 AND 1000");
                t.HasCheckConstraint("CK_CorrectionSettings_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionSettings_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasAlternateKey(x => new {x.Id,x.CompanyId});
            b.HasIndex(x => new {x.CompanyId,x.Revision}).IsUnique();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegationRecord>(b =>
        {
            b.ToTable("Delegations", t =>
            {
                t.HasCheckConstraint("CK_Delegations_Interval", "[ValidFrom] < [ValidUntilExclusive]");
                t.HasCheckConstraint("CK_Delegations_DifferentPeople", "[PrincipalUserId] <> [DelegateUserId] AND [PrincipalEmployeeId] <> [DelegateEmployeeId]");
                t.HasCheckConstraint("CK_Delegations_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_Delegations_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.PrincipalUserId, x.ValidFrom, x.ValidUntilExclusive });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.PrincipalEmployeeId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.DelegateEmployeeId, x.CompanyId }).HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DelegationRevocationRecord>(b =>
        {
            b.ToTable("DelegationRevocations", t =>
            {
                t.HasCheckConstraint("CK_DelegationRevocations_Utc", "DATEPART(TZOFFSET, [RevokedAtUtc]) = 0");
                t.HasTrigger("TR_DelegationRevocations_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.DelegationId); b.Property(x => x.DelegationId).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasOne<DelegationRecord>().WithMany().HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveTypeRecord>(b =>
        {
            b.ToTable("LeaveTypes", t =>
            {
                t.HasCheckConstraint("CK_LeaveTypes_MinutesPerDay", "[MinutesPerDay] BETWEEN 1 AND 1440");
                t.HasCheckConstraint("CK_LeaveTypes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_LeaveTypes_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Symbol).HasMaxLength(40).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasIndex(x => new { x.CompanyId, x.Symbol }).IsUnique();
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveLedgerEntryRecord>(b =>
        {
            b.ToTable("LeaveLedgerEntries", t =>
            {
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Year", "[PersianYear] BETWEEN 1300 AND 1500");
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Minutes", "[Minutes] BETWEEN -527040 AND 527040 AND (([Kind] = 0 AND [Minutes] >= 0) OR ([Kind] = 1 AND [Minutes] > 0) OR ([Kind] = 2 AND [Minutes] < 0) OR ([Kind] = 3 AND [Minutes] <> 0))");
                t.HasCheckConstraint("CK_LeaveLedgerEntries_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_LeaveLedgerEntries_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.DocumentNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.Description).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.PersianYear, x.EffectiveDate });
            b.HasIndex(x => new { x.EmployeeId, x.LeaveTypeId, x.PersianYear }).IsUnique().HasFilter("[Kind] = 0");
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveTypeRecord>().WithMany().HasForeignKey(x => new { x.LeaveTypeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveRequestRecord>(b =>
        {
            b.ToTable("LeaveRequests", t =>
            {
                t.HasCheckConstraint("CK_LeaveRequests_Kind", "[Kind] IN (0,1)");
                t.HasCheckConstraint("CK_LeaveRequests_Category", "[Category] IN (0,1,2)");
                t.HasCheckConstraint("CK_LeaveRequests_OvertimeHourly", "[Category] <> 1 OR [Kind] = 1");
                t.HasCheckConstraint("CK_LeaveRequests_Minutes", "[Minutes] BETWEEN 1 AND 1440");
                t.HasCheckConstraint("CK_LeaveRequests_State", "[State] IN (0,1,2,3)");
                t.HasCheckConstraint("CK_LeaveRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [FromAtUtc]) = 0 AND DATEPART(TZOFFSET, [UntilAtUtcExclusive]) = 0");
                t.HasTrigger("TR_LeaveRequests_ImmutablePayload"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.OperationId).IsUnique();
            b.HasIndex(x => x.RequesterUserId);
            b.HasIndex(x => new { x.EmployeeId, x.CompanyId });
            b.HasIndex(x => new { x.LeaveTypeId, x.CompanyId });
            b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.State });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveTypeRecord>().WithMany().HasForeignKey(x => new { x.LeaveTypeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveDecisionRecord>(b =>
        {
            b.ToTable("LeaveDecisions", t =>
            {
                t.HasCheckConstraint("CK_LeaveDecisions_Sequence", "[Sequence] > 0");
                t.HasCheckConstraint("CK_LeaveDecisions_Decision", "[Decision] IN (1,2,3)");
                t.HasCheckConstraint("CK_LeaveDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_LeaveDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500);
            b.HasIndex(x => x.ActorUserId);
            b.Property(x => x.Sequence).HasDefaultValue(1);
            b.HasIndex(x => new { x.RequestId, x.Sequence }).IsUnique();
            b.HasIndex(x => x.LedgerEntryId).IsUnique().HasFilter("[LedgerEntryId] IS NOT NULL");
            b.HasOne<LeaveLedgerEntryRecord>().WithMany().HasForeignKey(x => x.LedgerEntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveRequestRecord>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionDecisionReversalRecord>(b =>
        {
            b.ToTable("CorrectionDecisionReversals", t =>
            {
                t.HasCheckConstraint("CK_CorrectionDecisionReversals_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_Revision_CorrectionDecisionReversals");
                t.HasTrigger("TR_CorrectionDecisionReversals_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.DecisionId).IsUnique();
            b.HasOne<CorrectionDecisionRecord>().WithMany().HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LeaveDecisionReversalRecord>(b =>
        {
            b.ToTable("LeaveDecisionReversals", t =>
            {
                t.HasCheckConstraint("CK_LeaveDecisionReversals_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_LeaveDecisionReversals_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.DecisionId).IsUnique();
            b.HasOne<LeaveDecisionRecord>().WithMany().HasForeignKey(x => x.DecisionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.CompensationLedgerEntryId).IsUnique().HasFilter("[CompensationLedgerEntryId] IS NOT NULL");
            b.HasOne<LeaveLedgerEntryRecord>().WithMany().HasForeignKey(x => x.OriginalLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<LeaveLedgerEntryRecord>().WithMany().HasForeignKey(x => x.CompensationLedgerEntryId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<NotificationRecord>(b =>
        {
            b.ToTable("Notifications", t =>
            {
                t.HasCheckConstraint("CK_Notifications_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ReadAtUtc]) = 0");
                t.HasTrigger("TR_Notifications_ImmutablePayload"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Category).HasMaxLength(40).IsRequired(); b.Property(x => x.Text).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.UserId, x.ReadAtUtc });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrganizationRecord>(b =>
        {
            b.ToTable("Organizations"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
        });
        model.Entity<CompanyRecord>(b =>
        {
            b.ToTable("Companies"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<OrganizationRecord>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SiteRecord>(b =>
        {
            b.ToTable("Sites"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrgUnitRecord>(b =>
        {
            b.ToTable("OrgUnits", t => t.HasCheckConstraint("CK_OrgUnits_NotOwnParent", "[ParentId] IS NULL OR [ParentId] <> [Id]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => new { x.ParentId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeRecord>(b =>
        {
            b.ToTable("Employees"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.PersonnelNumber).HasMaxLength(80).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.CompanyId, x.PersonnelNumber }).IsUnique();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeAssignmentRecord>(b =>
        {
            b.ToTable("EmployeeAssignments", t => t.HasCheckConstraint("CK_EmployeeAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => new { x.OrgUnitId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmploymentProfileRecord>(b =>
        {
            b.ToTable("EmploymentProfiles", t => t.HasCheckConstraint("CK_EmploymentProfiles_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.StatusCode).HasMaxLength(80).IsRequired(); b.Property(x => x.Title).HasMaxLength(200);
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EmployeeSiteAssignmentRecord>(b =>
        {
            b.ToTable("EmployeeSiteAssignments", t => t.HasCheckConstraint("CK_EmployeeSiteAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => new { x.SiteId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserRecord>(b =>
        {
            b.ToTable("Users"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.UserName).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.IsActive).HasDefaultValue(false);
            b.HasIndex(x => x.UserName).IsUnique();
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RoleRecord>(b =>
        {
            b.ToTable("Roles"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Name).IsUnique();
        });
        model.Entity<PermissionRecord>(b =>
        {
            b.ToTable("Permissions"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Code).HasMaxLength(150).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<UserRoleRecord>(b =>
        {
            b.ToTable("UserRoles"); b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<RoleRecord>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RolePermissionRecord>(b =>
        {
            b.ToTable("RolePermissions"); b.HasKey(x => new { x.RoleId, x.PermissionId });
            b.HasOne<RoleRecord>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<PermissionRecord>().WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<UserOrgScopeRecord>(b =>
        {
            b.ToTable("UserOrgScopes", t => t.HasCheckConstraint("CK_UserOrgScopes_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.UserId, x.OrgUnitId, x.ValidFrom });
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<OrgUnitRecord>().WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CalendarRecord>(b =>
        {
            b.ToTable("Calendars"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CalendarDayRecord>(b =>
        {
            b.ToTable("CalendarDays", t =>
            {
                t.HasCheckConstraint("CK_CalendarDays_Kind", "[Kind] IN (0, 1, 2)");
                t.HasCheckConstraint("CK_CalendarDays_RecordedUtc", "DATEPART(TZOFFSET, [RecordedAtUtc]) = 0");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
            b.HasOne<CalendarRecord>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ShiftDefinitionRecord>(b =>
        {
            b.ToTable("ShiftDefinitions", t => t.HasCheckConstraint("CK_ShiftDefinitions_Interval",
                "([EndDayOffset] = 0 AND [EndsAt] > [StartsAt]) OR ([EndDayOffset] = 1 AND [EndsAt] <= [StartsAt])"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => new { x.SiteId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<WorkScheduleAssignmentRecord>(b =>
        {
            b.ToTable("WorkScheduleAssignments", t => t.HasCheckConstraint("CK_WorkScheduleAssignments_Interval",
                "[ValidUntilExclusive] IS NULL OR [ValidUntilExclusive] > [ValidFrom]"));
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.HasIndex(x => new { x.EmployeeId, x.ValidFrom });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ShiftDefinitionRecord>().WithMany().HasForeignKey(x => new { x.ShiftId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CalendarRecord>().WithMany().HasForeignKey(x => new { x.CalendarId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EventSourceRecord>(b =>
        {
            b.ToTable("EventSources"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Version).IsRowVersion();
            b.HasOne<SiteRecord>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PeriodDiagnosticRecord>(b =>
        {
            b.ToTable("PeriodDiagnostics", t =>
            {
                t.HasCheckConstraint("CK_PeriodDiagnostics_Revision", "[InputRevision] IS NULL OR [InputRevision] >= 0");
                t.HasCheckConstraint("CK_PeriodDiagnostics_Json", "ISJSON([EvidenceJson]) = 1");
                t.HasCheckConstraint("CK_PeriodDiagnostics_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_PeriodDiagnostics_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.EvidenceJson).IsRequired();
            b.HasIndex(x => new { x.DraftId, x.CreatedAtUtc });
            b.HasOne<PeriodDraftRecord>().WithMany().HasForeignKey(x => x.DraftId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PeriodDraftRecord>(b =>
        {
            b.ToTable("PeriodDrafts", t =>
            {
                t.HasCheckConstraint("CK_PeriodDrafts_Interval", "[EndExclusive] > [Start] AND DATEDIFF(day, [Start], [EndExclusive]) <= 62");
                t.HasCheckConstraint("CK_PeriodDrafts_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_PeriodDrafts_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.HasIndex(x => new { x.EmployeeId, x.Start, x.EndExclusive }).IsUnique();
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionPolicyRecord>(b =>
        {
            b.ToTable("CorrectionPolicies", t =>
            {
                t.HasCheckConstraint("CK_CorrectionPolicies_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionPolicies_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionRequestRecord>(b =>
        {
            b.ToTable("CorrectionRequests", t =>
            {
                t.HasCheckConstraint("CK_CorrectionRequests_State", "[State] IN (0,1,2,3)");
                t.HasCheckConstraint("CK_CorrectionRequests_Kind", "[PunchKind] IN (0,1)");
                t.HasCheckConstraint("CK_CorrectionRequests_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ProposedAtUtc]) = 0");
                t.HasTrigger("TR_CorrectionRequests_ImmutablePayload"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired(); b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.State });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CorrectionPolicyRecord>().WithMany().HasForeignKey(x => new { x.PolicyId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CorrectionSettingsRecord>().WithMany().HasForeignKey(x => new { x.CorrectionSettingsId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CorrectionDecisionRecord>(b =>
        {
            b.ToTable("CorrectionDecisions", t =>
            {
                t.HasCheckConstraint("CK_CorrectionDecisions_Sequence", "[Sequence] > 0");
                t.HasCheckConstraint("CK_CorrectionDecisions_Decision", "[Decision] IN (1,2,3)");
                t.HasCheckConstraint("CK_CorrectionDecisions_Utc", "DATEPART(TZOFFSET, [AtUtc]) = 0");
                t.HasTrigger("TR_CorrectionDecisions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500);
            b.Property(x => x.Sequence).HasDefaultValue(1);
            b.HasIndex(x => new { x.RequestId, x.Sequence }).IsUnique();
            b.HasOne<CorrectionRequestRecord>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CalculationInputRevisionRecord>(b =>
        {
            b.ToTable("CalculationInputRevision", t =>
            {
                t.HasCheckConstraint("CK_CalculationInputRevision_Singleton", "[Id] = 1 AND [Revision] >= 0");
                t.HasTrigger("TR_CalculationInputRevision_Monotonic"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        });
        model.Entity<ObservedRuleVersionRecord>(b =>
        {
            b.ToTable("ObservedRuleVersions", t =>
            {
                t.HasCheckConstraint("CK_ObservedRules_Margins", "[BeforeMinutes] BETWEEN 0 AND 720 AND [AfterMinutes] BETWEEN 0 AND 720");
                t.HasCheckConstraint("CK_ObservedRules_Algorithm", "[Algorithm] = N'paired-v1'");
                t.HasCheckConstraint("CK_ObservedRules_CreatedUtc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_ObservedRuleVersions_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Algorithm).HasMaxLength(32).IsRequired();
            b.HasAlternateKey(x => new { x.Id, x.CompanyId });
            b.HasOne<CompanyRecord>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DailyCalculationRunRecord>(b =>
        {
            b.ToTable("DailyCalculationRuns", t =>
            {
                t.HasCheckConstraint("CK_CalculationRuns_Window", "[WindowEndUtc] > [WindowStartUtc]");
                t.HasCheckConstraint("CK_CalculationRuns_Utc", "DATEPART(TZOFFSET, [WindowStartUtc]) = 0 AND DATEPART(TZOFFSET, [WindowEndUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasCheckConstraint("CK_CalculationRuns_Ticks", "[ObservedTicks] IS NULL OR [ObservedTicks] >= 0");
                t.HasCheckConstraint("CK_CalculationRuns_Revision", "[InputRevision] IS NULL OR [InputRevision] >= 0");
                t.HasCheckConstraint("CK_CalculationRuns_MonitorState", "[InputMonitorState] IS NULL OR [InputMonitorState] IN (N'present', N'unverified')");
                t.HasTrigger("TR_DailyCalculationRuns_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.InputSnapshotJson).IsRequired(); b.Property(x => x.AnomaliesJson).IsRequired();
            b.Property(x => x.InputMonitorState).HasMaxLength(16);
            b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.CreatedAtUtc });
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ObservedRuleVersionRecord>().WithMany().HasForeignKey(x => new { x.RuleVersionId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<WorkScheduleAssignmentRecord>().WithMany().HasForeignKey(x => x.ScheduleAssignmentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SourceMappingChangeRecord>(b =>
        {
            b.ToTable("SourceMappingChanges", t =>
            {
                t.HasCheckConstraint("CK_SourceMappingChanges_Utc", "DATEPART(TZOFFSET, [EffectiveAtUtc]) = 0 AND DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasCheckConstraint("CK_SourceMappingChanges_Distinct", "[PreviousMappingId] <> [NewMappingId]");
                t.HasTrigger("TR_SourceMappingChanges_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.HasIndex(x => x.PreviousMappingId).IsUnique(); b.HasIndex(x => x.NewMappingId).IsUnique();
            b.HasOne<SourceEmployeeMappingRecord>().WithMany().HasForeignKey(x => x.PreviousMappingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<SourceEmployeeMappingRecord>().WithMany().HasForeignKey(x => x.NewMappingId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawConflictNoteRecord>(b =>
        {
            b.ToTable("RawConflictNotes", t =>
            {
                t.HasCheckConstraint("CK_RawConflictNotes_Utc", "DATEPART(TZOFFSET, [CreatedAtUtc]) = 0");
                t.HasTrigger("TR_RawConflictNotes_Immutable"); t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.Note).HasMaxLength(500).IsRequired();
            b.HasIndex(x => new { x.ConflictId, x.CreatedAtUtc });
            b.HasOne<RawEventConflictRecord>().WithMany().HasForeignKey(x => x.ConflictId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<UserRecord>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SourceEmployeeMappingRecord>(b =>
        {
            b.ToTable("SourceEmployeeMappings", t =>
            {
                t.HasCheckConstraint("CK_SourceEmployeeMappings_Interval", "[ValidUntilUtcExclusive] IS NULL OR [ValidUntilUtcExclusive] > [ValidFromUtc]");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_StartUtc", "DATEPART(TZOFFSET, [ValidFromUtc]) = 0");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_EndUtc", "[ValidUntilUtcExclusive] IS NULL OR DATEPART(TZOFFSET, [ValidUntilUtcExclusive]) = 0");
                t.HasCheckConstraint("CK_SourceEmployeeMappings_Identifier", "DATALENGTH([SourcePersonId]) > 0");
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.Property(x => x.Version).IsRowVersion();
            b.Property(x => x.SourcePersonId).HasMaxLength(100).IsRequired();
            b.Property<byte[]>("SourcePersonIdBytes").HasColumnType("varbinary(200)")
                .HasComputedColumnSql("CONVERT(varbinary(200), [SourcePersonId])", stored: true);
            b.Property<int>("SourcePersonIdByteLength").HasComputedColumnSql("DATALENGTH([SourcePersonId])", stored: true);
            b.HasIndex(nameof(SourceEmployeeMappingRecord.SourceId), "SourcePersonIdBytes", "SourcePersonIdByteLength", nameof(SourceEmployeeMappingRecord.ValidFromUtc));
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x => new { x.EmployeeId, x.CompanyId })
                .HasPrincipalKey(x => new { x.Id, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawEventConflictRecord>(b =>
        {
            b.ToTable("RawEventConflicts", t =>
            {
                t.HasCheckConstraint("CK_RawEventConflicts_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEventConflicts_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                t.HasTrigger("TR_RawEventConflicts_Immutable");
                t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExternalEventId).HasMaxLength(180).IsRequired();
            b.Property(x => x.SourceEmployeeIdentifier).HasMaxLength(100); b.Property(x => x.SourceTimeZoneId).HasMaxLength(100);
            b.Property(x => x.ReportedEventType).HasMaxLength(80); b.Property(x => x.SourcePayload).IsRequired();
            b.HasIndex(x => new { x.SourceId, x.ReceivedAtUtc });
            b.HasOne<RawAttendanceEventRecord>().WithMany().HasForeignKey(x => x.OriginalEventId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<RawAttendanceEventRecord>(b =>
        {
            b.ToTable("RawAttendanceEvents", t =>
            {
                t.HasCheckConstraint("CK_RawEvents_ReceivedUtc", "DATEPART(TZOFFSET, [ReceivedAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEvents_OccurredUtc", "[OccurredAtUtc] IS NULL OR DATEPART(TZOFFSET, [OccurredAtUtc]) = 0");
                t.HasCheckConstraint("CK_RawEvents_ExternalIdNotEmpty", "[ExternalEventId] IS NULL OR DATALENGTH([ExternalEventId]) > 0");
                t.HasTrigger("TR_RawAttendanceEvents_Immutable");
                t.UseSqlOutputClause(false);
            });
            b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
            b.Property(x => x.ExternalEventId).HasMaxLength(180);
            b.Property<byte[]?>("ExternalEventIdBytes").HasColumnType("varbinary(360)")
                .HasComputedColumnSql("CONVERT(varbinary(360), [ExternalEventId])", stored: true);
            // SQL Server can pad binary comparisons with zeroes. Include byte length
            // so distinct trailing NUL code units are not collapsed as duplicates.
            b.Property<int?>("ExternalEventIdByteLength")
                .HasComputedColumnSql("DATALENGTH([ExternalEventId])", stored: true);
            b.HasIndex(nameof(RawAttendanceEventRecord.SourceId), "ExternalEventIdBytes", "ExternalEventIdByteLength").IsUnique()
                .HasFilter("[ExternalEventId] IS NOT NULL");
            b.Property(x => x.SourceEmployeeIdentifier).HasMaxLength(100);
            b.Property(x => x.SourceTimeZoneId).HasMaxLength(100);
            b.Property(x => x.ReportedEventType).HasMaxLength(80);
            b.Property(x => x.SourcePayload).IsRequired();
            b.HasIndex(x => new { x.SourceId, x.OccurredAtUtc });
            b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });
        // Monitored input tables have SQL revision triggers; EF must not use
        // bare OUTPUT clauses when writing to these tables.
        model.Entity<CompanyRecord>().ToTable("Companies", t => { t.HasTrigger("TR_Revision_Companies"); t.UseSqlOutputClause(false); });
        model.Entity<SiteRecord>().ToTable("Sites", t => { t.HasTrigger("TR_Revision_Sites"); t.UseSqlOutputClause(false); });
        model.Entity<EmployeeRecord>().ToTable("Employees", t => { t.HasTrigger("TR_Revision_Employees"); t.UseSqlOutputClause(false); });
        model.Entity<CalendarRecord>().ToTable("Calendars", t => { t.HasTrigger("TR_Revision_Calendars"); t.UseSqlOutputClause(false); });
        model.Entity<CalendarDayRecord>().ToTable("CalendarDays", t => { t.HasTrigger("TR_Revision_CalendarDays"); t.UseSqlOutputClause(false); });
        model.Entity<ShiftDefinitionRecord>().ToTable("ShiftDefinitions", t => { t.HasTrigger("TR_Revision_ShiftDefinitions"); t.UseSqlOutputClause(false); });
        model.Entity<WorkScheduleAssignmentRecord>().ToTable("WorkScheduleAssignments", t => { t.HasTrigger("TR_Revision_WorkScheduleAssignments"); t.UseSqlOutputClause(false); });
        model.Entity<EventSourceRecord>().ToTable("EventSources", t => { t.HasTrigger("TR_Revision_EventSources"); t.UseSqlOutputClause(false); });
        model.Entity<RawAttendanceEventRecord>().ToTable("RawAttendanceEvents", t => { t.HasTrigger("TR_Revision_RawAttendanceEvents"); t.UseSqlOutputClause(false); });
        model.Entity<RawEventConflictRecord>().ToTable("RawEventConflicts", t => { t.HasTrigger("TR_Revision_RawEventConflicts"); t.UseSqlOutputClause(false); });
        model.Entity<SourceEmployeeMappingRecord>().ToTable("SourceEmployeeMappings", t => { t.HasTrigger("TR_Revision_SourceEmployeeMappings"); t.UseSqlOutputClause(false); });
        model.Entity<ObservedRuleVersionRecord>().ToTable("ObservedRuleVersions", t => { t.HasTrigger("TR_Revision_ObservedRuleVersions"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionPolicyRecord>().ToTable("CorrectionPolicies", t => { t.HasTrigger("TR_Revision_CorrectionPolicies"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionRequestRecord>().ToTable("CorrectionRequests", t => { t.HasTrigger("TR_Revision_CorrectionRequests"); t.UseSqlOutputClause(false); });
        model.Entity<CorrectionDecisionRecord>().ToTable("CorrectionDecisions", t => { t.HasTrigger("TR_Revision_CorrectionDecisions"); t.UseSqlOutputClause(false); });
    }
}
