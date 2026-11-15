using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class AttendanceEraseModel
{
 internal static void Configure(ModelBuilder model)=>model.Entity<DeviceAttendanceEraseRecord>(b=>
 {
  b.ToTable("DeviceAttendanceErases",t=>{t.HasTrigger("TR_DeviceAttendanceErases_History");t.UseSqlOutputClause(false);});
  b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
  b.Property(x=>x.Id).HasColumnType("uniqueidentifier");b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");b.Property(x=>x.ArchiveId).HasColumnType("uniqueidentifier");b.Property(x=>x.RecoveryOfId).HasColumnType("uniqueidentifier");b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ApprovedByUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
  b.Property(x=>x.ArchiveVersion).HasColumnType("varbinary(8)").HasMaxLength(8).IsRequired();
  b.Property(x=>x.DeviceVersion).HasColumnType("varbinary(8)").HasMaxLength(8).IsRequired();
  b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.QuiescedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.ApprovedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.ClaimedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.CancelRequestedAtUtc).HasColumnType("datetimeoffset");b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
  b.Property(x=>x.ArchiveCount).HasColumnType("int");b.Property(x=>x.EvidenceReference).HasColumnType("nvarchar(200)").HasMaxLength(200).IsRequired();b.Property(x=>x.ReviewReference).HasColumnType("nvarchar(200)").HasMaxLength(200);b.Property(x=>x.ApprovalJson).HasColumnType("nvarchar(max)").IsRequired();b.Property(x=>x.ArchiveDigest).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();b.Property(x=>x.RequestHash).HasColumnType("nvarchar(64)").HasMaxLength(64).IsRequired();b.Property(x=>x.State).HasColumnType("nvarchar(40)").HasMaxLength(40).IsRequired();b.Property(x=>x.Version).HasColumnType("rowversion").IsRowVersion().IsRequired();
  b.HasIndex(x=>x.DeviceId).IsUnique().HasDatabaseName("UX_DeviceAttendanceErases_Device_Active").HasFilter("[State] IN(N'proposed',N'pending',N'running')");b.HasIndex(x=>new{x.DeviceId,x.State});b.HasIndex(x=>x.ArchiveId);b.HasIndex(x=>x.RequestedByUserId);b.HasIndex(x=>x.ApprovedByUserId);b.HasIndex(x=>x.ServiceUserId);b.HasIndex(x=>x.RecoveryOfId).IsUnique().HasFilter("[State]=N'released'");
  b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);b.HasOne<DeviceAttendanceDownloadRecord>().WithMany().HasForeignKey(x=>x.ArchiveId).OnDelete(DeleteBehavior.Restrict);b.HasOne<DeviceAttendanceEraseRecord>().WithMany().HasForeignKey(x=>x.RecoveryOfId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
 });
}
