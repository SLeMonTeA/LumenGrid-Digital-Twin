using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record EquipmentEventResponse(
    long Id,
    string EventId,
    string DeviceCode,
    string WorkCenter,
    string ProductionOrderNo,
    EquipmentEventType EventType,
    DateTimeOffset OccurredAt,
    string Source,
    string Summary,
    string Payload)
{
    public static EquipmentEventResponse From(EquipmentEvent record) => new(
        record.Id,
        record.EventId,
        record.DeviceCode,
        record.WorkCenter,
        record.ProductionOrderNo,
        record.EventType,
        record.OccurredAt,
        record.Source,
        record.Summary,
        record.Payload);
}

public sealed record EquipmentHistoryReplayResponse(
    string DeviceCode,
    DateTimeOffset ReplayAt,
    bool HasSnapshot,
    EquipmentSnapshotResponse? Snapshot,
    long EventsApplied,
    string? SourceEventId,
    EquipmentEventType? SourceEventType,
    DateTimeOffset? SourceEventAt);

public sealed record EquipmentEventPageResponse(
    IReadOnlyList<EquipmentEventResponse> Items,
    string? NextCursor,
    int PageSize);
