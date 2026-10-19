using MasterServer.Data;

namespace MasterServer.Payments;

/// <summary>Maps an order row or return-URL status into the post-checkout page outcome.</summary>
public static class OrderStatusLookup
{
    public static bool IsFailIntent(string status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s.Contains("fail") || s.Contains("cancel") || s.Contains("declin")
            || s.Contains("error") || s.Contains("expired") || s.Contains("unpaid")
            || s.Contains("reject") || s.Contains("void");
    }

    public static bool IsSuccessIntent(string status)
    {
        var s = (status ?? "").Trim().ToLowerInvariant();
        return s.Contains("succe") || s is "active" or "paid" or "complete" or "completed" or "true";
    }

    public static bool CanQuery(string publicId, string paymentId, string subscriptionId, string email, string userId, string urlStatus) =>
        publicId.Length > 0
        || paymentId.Length > 0
        || subscriptionId.Length > 0
        || email.Length > 0
        || userId.Length > 0
        || IsSuccessIntent(urlStatus)
        || IsFailIntent(urlStatus);

    public static string OutcomeFromOrderStatus(string status) =>
        (status ?? "").Trim().ToLowerInvariant() switch
        {
            OrderStatuses.Paid => "success",
            OrderStatuses.Failed => "failed",
            OrderStatuses.Cancelled => "cancelled",
            OrderStatuses.Refunded => "failed",
            _ => "pending"
        };

    public static string PaymentMethod(string? gateway)
    {
        var value = (gateway ?? "").Trim();
        return value.ToLowerInvariant() switch
        {
            "dodo" => "Dodo Payments",
            "freemius" => "Freemius",
            "dev" => "Dev",
            "manual" => "Manual",
            "" => "",
            _ => char.ToUpperInvariant(value[0]) + (value.Length > 1 ? value[1..] : "")
        };
    }
}
