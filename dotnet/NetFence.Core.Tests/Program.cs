using NetFence.Core;

var tests = new List<(string Name, Action Body)>
{
    ("profile names are filesystem safe", TestProfileName),
    ("profile names preserve unicode letters", TestUnicodeProfileName),
    ("rule names are direction specific and stable", TestRuleNames),
    ("selected firewall rules collapse to program unblock targets", TestSelectedProgramUnblockTargets),
    ("system paths are protected", TestSystemPaths),
    ("folder targets include recursive exe files only", TestExecutableTargets),
    ("empty target folders fail clearly", TestEmptyTargetFolder),
    ("related candidates merge reasons and skip system paths", TestRelatedCandidates),
    ("related candidates do not auto-select shared runtimes", TestSharedRuntimeCandidates),
    ("snapshot export writes rules and candidates", TestSnapshotExport),
    ("operation log appends action and items", TestOperationLog),
    ("first run state persists marker", TestFirstRunState),
    ("powershell runner preserves unicode output", TestPowerShellRunnerUnicode),
    ("powershell runner handles long scripts", TestPowerShellRunnerLongScript),
    ("powershell runner captures large stderr without hanging", TestPowerShellRunnerLargeError),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"FAIL: {test.Name}");
        Console.WriteLine($"  {ex.Message}");
    }
}

if (failures > 0)
{
    Console.WriteLine($"{failures} test(s) failed.");
    return 1;
}

Console.WriteLine("All .NET NetFence tests passed.");
return 0;

static void TestProfileName()
{
    AssertEqual(NetFenceRules.GetProfileName(@"C:\Program Files\Example App\app.exe", "Example App!"), "Example_App");
}

static void TestUnicodeProfileName()
{
    AssertEqual(NetFenceRules.GetProfileName(@"C:\Apps\示例应用\main.exe", "示例 应用!"), "示例_应用");
}

static void TestRuleNames()
{
    var outbound = NetFenceRules.GetRuleName("Example_App", @"C:\Program Files\Example App\app.exe", FirewallDirection.Outbound);
    var inbound = NetFenceRules.GetRuleName("Example_App", @"C:\Program Files\Example App\app.exe", FirewallDirection.Inbound);

    AssertTrue(outbound.StartsWith("NetFence Example_App Outbound "), "outbound name should include direction");
    AssertTrue(inbound.StartsWith("NetFence Example_App Inbound "), "inbound name should include direction");
    AssertTrue(outbound != inbound, "different directions should produce different names");
    AssertTrue(NetFenceRules.IsManagedGroup("NetFence:Example_App"), "NetFence group should be managed");
    AssertTrue(!NetFenceRules.IsManagedGroup("Other:Example_App"), "other group should not be managed");
}

static void TestSelectedProgramUnblockTargets()
{
    var rules = new[]
    {
        new FirewallRuleInfo("Demo", "NetFence Demo Outbound one", "Outbound", true, "Block", @"C:\Apps\Demo\demo.exe"),
        new FirewallRuleInfo("Demo", "NetFence Demo Inbound one", "Inbound", true, "Block", @"C:\Apps\Demo\demo.exe"),
        new FirewallRuleInfo("Demo", "NetFence Demo Outbound two", "Outbound", true, "Block", @"C:\Apps\Demo\helper.exe"),
        new FirewallRuleInfo("", "Broken", "Outbound", true, "Block", @"C:\Apps\Demo\ignored.exe"),
        new FirewallRuleInfo("Demo", "Broken", "Outbound", true, "Block", "Any"),
    };

    var targets = FirewallService.GetSelectedProgramUnblockTargets(rules).ToArray();

    AssertEqual(targets.Length, 2);
    AssertTrue(targets.Contains(new FirewallProgramTarget("Demo", @"C:\Apps\Demo\demo.exe")), "same program should collapse to one target");
    AssertTrue(targets.Contains(new FirewallProgramTarget("Demo", @"C:\Apps\Demo\helper.exe")), "different programs should remain separate");
}

static void TestSystemPaths()
{
    var systemExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "svchost.exe");
    AssertTrue(NetFenceRules.IsProtectedSystemPath(systemExe), "Windows directory should be protected");
    AssertTrue(!NetFenceRules.IsProtectedSystemPath(@"C:\Program Files\Example\app.exe"), "Program Files should not be protected");
}

static void TestExecutableTargets()
{
    var root = NewTempDir();
    try
    {
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var app = Path.Combine(root, "app.exe");
        var helper = Path.Combine(root, "sub", "helper.EXE");
        File.WriteAllText(app, "");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "");
        File.WriteAllText(helper, "");

        var targets = NetFenceTargets.GetExecutableTargets(root).ToArray();

        AssertEqual(targets.Length, 2);
        AssertTrue(targets.Contains(app), "root exe should be included");
        AssertTrue(targets.Contains(helper), "nested exe should be included");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestEmptyTargetFolder()
{
    var root = NewTempDir();
    try
    {
        var ex = AssertThrows<InvalidOperationException>(() => NetFenceTargets.GetPlannedBlockTargets(root));
        AssertTrue(ex.Message.Contains("No executable files", StringComparison.OrdinalIgnoreCase), "empty folder should report no executable files");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestRelatedCandidates()
{
    var root = NewTempDir();
    try
    {
        var appDir = Path.Combine(root, "Vendor", "App");
        var sharedDir = Path.Combine(root, "Vendor", "Shared");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(sharedDir);
        var main = Path.Combine(appDir, "main.exe");
        var child = Path.Combine(sharedDir, "child.exe");
        var helper = Path.Combine(appDir, "helper.exe");
        var launcher = Path.Combine(sharedDir, "launcher.exe");
        var network = Path.Combine(appDir, "nethelper.exe");
        foreach (var file in new[] { main, child, helper, launcher, network })
        {
            File.WriteAllText(file, "");
        }
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "svchost.exe");

        var rows = new[]
        {
            new ProcessRow(100, 1, "main", main, Quote(main)),
            new ProcessRow(101, 100, "child", child, Quote(child)),
            new ProcessRow(102, 1, "helper", helper, Quote(helper)),
            new ProcessRow(103, 1, "launcher", launcher, $"{Quote(launcher)} --app-dir {Quote(appDir)}"),
            new ProcessRow(104, 1, "nethelper", network, Quote(network)),
            new ProcessRow(105, 100, "system", system, Quote(system)),
        };

        var candidates = RelatedProcessScanner.GetRelatedCandidates(main, rows, new[] { 104 }).ToArray();
        var programs = candidates.Select(c => c.Program).ToHashSet(StringComparer.OrdinalIgnoreCase);

        AssertTrue(programs.Contains(main), "target should be included");
        AssertTrue(programs.Contains(child), "child should be included");
        AssertTrue(programs.Contains(helper), "same folder should be included");
        AssertTrue(programs.Contains(launcher), "command-line reference should be included");
        AssertTrue(programs.Contains(network), "network candidate should be included");
        AssertTrue(!programs.Contains(system), "system executable should be skipped");
        AssertEqual(candidates.Count(c => string.Equals(c.Program, network, StringComparison.OrdinalIgnoreCase)), 1);
        AssertTrue(candidates.Single(c => string.Equals(c.Program, network, StringComparison.OrdinalIgnoreCase)).Reason.Contains("active network connection"), "network reason should be merged");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestSharedRuntimeCandidates()
{
    var root = NewTempDir();
    try
    {
        var appDir = Path.Combine(root, "Vendor", "App");
        var runtimeDir = Path.Combine(root, "SharedRuntime", "dotnet");
        Directory.CreateDirectory(appDir);
        Directory.CreateDirectory(runtimeDir);
        var main = Path.Combine(appDir, "main.exe");
        var helper = Path.Combine(appDir, "helper.exe");
        var runtime = Path.Combine(runtimeDir, "dotnet.exe");
        foreach (var file in new[] { main, helper, runtime })
        {
            File.WriteAllText(file, "");
        }

        var rows = new[]
        {
            new ProcessRow(100, 1, "main", main, Quote(main)),
            new ProcessRow(101, 100, "helper", helper, Quote(helper)),
            new ProcessRow(102, 100, "dotnet", runtime, $"{Quote(runtime)} {Quote(main)}"),
        };

        var candidates = RelatedProcessScanner.GetRelatedCandidates(main, rows, Array.Empty<int>()).ToArray();
        var mainCandidate = candidates.Single(item => string.Equals(item.Program, main, StringComparison.OrdinalIgnoreCase));
        var helperCandidate = candidates.Single(item => string.Equals(item.Program, helper, StringComparison.OrdinalIgnoreCase));
        var runtimeCandidate = candidates.Single(item => string.Equals(item.Program, runtime, StringComparison.OrdinalIgnoreCase));

        AssertTrue(mainCandidate.Selected, "selected target should remain checked");
        AssertTrue(helperCandidate.Selected, "same install folder helper should remain checked");
        AssertTrue(!runtimeCandidate.Selected, "shared runtime outside the install folder should not be checked by default");
        AssertTrue(runtimeCandidate.Reason.Contains("shared runtime", StringComparison.OrdinalIgnoreCase), "shared runtime reason should be visible");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestSnapshotExport()
{
    var root = NewTempDir();
    try
    {
        var path = Path.Combine(root, "snapshot.csv");
        var result = SnapshotExporter.Export(path,
            new[] { new FirewallRuleInfo("Demo", "NetFence Demo Outbound abc", "Outbound", true, "Block", @"C:\Apps\Demo\demo.exe") },
            new[] { new RelatedCandidate(true, @"C:\Apps\Demo\helper.exe", "same install folder", 123, "helper") });
        var csv = File.ReadAllText(path);

        AssertEqual(result.RowCount, 2);
        AssertTrue(csv.Contains("FirewallRule"), "rule row should be exported");
        AssertTrue(csv.Contains("Candidate"), "candidate row should be exported");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestOperationLog()
{
    var root = NewTempDir();
    try
    {
        var path = Path.Combine(root, "NetFence.log");
        OperationLog.Write(path, "Block", "Blocked Demo", new[] { @"C:\Apps\Demo\demo.exe" });
        var log = File.ReadAllText(path);

        AssertTrue(log.Contains("Block"), "log should contain action");
        AssertTrue(log.Contains("Blocked Demo"), "log should contain message");
        AssertTrue(log.Contains(@"C:\Apps\Demo\demo.exe"), "log should contain item");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestFirstRunState()
{
    var root = NewTempDir();
    try
    {
        AssertTrue(!FirstRunState.IsAcknowledged(root), "new directory should not be acknowledged");
        FirstRunState.SetAcknowledged(root);
        AssertTrue(FirstRunState.IsAcknowledged(root), "marker should persist");
    }
    finally
    {
        Directory.Delete(root, true);
    }
}

static void TestPowerShellRunnerUnicode()
{
    var expected = "\u8054\u7f51\u6d4b\u8bd5";
    var output = PowerShellRunner.RunRequired($"Write-Output '{expected}'").Trim();

    AssertEqual(output, expected);
}

static void TestPowerShellRunnerLongScript()
{
    var longComment = "# " + new string('x', 50000);
    var output = PowerShellRunner.RunRequired(longComment + Environment.NewLine + "Write-Output 'ok'").Trim();

    AssertEqual(output, "ok");
}

static void TestPowerShellRunnerLargeError()
{
    var result = PowerShellRunner.Run("[Console]::Error.Write(('x' * 100000)); exit 3");

    AssertEqual(result.ExitCode, 3);
    AssertTrue(result.StandardError.Length >= 100000, "large stderr should be captured completely");
}

static string NewTempDir()
{
    var path = Path.Combine(Path.GetTempPath(), "netfence-dotnet-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static string Quote(string value) => $"\"{value}\"";

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T actual, T expected)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static TException AssertThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException ex)
    {
        return ex;
    }

    throw new InvalidOperationException($"Expected exception {typeof(TException).Name}.");
}
