namespace WDigger.Core;

public enum OperationDecision
{
    Allowed,
    BlockedPendingReview,
    AdministratorRequired,
    AuditLogRequired,
    OperationJournalRequired
}

/// <summary>Fail-closed gate for actions which can modify Windows.</summary>
public static class OperationPolicy
{
    public static OperationDecision Evaluate(bool changesSystem, bool simulation,
        bool reviewedForElevatedSession, bool isAdministrator, bool hasPersistentLog,
        bool hasPersistentJournal = true)
    {
        if (!changesSystem || simulation) return OperationDecision.Allowed;
        if (!reviewedForElevatedSession) return OperationDecision.BlockedPendingReview;
        if (!isAdministrator) return OperationDecision.AdministratorRequired;
        if (!hasPersistentLog) return OperationDecision.AuditLogRequired;
        if (!hasPersistentJournal) return OperationDecision.OperationJournalRequired;
        return OperationDecision.Allowed;
    }
}
