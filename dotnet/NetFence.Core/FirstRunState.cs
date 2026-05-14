namespace NetFence.Core;

public static class FirstRunState
{
    public static string DefaultStateDirectory => OperationLog.DataDirectory;

    public static bool IsAcknowledged(string? stateDirectory = null)
    {
        return File.Exists(GetMarkerPath(stateDirectory));
    }

    public static void SetAcknowledged(string? stateDirectory = null)
    {
        var directory = stateDirectory ?? DefaultStateDirectory;
        Directory.CreateDirectory(directory);
        File.WriteAllText(GetMarkerPath(directory), "acknowledged");
    }

    private static string GetMarkerPath(string? stateDirectory) =>
        Path.Combine(stateDirectory ?? DefaultStateDirectory, "first-run.ack");
}
