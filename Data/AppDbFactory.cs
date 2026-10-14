using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MasterServer.Data;

public sealed class AppDbFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args)
    {
        var raw = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            raw = "Host=localhost;Port=5432;Database=master;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDb>()
            .UseNpgsql(ConnectionStrings.Normalize(raw))
            .Options;
        return new AppDb(options);
    }
}
