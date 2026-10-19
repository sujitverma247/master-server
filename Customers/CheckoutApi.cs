using System.Text.Json;
using MasterServer.Admin;
using MasterServer.Data;
using MasterServer.Support;
using Microsoft.EntityFrameworkCore;

namespace MasterServer.Customers;

/// <summary>
/// Public Buy Now endpoint: upsert the customer, record refund-policy acceptance,
/// then the website redirects to the gateway buy link with the same email.
/// </summary>
public static class CheckoutApi
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Map(WebApplication app)
    {
        app.MapPost("/v1/checkout/prepare", (HttpContext c, AppDb db, CancellationToken ct) => Prepare(c, db, ct));
    }

    private static async Task<IResult> Prepare(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var body = await ReadBodyAsync(ctx, ct);
        if (body is null) return Fail(400, "invalid JSON body");

        if (!TryBool(body.Value, "acceptRefundPolicy", out var accepted) || !accepted)
            return Fail(400, "Accept the refund policy to continue.");

        var name = FirstNonEmpty(Str(body.Value, "name"), Str(body.Value, "displayName"));
        if (name.Length < 2) return Fail(400, "Enter your name.");
        if (name.Length > 200) return Fail(400, "name too long");

        if (!EmailAddress.TryNormalize(Str(body.Value, "email"), out var email))
            return Fail(400, "Enter a valid email.");

        var phone = Str(body.Value, "phone");
        if (phone.Length > 0 && phone.Length < 6) return Fail(400, "Enter a valid phone number.");
        if (phone.Length > 32) phone = phone[..32];

        var current = await db.RefundPolicies.FirstOrDefaultAsync(p => p.IsCurrent, ct);
        if (current is null) return Fail(404, "no current refund policy");

        var version = Str(body.Value, "policyVersion");
        if (version.Length > 0 && current.Version != version)
            return Fail(422, "accept the current policy");

        var source = FirstNonEmpty(Str(body.Value, "source"), "website");
        var customer = await CustomerStore.FindOrCreateAsync(db, email, name, source, ct);
        customer.DisplayName = name;
        if (phone.Length > 0) customer.Phone = phone;
        customer.UpdatedAt = DateTime.UtcNow;
        CustomerStore.ApplyLocale(customer, FirstNonEmpty(Str(body.Value, "locale"), Str(body.Value, "language")));
        await db.SaveChangesAsync(ct);

        var session = await CheckoutCapture.CaptureAsync(
            ctx,
            body.Value,
            customer.Id,
            FirstNonEmpty(Str(body.Value, "productSlug"), Str(body.Value, "slug")),
            FirstNonEmpty(Str(body.Value, "planCode"), Str(body.Value, "plan")),
            source,
            ct);
        db.CheckoutSessions.Add(session);

        var existing = await db.RefundPolicyAcceptances
            .FirstOrDefaultAsync(a => a.CustomerId == customer.Id && a.PolicyId == current.Id, ct);
        if (existing is null)
        {
            db.RefundPolicyAcceptances.Add(new RefundPolicyAcceptance
            {
                CustomerId = customer.Id,
                PolicyId = current.Id,
                AcceptedAt = DateTime.UtcNow,
                IpAddress = session.IpAddress,
                UserAgent = TicketReporter.Clip(session.UserAgent, 256),
                Source = source
            });
            Audit.Record(db, ctx, null, "policy.accept", email, $"version={current.Version};checkout");
        }

        await db.SaveChangesAsync(ct);

        return Json(200, new
        {
            ok = true,
            email,
            policyVersion = current.Version
        });
    }

    private static async Task<JsonElement?> ReadBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        try { using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ct); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "").Trim() : "";

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
            if (value.Length > 0) return value;
        return "";
    }

    private static bool TryBool(JsonElement body, string name, out bool result)
    {
        result = false;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (v.ValueKind == JsonValueKind.False) { result = false; return true; }
        return false;
    }

    private static IResult Fail(int status, string error) => Json(status, new { error });
    private static IResult Json(int status, object body) => Results.Json(body, JsonOptions, statusCode: status);
}
