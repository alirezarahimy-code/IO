using IO.Infrastructure.Devices;
using Microsoft.AspNetCore.Antiforgery;
internal static class EnrollmentEndpoints
{
    internal static void MapEnrollment(this WebApplication app)
    {
        app.MapGet("/devices/enrollments",(Guid? deviceId,int? offset,HttpContext c,RemoteEnrollments service)=>GatewayEndpoints.Read(c,actor=>service.ListAsync(actor,deviceId,offset??0,c.RequestAborted))).RequireAuthorization();
        app.MapPost("/devices/enrollments",(RemoteEnrollments.Input input,HttpContext c,IAntiforgery csrf,RemoteEnrollments service)=>GatewayEndpoints.Admin(c,csrf,actor=>service.RequestAsync(actor,input,c.RequestAborted))).RequireAuthorization().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(16384));
        app.MapPost("/devices/enrollments/{id:guid}/cancel",(Guid id,GatewayEndpoints.GatewayVersion input,HttpContext c,IAntiforgery csrf,RemoteEnrollments service)=>GatewayEndpoints.Admin(c,csrf,actor=>service.CancelAsync(actor,id,input.Version,c.RequestAborted))).RequireAuthorization();
    }
}
