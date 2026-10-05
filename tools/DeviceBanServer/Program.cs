using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using DeviceBanServer;
using FufuLauncher.Models;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://127.0.0.1:5088");
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);
var adminKey = builder.Configuration["DeviceBan:AdminKey"];
if (string.IsNullOrWhiteSpace(adminKey) || adminKey.Length is < 32 or > 256)
    throw new InvalidOperationException("Set DeviceBan__AdminKey to a random secret of 32–256 characters");
var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(adminKey));
var dataFile = builder.Configuration["DeviceBan:DataFile"] ?? Path.Combine(builder.Environment.ContentRootPath, "data", "devices.json");
builder.Services.AddSingleton(new DeviceBanRegistry(dataFile));
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("device-check", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/api/admin"))
    {
        var header = context.Request.Headers.Authorization.ToString();
        var supplied = header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : string.Empty;
        if (supplied.Length > 256 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), expectedKeyHash))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
    }
    try { await next(); }
    catch (ArgumentException ex)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.MapPost("/api/device/check", (DeviceObservation report, DeviceBanRegistry registry) => registry.Observe(report))
    .RequireRateLimiting("device-check");
app.MapGet("/api/admin/devices", (string uid, DeviceBanRegistry registry) => registry.FindByUid(uid));
app.MapPost("/api/admin/bans", (DeviceBanMutation mutation, DeviceBanRegistry registry) =>
{
    registry.SetBans(mutation);
    return Results.NoContent();
});
app.MapDelete("/api/admin/devices/{motherboardId}", (string motherboardId, DeviceBanRegistry registry) =>
{
    registry.Delete(motherboardId);
    return Results.NoContent();
});
app.Run();

sealed class RetentionWorker(DeviceBanRegistry registry) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        registry.Prune();
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken)) registry.Prune();
    }
}
