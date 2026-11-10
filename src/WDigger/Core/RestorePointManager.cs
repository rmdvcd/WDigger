using System.Management;

namespace WDigger.Core;

/// <summary>Creates and verifies a new System Restore point; failure is never treated as success.</summary>
public sealed class RestorePointManager
{
    private readonly AppLogger _logger;
    private const int RestorePointTypeModifySettings = 12;
    private const int EventTypeBeginSystemChange = 100;

    public RestorePointManager(AppLogger logger) => _logger = logger;

    public bool Create(string description)
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
                _logger.Warn($"Restore point creation returned code {returnValue}.");
                return false;
            }

            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT SequenceNumber, Description, CreationTime FROM SystemRestore"));
            using var points = searcher.Get();
            foreach (ManagementObject point in points)
            {
                using (point)
                {
                    if (!string.Equals(point["Description"]?.ToString(), uniqueDescription, StringComparison.Ordinal)) continue;
                    _logger.Info($"Verified restore point sequence={point["SequenceNumber"]}; creationTime={point["CreationTime"]}; description={uniqueDescription}");
                    return true;
                }
            }
            _logger.Warn("Windows returned success but a new restore point could not be verified (it may have been throttled).");
            return false;
        }
        catch (Exception exception)
        {
            _logger.Error($"Failed to create or verify restore point: {exception.Message}");
            return false;
        }
    }
}
