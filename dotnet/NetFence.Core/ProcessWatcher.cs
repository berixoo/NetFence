using System.Management;

namespace NetFence.Core;

public sealed class WatcherEventArgs : EventArgs
{
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
}

public static class ProcessWatcher
{
    private static ManagementEventWatcher? _watcher;
    public static bool IsRunning => _watcher is not null;

    public static event EventHandler<WatcherEventArgs>? ProcessStarted;

    public static void Start()
    {
        if (_watcher is not null) return;

        var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
        _watcher = new ManagementEventWatcher(query);
        _watcher.EventArrived += OnEventArrived;
        _watcher.Start();
    }

    public static void Stop()
    {
        if (_watcher is null) return;
        _watcher.Stop();
        _watcher.EventArrived -= OnEventArrived;
        _watcher.Dispose();
        _watcher = null;
    }

    private static void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var parentPid = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"].Value);
            ProcessStarted?.Invoke(null, new WatcherEventArgs
            {
                ProcessId = pid,
                ParentProcessId = parentPid
            });
        }
        catch { }
    }
}
