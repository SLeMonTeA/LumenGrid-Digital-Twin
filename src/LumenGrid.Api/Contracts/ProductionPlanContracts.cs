using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record ErpProductionPlanRequest(
    string EventKey,
    string EventType,
    string OrderNo,
    string ProductSku,
    string ProductName,
    string Status,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    decimal ReportedGoodQuantity,
    decimal ReportedDefectQuantity,
    DateTimeOffset RequiredAt,
    DateTimeOffset? PlannedStartAt = null,
    DateTimeOffset? PlannedEndAt = null,
    IReadOnlyList<string>? WorkCenters = null,
    DateTimeOffset? OccurredAt = null);

public sealed record ProductionPlanSnapshotResponse(
    string OrderNo,
    string ProductSku,
    string ProductName,
    string Status,
    decimal PlannedQuantity,
    decimal CompletedQuantity,
    decimal ReportedGoodQuantity,
    decimal ReportedDefectQuantity,
    DateTimeOffset RequiredAt,
    DateTimeOffset? PlannedStartAt,
    DateTimeOffset? PlannedEndAt,
    IReadOnlyList<string> WorkCenters,
    string LastEventKey,
    DateTimeOffset LastEventAt,
    DateTimeOffset SyncedAt,
    bool IsActive)
{
    public static ProductionPlanSnapshotResponse From(ProductionPlanSnapshot plan) => new(
        plan.OrderNo,
        plan.ProductSku,
        plan.ProductName,
        plan.Status,
        plan.PlannedQuantity,
        plan.CompletedQuantity,
        plan.ReportedGoodQuantity,
        plan.ReportedDefectQuantity,
        plan.RequiredAt,
        plan.PlannedStartAt,
        plan.PlannedEndAt,
        System.Text.Json.JsonSerializer.Deserialize<List<string>>(plan.WorkCentersJson) ?? [],
        plan.LastEventKey,
        plan.LastEventAt,
        plan.SyncedAt,
        plan.IsActive);
}
