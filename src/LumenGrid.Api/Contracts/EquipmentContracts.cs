using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record EquipmentSnapshotResponse(
    string DeviceCode,
    string Name,
    string EquipmentType,
    string Factory,
    string Workshop,
    string WorkCenter,
    decimal RatedCapacityPerHour,
    EquipmentStatus Status,
    bool CanReceiveProduction,
    bool SimulationEnabled,
    SimulationScenario Scenario,
    DateTimeOffset? LastHeartbeat,
    DateTimeOffset SnapshotAt,
    decimal TemperatureCelsius,
    decimal CurrentAmps,
    decimal VibrationMillimetersPerSecond,
    decimal ProducedQuantity,
    decimal GoodQuantity,
    decimal DefectQuantity,
    decimal CycleSeconds,
    string FaultCode)
{
    public static EquipmentSnapshotResponse From(Equipment equipment) => new(
        equipment.DeviceCode,
        equipment.Name,
        equipment.EquipmentType,
        equipment.Factory,
        equipment.Workshop,
        equipment.WorkCenter,
        equipment.RatedCapacityPerHour,
        equipment.Status,
        equipment.CanReceiveProduction,
        equipment.SimulationEnabled,
        equipment.Scenario,
        equipment.LastHeartbeat,
        equipment.SnapshotAt,
        equipment.TemperatureCelsius,
        equipment.CurrentAmps,
        equipment.VibrationMillimetersPerSecond,
        equipment.ProducedQuantity,
        equipment.GoodQuantity,
        equipment.DefectQuantity,
        equipment.CycleSeconds,
        equipment.FaultCode);
}

public sealed record EquipmentDashboardSummary(
    int Total,
    int Running,
    int Stopped,
    int Alarm,
    decimal CapacityLossPerHour);

public sealed record AlertRuleRequest(
    string RuleCode,
    string Name,
    AlertMetric Metric,
    AlertComparison Comparison,
    decimal Threshold,
    int WindowSeconds,
    int DurationSeconds,
    int ConsecutiveSamples,
    AlertSeverity Severity,
    bool Enabled,
    string WorkCenter = "",
    string Description = "");

public sealed record SetAlertRuleEnabledRequest(bool Enabled);

public sealed record AlertActionRequest(string Operator = "operator-01", string Reason = "");

public sealed record AlertRuleResponse(
    string RuleCode,
    string Name,
    AlertMetric Metric,
    AlertComparison Comparison,
    decimal Threshold,
    int WindowSeconds,
    int DurationSeconds,
    int ConsecutiveSamples,
    AlertSeverity Severity,
    bool Enabled,
    string WorkCenter,
    string Description)
{
    public static AlertRuleResponse From(AlertRule rule) => new(
        rule.RuleCode,
        rule.Name,
        rule.Metric,
        rule.Comparison,
        rule.Threshold,
        rule.WindowSeconds,
        rule.DurationSeconds,
        rule.ConsecutiveSamples,
        rule.Severity,
        rule.Enabled,
        rule.WorkCenter,
        rule.Description);
}

public sealed record EquipmentAlertResponse(
    string AlertCode,
    string RuleCode,
    string DeviceCode,
    string WorkCenter,
    AlertMetric Metric,
    AlertSeverity Severity,
    AlertStatus Status,
    decimal CurrentValue,
    decimal Threshold,
    DateTimeOffset TriggeredAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string Handler,
    string ClosedReason,
    bool IsActive)
{
    public static EquipmentAlertResponse From(EquipmentAlert alert) => new(
        alert.AlertCode,
        alert.RuleCode,
        alert.DeviceCode,
        alert.WorkCenter,
        alert.Metric,
        alert.Severity,
        alert.Status,
        alert.CurrentValue,
        alert.Threshold,
        alert.TriggeredAt,
        alert.LastObservedAt,
        alert.AcknowledgedAt,
        alert.ResolvedAt,
        alert.Handler,
        alert.ClosedReason,
        alert.IsActive);
}

public sealed record CreateMaintenanceTaskRequest(
    string DeviceCode,
    string AlertCode = "",
    string Symptom = "",
    string Technician = "");

public sealed record CompleteMaintenanceTaskRequest(
    string Cause = "",
    string ReplacedPart = "",
    string Result = "",
    string Technician = "",
    bool RestoreEquipment = true);

public sealed record MaintenanceTaskResponse(
    string TaskNo,
    string DeviceCode,
    string AlertCode,
    string Symptom,
    string Cause,
    string Technician,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string ReplacedPart,
    string Result,
    MaintenanceStatus Status,
    bool RestoreEquipment)
{
    public static MaintenanceTaskResponse From(MaintenanceTask task) => new(
        task.TaskNo,
        task.DeviceCode,
        task.AlertCode,
        task.Symptom,
        task.Cause,
        task.Technician,
        task.CreatedAt,
        task.StartedAt,
        task.CompletedAt,
        task.ReplacedPart,
        task.Result,
        task.Status,
        task.RestoreEquipment);
}
