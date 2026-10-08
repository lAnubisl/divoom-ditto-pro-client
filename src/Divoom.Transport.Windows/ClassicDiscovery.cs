using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Divoom;

internal static class ClassicDiscovery
{
    public static IReadOnlyList<ClassicBluetoothDevice> Scan()
    {
        var search = new BluetoothSearchParameters
        {
            Size = (uint)Marshal.SizeOf<BluetoothSearchParameters>(),
            Authenticated = true, Remembered = true, Unknown = true,
            Connected = true, Inquiry = true, TimeoutMultiplier = 8
        };
        var info = new BluetoothDeviceInfo { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>() };
        var handle = BluetoothFindFirstDevice(ref search, ref info);
        var devices = new List<ClassicBluetoothDevice>();
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 259) throw new Win32Exception(error);
            return devices;
        }
        try
        {
            do
            {
                devices.Add(new(info.Name, info.Address, info.Authenticated));
                info.Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>();
            } while (BluetoothFindNextDevice(handle, ref info));
            var error = Marshal.GetLastWin32Error();
            if (error != 259) throw new Win32Exception(error);
        }
        finally { BluetoothFindDeviceClose(handle); }
        return devices;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstDevice(ref BluetoothSearchParameters search, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindNextDevice(IntPtr handle, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BluetoothFindDeviceClose(IntPtr handle);
}
