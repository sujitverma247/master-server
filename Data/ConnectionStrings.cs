using Npgsql;

namespace MasterServer.Data;

public static class ConnectionStrings
{
    public static string Normalize(string raw)
    {
        var value = raw.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.TrimStart('/');
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = string.IsNullOrEmpty(database) ? "master" : database,
            Username = user,
            Password = pass
        };
        if (uri.Query.Contains("sslmode=require", StringComparison.OrdinalIgnoreCase))
            builder.SslMode = SslMode.Require;
        return builder.ConnectionString;
    }
}
