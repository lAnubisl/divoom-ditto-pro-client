using System.Globalization;
using Divoom;

namespace Divoom.Api;

internal static class DeviceTransportFactory
{
    public static IDittoProTransport Create(string address, IConfiguration configuration)
    {
#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            var normalized = address.Replace(":", "").Replace("-", "");
            if (normalized.Length != 12 || !ulong.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException("DIVOOM_ADDRESS must be a 48-bit Bluetooth MAC address.");
            return new WindowsBleTransport(value);
        }
#endif
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("On Windows run the net10.0-windows10.0.19041.0 target; Linux uses BlueZ.");
        return new BlueZBleTransport(address, new BlueZTransportOptions
        {
            Adapter = configuration["DIVOOM_ADAPTER"] ?? "hci0",
            BusAddress = configuration["DBUS_SYSTEM_BUS_ADDRESS"] ?? "unix:path=/run/dbus/system_bus_socket"
        });
    }
}
