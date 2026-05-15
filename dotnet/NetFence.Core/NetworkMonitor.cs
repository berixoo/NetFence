using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace NetFence.Core;

public static class NetworkMonitor
{
    private static readonly Dictionary<int, string> TcpStates = new()
    {
        [1] = "Closed",      [2] = "Listen",       [3] = "SynSent",
        [4] = "SynReceived", [5] = "Established",   [6] = "FinWait1",
        [7] = "FinWait2",    [8] = "CloseWait",     [9] = "LastAck",
        [10] = "LastAck",    [11] = "TimeWait",     [12] = "DeleteTcb"
    };

    public static IReadOnlyList<NetworkConnection> GetConnections()
    {
        var result = new List<NetworkConnection>();
        var blockedPrograms = GetNetFenceBlockedPrograms();

        EnumerateTcp(result, blockedPrograms);
        EnumerateUdp(result, blockedPrograms);

        return result;
    }

    private static HashSet<string> GetNetFenceBlockedPrograms()
    {
        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rule in FirewallService.GetStatus())
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                    programs.Add(Path.GetFullPath(rule.Program));
            }
        }
        catch { }
        return programs;
    }

    private static void EnumerateTcp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        var bufSize = 0u;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufSize, false, 2, 5, 0);
        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            if (GetExtendedTcpTable(buf, ref bufSize, false, 2, 5, 0) != 0) return;

            var count = Marshal.ReadInt32(buf);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var rowPtr = buf + 4 + i * rowSize;
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);

                var localAddr = new IPAddress((long)row.dwLocalAddr);
                var remoteAddr = new IPAddress((long)row.dwRemoteAddr);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);
                var remotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort);
                var state = TcpStates.GetValueOrDefault((int)row.dwState, $"State{row.dwState}");

                var (procName, exePath) = ResolveProcess(row.dwOwningPid);
                var blocked = exePath is not null && blockedPrograms.Contains(exePath);

                results.Add(new NetworkConnection(
                    (int)row.dwOwningPid, procName, exePath, "TCP",
                    localAddr.ToString(), localPort,
                    remoteAddr.ToString(), remotePort,
                    state, blocked));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void EnumerateUdp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        var bufSize = 0u;
        _ = GetExtendedUdpTable(IntPtr.Zero, ref bufSize, false, 2, 1, 0);
        var buf = Marshal.AllocHGlobal((int)bufSize);
        try
        {
            if (GetExtendedUdpTable(buf, ref bufSize, false, 2, 1, 0) != 0) return;

            var count = Marshal.ReadInt32(buf);
            var rowSize = Marshal.SizeOf<MibUdpRowOwnerPid>();
            for (var i = 0; i < count; i++)
            {
                var rowPtr = buf + 4 + i * rowSize;
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);

                var localAddr = new IPAddress((long)row.dwLocalAddr);
                var localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);

                var (procName, exePath) = ResolveProcess(row.dwOwningPid);
                var blocked = exePath is not null && blockedPrograms.Contains(exePath);

                results.Add(new NetworkConnection(
                    (int)row.dwOwningPid, procName, exePath, "UDP",
                    localAddr.ToString(), localPort,
                    "*", 0,
                    "-", blocked));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static (string ProcessName, string? ExePath) ResolveProcess(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return (proc.ProcessName, proc.MainModule?.FileName);
        }
        catch
        {
            return ($"PID:{pid}", null);
        }
    }

    #region P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref uint dwOutBufLen, bool bOrder,
        uint ulAf, uint dwTableClass, uint dwReserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref uint dwOutBufLen, bool bOrder,
        uint ulAf, uint dwTableClass, uint dwReserved);

    #endregion
}
