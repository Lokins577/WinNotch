using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using WinNotch.AppManagement;

namespace WinNotch.Platform.Windows.AppManagement;

public sealed class PortInspectionService
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int OwnerPidAll = 5;
    private const int UdpOwnerPid = 1;
    private const int ErrorInsufficientBuffer = 122;

    public IReadOnlyList<PortOwner> GetTcpPorts()
    {
        var result = new List<PortOwner>();
        ReadTable(AfInet, Marshal.SizeOf<TcpRow>(), pointer => ParseV4(pointer), result);
        ReadTable(AfInet6, Marshal.SizeOf<Tcp6Row>(), pointer => ParseV6(pointer), result);
        return result.OrderBy(value => value.LocalPort).ThenBy(value => value.ProcessName).ToArray();
    }

    public IReadOnlyList<PortOwner> GetPorts()
    {
        var result = GetTcpPorts().ToList();
        ReadUdpTable(AfInet, Marshal.SizeOf<UdpRow>(), ParseUdpV4, result);
        ReadUdpTable(AfInet6, Marshal.SizeOf<Udp6Row>(), ParseUdpV6, result);
        return result.OrderBy(value => value.LocalPort).ThenBy(value => value.Protocol).ToArray();
    }

    private static void ReadTable(
        int addressFamily,
        int rowSize,
        Func<IntPtr, PortOwner> parser,
        ICollection<PortOwner> result)
    {
        int size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, true, addressFamily, OwnerPidAll, 0)
            != ErrorInsufficientBuffer || size <= 4) return;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, addressFamily, OwnerPidAll, 0) != 0) return;
            int count = Marshal.ReadInt32(buffer);
            IntPtr row = buffer + sizeof(int);
            for (int index = 0; index < count; index++) result.Add(parser(row + index * rowSize));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static PortOwner ParseV4(IntPtr pointer)
    {
        var row = Marshal.PtrToStructure<TcpRow>(pointer);
        return Create(
            "TCPv4", new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort),
            new IPAddress(row.RemoteAddress).ToString(), Port(row.RemotePort),
            row.State, (int)row.ProcessId);
    }

    private static PortOwner ParseV6(IntPtr pointer)
    {
        var row = Marshal.PtrToStructure<Tcp6Row>(pointer);
        return Create(
            "TCPv6", new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(), Port(row.LocalPort),
            new IPAddress(row.RemoteAddress, row.RemoteScopeId).ToString(), Port(row.RemotePort),
            row.State, (int)row.ProcessId);
    }

    private static PortOwner ParseUdpV4(IntPtr pointer)
    {
        var row = Marshal.PtrToStructure<UdpRow>(pointer);
        return Create("UDPv4", new IPAddress(row.LocalAddress).ToString(), Port(row.LocalPort),
            "*", 0, 0, (int)row.ProcessId);
    }

    private static PortOwner ParseUdpV6(IntPtr pointer)
    {
        var row = Marshal.PtrToStructure<Udp6Row>(pointer);
        return Create("UDPv6", new IPAddress(row.LocalAddress, row.LocalScopeId).ToString(), Port(row.LocalPort),
            "*", 0, 0, (int)row.ProcessId);
    }

    private static void ReadUdpTable(
        int addressFamily,
        int rowSize,
        Func<IntPtr, PortOwner> parser,
        ICollection<PortOwner> result)
    {
        int size = 0;
        if (GetExtendedUdpTable(IntPtr.Zero, ref size, true, addressFamily, UdpOwnerPid, 0)
            != ErrorInsufficientBuffer || size <= 4) return;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buffer, ref size, true, addressFamily, UdpOwnerPid, 0) != 0) return;
            int count = Marshal.ReadInt32(buffer);
            IntPtr row = buffer + sizeof(int);
            for (int index = 0; index < count; index++) result.Add(parser(row + index * rowSize));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static PortOwner Create(
        string protocol, string localAddress, int localPort,
        string remoteAddress, int remotePort, uint state, int processId)
    {
        string processName;
        try { processName = Process.GetProcessById(processId).ProcessName; }
        catch { processName = "System"; }
        return new PortOwner(
            protocol, localAddress, localPort, remoteAddress, remotePort,
            protocol.StartsWith("UDP", StringComparison.Ordinal) ? "BOUND" : StateName(state),
            processId, processName);
    }

    private static int Port(uint value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        return bytes[0] << 8 | bytes[1];
    }

    private static string StateName(uint state) => state switch
    {
        1 => "CLOSED", 2 => "LISTEN", 3 => "SYN-SENT", 4 => "SYN-RECEIVED",
        5 => "ESTABLISHED", 6 => "FIN-WAIT-1", 7 => "FIN-WAIT-2", 8 => "CLOSE-WAIT",
        9 => "CLOSING", 10 => "LAST-ACK", 11 => "TIME-WAIT", 12 => "DELETE-TCB",
        _ => state.ToString()
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRow
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddress;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRow
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Udp6Row
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddress;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint ProcessId;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedUdpTable(
        IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int addressFamily, int tableClass, uint reserved);
}
