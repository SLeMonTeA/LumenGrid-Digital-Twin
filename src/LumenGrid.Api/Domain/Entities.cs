namespace LumenGrid.Api.Domain;

public enum EquipmentStatus
{
    Stopped,
    Starting,
    Running,
    Degraded,
    Faulted,
    Maintenance,
    Offline
}

public enum SimulationScenario
{
    Normal,
    HighTemperature,
    Vibration,
    Offline,
    CapacityDrop
}

public enum AlertMetric
{
    Temperature,
    Vibration,
    Heartbeat,
    Capacity
}

public enum AlertComparison
{
    GreaterThan,
    LessThan
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum AlertStatus
{
    New,
    Confirmed,
    Processing,
    Resolved,
    Closed
}

public enum MaintenanceStatus
{
    Created,
    InProgress,
    Completed,
    Cancelled
}

public enum ExecutionBindingStatus
{
    Bound,
    Running,
    Paused,
    Completed,
    Cancelled
}

public enum EquipmentEventType
{
    MetricSample,
    DeviceStarted,
    DeviceStopped,
    DeviceOnline,
    DeviceOffline,
    StatusChanged,
    ScenarioChanged,
    DeviceRecovered,
    DeviceReset,
    AlertTriggered,
    AlertAcknowledged,
    AlertProcessing,
    AlertResolved,
    AlertClosed,
    MaintenanceCreated,
    MaintenanceStarted,
    MaintenanceCompleted,
    RuleChanged,
    ProductionPlanPublished,
    ProductionPlanChanged,
    ProductionPlanCancelled,
    ProductionDeviceBound,
    ProductionOperationStarted,
    ProductionOperationPaused,
    ProductionOperationCompleted,
    ProductionOperationCancelled
}

public sealed class Equipment
{
    public int Id { get; set; }
    public string DeviceCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EquipmentType { get; set; } = string.Empty;
    public string Factory { get; set; } = string.Empty;
    public string Workshop { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public decimal RatedCapacityPerHour { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Stopped;
    public bool CanReceiveProduction { get; set; }
    public bool SimulationEnabled { get; set; }
    public SimulationScenario Scenario { get; set; } = SimulationScenario.Normal;
    public DateTimeOffset? LastHeartbeat { get; set; }
    public DateTimeOffset SnapshotAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal TemperatureCelsius { get; set; }
    public decimal CurrentAmps { get; set; }
    public decimal VibrationMillimetersPerSecond { get; set; }
    public decimal ProducedQuantity { get; set; }
    public decimal GoodQuantity { get; set; }
    public decimal DefectQuantity { get; set; }
    public decimal CycleSeconds { get; set; }
    public string FaultCode { get; set; } = string.Empty;
}

public sealed class AlertRule
{
    public int Id { get; set; }
    public string RuleCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AlertMetric Metric { get; set; }
    public AlertComparison Comparison { get; set; } = AlertComparison.GreaterThan;
    public decimal Threshold { get; set; }
    public int WindowSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public int ConsecutiveSamples { get; set; }
    public AlertSeverity Severity { get; set; } = AlertSeverity.Warning;
    public bool Enabled { get; set; } = true;
    public string WorkCenter { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class EquipmentAlert
{
    public long Id { get; set; }
    public string AlertCode { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleCode { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public AlertMetric Metric { get; set; }
    public AlertSeverity Severity { get; set; }
    public AlertStatus Status { get; set; } = AlertStatus.New;
    public decimal CurrentValue { get; set; }
    public decimal Threshold { get; set; }
    public DateTimeOffset TriggeredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastObservedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string Handler { get; set; } = string.Empty;
    public string ClosedReason { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class MaintenanceTask
{
    public long Id { get; set; }
    public string TaskNo { get; set; } = string.Empty;
    public string DeviceCode { get; set; } = string.Empty;
    public string AlertCode { get; set; } = string.Empty;
    public string Symptom { get; set; } = string.Empty;
    public string Cause { get; set; } = string.Empty;
    public string Technician { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ReplacedPart { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public MaintenanceStatus Status { get; set; } = MaintenanceStatus.Created;
    public bool RestoreEquipment { get; set; } = true;
}

public sealed class EquipmentEvent
{
    public long Id { get; set; }
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceCode { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string ProductionOrderNo { get; set; } = string.Empty;
    public EquipmentEventType EventType { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string Source { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Payload { get; set; } = "{}";
}

public sealed class ProductionPlanSnapshot
{
    public long Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string ProductSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public decimal CompletedQuantity { get; set; }
    public decimal ReportedGoodQuantity { get; set; }
    public decimal ReportedDefectQuantity { get; set; }
    public DateTimeOffset RequiredAt { get; set; }
    public DateTimeOffset? PlannedStartAt { get; set; }
    public DateTimeOffset? PlannedEndAt { get; set; }
    public string WorkCentersJson { get; set; } = "[]";
    public string LastEventKey { get; set; } = string.Empty;
    public DateTimeOffset LastEventAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
}

public sealed class ProductionExecutionBinding
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string ProcessedEventKeysJson { get; set; } = "[]";
    public string DeviceCode { get; set; } = string.Empty;
    public string WorkCenter { get; set; } = string.Empty;
    public string WorkOrderNo { get; set; } = string.Empty;
    public string OperationCode { get; set; } = string.Empty;
    public string OperationName { get; set; } = string.Empty;
    public ExecutionBindingStatus Status { get; set; } = ExecutionBindingStatus.Bound;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal ReportedGoodQuantity { get; set; }
    public decimal ReportedDefectQuantity { get; set; }
    public DateTimeOffset LastEventAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsActive { get; set; } = true;
}
