using System.Runtime.InteropServices;

namespace Divoom;

[StructLayout(LayoutKind.Sequential)]
internal struct BluetoothSystemTime
{
    public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
}
