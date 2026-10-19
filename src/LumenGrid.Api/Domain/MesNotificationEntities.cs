namespace LumenGrid.Api.Domain;

public enum MesNotificationDeliveryStatus
{
    Pending,
    Sent,
    Failed
}

public sealed class MesEquipmentNotificationDelivery
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string EquipmentStatus { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsAvailable { get; set; }
    public bool CanReceiveProduction { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
    public MesNotificationDeliveryStatus Status { get; set; } = MesNotificationDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
}
