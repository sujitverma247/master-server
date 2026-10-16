using MasterServer.Data;

namespace MasterServer.Support;

public static class TicketRules
{
    public static bool IsStatus(string? status) =>
        status is TicketStatuses.Open or TicketStatuses.Waiting or TicketStatuses.Closed;

    public static bool IsPriority(string? priority) =>
        priority is TicketPriorities.Normal or TicketPriorities.High;

    public static bool CanCustomerReply(string status) => status != TicketStatuses.Closed;

    public static string AfterCustomerMessage(string status) =>
        status == TicketStatuses.Closed ? status : TicketStatuses.Open;

    public static string AfterStaffMessage(string status) =>
        status == TicketStatuses.Closed ? status : TicketStatuses.Waiting;

    public static string NormalizeSubject(string subject)
    {
        var s = (subject ?? "").Trim();
        if (s.Length > 200) s = s[..200];
        return s;
    }

    public static string SubjectFromIssue(string issue)
    {
        var s = (issue ?? "").Trim();
        var breakAt = s.IndexOfAny(['\r', '\n']);
        if (breakAt >= 0) s = s[..breakAt].Trim();
        return NormalizeSubject(s);
    }

    public static string NormalizeBody(string body)
    {
        var s = (body ?? "").Trim();
        if (s.Length > 8000) s = s[..8000];
        return s;
    }

    public static bool IsPublicId(string? publicId) => TicketNumber.IsValid(publicId);
}
