using System.Management;

namespace NetFence.Core;

public sealed class WatcherEventArgs : EventArgs
{
    public int ProcessId { get; init; }
    public int ParentProcessId { get; init; }
}

public static class ProcessWatcher
{
    private static readonly object _lock = new();
    private static ManagementEventWatcher? _watcher;
    public static bool IsRunning { get { lock (_lock) return _watcher is not null; } }

    public static event EventHandler<WatcherEventArgs>? ProcessStarted;

    public static void Start()
    {
        lock (_lock)
        {
            if (_watcher is not null) return;

            var query = new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace");
            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            if (_watcher is null) return;
            _watcher.Stop();
            _watcher.EventArrived -= OnEventArrived;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private static void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var pid = Convert.ToInt32(e.NewEvent.Properties["ProcessID"]?.Value ?? 0);
            var parentPid = Convert.ToInt32(e.NewEvent.Properties["ParentProcessID"]?.Value ?? 0);
            if (pid == 0) return;
            ProcessStarted?.Invoke(null, new WatcherEventArgs
            {
                ProcessId = pid,
                ParentProcessId = parentPid
            });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ProcessWatcher error: {ex.Message}"); }
    }
}
