using MasterServer.Data;
using MasterServer.Licensing;
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

var app = builder.Build();

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
}

app.Run();

public partial class Program;
