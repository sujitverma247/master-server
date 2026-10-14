namespace MasterServer.Data;

public class Ticket
{
    public int Id { get; set; }
    public string PublicId { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int? ProductId { get; set; }
    public int? LicenseId { get; set; }
    public string Subject { get; set; } = "";
    public string Status { get; set; } = TicketStatuses.Open;
    public string Priority { get; set; } = TicketPriorities.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();
}

public class TicketMessage
{
    public long Id { get; set; }
    public int TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public string Author { get; set; } = TicketAuthors.Customer;
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class TicketStatuses
{
    public const string Open = "open";
    public const string Waiting = "waiting";
    public const string Closed = "closed";
}

public static class TicketPriorities
{
    public const string Normal = "normal";
    public const string High = "high";
}

public static class TicketAuthors
{
    public const string Customer = "customer";
    public const string Staff = "staff";
}
