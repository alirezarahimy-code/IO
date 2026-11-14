using System.Globalization;
using System.Security.Claims;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using Microsoft.AspNetCore.Antiforgery;

internal static class GatewayEndpoints
{
    internal static void MapGateway(this WebApplication app,bool enabled)
    {
        app.MapGet("/devices/gateway-bindings",(HttpContext c,PushGatewayIngress service)=>Read(c,actor=>service.ListAsync(actor,c.RequestAborted))).RequireAuthorization();
        app.MapPost("/devices/gateway-bindings",(PushGatewayIngress.Provision input,HttpContext c,IAntiforgery csrf,PushGatewayIngress service)=>Admin(c,csrf,actor=>service.ProvisionAsync(actor,input,c.RequestAborted))).RequireAuthorization();
        app.MapPost("/devices/gateway-bindings/{id:guid}/revoke",(Guid id,GatewayVersion input,HttpContext c,IAntiforgery csrf,PushGatewayIngress service)=>Admin(c,csrf,actor=>service.RevokeAsync(actor,id,input.Version,c.RequestAborted))).RequireAuthorization();
        if(!enabled)return;
        // Deliberately no cookie authority/CSRF exemption on administrative routes. This separate machine route authenticates its exact body using HMAC.
        app.MapPost(AuthenticatedPushGateway.Path,Receive).AllowAnonymous().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(ZkPushAttendanceCodec.MaximumBytes));
    }
    private static async Task<IResult> Receive(HttpContext c,PushGatewayIngress service)
    {
        c.Response.Headers.CacheControl="no-store";
        if(!c.Request.IsHttps)return Results.StatusCode(400);
        if(c.Request.ContentType!="application/octet-stream"||c.Request.Headers.ContainsKey("Content-Encoding"))return Results.BadRequest();
        string? Header(string name)=>c.Request.Headers.TryGetValue(name,out var values)&&values.Count==1?values[0]:null;
        if(!Guid.TryParseExact(Header("X-IO-Binding"),"D",out var binding)||!Guid.TryParseExact(Header("X-IO-Transfer"),"D",out var transfer)||
           !long.TryParse(Header("X-IO-Sent"),NumberStyles.None,CultureInfo.InvariantCulture,out var seconds)||Header("X-IO-Signature") is not{} signature)return Results.Unauthorized();
        string? stamp=null;
        try
        {
            if(c.Request.Headers.ContainsKey("X-IO-Stamp"))stamp=new System.Text.UTF8Encoding(false,true).GetString(Convert.FromBase64String(Header("X-IO-Stamp")??throw new ArgumentException()));
            if(c.Request.ContentLength is >ZkPushAttendanceCodec.MaximumBytes)return Results.StatusCode(413);
            using var bytes=new MemoryStream();var buffer=new byte[8192];int n;
            while((n=await c.Request.Body.ReadAsync(buffer,c.RequestAborted))>0){if(bytes.Length+n>ZkPushAttendanceCodec.MaximumBytes)return Results.StatusCode(413);bytes.Write(buffer,0,n);}
            var receipt=await service.ReceiveAsync(binding,new(transfer,DateTimeOffset.FromUnixTimeSeconds(seconds),stamp,bytes.ToArray(),signature),c.RequestAborted);
            return Results.Ok(receipt);
        }
        catch(UnauthorizedAccessException){return Results.Unauthorized();}
        catch(DomainConflictException){return Results.Conflict();}
        catch(Exception e)when(e is ArgumentException or FormatException){return Results.BadRequest();}
    }
    internal static async Task<IResult> Read<T>(HttpContext c,Func<Guid,Task<T>> action)
    {
        c.Response.Headers.CacheControl="no-store";
        if(!Guid.TryParse(c.User.FindFirstValue(ClaimTypes.NameIdentifier),out var actor))return Results.Unauthorized();
        try{return Results.Ok(new{result=await action(actor)});}catch(UnauthorizedAccessException){return Results.StatusCode(403);}catch(DomainConflictException){return Results.Conflict();}catch(ArgumentException){return Results.BadRequest();}
    }
    internal static async Task<IResult> Admin<T>(HttpContext c,IAntiforgery csrf,Func<Guid,Task<T>> action)
    {try{await csrf.ValidateRequestAsync(c);}catch(AntiforgeryValidationException){return Results.BadRequest();}return await Read(c,action);}
    internal sealed record GatewayVersion(byte[] Version);
}
