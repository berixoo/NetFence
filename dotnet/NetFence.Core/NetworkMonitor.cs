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
        [7] = "FinWait2",    [8] = "CloseWait",     [9] = "Closing",
        [10] = "LastAck",    [11] = "TimeWait",     [12] = "DeleteTcb",
        [13] = "Bound"
    };

    private static HashSet<string>? _cachedBlockedPrograms;
    private static DateTime _blockedCacheTime = DateTime.MinValue;
    private static readonly TimeSpan BlockedCacheTtl = TimeSpan.FromSeconds(30);

    public static void InvalidateBlockedCache()
    {
        _cachedBlockedPrograms = null;
        _blockedCacheTime = DateTime.MinValue;
    }

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
        if (_cachedBlockedPrograms is not null && (DateTime.Now - _blockedCacheTime) < BlockedCacheTtl)
            return _cachedBlockedPrograms;

        var programs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var rule in FirewallService.GetStatus())
            {
                if (!string.IsNullOrWhiteSpace(rule.Program) && Path.IsPathFullyQualified(rule.Program))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(rule.Program);
                    programs.Add(Path.GetFullPath(expanded));
                }
            }
        }
        catch { }

        _cachedBlockedPrograms = programs;
        _blockedCacheTime = DateTime.Now;
        return programs;
    }

    private static void EnumerateTcp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        EnumerateTable(results, blockedPrograms, "TCP",
            GetExtendedTcpTable, 5, (rowPtr, state) =>
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                return (row.dwOwningPid,
                        new IPAddress((long)row.dwLocalAddr).ToString(),
                        (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                        new IPAddress((long)row.dwRemoteAddr).ToString(),
                        (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort),
                        state);
            });
    }

    private static void EnumerateUdp(List<NetworkConnection> results, HashSet<string> blockedPrograms)
    {
        EnumerateTable(results, blockedPrograms, "UDP",
            GetExtendedUdpTable, 1, (rowPtr, _) =>
            {
                var row = Marshal.PtrToStructure<MibUdpRowOwnerPid>(rowPtr);
                return (row.dwOwningPid,
                        new IPAddress((long)row.dwLocalAddr).ToString(),
                        (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort),
                        "*", 0,
                        "-");
            });
    }

    private delegate uint TableApiDelegate(IntPtr buf, ref uint bufSize, bool order,
        uint af, uint tableClass, uint reserved);

    private delegate (uint pid, string localAddr, int localPort,
        string remoteAddr, int remotePort, string state)
        RowReaderDelegate(IntPtr rowPtr, string defaultState);

    private static void EnumerateTable(List<NetworkConnection> results,
        HashSet<string> blockedPrograms, string protocol,
        TableApiDelegate api, uint tableClass, RowReaderDelegate readRow)
    {
        const int maxRetries = 2;
        const uint afInet = 2;
        var buf = IntPtr.Zero;
        var bufSize = 0u;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var ret = api(IntPtr.Zero, ref bufSize, false, afInet, tableClass, 0);
            if (bufSize == 0) return;

            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            buf = Marshal.AllocHGlobal((int)bufSize);

            ret = api(buf, ref bufSize, false, afInet, tableClass, 0);
            if (ret == 0) break;

            if (ret != 122 /* ERROR_INSUFFICIENT_BUFFER */ || attempt == maxRetries)
            {
                Marshal.FreeHGlobal(buf);
                return;
            }
        }

        try
        {
            var count = Marshal.ReadInt32(buf);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>(); // Same size for both structs
            for (var i = 0; i < count; i++)
            {
                var rowPtr = buf + 4 + i * rowSize;
                var defaultState = protocol == "TCP"
                    ? TcpStates.GetValueOrDefault(Marshal.ReadInt32(rowPtr), "-")
                    : "-";

                var (pid, localAddr, localPort, remoteAddr, remotePort, state) =
                    readRow(rowPtr, defaultState);

                var (procName, exePath) = ResolveProcess(pid);
                var blocked = exePath is not null && blockedPrograms.Contains(exePath);

                results.Add(new NetworkConnection(
                    (int)pid, procName, exePath, protocol,
                    localAddr, localPort, remoteAddr, remotePort,
                    state, blocked));
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
