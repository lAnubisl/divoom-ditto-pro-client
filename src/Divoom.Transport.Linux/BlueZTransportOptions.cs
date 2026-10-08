namespace Divoom;

public sealed class BlueZTransportOptions
{
    /// <summary>Host system D-Bus; mount /run/dbus into a Docker container.</summary>
    public string BusAddress { get; init; } = Environment.GetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS") ?? "unix:path=/run/dbus/system_bus_socket";
    /// <summary>BlueZ index (e.g. hci0) or controller MAC address for stable selection across reboots.</summary>
    public string Adapter { get; init; } = "hci0";
    public TimeSpan DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
