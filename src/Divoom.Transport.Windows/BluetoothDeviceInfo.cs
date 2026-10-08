using System.Runtime.InteropServices;

namespace Divoom;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct BluetoothDeviceInfo
{
    public uint Size;
    public ulong Address;
    public uint ClassOfDevice;
    [MarshalAs(UnmanagedType.Bool)] public bool Connected;
    [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
    [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
    public BluetoothSystemTime LastSeen;
    public BluetoothSystemTime LastUsed;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
}
