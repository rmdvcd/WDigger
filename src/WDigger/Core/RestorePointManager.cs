using System.Management;

namespace WDigger.Core;

/// <summary>
/// Creates Windows System Restore points through the WMI SystemRestore class
/// (\\.\root\default:SystemRestore) before any tweak or major repair.
/// </summary>
public sealed class RestorePointManager
{
    private readonly AppLogger _logger;

    // SystemRestore restore point types (see WMI docs).
    private const int RestorePointTypeApplicationInstall = 12;
    private const int EventTypeBeginNestedSystemChange = 100;

    public RestorePointManager(AppLogger logger) => _logger = logger;

    /// <summary>
    /// Creates a restore point. Returns true on success. Failure is logged and
    /// reported but never fatal: the caller decides whether to proceed.
    /// </summary>
    public bool Create(string description)
    {
        try
        {
            _logger.Info($"Creating system restore point: {description}");

            var scope = new ManagementScope(@"\\.\root\default");
            using var classObj = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);

            var inParams = classObj.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = description ?? "WDigger system change";
            inParams["RestorePointType"] = RestorePointTypeApplicationInstall;
            inParams["EventType"] = EventTypeBeginNestedSystemChange;

            using var outParams = (ManagementBaseObject)classObj.InvokeMethod("CreateRestorePoint", inParams, null)!;
            var returnValue = Convert.ToInt32(outParams["ReturnValue"]);
            var sequenceNumber = outParams["SequenceNumber"];

            if (returnValue == 0)
            {
                _logger.Info($"Restore point created successfully (sequence {sequenceNumber}).");
                return true;
            }

            _logger.Warn($"Restore point creation returned code {returnValue}.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create restore point: {ex.Message}");
            return false;
        }
    }
}
