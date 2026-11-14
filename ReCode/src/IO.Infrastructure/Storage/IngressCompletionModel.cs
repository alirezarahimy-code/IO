using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class IngressCompletionModel
{
 internal static void Configure(ModelBuilder model)
 {
  model.Entity<DevicePushCommitRecord>(b=>
  {
   b.ToTable("DevicePushCommits",t=>{t.HasTrigger("TR_DevicePushCommits_Immutable");t.UseSqlOutputClause(false);});
   b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
   b.Property(x=>x.Id).HasColumnType("uniqueidentifier");b.Property(x=>x.JournalId).HasColumnType("uniqueidentifier");
   b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.LineNumber).HasColumnType("int");
   b.Property(x=>x.MappingId).HasColumnType("uniqueidentifier");b.Property(x=>x.EmployeeId).HasColumnType("uniqueidentifier");
   b.Property(x=>x.RawEventId).HasColumnType("uniqueidentifier");b.Property(x=>x.ReviewedByUserId).HasColumnType("uniqueidentifier");
   b.Property(x=>x.ReviewedAtUtc).HasColumnType("datetimeoffset");
   b.Property(x=>x.EventType).HasColumnType("nvarchar(3)").HasMaxLength(3).IsRequired();
   b.Property(x=>x.TrustEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
   b.Property(x=>x.DuplicateReviewEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
   b.Property(x=>x.RequestHash).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();
   b.Property(x=>x.PreviewHash).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();
   b.HasIndex(x=>new{x.JournalId,x.LineNumber}).IsUnique();b.HasIndex(x=>x.DeviceId);b.HasIndex(x=>x.MappingId);b.HasIndex(x=>x.EmployeeId);b.HasIndex(x=>x.RawEventId).IsUnique();b.HasIndex(x=>x.ReviewedByUserId);
   b.HasOne<DevicePushJournalRecord>().WithMany().HasForeignKey(x=>x.JournalId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<SourceEmployeeMappingRecord>().WithMany().HasForeignKey(x=>x.MappingId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x=>x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<RawAttendanceEventRecord>().WithMany().HasForeignKey(x=>x.RawEventId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
  });
  model.Entity<RemoteEnrollmentRecoveryRecord>(b=>
  {
   b.ToTable("RemoteEnrollmentRecoveries",t=>{t.HasTrigger("TR_RemoteEnrollmentRecoveries_History");t.UseSqlOutputClause(false);});
   b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
   b.Property(x=>x.Id).HasColumnType("uniqueidentifier");b.Property(x=>x.EnrollmentId).HasColumnType("uniqueidentifier");
   b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.ProposedByUserId).HasColumnType("uniqueidentifier");
   b.Property(x=>x.ProposedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.PhysicalResetAtUtc).HasColumnType("datetimeoffset");
   b.Property(x=>x.DeviceVersion).HasColumnType("varbinary(8)").HasMaxLength(8).IsRequired();
   b.Property(x=>x.ApprovalJson).HasColumnType("nvarchar(max)").IsRequired();
   b.Property(x=>x.ProcessStoppedEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
   b.Property(x=>x.PhysicalResetEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
   b.Property(x=>x.SlotObservationEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();
   b.Property(x=>x.ObservedSlot).HasColumnType("nvarchar(7)").HasMaxLength(7).IsRequired();
   b.Property(x=>x.ApprovedByUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ApprovedAtUtc).HasColumnType("datetimeoffset");
   b.Property(x=>x.ReviewEvidence).HasColumnType("nvarchar(200)").HasMaxLength(200);
   b.Property(x=>x.Version).HasColumnType("rowversion").IsRowVersion().IsRequired();
   b.HasIndex(x=>x.EnrollmentId).IsUnique().HasFilter("[ApprovedAtUtc] IS NOT NULL");b.HasIndex(x=>x.DeviceId);b.HasIndex(x=>x.ProposedByUserId);b.HasIndex(x=>x.ApprovedByUserId);
   b.HasOne<RemoteEnrollmentRecord>().WithMany().HasForeignKey(x=>x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ProposedByUserId).OnDelete(DeleteBehavior.Restrict);
   b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
  });
 }
}
