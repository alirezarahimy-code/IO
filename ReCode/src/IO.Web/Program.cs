// Infrastructure skeleton only. No authentication, employee/admin routes,
// persistence, or business capabilities are advertised by this endpoint.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/", () => Results.Ok(new { service = "IO", status = "foundation-only" }));

app.Run();
