using System.Runtime.InteropServices;

namespace WinNotch.Helpers;

public static class SystemActions
{
    public static bool LockWorkstation() => LockWorkStation();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
}
