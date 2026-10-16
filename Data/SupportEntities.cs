namespace MasterServer.Data;

public class Ticket
{
    public int Id { get; set; }
    public string PublicId { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int? ProductId { get; set; }
    public int? BrandId { get; set; }
    public int? LicenseId { get; set; }
    public string Subject { get; set; } = "";
    public string Status { get; set; } = TicketStatuses.Open;
    public string Priority { get; set; } = TicketPriorities.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string Phone { get; set; } = "";
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string Region { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string Address { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string Device { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string Browser { get; set; } = "";
    public string Language { get; set; } = "";
    public string Timezone { get; set; } = "";
    public string Screen { get; set; } = "";
    public string Geo { get; set; } = "";
    public string Isp { get; set; } = "";

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
