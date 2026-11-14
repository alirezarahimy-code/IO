using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Storage;
internal static class RemoteEnrollmentModel
{
    internal static void Configure(ModelBuilder model)=>model.Entity<RemoteEnrollmentRecord>(b=>
    {
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
