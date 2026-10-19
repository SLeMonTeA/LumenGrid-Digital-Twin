using System.Text.Json;
using System.Text.Json.Serialization;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;

namespace LumenGrid.Api.Simulation;

/// <summary>
/// 阶段三的轻量事件追加器。事件和当前状态在同一个 DbContext 中提交，暂不引入消息中间件。
/// </summary>
public sealed class EquipmentEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public EquipmentEvent AppendEquipment(
        LumenGridDbContext db,
        Equipment equipment,
        EquipmentEventType eventType,
        DateTimeOffset occurredAt,
        string source,
        string summary,
        string? productionOrderNo = null,
        object? details = null)
    {
        var payload = new EquipmentEventPayload(
            EquipmentSnapshotResponse.From(equipment),
            details is null ? null : JsonSerializer.SerializeToElement(details, JsonOptions));

        var record = new EquipmentEvent
        {
            DeviceCode = equipment.DeviceCode,
            WorkCenter = equipment.WorkCenter,
            ProductionOrderNo = productionOrderNo ?? string.Empty,
            EventType = eventType,
            OccurredAt = occurredAt,
            Source = source,
            Summary = summary,
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        db.EquipmentEvents.Add(record);
        if (NeedsMesNotification(eventType))
        {
            db.MesEquipmentNotificationDeliveries.Add(new MesEquipmentNotificationDelivery
            {
                EventKey = $"LG-EQUIPMENT-{record.EventId}",
                EventId = record.EventId,
                EventType = eventType.ToString(),
                DeviceCode = equipment.DeviceCode,
                WorkCenter = equipment.WorkCenter,
                EquipmentStatus = equipment.Status.ToString(),
                IsOnline = equipment.LastHeartbeat is not null && occurredAt - equipment.LastHeartbeat.Value <= TimeSpan.FromSeconds(60),
                IsAvailable = equipment.CanReceiveProduction && equipment.Status is EquipmentStatus.Running or EquipmentStatus.Degraded,
                CanReceiveProduction = equipment.CanReceiveProduction,
                OccurredAt = occurredAt,
                Summary = summary,
                Payload = record.Payload
            });
        }
        return record;
    }

    public EquipmentEvent AppendAlert(
        LumenGridDbContext db,
        EquipmentAlert alert,
        EquipmentEventType eventType,
        DateTimeOffset occurredAt,
        string source,
        string summary,
        Equipment? equipment = null)
    {
        var details = new
        {
            alert.AlertCode,
            alert.RuleCode,
            alert.Status,
            alert.CurrentValue,
            alert.Threshold,
            alert.IsActive
        };
        var payload = equipment is null
            ? JsonSerializer.Serialize(details, JsonOptions)
            : JsonSerializer.Serialize(new EquipmentEventPayload(EquipmentSnapshotResponse.From(equipment), JsonSerializer.SerializeToElement(details, JsonOptions)), JsonOptions);

        var record = new EquipmentEvent
        {
            DeviceCode = alert.DeviceCode,
            WorkCenter = alert.WorkCenter,
            EventType = eventType,
            OccurredAt = occurredAt,
            Source = source,
            Summary = summary,
            Payload = payload
        };
        db.EquipmentEvents.Add(record);
        return record;
    }

    public EquipmentEvent AppendMaintenance(
        LumenGridDbContext db,
        MaintenanceTask task,
        Equipment equipment,
        EquipmentEventType eventType,
        DateTimeOffset occurredAt,
        string source,
        string summary)
    {
        var details = new { task.TaskNo, task.AlertCode, task.Status, task.Technician, task.Result };
        return AppendEquipment(db, equipment, eventType, occurredAt, source, summary, details: details);
    }

    public EquipmentEvent AppendRule(
        LumenGridDbContext db,
        AlertRule rule,
        DateTimeOffset occurredAt,
        string source,
        string summary)
    {
        var record = new EquipmentEvent
        {
            EventType = EquipmentEventType.RuleChanged,
            OccurredAt = occurredAt,
            Source = source,
            Summary = summary,
            Payload = JsonSerializer.Serialize(rule, JsonOptions)
        };
        db.EquipmentEvents.Add(record);
        return record;
    }

    public EquipmentEvent AppendPlan(
        LumenGridDbContext db,
        ProductionPlanSnapshot plan,
        string workCenter,
        EquipmentEventType eventType,
        DateTimeOffset occurredAt,
        string sourceEventType)
    {
        var payload = new
        {
            plan.OrderNo,
            plan.ProductSku,
            plan.ProductName,
            plan.Status,
            plan.PlannedQuantity,
            plan.CompletedQuantity,
            plan.RequiredAt,
            plan.PlannedStartAt,
            plan.PlannedEndAt,
            plan.WorkCentersJson,
            plan.IsActive,
            sourceEventType
        };
        var record = new EquipmentEvent
        {
            ProductionOrderNo = plan.OrderNo,
            WorkCenter = workCenter,
            EventType = eventType,
            OccurredAt = occurredAt,
            Source = "erp",
            Summary = $"ERP 生产计划 {sourceEventType}: {plan.OrderNo}",
            Payload = JsonSerializer.Serialize(payload, JsonOptions)
        };
        db.EquipmentEvents.Add(record);
        return record;
    }

    public EquipmentEvent AppendExecution(
        LumenGridDbContext db,
        ProductionExecutionBinding binding,
        Equipment equipment,
        EquipmentEventType eventType,
        DateTimeOffset occurredAt,
        string sourceEventType)
    {
        var details = new
        {
            binding.EventKey,
            binding.WorkOrderNo,
            binding.OperationCode,
            binding.OperationName,
            binding.Status,
            binding.StartedAt,
            binding.PausedAt,
            binding.CompletedAt,
            binding.ReportedGoodQuantity,
            binding.ReportedDefectQuantity,
            sourceEventType
        };
        return AppendEquipment(
            db,
            equipment,
            eventType,
            occurredAt,
            "mes",
            $"MES 执行绑定 {sourceEventType}: {binding.WorkOrderNo}/{binding.OperationCode}",
            binding.WorkOrderNo,
            details);
    }

    public static EquipmentSnapshotResponse? ReadSnapshot(EquipmentEvent record)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<EquipmentEventPayload>(record.Payload, JsonOptions);
            return payload?.Snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool NeedsMesNotification(EquipmentEventType eventType) => eventType is
        EquipmentEventType.DeviceOnline or
        EquipmentEventType.DeviceOffline or
        EquipmentEventType.DeviceStarted or
        EquipmentEventType.DeviceStopped or
        EquipmentEventType.DeviceRecovered or
        EquipmentEventType.StatusChanged or
        EquipmentEventType.MaintenanceStarted or
        EquipmentEventType.MaintenanceCompleted;

    public sealed record EquipmentEventPayload(
        EquipmentSnapshotResponse Snapshot,
        JsonElement? Details = null);
}
