using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class RemoteEnrollmentModel
{
    internal static void Configure(ModelBuilder model)=>model.Entity<RemoteEnrollmentRecord>(b=>
    {
            b.Property(x=>x.Id).HasColumnType("uniqueidentifier");
            b.Property(x=>x.DeviceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.EmployeeId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.SourceId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedByUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.RequestedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ExpectedDeviceVersion).HasColumnType("varbinary(8)");
            b.Property(x=>x.PersonIdentifier).HasColumnType("nvarchar(100)");
            b.Property(x=>x.ApprovalJson).HasColumnType("nvarchar(8192)");
            b.Property(x=>x.TimeoutSeconds).HasColumnType("int");
            b.Property(x=>x.State).HasColumnType("nvarchar(40)");
            b.Property(x=>x.ServiceUserId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimId).HasColumnType("uniqueidentifier");
            b.Property(x=>x.ClaimedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.CancelRequestedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.FinishedAtUtc).HasColumnType("datetimeoffset");
            b.Property(x=>x.ResultJson).HasColumnType("nvarchar(4096)");
            b.Property(x=>x.Version).HasColumnType("rowversion");
        b.ToTable("RemoteEnrollments",t=>{t.HasTrigger("TR_RemoteEnrollments_History");t.UseSqlOutputClause(false);});
        b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();b.Property(x=>x.Version).IsRowVersion();
        b.Property(x=>x.ExpectedDeviceVersion).HasMaxLength(8).IsRequired();b.Property(x=>x.PersonIdentifier).HasMaxLength(100).IsRequired();
        b.Property(x=>x.ApprovalJson).HasMaxLength(8192).IsRequired();b.Property(x=>x.ResultJson).HasMaxLength(4096);b.Property(x=>x.State).HasMaxLength(40).IsRequired();
        b.HasIndex(x=>new{x.DeviceId,x.State});
        b.HasOne<DeviceRecord>().WithMany().HasForeignKey(x=>x.DeviceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EmployeeRecord>().WithMany().HasForeignKey(x=>x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventSourceRecord>().WithMany().HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserRecord>().WithMany().HasForeignKey(x=>x.ServiceUserId).OnDelete(DeleteBehavior.Restrict);
    });
}
