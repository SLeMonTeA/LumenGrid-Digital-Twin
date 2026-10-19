using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record MesEquipmentAvailabilityItem(
    string DeviceCode,
    string Name,
    string WorkCenter,
    EquipmentStatus Status,
    bool IsOnline,
    bool IsAvailable,
    bool CanReceiveProduction,
    decimal RatedCapacityPerHour,
    decimal CurrentCapacityPerHour,
    decimal TargetCapacityPerHour,
    string CapacityTargetSource,
    IReadOnlyList<string> MatchedProductionOrderNos,
    string ActiveWorkOrderNo,
    string ActiveOperationCode,
    string ActiveOperationStatus,
    int ActiveAlertCount,
    IReadOnlyList<string> ActiveAlertCodes,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset SnapshotAt,
    DateTimeOffset? EstimatedRecoveryAt);

public sealed record MesEquipmentAvailabilityResponse(
    DateTimeOffset GeneratedAt,
    string WorkCenter,
    IReadOnlyList<MesEquipmentAvailabilityItem> Items,
    decimal TotalRatedCapacityPerHour,
    decimal AvailableCapacityPerHour);

public sealed record MesEquipmentEventBatchResponse(
    long AfterId,
    long? NextAfterId,
    bool HasMore,
    IReadOnlyList<EquipmentEventResponse> Items);

public sealed record EquipmentEventSummaryResponse(
    DateTimeOffset GeneratedAt,
    DateTimeOffset? OldestEventAt,
    DateTimeOffset? NewestEventAt,
    long TotalEvents,
    IReadOnlyDictionary<string, long> CountsByType);

public sealed record MesExecutionBindingRequest(
    string EventKey,
    string EventType,
    string DeviceCode,
    string WorkOrderNo,
    string OperationCode,
    string OperationName,
    string WorkCenter,
    string? Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? OccurredAt,
    decimal? ReportedGoodQuantity,
    decimal? ReportedDefectQuantity);

public sealed record MesExecutionBindingResponse(
    long Id,
    string EventKey,
    string DeviceCode,
    string WorkCenter,
    string WorkOrderNo,
    string OperationCode,
    string OperationName,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? PausedAt,
    DateTimeOffset? CompletedAt,
    decimal ReportedGoodQuantity,
    decimal ReportedDefectQuantity,
    DateTimeOffset LastEventAt,
    bool IsActive);

public sealed record MesExecutionBindingWriteResponse(
    bool Duplicate,
    MesExecutionBindingResponse Binding);
