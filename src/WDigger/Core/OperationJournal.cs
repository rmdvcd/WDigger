using System.Text.Json;

namespace WDigger.Core;

/// <summary>
/// Stores an atomic, redacted lifecycle record for operations that can modify
/// Windows. Interrupted records remain visible on the next launch.
/// </summary>
public sealed class OperationJournal
{
    private const int MaxCompletedRecords = 100;
    private readonly AppLogger _logger;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public OperationJournal(string directory, AppLogger logger)
    {
        _logger = logger;
        DirectoryPath = directory;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            IsPersistent = true;
            TrimCompletedRecords();
        }
        catch (Exception exception)
        {
            IsPersistent = false;
            _logger.Warn($"Operation journal is unavailable: {exception.Message}");
        }
    }

    public string DirectoryPath { get; }
    public bool IsPersistent { get; private set; }

    public OperationSession Begin(string actionTitle, OperationRisk risk, bool simulation, bool elevated)
    {
        var record = new OperationRecord
        {
            SchemaVersion = 1,
            OperationId = Guid.NewGuid(),
            ActionTitle = AppLogger.RedactForStorage(actionTitle),
            Risk = risk,
            IsSimulation = simulation,
            IsElevated = elevated,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = OperationStatus.Running,
            Summary = "Operación iniciada."
        };
        var session = new OperationSession(this, record, _logger.BeginOperation(record.OperationId));
        session.Save();
        return session;
    }

    public IReadOnlyList<OperationRecord> GetIncompleteRecords()
        => GetRecords().Where(record => record.Status == OperationStatus.Running).ToArray();

    public IReadOnlyList<OperationRecord> GetRecords(int maximum = 100)
    {
        if (!IsPersistent || maximum <= 0) return Array.Empty<OperationRecord>();

        try
        {
            return Directory.EnumerateFiles(DirectoryPath, "*.json")
                .Select(Read)
                .Where(record => record is not null)
                .Cast<OperationRecord>()
                .OrderByDescending(record => record.StartedAtUtc)
                .Take(maximum)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.Warn($"Could not read operation journal: {exception.Message}");
            return Array.Empty<OperationRecord>();
        }
    }

    private OperationRecord? Read(string path)
    {
        try
        {
            var record = JsonSerializer.Deserialize<OperationRecord>(File.ReadAllText(path), _json);
            if (record is null || record.OperationId == Guid.Empty || record.SchemaVersion is < 1 or > 1)
            {
                throw new JsonException("El esquema del registro de operación no es compatible.");
            }
            return record;
        }
        catch (Exception exception)
        {
            _logger.Warn($"Could not parse operation record '{Path.GetFileName(path)}': {exception.Message}");
            return null;
        }
    }

    internal bool Save(OperationRecord record)
    {
        if (!IsPersistent) return false;

        lock (_lock)
        {
            try
            {
                var destination = Path.Combine(DirectoryPath, $"{record.StartedAtUtc:yyyyMMdd_HHmmss}_{record.OperationId:N}.json");
                var temporary = destination + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(record, _json));
                File.Move(temporary, destination, overwrite: true);
                return true;
            }
            catch (Exception exception)
            {
                IsPersistent = false;
                _logger.Error($"Could not persist operation journal '{record.OperationId:N}': {exception.Message}");
                return false;
            }
        }
    }

    private void TrimCompletedRecords()
    {
        try
        {
            var completed = Directory.EnumerateFiles(DirectoryPath, "*.json")
                .Select(path => new { Path = path, Record = Read(path) })
                .Where(item => item.Record is not null && item.Record.Status != OperationStatus.Running)
                .OrderByDescending(item => File.GetLastWriteTimeUtc(item.Path))
                .Skip(MaxCompletedRecords)
                .ToArray();
            foreach (var item in completed)
            {
                try { File.Delete(item.Path); } catch { }
            }
        }
        catch
        {
            // Retention must not make the journal unavailable.
        }
    }

    internal void OnCompleted() => TrimCompletedRecords();
}

/// <summary>Mutable lifecycle that persists after every state transition.</summary>
public sealed class OperationSession : IDisposable
{
    private readonly OperationJournal _journal;
    private IDisposable? _logScope;
    private bool _completed;

    internal OperationSession(OperationJournal journal, OperationRecord record, IDisposable logScope)
    {
        _journal = journal;
        Record = record;
        _logScope = logScope;
    }

    public OperationRecord Record { get; }
    public bool IsPersistent => _journal.IsPersistent;
    public bool IsCompleted => _completed;

    public void StartStep(string name, string summary)
    {
        Record.Steps.Add(new OperationStepRecord
        {
            Name = AppLogger.RedactForStorage(name),
            Summary = AppLogger.RedactForStorage(summary),
            StartedAtUtc = DateTimeOffset.UtcNow,
            Status = OperationStatus.Running
        });
        Save();
    }

    public void CompleteLatestStep(OperationStatus status, string summary, int? exitCode = null)
    {
        var step = Record.Steps.LastOrDefault(item => item.CompletedAtUtc is null);
        if (step is null)
        {
            StartStep("unspecified", summary);
            step = Record.Steps[^1];
        }
        step.Status = status;
        step.Summary = AppLogger.RedactForStorage(summary);
        step.ExitCode = exitCode;
        step.CompletedAtUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public void SetRestorePoint(bool created, string? reference)
    {
        Record.RestorePointCreated = created;
        Record.RestorePointReference = reference is null ? null : AppLogger.RedactForStorage(reference);
        Save();
    }

    public void Complete(OperationOutcome outcome)
    {
        if (_completed) return;
        var unfinishedStatus = outcome.Status == OperationStatus.Cancelled ? OperationStatus.Cancelled
            : outcome.Status == OperationStatus.Failed ? OperationStatus.Failed
            : OperationStatus.Partial;
        foreach (var step in Record.Steps.Where(item => item.CompletedAtUtc is null))
        {
            step.Status = unfinishedStatus;
            step.Summary = "El paso no terminó antes de finalizar la operación.";
            step.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        Record.Status = outcome.Status;
        Record.Summary = AppLogger.RedactForStorage(outcome.Summary);
        Record.RestartRequired = outcome.RestartRequired;
        Record.ManualAction = outcome.ManualAction is null ? null : AppLogger.RedactForStorage(outcome.ManualAction);
        Record.CompletedAtUtc = DateTimeOffset.UtcNow;
        _completed = true;
        Save();
        _journal.OnCompleted();
        Dispose();
    }

    internal bool Save() => _journal.Save(Record);

    public void Dispose()
    {
        var scope = Interlocked.Exchange(ref _logScope, null);
        scope?.Dispose();
    }
}
