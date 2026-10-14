namespace MasterServer.Payments;

public sealed class GatewayRegistry
{
    private readonly Dictionary<string, IPaymentGateway> _gateways;
    public string DefaultName { get; }

    public GatewayRegistry(IEnumerable<IPaymentGateway> gateways, string defaultName)
    {
        _gateways = gateways.ToDictionary(g => g.Name, g => g, StringComparer.OrdinalIgnoreCase);
        DefaultName = _gateways.ContainsKey(defaultName) ? defaultName : ManualGateway.GatewayName;
    }

    public static GatewayRegistry FromEnvironment()
    {
        var devSecret = Environment.GetEnvironmentVariable("GATEWAY_DEV_SECRET") ?? "";
        var defaultName = Environment.GetEnvironmentVariable("GATEWAY_DEFAULT") ?? DevGateway.GatewayName;
        return new GatewayRegistry(
            new IPaymentGateway[] { new DevGateway(devSecret), new ManualGateway() },
            defaultName);
    }

    public IPaymentGateway? Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? DefaultName : name.Trim();
        return _gateways.TryGetValue(key, out var g) ? g : null;
    }

    public IReadOnlyCollection<string> Names => _gateways.Keys.ToArray();
}
