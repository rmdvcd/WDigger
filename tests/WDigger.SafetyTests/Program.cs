using System.Diagnostics;
using System.Reflection;
using WDigger.Core;
using WDigger.Diagnostics;
using Microsoft.Win32;
using System.Xml.Linq;

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
    var manifestPath = FindRepositoryFile("src", "WDigger", "app.manifest");
    var manifest = XDocument.Load(manifestPath);
    var executionLevel = manifest.Descendants().FirstOrDefault(element =>
        element.Name.LocalName == "requestedExecutionLevel")?.Attribute("level")?.Value;
    Require(string.Equals(executionLevel, "requireAdministrator", StringComparison.Ordinal),
        "application manifest must require an elevated administrator token");

    ValidateReviewedScriptsAndUi();

    using var logger = new AppLogger(testDirectory);
    var executable = Environment.ProcessPath ?? throw new Exception("No se pudo determinar el proceso de pruebas.");
    IReadOnlyList<string> Child(string mode, params string[] extra)
    {
        var values = new List<string>();
        if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            values.Add(Assembly.GetExecutingAssembly().Location);
        }
        values.Add("--child");
        values.Add(mode);
        values.AddRange(extra);
        return values;
    }
    var executor = new CommandExecutor(logger, () => true, TimeSpan.FromSeconds(20));
    var timeoutExecutor = new CommandExecutor(logger, () => true, TimeSpan.FromSeconds(2));

    var actual = await executor.RunForTestAsync(executable, Child("exit"), simulate: false);
    Require(!actual.Success && actual.ExitCode == 7, "nonzero exit was not preserved");
    Require(actual.StandardOutput.Contains("output-marker") && actual.StandardError.Contains("error-marker"), "stdout/stderr not captured separately");

    var rejectedTool = false;
    try
    {
        await executor.RunAsync(new CommandSpec((WindowsTool)999, Array.Empty<string>()), simulate: false);
    }
    catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
    {
        rejectedTool = true;
    }
    Require(rejectedTool, "unknown executable identifier was not rejected");

    var argumentValues = new[] { "with spaces", "quote\" --option", @"C:\path with spaces\" };
    var argsResult = await executor.RunForTestAsync(executable, Child("arguments", argumentValues), simulate: false);
    var received = System.Text.Json.JsonSerializer.Deserialize<string[]>(argsResult.StandardOutput.Trim());
    Require(argsResult.Success && received is not null && received.SequenceEqual(argumentValues), "argument boundaries not preserved");

    var large = await executor.RunForTestAsync(executable, Child("large"), simulate: false);
    Require(large.Success && large.StandardOutput.Length < 270000 && large.StandardOutput.Contains("truncada"), "large output not capped or process blocked");

    var marker = Path.Combine(testDirectory, "should-not-exist.txt");
    var simulation = await executor.RunForTestAsync(executable, Child("write", marker), simulate: true);
    Require(simulation.IsSimulated && !simulation.Success && !File.Exists(marker), "simulation executed or reported success");
    var realRead = await executor.RunForTestAsync(executable, Child("exit"), simulate: false);
    Require(!realRead.IsSimulated && realRead.ExitCode == 7, "read-only command incorrectly simulated");

    var watch = Stopwatch.StartNew();
    try
    {
        await timeoutExecutor.RunForTestAsync(executable, Child("wait"), simulate: false);
        throw new Exception("timeout did not trigger");
    }
    catch (TimeoutException) { }
    Require(watch.Elapsed < TimeSpan.FromSeconds(15), "timeout did not bound a live stdout pipe");

    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
    try
    {
        await executor.RunForTestAsync(executable, Child("wait"), cancellationToken: cancellation.Token, simulate: false);
        throw new Exception("cancellation did not trigger");
    }
    catch (OperationCanceledException) { }

    Require(AppxPackagePolicy.IsValidFullName("Microsoft.Example_1.2.3.4_x64__8wekyb3d8bbwe"), "valid Appx name rejected");
    foreach (var invalid in new[] { "", "x' ; Remove-Item", "x/../../bad", new string('a', 256) })
        Require(!AppxPackagePolicy.IsValidFullName(invalid), "unsafe Appx name accepted");
    Require(RegistryTweakService.Catalog.Count == 3, "unexpected registry tweak catalog");
    Require(RegistryTweakService.Resolve("disable_telemetry").Value == "AllowTelemetry", "registry tweak mapping changed");
    try { RegistryTweakService.Resolve("arbitrary"); throw new Exception("unknown registry tweak accepted"); }
    catch (InvalidOperationException) { }

    if (OperatingSystem.IsWindows())
    {
        var sandbox = @"SOFTWARE\WDigger-test-" + Guid.NewGuid().ToString("N");
        var tweak = RegistryTweakService.Resolve("disable_telemetry");
        using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            var service = new RegistryTweakService(RegistryHive.CurrentUser, sandbox + @"\undo", sandbox + @"\target\");
            var targetPath = sandbox + @"\target\" + tweak.Key;
            using (var key = root.CreateSubKey(targetPath, writable: true)) key.SetValue(tweak.Value, 2, RegistryValueKind.DWord);
            service.Apply(tweak.Id);
            Require(service.HasSnapshot(tweak.Id), "snapshot was not persisted");
            try { service.Apply(tweak.Id); throw new Exception("duplicate application accepted"); }
            catch (InvalidOperationException) { }
            using (var key = root.OpenSubKey(targetPath, writable: true)!)
            {
                Require((int)key.GetValue(tweak.Value)! == 0, "tweak was not applied");
                key.SetValue(tweak.Value, 3, RegistryValueKind.DWord);
            }
            try { service.Revert(tweak.Id); throw new Exception("external change was overwritten"); }
            catch (InvalidOperationException) { }
            Require(service.HasSnapshot(tweak.Id), "conflicted snapshot was deleted");
            using (var key = root.OpenSubKey(targetPath, writable: true)!) key.SetValue(tweak.Value, 0, RegistryValueKind.DWord);
            service.Revert(tweak.Id);
            using (var key = root.OpenSubKey(targetPath, writable: true)!)
            {
                Require((int)key.GetValue(tweak.Value)! == 2, "prior value was not restored");
                key.DeleteValue(tweak.Value);
            }
            service.Apply(tweak.Id);
            service.Revert(tweak.Id);
            using (var key = root.OpenSubKey(targetPath)!) Require(!key.GetValueNames().Contains(tweak.Value), "originally absent value was not removed");
            Require(!service.HasSnapshot(tweak.Id), "completed snapshot was not cleared");
            using (var key = root.OpenSubKey(targetPath, writable: true)!) key.SetValue(tweak.Value, "not a DWORD", RegistryValueKind.String);
            try { service.Apply(tweak.Id); throw new Exception("non-DWORD value accepted"); }
            catch (InvalidOperationException) { }
            Require(!service.HasSnapshot(tweak.Id), "snapshot created for unsupported value");
            using (var snapshot = root.CreateSubKey(sandbox + @"\undo\" + tweak.Id, writable: true)) snapshot.SetValue("Ready", 0, RegistryValueKind.DWord);
            try { service.Revert(tweak.Id); throw new Exception("incomplete snapshot accepted"); }
            catch (InvalidOperationException) { }
            Require(service.HasSnapshot(tweak.Id), "incomplete snapshot should be retained");
        }
        finally { root.DeleteSubKeyTree(sandbox, throwOnMissingSubKey: false); }
    }

    Require(OperationPolicy.Evaluate(true, false, true, false, true) == OperationDecision.AdministratorRequired, "missing administrator gate");
    Require(OperationPolicy.Evaluate(true, false, false, true, true) == OperationDecision.BlockedPendingReview, "unreviewed operation allowed");
    Require(OperationPolicy.Evaluate(true, false, true, true, false) == OperationDecision.AuditLogRequired, "missing persistent log allowed");
    Require(OperationPolicy.Evaluate(true, false, true, true, true) == OperationDecision.Allowed, "reviewed operation unexpectedly blocked");
    Require(OperationPolicy.Evaluate(true, true, false, false, false) == OperationDecision.Allowed, "simulation should not require elevation");
    Require(OperationPolicy.Evaluate(false, false, false, false, false) == OperationDecision.Allowed, "read action unexpectedly blocked");
    Require(OperationPolicy.Evaluate(true, false, true, true, true, false) == OperationDecision.OperationJournalRequired,
        "missing persistent journal allowed");

    var journal = new OperationJournal(Path.Combine(testDirectory, "operations"), logger);
    Require(journal.IsPersistent, "test journal was not created");
    using (var operation = journal.Begin("Operación de prueba", OperationRisk.High, simulation: false, elevated: true))
    {
        operation.StartStep("preflight", "Comprobación inicial");
        Require(journal.GetIncompleteRecords().Single().OperationId == operation.Record.OperationId, "running operation was not recoverable");
        operation.CompleteLatestStep(OperationStatus.Succeeded, "Comprobación terminada.");
        operation.StartStep("active-step", "Paso activo antes de terminar.");
        operation.Complete(OperationOutcome.Partial("Resultado parcial", "Revisar manualmente."));
    }
    var recorded = journal.GetRecords().Single();
    Require(recorded.Status == OperationStatus.Partial && recorded.CompletedAtUtc is not null,
        "terminal operation result was not persisted");
    Require(recorded.Steps[0].Status == OperationStatus.Succeeded, "completed operation step was not persisted");
    Require(recorded.Steps[1].Status == OperationStatus.Partial && recorded.Steps[1].CompletedAtUtc is not null,
        "unfinished operation step was not finalized as partial");
    Require(!Directory.EnumerateFiles(journal.DirectoryPath, "*.tmp").Any(), "journal left a temporary record behind");

    var interruptedDirectory = Path.Combine(testDirectory, "interrupted-operations");
    var interruptedJournal = new OperationJournal(interruptedDirectory, logger);
    using (var interrupted = interruptedJournal.Begin("Operación interrumpida", OperationRisk.High, simulation: false, elevated: true))
    {
        interrupted.StartStep("mutating-step", "Paso que quedará en ejecución.");
    }
    var reloadedJournal = new OperationJournal(interruptedDirectory, logger);
    var interruptedRecord = reloadedJournal.GetIncompleteRecords().Single();
    Require(interruptedRecord.Status == OperationStatus.Running && interruptedRecord.Steps.Single().Status == OperationStatus.Running,
        "interrupted operation was not recoverable after reopening the journal");

    File.WriteAllText(Path.Combine(journal.DirectoryPath, "corrupt.json"), "not-json");
    Require(journal.GetRecords().Count == 1, "corrupt journal record should be ignored");

    var temporaryRoot = Path.Combine(testDirectory, "temporary");
    Directory.CreateDirectory(Path.Combine(temporaryRoot, "nested"));
    File.WriteAllText(Path.Combine(temporaryRoot, "one.tmp"), "one");
    File.WriteAllText(Path.Combine(temporaryRoot, "nested", "two.tmp"), "two");
    var cleaner = new TemporaryFileCleaner();
    var preview = cleaner.Inspect(temporaryRoot);
    Require(preview.Files == 2 && preview.Directories == 1, "temporary cleanup preview did not enumerate safely");
    var cleanup = cleaner.Delete(temporaryRoot);
    Require(cleanup.DeletedFiles == 2 && Directory.Exists(temporaryRoot) && !Directory.EnumerateFileSystemEntries(temporaryRoot).Any(),
        "temporary cleanup should delete children but preserve its root");
    var cancelledRoot = Path.Combine(testDirectory, "temporary-cancelled");
    Directory.CreateDirectory(cancelledRoot);
    var cancelledFile = Path.Combine(cancelledRoot, "keep.tmp");
    File.WriteAllText(cancelledFile, "keep");
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try { cleaner.Delete(cancelledRoot, cancelled.Token); throw new Exception("temporary cleanup ignored cancellation"); }
        catch (OperationCanceledException) { }
    }
    Require(File.Exists(cancelledFile), "cancelled temporary cleanup deleted a file");

    Require(DiagnosticService.EvaluateSystemDisk(100, 4).Status == DiagnosticStatus.Error, "critical disk not flagged");
    Require(DiagnosticService.EvaluateSystemDisk(100, 5).Status == DiagnosticStatus.Warning, "warning disk not flagged");
    Require(DiagnosticService.EvaluateSystemDisk(100, 10).Status == DiagnosticStatus.Ok, "healthy disk not reported");
    Require(DiagnosticService.EvaluateSystemDisk(0, 0).Status == DiagnosticStatus.Unavailable, "unknown disk reported healthy");
    var report = await new DiagnosticService().AnalyzeAsync();
    Require(report.Results.Any(r => r.Status == DiagnosticStatus.Unavailable), "unimplemented checks omitted");
    Require(report.ToPlainText().Contains("No disponible"), "report lacks unavailable checks");
    var supportReport = SupportReportExporter.Create(report, journal.GetRecords());
    Require(supportReport.Contains(recorded.OperationId.ToString("N"), StringComparison.Ordinal),
        "support report does not correlate the operation identifier");
    logger.Info("private 192.168.0.1");
    Require(logger.RecentEvents.Last().Contains("[IP]"), "IP address not redacted from log");
    Console.WriteLine("Safety tests passed.");
    return 0;
}
finally
{
    try { Directory.Delete(testDirectory, recursive: true); } catch { }
}

static void ValidateReviewedScriptsAndUi()
{
    var projectPath = FindRepositoryFile("src", "WDigger", "WDigger.csproj");
    var project = XDocument.Load(projectPath);
    var scriptResource = project.Descendants().SingleOrDefault(element =>
        element.Name.LocalName == "EmbeddedResource" &&
        string.Equals(element.Attribute("Include")?.Value, @"PowerShell\Scripts\*.ps1", StringComparison.Ordinal));
    Require(scriptResource?.Attribute("LogicalName")?.Value == "WDigger.PowerShell.Scripts.%(Filename).ps1",
        "reviewed PowerShell scripts must be embedded with deterministic resource names");

    var expectedScripts = Enum.GetNames<PowerShellScriptId>().OrderBy(name => name).ToArray();
    var scriptsDirectory = Path.GetDirectoryName(FindRepositoryFile("src", "WDigger", "PowerShell", "Scripts", "AppxInventory.ps1"))!;
    var actualScripts = Directory.EnumerateFiles(scriptsDirectory, "*.ps1")
        .Select(Path.GetFileNameWithoutExtension).OrderBy(name => name).ToArray();
    Require(actualScripts.SequenceEqual(expectedScripts), "embedded PowerShell script files and identifiers differ");

    foreach (var name in expectedScripts)
    {
        var script = File.ReadAllText(Path.Combine(scriptsDirectory, name + ".ps1"));
        var isArgumentFree = name is nameof(PowerShellScriptId.AppxInventory) or nameof(PowerShellScriptId.ResetWindowsUpdate);
        Require(isArgumentFree ? script.Contains("param()", StringComparison.Ordinal) : script.Contains("param(", StringComparison.Ordinal),
            $"script {name} does not declare its expected parameter contract");
        foreach (var forbidden in new[] { "Invoke-Expression", "Start-Process", "cmd.exe", "powershell.exe", "-Command" })
        {
            Require(!script.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"reviewed script {name} contains forbidden execution primitive {forbidden}");
        }

        var identifier = Enum.Parse<PowerShellScriptId>(name);
        var arguments = isArgumentFree ? Array.Empty<string>() : new[] { "validation-value" };
        var specification = PowerShellScriptCatalog.Create(identifier, arguments);
        Require(specification.Tool == WindowsTool.PowerShell && specification.Arguments.Count == 7,
            $"catalog did not create the expected PowerShell specification for {name}");
        var expectedPrefix = new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand" };
        Require(specification.Arguments.Take(expectedPrefix.Length).SequenceEqual(expectedPrefix),
            $"catalog command-line prefix changed for {name}");
        var wrapper = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(specification.Arguments[^1]));
        Require(wrapper.Contains("Set-StrictMode -Version Latest", StringComparison.Ordinal) &&
                wrapper.Contains("[ScriptBlock]::Create", StringComparison.Ordinal),
            $"catalog wrapper contract changed for {name}");
        try { PowerShellScriptCatalog.Create(identifier, arguments.Append("unexpected").ToArray()); throw new Exception($"catalog accepted unexpected arguments for {name}"); }
        catch (ArgumentException) { }
    }

    var root = Directory.GetParent(Directory.GetParent(Directory.GetParent(projectPath)!.FullName)!.FullName)!.FullName;
    Require(!File.Exists(Path.Combine(root, "config", "tweaks.json")) && !File.Exists(Path.Combine(root, "config", "software.json")),
        "legacy editable privileged configuration is still present");
    var sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src", "WDigger"), "*.cs", SearchOption.AllDirectories).ToArray();
    foreach (var source in sourceFiles)
    {
        var code = File.ReadAllText(source);
        foreach (var forbidden in new[] { "ProcessStartInfo.Arguments", "cmd /c", "RunCommandAsync(\"", "RunPowerShellAsync($" })
        {
            Require(!code.Contains(forbidden, StringComparison.Ordinal),
                $"legacy command execution boundary found in {Path.GetFileName(source)}: {forbidden}");
        }
    }

    var shellXaml = File.ReadAllText(FindRepositoryFile("src", "WDigger", "UI", "ShellWindow.xaml"));
    var actionCardXaml = File.ReadAllText(FindRepositoryFile("src", "WDigger", "Views", "ActionCard.xaml"));
    var appXaml = File.ReadAllText(FindRepositoryFile("src", "WDigger", "App.xaml"));
    Require(shellXaml.Contains("StatusText\" AutomationProperties.LiveSetting=\"Polite\"", StringComparison.Ordinal),
        "operation status must remain a polite live region");
    Require(actionCardXaml.Contains("AutomationProperties.Name", StringComparison.Ordinal) &&
            actionCardXaml.Contains("AutomationProperties.HelpText", StringComparison.Ordinal),
        "action cards must expose an accessible name and help text");
    Require(appXaml.Contains("ResourceDictionary x:Key=\"HighContrast\"", StringComparison.Ordinal),
        "high contrast resource dictionary is missing");
}

static string FindRepositoryFile(params string[] parts)
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    throw new FileNotFoundException($"No se encontró el archivo de repositorio: {Path.Combine(parts)}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
