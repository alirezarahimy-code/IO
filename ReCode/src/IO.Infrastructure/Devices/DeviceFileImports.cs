using System.Security.Cryptography;
using System.Text;
using IO.DeviceContracts;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

public sealed class DeviceFileImports(IoDbContext db,PermissionTransaction transaction,AttendanceIngestion ingestion,TimeProvider clock)
{
    public const string Read="devices.files.read",Preview="devices.files.preview",Commit="devices.files.commit";
    public sealed record Input(Guid OperationId,Guid DeviceId,byte[] ExpectedDeviceVersion,string Calendar,int MachineNumber,int InCode,int OutCode,string FileBase64)
    {
        public override string ToString()=>"DeviceFileImportInput [original file and identifiers redacted]";
    }
    public Task<Guid> CreateAsync(Guid actor,Input input,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Preview,"device-file.preview-created",async token=>
    {
        if(input is null||input.OperationId==Guid.Empty||input.DeviceId==Guid.Empty||input.ExpectedDeviceVersion is not{Length:8}||input.Calendar is not("gregorian" or "persian")||input.MachineNumber<=0||input.InCode is <0 or >255||input.OutCode is <0 or >255||input.InCode==input.OutCode||input.FileBase64 is null||input.FileBase64.Length is 0 or >1398104)throw new ArgumentException("Explicit file layout interpretation and device version required.");
        byte[] bytes;try{bytes=Convert.FromBase64String(input.FileBase64);}catch(FormatException){throw new ArgumentException("Invalid base64 file.");}
        if(bytes.Length is 0 or >1048576)throw new ArgumentException("Preview file budget exceeded.");
        var hash=Convert.ToHexString(SHA256.HashData(bytes));await transaction.LockAsync("IO.DeviceRegistry",token);await transaction.LockAsync("IO.DeviceFileImports",token);
        var old=await db.DeviceFileImports.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.OperationId,token);
        if(old is not null){if(old.CreatedByUserId!=actor||old.DeviceId!=input.DeviceId||!old.ExpectedDeviceVersion.SequenceEqual(input.ExpectedDeviceVersion)||!old.OriginalFile.SequenceEqual(bytes)||old.Calendar!=input.Calendar||old.MachineNumber!=input.MachineNumber||old.InCode!=input.InCode||old.OutCode!=input.OutCode)throw new DomainConflictException("Changed preview retry.");return(old.Id,old.Id);}
        var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.DeviceId,token)??throw new ArgumentException("Unknown device.");if(!d.Enabled||!d.Version.SequenceEqual(input.ExpectedDeviceVersion))throw new DomainConflictException("Device changed.");
        if(await db.DeviceFileImports.AnyAsync(x=>x.DeviceId==d.Id&&x.FileHash==hash,token))throw new DomainConflictException("This exact file already has a retained preview for this device; use its history.");
        var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==d.SiteId,token);var parsed=Parse(bytes,input.Calendar,site.TimeZoneId,input.MachineNumber);
        if(parsed.Lines.Count==0||parsed.Lines.All(x=>x.Status is "layout-mismatch" or "malformed-line"))throw new ArgumentException("No valid reference-layout rows; another vendor layout must not be guessed.");
        db.DeviceFileImports.Add(new(){Id=input.OperationId,DeviceId=d.Id,SourceId=d.SourceId,SiteId=d.SiteId,CompanyId=site.CompanyId,CreatedByUserId=actor,CreatedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=input.ExpectedDeviceVersion.ToArray(),OriginalFile=bytes,FileHash=hash,Calendar=input.Calendar,TimeZoneId=site.TimeZoneId,MachineNumber=input.MachineNumber,InCode=input.InCode,OutCode=input.OutCode,LineCount=parsed.Lines.Count});return(input.OperationId,input.OperationId);
    },ct);
    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-file.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var items=await db.DeviceFileImports.AsNoTracking().Where(x=>deviceId==null||x.DeviceId==deviceId).OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).Select(x=>new{x.Id,x.DeviceId,x.SourceId,x.SiteId,x.CompanyId,x.CreatedAtUtc,x.FileHash,x.Calendar,x.TimeZoneId,x.MachineNumber,x.InCode,x.OutCode,x.LineCount,x.LastReviewedAtUtc,x.Version,reviewed=db.DeviceFileImportLines.Count(r=>r.ImportId==x.Id),imported=db.DeviceFileImportLines.Count(r=>r.ImportId==x.Id&&r.RawEventId!=null&&r.ConflictId==null)}).ToListAsync(token);return(new{offset,pageSize=50,items},actor);
    },ct);
    public Task<object> OriginalAsync(Guid actor,Guid id,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-file.original-viewed",async token=>
    {
        var row=await db.DeviceFileImports.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown import.");return(new{row.Id,row.FileHash,fileBase64=Convert.ToBase64String(row.OriginalFile)},id);
    },ct);
    public Task<object> RowsAsync(Guid actor,Guid id,int after,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-file.rows-viewed",async token=>
    {
        if(after<0)throw new ArgumentException("Invalid cursor.");var job=await Load(id,token);var lines=Parse(job.OriginalFile,job.Calendar,job.TimeZoneId,job.MachineNumber).Lines.Where(x=>x.LineNumber>after).Take(50).ToArray();
        var same=await SameTarget(job,token);var items=new List<object>();foreach(var line in lines){var receipt=await db.DeviceFileImportLines.AsNoTracking().SingleOrDefaultAsync(x=>x.ImportId==id&&x.LineNumber==line.LineNumber,token);var status=receipt?.RawEventId is not null?receipt.Status:await Status(job,line,same,token);items.Add(new{line.LineNumber,line.PersonIdentifier,line.OccurredAtUtc,line.MachineNumber,line.CheckType,line.VerifyMode,line.PayloadHash,status,rawEventId=receipt?.RawEventId,conflictId=receipt?.ConflictId});}
        return(new{importId=id,job.FileHash,job.SourceId,job.TimeZoneId,job.LineCount,job.Version,sameTarget=same,after,nextAfter=lines.LastOrDefault()?.LineNumber??after,items},id);
    },ct);
    public Task<object> CommitPageAsync(Guid actor,Guid id,byte[] version,int after,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Commit,"device-file.page-reviewed",async token=>
    {
        if(version is not{Length:8}||after<0||confirmation!="IMPORT-READY-ROWS")throw new ArgumentException("Explicit page confirmation and version required.");
        if(!await transaction.HasPermissionAsync(actor,"attendance.events.receive",token)&&!await transaction.HasPermissionAsync(actor,AttendanceIngestion.Manage,token))throw new UnauthorizedAccessException();
        await transaction.LockAsync("IO.DeviceRegistry",token);await transaction.LockAsync("IO.DeviceFileImports",token);var job=await Load(id,token);
        if(!job.Version.SequenceEqual(version))throw new DomainConflictException("Stale preview version; reread receipts before another page.");
        var same=await SameTarget(job,token);if(!same)throw new DomainConflictException("Source/site/company/zone changed; do not retarget this file.");
        var lines=Parse(job.OriginalFile,job.Calendar,job.TimeZoneId,job.MachineNumber).Lines.Where(x=>x.LineNumber>after).Take(50).ToArray();int added=0,quarantined=0,already=0,conflicts=0;
        foreach(var line in lines)
        {
            var receipt=await db.DeviceFileImportLines.AsTracking().SingleOrDefaultAsync(x=>x.ImportId==id&&x.LineNumber==line.LineNumber,token);
            if(receipt?.RawEventId is not null){already++;continue;}
            if(receipt is null){receipt=new(){ImportId=id,LineNumber=line.LineNumber};db.DeviceFileImportLines.Add(receipt);}
            receipt.Status=await Status(job,line,true,token);receipt.ReviewedByUserId=actor;receipt.ReviewedAtUtc=clock.GetUtcNow();
            if(receipt.Status!="ready"){quarantined++;continue;}
            // File-local identity, not a vendor-native event id. Exact original bytes remain in OriginalFile.
            var external="usb-file:"+job.FileHash+":"+line.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var result=await ingestion.AppendInTransactionAsync(job.SourceId,external,line.PersonIdentifier,line.OccurredAtUtc,job.TimeZoneId,line.CheckType==job.InCode?"in":"out",Encoding.UTF8.GetBytes(line.SourceLine),token);
            receipt.RawEventId=result.Result.EventId;receipt.ConflictId=result.Result.ConflictId;receipt.Status=result.Result.ConflictId is not null?"ingestion-conflict":result.Result.Outcome==IO.Domain.Attendance.RawEventAppendResult.AlreadyRecorded?"already-recorded":"imported";
            if(receipt.ConflictId is not null)conflicts++;else if(receipt.Status=="already-recorded")already++;else added++;
        }
        job.LastReviewedAtUtc=clock.GetUtcNow();db.Entry(job).Property(x=>x.LastReviewedAtUtc).IsModified=true;
        return(new{importId=id,after,nextAfter=lines.LastOrDefault()?.LineNumber??after,added,already,quarantined,conflicts},id);
    },ct);
    private async Task<DeviceFileImportRecord> Load(Guid id,CancellationToken ct)=>await db.DeviceFileImports.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown import.");
    private Task<bool> SameTarget(DeviceFileImportRecord job,CancellationToken ct)=>(from d in db.Devices join site in db.Sites on d.SiteId equals site.Id where d.Id==job.DeviceId&&d.SourceId==job.SourceId&&d.SiteId==job.SiteId&&site.CompanyId==job.CompanyId&&site.TimeZoneId==job.TimeZoneId&&d.Enabled select d.Id).AnyAsync(ct);
    private async Task<string> Status(DeviceFileImportRecord job,UsbPreviewLine line,bool same,CancellationToken ct)
    {
        if(!same)return "target-changed";if(line.Status!="parsed-requires-mapping-review")return line.Status;
        if(line.CheckType!=job.InCode&&line.CheckType!=job.OutCode)return "unmapped-check-code";
        var identity=Encoding.Unicode.GetBytes(line.PersonIdentifier!);var at=line.OccurredAtUtc!.Value;
        var count=await db.SourceEmployeeMappings.CountAsync(x=>x.SourceId==job.SourceId&&x.CompanyId==job.CompanyId&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==identity&&EF.Property<int>(x,"SourcePersonIdByteLength")==identity.Length&&x.ValidFromUtc<=at&&(x.ValidUntilUtcExclusive==null||at<x.ValidUntilUtcExclusive),ct);
        return count==1?"ready":count==0?"unknown-employee":"mapping-conflict";
    }
    private static UsbPreview Parse(byte[] bytes,string calendar,string zone,int machine)
    {
        try{return PersonnelUsbPreview.Parse(bytes,calendar,TimeZoneInfo.FindSystemTimeZoneById(zone),machine);}catch(TimeZoneNotFoundException){throw new ArgumentException("Unsupported site time zone.");}catch(InvalidTimeZoneException){throw new ArgumentException("Invalid site time zone.");}
    }
}
