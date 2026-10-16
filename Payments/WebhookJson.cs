using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MasterServer.Payments;

internal static class WebhookJson
{
    public static bool TryParse(string? raw, out JsonElement root)
    {
        root = default;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(raw) ? "{}" : raw);
            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static JsonElement Obj(JsonElement parent, params string[] names)
    {
        if (parent.ValueKind != JsonValueKind.Object) return default;
        foreach (var name in names)
        {
            if (parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
                return value;
        }
        return default;
    }

    public static JsonElement Nested(JsonElement root, string name)
    {
        var fromObjects = Obj(Obj(root, "objects"), name);
        if (fromObjects.ValueKind == JsonValueKind.Object) return fromObjects;
        return Obj(root, name);
    }

    public static JsonElement FirstArrayItem(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return default;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object) return item;
        }
        return default;
    }

    public static string Str(JsonElement body, string name)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
            return "";
        if (value.ValueKind == JsonValueKind.String) return (value.GetString() ?? "").Trim();
        if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            return value.ToString().Trim();
        return "";
    }

    public static string StrAny(JsonElement body, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Str(body, name);
            if (value.Length > 0) return value;
        }
        return "";
    }

    public static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        return "";
    }

    public static decimal? Dec(JsonElement body, params string[] names)
    {
        foreach (var name in names)
        {
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
                return number;
            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number))
                return number;
        }
        return null;
    }

    public static DateTime? Dt(JsonElement body, params string[] names)
    {
        var text = StrAny(body, names);
        if (text.Length == 0) return null;
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return null;
        if (dt.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        return dt.ToUniversalTime();
    }

    public static bool FixedEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a ?? "");
        var right = Encoding.UTF8.GetBytes(b ?? "");
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    public static string Sha256Hex(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw ?? ""));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Env(params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return "";
    }
}
