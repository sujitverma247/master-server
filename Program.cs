using MasterServer.Admin;
using MasterServer.Catalog;
using MasterServer.Customers;
using MasterServer.Data;
using MasterServer.Keys;
using MasterServer.Licensing;
using MasterServer.Mail;
using MasterServer.Payments;
using MasterServer.Support;
using MasterServer.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

var rawConnection = Environment.GetEnvironmentVariable("DATABASE_URL");
if (string.IsNullOrWhiteSpace(rawConnection))
    rawConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(rawConnection))
    throw new InvalidOperationException("Set DATABASE_URL or ConnectionStrings:DefaultConnection.");

var connectionString = ConnectionStrings.Normalize(rawConnection);
builder.Services.AddDbContext<AppDb>(options => options.UseNpgsql(connectionString));

const string AdminCorsPolicy = "admin-panel";
var adminOrigins = (Environment.GetEnvironmentVariable("ADMIN_ORIGIN") ?? builder.Configuration["Admin:Origin"] ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy(AdminCorsPolicy, policy =>
    {
        if (adminOrigins.Length > 0)
            policy.WithOrigins(adminOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors(AdminCorsPolicy);

app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"error\":\"internal error\"}");
        }
    }
});

app.MapGet("/health", () => Results.Json(new { status = "ok" }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));

var skewSeconds = builder.Configuration.GetValue("License:TimestampSkewSeconds", 300);
LicenseApi.Map(app, skewSeconds);

var sessionHours = builder.Configuration.GetValue("Admin:SessionHours", 12);
AdminApi.Map(app, sessionHours);
CatalogApi.Map(app, skewSeconds);
var mail = MailSenderFactory.FromEnvironment();
KeysApi.Map(app, mail);
CustomersApi.Map(app);
var gateways = GatewayRegistry.FromEnvironment();
PaymentsApi.Map(app, gateways, mail);
MailApi.Map(app, mail);
TelemetryApi.Map(app);
SupportApi.Map(app);

try
{
    var csb = new NpgsqlConnectionStringBuilder(connectionString);
    app.Logger.LogInformation("Database host {Host} database {Database}", csb.Host, csb.Database);
}
catch
{
    app.Logger.LogInformation("Database connection string is set.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.MigrateAsync();
    await ProductSeed.ApplyAsync(db, app.Configuration, app.Logger, CancellationToken.None);
    await AdminSeed.ApplyAsync(db, app.Configuration, app.Logger, CancellationToken.None);
    await RefundPolicySeed.ApplyAsync(db, app.Logger, CancellationToken.None);
}

app.Run();

public partial class Program;
