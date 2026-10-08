using System.Runtime.InteropServices;

namespace Divoom;

[StructLayout(LayoutKind.Sequential)]
internal struct BluetoothSearchParameters
{
    public uint Size;
    [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
    [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
    [MarshalAs(UnmanagedType.Bool)] public bool Unknown;
    [MarshalAs(UnmanagedType.Bool)] public bool Connected;
    [MarshalAs(UnmanagedType.Bool)] public bool Inquiry;
    public byte TimeoutMultiplier;
    public IntPtr Radio;
}
