namespace LumenGrid.Api.Domain;

public enum ErpPlanSyncStatus
{
    Succeeded,
    Failed,
    Skipped
}

public sealed class ErpPlanSyncCursor
{
    public int Id { get; set; }
    public string StreamName { get; set; } = string.Empty;
    public long LastEventId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ErpPlanSyncLog
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public ErpPlanSyncStatus Status { get; set; } = ErpPlanSyncStatus.Succeeded;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
