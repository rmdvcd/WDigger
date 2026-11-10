using System.Diagnostics;
using System.Reflection;
using WDigger.Core;
using WDigger.Models;
using WDigger.Diagnostics;

if (args.Length > 0 && args[0] == "--child")
{
    switch (args[1])
    {
        case "exit":
            Console.WriteLine("output-marker");
            Console.Error.WriteLine("error-marker");
            return 7;
        case "wait":
            Console.WriteLine("started");
            await Task.Delay(TimeSpan.FromMinutes(1));
            return 0;
        case "arguments":
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(args.Skip(2).ToArray()));
            return 0;
        case "large":
            Console.Write(new string('x', 1024 * 1024));
            return 0;
        case "write":
            File.WriteAllText(args[2], "changed");
            return 0;
    }
}

var testDirectory = Path.Combine(Path.GetTempPath(), "WDigger-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDirectory);
try
{
    using var logger = new AppLogger(testDirectory);
    var executable = Environment.ProcessPath ?? throw new Exception("No se pudo determinar el proceso de pruebas.");
    var prefix = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)
        ? $"\"{Assembly.GetExecutingAssembly().Location}\" " : "";
    string Child(string mode) => $"{prefix}--child {mode}";
    var executor = new CommandExecutor(logger, () => true, TimeSpan.FromSeconds(20));
    var timeoutExecutor = new CommandExecutor(logger, () => true, TimeSpan.FromSeconds(2));

    var actual = await executor.RunAsync(executable, Child("exit"), simulate: false);
    Require(!actual.Success && actual.ExitCode == 7, "nonzero exit was not preserved");
    Require(actual.StandardOutput.Contains("output-marker") && actual.StandardError.Contains("error-marker"), "stdout/stderr not captured separately");

    var argumentValues = new[] { "with spaces", "quote\" --option", @"C:\path with spaces\" };
    var childArgs = new List<string>();
    if (prefix.Length > 0) childArgs.Add(Assembly.GetExecutingAssembly().Location);
    childArgs.Add("--child");
    childArgs.Add("arguments");
    childArgs.AddRange(argumentValues);
    var argsResult = await executor.RunArgumentsAsync(executable, childArgs, simulate: false);
    var received = System.Text.Json.JsonSerializer.Deserialize<string[]>(argsResult.StandardOutput.Trim());
    Require(argsResult.Success && received is not null && received.SequenceEqual(argumentValues), "argument boundaries not preserved");

    var large = await executor.RunAsync(executable, Child("large"), simulate: false);
    Require(large.Success && large.StandardOutput.Length < 270000 && large.StandardOutput.Contains("truncada"), "large output not capped or process blocked");

    var marker = Path.Combine(testDirectory, "should-not-exist.txt");
    var simulation = await executor.RunAsync(executable, Child($"write \"{marker}\""), simulate: true);
    Require(simulation.IsSimulated && !simulation.Success && !File.Exists(marker), "simulation executed or reported success");
    var realRead = await executor.RunAsync(executable, Child("exit"), simulate: false);
    Require(!realRead.IsSimulated && realRead.ExitCode == 7, "read-only command incorrectly simulated");

    var watch = Stopwatch.StartNew();
    try
    {
        await timeoutExecutor.RunAsync(executable, Child("wait"), simulate: false);
        throw new Exception("timeout did not trigger");
    }
    catch (TimeoutException) { }
    Require(watch.Elapsed < TimeSpan.FromSeconds(15), "timeout did not bound a live stdout pipe");

    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
    try
    {
        await executor.RunAsync(executable, Child("wait"), cancellationToken: cancellation.Token, simulate: false);
        throw new Exception("cancellation did not trigger");
    }
    catch (OperationCanceledException) { }

    SoftwarePackagePolicy.Validate(new SoftwareDefinition { Id = "7zip.7zip", Source = "winget", Arguments = "--silent" });
    SoftwarePackagePolicy.Validate(new SoftwareDefinition { Id = "CCleaner.CCleaner", Source = "chocolatey", Arguments = "-y" });
    foreach (var package in new[]
    {
        new SoftwareDefinition { Id = "x\" --override evil", Source = "winget" },
        new SoftwareDefinition { Id = "ok", Source = "winget", Arguments = "--override evil" },
        new SoftwareDefinition { Id = "ok", Source = "unknown" }
    })
    {
        var rejected = false;
        try { SoftwarePackagePolicy.Validate(package); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "unsafe package accepted");
    }
    Require(OperationPolicy.Evaluate(true, false, true, false, true) == OperationDecision.AdministratorRequired, "missing administrator gate");
    Require(OperationPolicy.Evaluate(true, false, false, true, true) == OperationDecision.BlockedPendingReview, "unreviewed operation allowed");
    Require(OperationPolicy.Evaluate(true, false, true, true, false) == OperationDecision.AuditLogRequired, "missing persistent log allowed");
    Require(OperationPolicy.Evaluate(true, false, true, true, true) == OperationDecision.Allowed, "reviewed operation unexpectedly blocked");
    Require(OperationPolicy.Evaluate(true, true, false, false, false) == OperationDecision.Allowed, "simulation should not require elevation");
    Require(OperationPolicy.Evaluate(false, false, false, false, false) == OperationDecision.Allowed, "read action unexpectedly blocked");

    Require(DiagnosticService.EvaluateSystemDisk(100, 4).Status == DiagnosticStatus.Error, "critical disk not flagged");
    Require(DiagnosticService.EvaluateSystemDisk(100, 5).Status == DiagnosticStatus.Warning, "warning disk not flagged");
    Require(DiagnosticService.EvaluateSystemDisk(100, 10).Status == DiagnosticStatus.Ok, "healthy disk not reported");
    Require(DiagnosticService.EvaluateSystemDisk(0, 0).Status == DiagnosticStatus.Unavailable, "unknown disk reported healthy");
    var report = await new DiagnosticService().AnalyzeAsync();
    Require(report.Results.Any(r => r.Status == DiagnosticStatus.Unavailable), "unimplemented checks omitted");
    Require(report.ToPlainText().Contains("No disponible"), "report lacks unavailable checks");
    logger.Info("private 192.168.0.1");
    Require(logger.RecentEvents.Last().Contains("[IP]"), "IP address not redacted from log");
    Console.WriteLine("Safety tests passed.");
    return 0;
}
finally
{
    try { Directory.Delete(testDirectory, recursive: true); } catch { }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
