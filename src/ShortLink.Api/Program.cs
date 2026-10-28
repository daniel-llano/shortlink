using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using ShortLink.Api.Data;
using ShortLink.Api.Links;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// PostgreSQL in production. SQLite works for a single box and runs the test suite.
var provider = config["Database:Provider"] ?? "Postgres";
builder.Services.AddDbContext<ShortLinkDbContext>(o =>
{
    var cs = config.GetConnectionString("ShortLink") ?? throw new InvalidOperationException("ConnectionStrings:ShortLink is required.");
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(cs);
    else o.UseNpgsql(cs);
});

var redis = config.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redis)) builder.Services.AddDistributedMemoryCache();
else builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redis; o.InstanceName = "shortlink:"; });

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ShortLinkOptions>(config.GetSection(ShortLinkOptions.Section));
builder.Services.AddScoped<LinkService>();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<ShortLinkDbContext>("database", HealthStatus.Unhealthy, ["ready"]);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("create", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = config.GetValue("RateLimiting:CreatesPerMinute", 30)
        }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShortLinkDbContext>();
    if (db.Database.IsNpgsql())
    {
        if (config.GetValue("Database:MigrateOnStartup", false))
            await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

if (app.Environment.IsDevelopment() || config.GetValue<bool>("EnableApiDocs"))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/docs");
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapLinks();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();

public partial class Program;
