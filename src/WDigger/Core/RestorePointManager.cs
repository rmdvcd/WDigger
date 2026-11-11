using System.Management;

namespace WDigger.Core;

public sealed record RestorePointResult(bool Created, string? Reference, string Summary);

/// <summary>Creates and verifies a new System Restore point; failure is never treated as success.</summary>
public sealed class RestorePointManager
{
    private readonly AppLogger _logger;
    private const int RestorePointTypeModifySettings = 12;
    private const int EventTypeBeginSystemChange = 100;

    public RestorePointManager(AppLogger logger) => _logger = logger;

    public RestorePointResult Create(string description)
    {
        try
        {
            // A unique description lets us distinguish a newly created point from a
            // previous point when Windows returns success but throttles creation.
            var uniqueDescription = $"WDigger {Guid.NewGuid():N} {description}";
            if (uniqueDescription.Length > 250) uniqueDescription = uniqueDescription[..250];
            var scope = new ManagementScope(@"\\.\root\default");
            using var classObj = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var inParams = classObj.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = uniqueDescription;
            inParams["RestorePointType"] = RestorePointTypeModifySettings;
            inParams["EventType"] = EventTypeBeginSystemChange;
            using var outParams = (ManagementBaseObject)classObj.InvokeMethod("CreateRestorePoint", inParams, null)!;
            var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
            if (returnValue != 0)
            {
                var summary = $"Windows devolvió el código {returnValue} al crear el punto de restauración.";
                _logger.Warn(summary);
                return new RestorePointResult(false, null, summary);
            }

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SequenceNumber, Description, CreationTime FROM SystemRestore"));
            using var points = searcher.Get();
            foreach (ManagementObject point in points)
            {
                using (point)
                {
                    if (!string.Equals(point["Description"]?.ToString(), uniqueDescription, StringComparison.Ordinal)) continue;
                    var sequence = point["SequenceNumber"]?.ToString() ?? "unknown";
                    var createdAt = point["CreationTime"]?.ToString() ?? "unknown";
                    var reference = $"sequence={sequence}; creationTime={createdAt}";
                    var summary = $"Punto de restauración verificado ({reference}).";
                    _logger.Info($"{summary}; description={uniqueDescription}");
                    return new RestorePointResult(true, reference, summary);
                }
            }

            const string notVerified = "Windows devolvió éxito, pero el punto de restauración no se pudo verificar (puede haberse aplicado la limitación de frecuencia).";
            _logger.Warn(notVerified);
            return new RestorePointResult(false, null, notVerified);
        }
        catch (Exception exception)
        {
            var summary = $"No se pudo crear o verificar el punto de restauración: {exception.Message}";
            _logger.Error(summary);
            return new RestorePointResult(false, null, summary);
        }
    }
}
