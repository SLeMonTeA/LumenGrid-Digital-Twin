using System.Text.Json.Serialization;

namespace LumenGrid.Api.Contracts;

public sealed record WarehouseFlowProductionOrderOperation(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("operationCode")] string OperationCode,
    [property: JsonPropertyName("operationName")] string OperationName,
    [property: JsonPropertyName("workCenter")] string WorkCenter,
    [property: JsonPropertyName("standardMinutes")] decimal StandardMinutes,
    [property: JsonPropertyName("requiresQuality")] bool RequiresQuality,
    [property: JsonPropertyName("reportedGoodQuantity")] decimal ReportedGoodQuantity,
    [property: JsonPropertyName("reportedDefectQuantity")] decimal ReportedDefectQuantity,
    [property: JsonPropertyName("status")] string Status);

public sealed record WarehouseFlowProductionOrderSnapshot(
    [property: JsonPropertyName("orderNo")] string OrderNo,
    [property: JsonPropertyName("productSku")] string ProductSku,
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("plannedQuantity")] decimal PlannedQuantity,
    [property: JsonPropertyName("completedQuantity")] decimal CompletedQuantity,
    [property: JsonPropertyName("reportedGoodQuantity")] decimal ReportedGoodQuantity,
    [property: JsonPropertyName("reportedDefectQuantity")] decimal ReportedDefectQuantity,
    [property: JsonPropertyName("qualityStatus")] string QualityStatus,
    [property: JsonPropertyName("qualityRemark")] string QualityRemark,
    [property: JsonPropertyName("requiredAt")] DateTimeOffset RequiredAt,
    [property: JsonPropertyName("operations")] IReadOnlyList<WarehouseFlowProductionOrderOperation> Operations);

public sealed record WarehouseFlowProductionOrderEvent(
    [property: JsonPropertyName("eventId")] long EventId,
    [property: JsonPropertyName("eventKey")] string EventKey,
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("aggregateNo")] string AggregateNo,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("snapshot")] WarehouseFlowProductionOrderSnapshot? Snapshot);

public sealed record ErpPlanSyncResult(
    int SnapshotFetched,
    int SnapshotApplied,
    int EventsFetched,
    int EventsApplied,
    int EventsSkipped,
    int EventsFailed,
    long LastEventId,
    DateTimeOffset CompletedAt);

public sealed record ErpPlanSyncStatusResponse(
    string StreamName,
    long LastEventId,
    DateTimeOffset? UpdatedAt,
    int Succeeded,
    int Failed,
    int Skipped,
    DateTimeOffset? LastFailureAt,
    string? LastError);
