using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record MesEquipmentNotificationRequest(
    string EventKey,
    string EventId,
    string EventType,
    string DeviceCode,
    string WorkCenter,
    string EquipmentStatus,
    bool IsOnline,
    bool IsAvailable,
    bool CanReceiveProduction,
    DateTimeOffset OccurredAt,
    string Summary,
    string Payload);

public sealed record MesNotificationDeliveryResponse(
    long Id,
    string EventKey,
    string EventType,
    string DeviceCode,
    string WorkCenter,
    string EquipmentStatus,
    bool IsAvailable,
    MesNotificationDeliveryStatus Status,
    int AttemptCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? SentAt)
{
    public static MesNotificationDeliveryResponse From(MesEquipmentNotificationDelivery delivery) => new(
        delivery.Id,
        delivery.EventKey,
        delivery.EventType,
        delivery.DeviceCode,
        delivery.WorkCenter,
        delivery.EquipmentStatus,
        delivery.IsAvailable,
        delivery.Status,
        delivery.AttemptCount,
        delivery.LastError,
        delivery.CreatedAt,
        delivery.NextAttemptAt,
        delivery.SentAt);
}
