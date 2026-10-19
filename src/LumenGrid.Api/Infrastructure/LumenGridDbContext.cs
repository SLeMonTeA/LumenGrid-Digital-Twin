using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Infrastructure;

public sealed class LumenGridDbContext(DbContextOptions<LumenGridDbContext> options) : DbContext(options)
{
    public DbSet<Equipment> Equipments => Set<Equipment>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<EquipmentAlert> EquipmentAlerts => Set<EquipmentAlert>();
    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();
    public DbSet<EquipmentEvent> EquipmentEvents => Set<EquipmentEvent>();
    public DbSet<ProductionPlanSnapshot> ProductionPlanSnapshots => Set<ProductionPlanSnapshot>();
    public DbSet<ProductionExecutionBinding> ProductionExecutionBindings => Set<ProductionExecutionBinding>();
    public DbSet<ErpPlanSyncCursor> ErpPlanSyncCursors => Set<ErpPlanSyncCursor>();
    public DbSet<ErpPlanSyncLog> ErpPlanSyncLogs => Set<ErpPlanSyncLog>();
    public DbSet<MesEquipmentNotificationDelivery> MesEquipmentNotificationDeliveries => Set<MesEquipmentNotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Equipment>().HasIndex(x => x.DeviceCode).IsUnique();
        modelBuilder.Entity<Equipment>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Equipment>().Property(x => x.Scenario).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Equipment>().Property(x => x.RatedCapacityPerHour).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.TemperatureCelsius).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.CurrentAmps).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.VibrationMillimetersPerSecond).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.ProducedQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.GoodQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.DefectQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<Equipment>().Property(x => x.CycleSeconds).HasPrecision(18, 3);
        modelBuilder.Entity<AlertRule>().HasIndex(x => x.RuleCode).IsUnique();
        modelBuilder.Entity<AlertRule>().Property(x => x.Metric).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<AlertRule>().Property(x => x.Comparison).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<AlertRule>().Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<AlertRule>().Property(x => x.Threshold).HasPrecision(18, 3);
        modelBuilder.Entity<EquipmentAlert>().HasIndex(x => x.AlertCode).IsUnique();
        modelBuilder.Entity<EquipmentAlert>().HasIndex(x => new { x.DeviceCode, x.IsActive });
        modelBuilder.Entity<EquipmentAlert>().Property(x => x.Metric).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<EquipmentAlert>().Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<EquipmentAlert>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<EquipmentAlert>().Property(x => x.CurrentValue).HasPrecision(18, 3);
        modelBuilder.Entity<EquipmentAlert>().Property(x => x.Threshold).HasPrecision(18, 3);
        modelBuilder.Entity<MaintenanceTask>().HasIndex(x => x.TaskNo).IsUnique();
        modelBuilder.Entity<MaintenanceTask>().HasIndex(x => new { x.DeviceCode, x.Status });
        modelBuilder.Entity<MaintenanceTask>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<EquipmentEvent>().HasIndex(x => x.EventId).IsUnique();
        modelBuilder.Entity<EquipmentEvent>().HasIndex(x => new { x.DeviceCode, x.OccurredAt, x.Id });
        modelBuilder.Entity<EquipmentEvent>().HasIndex(x => new { x.WorkCenter, x.OccurredAt });
        modelBuilder.Entity<EquipmentEvent>().HasIndex(x => new { x.ProductionOrderNo, x.OccurredAt });
        modelBuilder.Entity<EquipmentEvent>().Property(x => x.EventType).HasConversion<string>().HasMaxLength(40);
        modelBuilder.Entity<EquipmentEvent>().Property(x => x.Payload).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<ProductionPlanSnapshot>().HasIndex(x => x.OrderNo).IsUnique();
        modelBuilder.Entity<ProductionPlanSnapshot>().HasIndex(x => x.LastEventKey).IsUnique();
        modelBuilder.Entity<ProductionPlanSnapshot>().Property(x => x.PlannedQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ProductionPlanSnapshot>().Property(x => x.CompletedQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ProductionPlanSnapshot>().Property(x => x.ReportedGoodQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ProductionPlanSnapshot>().Property(x => x.ReportedDefectQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ProductionPlanSnapshot>().Property(x => x.WorkCentersJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<ProductionExecutionBinding>().HasIndex(x => x.EventKey).IsUnique();
        modelBuilder.Entity<ProductionExecutionBinding>().HasIndex(x => new { x.DeviceCode, x.IsActive });
        modelBuilder.Entity<ProductionExecutionBinding>().HasIndex(x => new { x.WorkOrderNo, x.OperationCode });
        modelBuilder.Entity<ProductionExecutionBinding>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<ProductionExecutionBinding>().Property(x => x.ProcessedEventKeysJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<ProductionExecutionBinding>().Property(x => x.ReportedGoodQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ProductionExecutionBinding>().Property(x => x.ReportedDefectQuantity).HasPrecision(18, 3);
        modelBuilder.Entity<ErpPlanSyncCursor>().HasIndex(x => x.StreamName).IsUnique();
        modelBuilder.Entity<ErpPlanSyncLog>().HasIndex(x => x.EventKey).IsUnique();
        modelBuilder.Entity<ErpPlanSyncLog>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<MesEquipmentNotificationDelivery>().HasIndex(x => x.EventKey).IsUnique();
        modelBuilder.Entity<MesEquipmentNotificationDelivery>().HasIndex(x => new { x.Status, x.NextAttemptAt });
        modelBuilder.Entity<MesEquipmentNotificationDelivery>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<MesEquipmentNotificationDelivery>().Property(x => x.Payload).HasColumnType("nvarchar(max)");
    }
}

public static class LumenGridDbInitializer
{
    public static async Task EnsureCreatedAndSeedAsync(LumenGridDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[AlertRules]', N'U') IS NULL
            BEGIN
                CREATE TABLE [AlertRules]
                (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AlertRules] PRIMARY KEY,
                    [RuleCode] nvarchar(100) NOT NULL,
                    [Name] nvarchar(200) NOT NULL,
                    [Metric] nvarchar(30) NOT NULL,
                    [Comparison] nvarchar(30) NOT NULL,
                    [Threshold] decimal(18,3) NOT NULL,
                    [WindowSeconds] int NOT NULL,
                    [DurationSeconds] int NOT NULL,
                    [ConsecutiveSamples] int NOT NULL,
                    [Severity] nvarchar(30) NOT NULL,
                    [Enabled] bit NOT NULL,
                    [WorkCenter] nvarchar(100) NOT NULL,
                    [Description] nvarchar(500) NOT NULL
                );
                CREATE UNIQUE INDEX [IX_AlertRules_RuleCode] ON [AlertRules] ([RuleCode]);
            END;
            IF OBJECT_ID(N'[EquipmentAlerts]', N'U') IS NULL
            BEGIN
                CREATE TABLE [EquipmentAlerts]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EquipmentAlerts] PRIMARY KEY,
                    [AlertCode] nvarchar(100) NOT NULL,
                    [RuleCode] nvarchar(100) NOT NULL,
                    [DeviceCode] nvarchar(100) NOT NULL,
                    [WorkCenter] nvarchar(100) NOT NULL,
                    [Metric] nvarchar(30) NOT NULL,
                    [Severity] nvarchar(30) NOT NULL,
                    [Status] nvarchar(30) NOT NULL,
                    [CurrentValue] decimal(18,3) NOT NULL,
                    [Threshold] decimal(18,3) NOT NULL,
                    [TriggeredAt] datetimeoffset NOT NULL,
                    [LastObservedAt] datetimeoffset NOT NULL,
                    [AcknowledgedAt] datetimeoffset NULL,
                    [ResolvedAt] datetimeoffset NULL,
                    [Handler] nvarchar(100) NOT NULL,
                    [ClosedReason] nvarchar(500) NOT NULL,
                    [IsActive] bit NOT NULL
                );
                CREATE UNIQUE INDEX [IX_EquipmentAlerts_AlertCode] ON [EquipmentAlerts] ([AlertCode]);
                CREATE INDEX [IX_EquipmentAlerts_DeviceCode_IsActive] ON [EquipmentAlerts] ([DeviceCode], [IsActive]);
            END;
            IF OBJECT_ID(N'[MaintenanceTasks]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MaintenanceTasks]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MaintenanceTasks] PRIMARY KEY,
                    [TaskNo] nvarchar(100) NOT NULL,
                    [DeviceCode] nvarchar(100) NOT NULL,
                    [AlertCode] nvarchar(100) NOT NULL,
                    [Symptom] nvarchar(500) NOT NULL,
                    [Cause] nvarchar(500) NOT NULL,
                    [Technician] nvarchar(100) NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [StartedAt] datetimeoffset NULL,
                    [CompletedAt] datetimeoffset NULL,
                    [ReplacedPart] nvarchar(200) NOT NULL,
                    [Result] nvarchar(500) NOT NULL,
                    [Status] nvarchar(30) NOT NULL,
                    [RestoreEquipment] bit NOT NULL
                );
                CREATE UNIQUE INDEX [IX_MaintenanceTasks_TaskNo] ON [MaintenanceTasks] ([TaskNo]);
                CREATE INDEX [IX_MaintenanceTasks_DeviceCode_Status] ON [MaintenanceTasks] ([DeviceCode], [Status]);
            END;
            IF COL_LENGTH(N'[AlertRules]', N'WindowSeconds') IS NULL
                ALTER TABLE [AlertRules] ADD [WindowSeconds] int NOT NULL CONSTRAINT [DF_AlertRules_WindowSeconds] DEFAULT 0;
            IF OBJECT_ID(N'[EquipmentEvents]', N'U') IS NULL
            BEGIN
                CREATE TABLE [EquipmentEvents]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_EquipmentEvents] PRIMARY KEY,
                    [EventId] nvarchar(100) NOT NULL,
                    [DeviceCode] nvarchar(100) NOT NULL,
                    [WorkCenter] nvarchar(100) NOT NULL,
                    [ProductionOrderNo] nvarchar(100) NOT NULL,
                    [EventType] nvarchar(40) NOT NULL,
                    [OccurredAt] datetimeoffset NOT NULL,
                    [Source] nvarchar(100) NOT NULL,
                    [Summary] nvarchar(500) NOT NULL,
                    [Payload] nvarchar(max) NOT NULL
                );
                CREATE UNIQUE INDEX [IX_EquipmentEvents_EventId] ON [EquipmentEvents] ([EventId]);
                CREATE INDEX [IX_EquipmentEvents_DeviceCode_OccurredAt_Id] ON [EquipmentEvents] ([DeviceCode], [OccurredAt], [Id]);
                CREATE INDEX [IX_EquipmentEvents_WorkCenter_OccurredAt] ON [EquipmentEvents] ([WorkCenter], [OccurredAt]);
                CREATE INDEX [IX_EquipmentEvents_ProductionOrderNo_OccurredAt] ON [EquipmentEvents] ([ProductionOrderNo], [OccurredAt]);
            END;
            IF OBJECT_ID(N'[ProductionPlanSnapshots]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ProductionPlanSnapshots]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProductionPlanSnapshots] PRIMARY KEY,
                    [OrderNo] nvarchar(100) NOT NULL,
                    [ProductSku] nvarchar(100) NOT NULL,
                    [ProductName] nvarchar(200) NOT NULL,
                    [Status] nvarchar(50) NOT NULL,
                    [PlannedQuantity] decimal(18,3) NOT NULL,
                    [CompletedQuantity] decimal(18,3) NOT NULL,
                    [ReportedGoodQuantity] decimal(18,3) NOT NULL,
                    [ReportedDefectQuantity] decimal(18,3) NOT NULL,
                    [RequiredAt] datetimeoffset NOT NULL,
                    [PlannedStartAt] datetimeoffset NULL,
                    [PlannedEndAt] datetimeoffset NULL,
                    [WorkCentersJson] nvarchar(max) NOT NULL,
                    [LastEventKey] nvarchar(150) NOT NULL,
                    [LastEventAt] datetimeoffset NOT NULL,
                    [SyncedAt] datetimeoffset NOT NULL,
                    [IsActive] bit NOT NULL
                );
                CREATE UNIQUE INDEX [IX_ProductionPlanSnapshots_OrderNo] ON [ProductionPlanSnapshots] ([OrderNo]);
                CREATE UNIQUE INDEX [IX_ProductionPlanSnapshots_LastEventKey] ON [ProductionPlanSnapshots] ([LastEventKey]);
            END;
            IF OBJECT_ID(N'[ProductionExecutionBindings]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ProductionExecutionBindings]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ProductionExecutionBindings] PRIMARY KEY,
                    [EventKey] nvarchar(150) NOT NULL,
                    [DeviceCode] nvarchar(100) NOT NULL,
                    [WorkCenter] nvarchar(100) NOT NULL,
                    [WorkOrderNo] nvarchar(100) NOT NULL,
                    [OperationCode] nvarchar(100) NOT NULL,
                    [OperationName] nvarchar(200) NOT NULL,
                    [Status] nvarchar(30) NOT NULL,
                    [StartedAt] datetimeoffset NULL,
                    [PausedAt] datetimeoffset NULL,
                    [CompletedAt] datetimeoffset NULL,
                    [ReportedGoodQuantity] decimal(18,3) NOT NULL,
                    [ReportedDefectQuantity] decimal(18,3) NOT NULL,
                    [LastEventAt] datetimeoffset NOT NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [IsActive] bit NOT NULL
                );
                CREATE UNIQUE INDEX [IX_ProductionExecutionBindings_EventKey] ON [ProductionExecutionBindings] ([EventKey]);
                CREATE INDEX [IX_ProductionExecutionBindings_DeviceCode_IsActive] ON [ProductionExecutionBindings] ([DeviceCode], [IsActive]);
                CREATE INDEX [IX_ProductionExecutionBindings_WorkOrderNo_OperationCode] ON [ProductionExecutionBindings] ([WorkOrderNo], [OperationCode]);
            END;
            IF COL_LENGTH(N'[ProductionExecutionBindings]', N'ProcessedEventKeysJson') IS NULL
                ALTER TABLE [ProductionExecutionBindings] ADD [ProcessedEventKeysJson] nvarchar(max) NOT NULL CONSTRAINT [DF_ProductionExecutionBindings_ProcessedEventKeysJson] DEFAULT N'[]';
            IF OBJECT_ID(N'[ErpPlanSyncCursors]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ErpPlanSyncCursors]
                (
                    [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ErpPlanSyncCursors] PRIMARY KEY,
                    [StreamName] nvarchar(100) NOT NULL,
                    [LastEventId] bigint NOT NULL,
                    [UpdatedAt] datetimeoffset NOT NULL
                );
                CREATE UNIQUE INDEX [IX_ErpPlanSyncCursors_StreamName] ON [ErpPlanSyncCursors] ([StreamName]);
            END;
            IF OBJECT_ID(N'[ErpPlanSyncLogs]', N'U') IS NULL
            BEGIN
                CREATE TABLE [ErpPlanSyncLogs]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_ErpPlanSyncLogs] PRIMARY KEY,
                    [EventKey] nvarchar(150) NOT NULL,
                    [EventType] nvarchar(100) NOT NULL,
                    [OrderNo] nvarchar(100) NOT NULL,
                    [Status] nvarchar(30) NOT NULL,
                    [AttemptCount] int NOT NULL,
                    [LastError] nvarchar(max) NULL,
                    [OccurredAt] datetimeoffset NOT NULL,
                    [ProcessedAt] datetimeoffset NULL,
                    [CreatedAt] datetimeoffset NOT NULL
                );
                CREATE UNIQUE INDEX [IX_ErpPlanSyncLogs_EventKey] ON [ErpPlanSyncLogs] ([EventKey]);
                CREATE INDEX [IX_ErpPlanSyncLogs_Status_CreatedAt] ON [ErpPlanSyncLogs] ([Status], [CreatedAt]);
            END;
            IF OBJECT_ID(N'[MesEquipmentNotificationDeliveries]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MesEquipmentNotificationDeliveries]
                (
                    [Id] bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MesEquipmentNotificationDeliveries] PRIMARY KEY,
                    [EventKey] nvarchar(150) NOT NULL,
                    [EventId] nvarchar(100) NOT NULL,
                    [EventType] nvarchar(60) NOT NULL,
                    [DeviceCode] nvarchar(100) NOT NULL,
                    [WorkCenter] nvarchar(100) NOT NULL,
                    [EquipmentStatus] nvarchar(30) NOT NULL,
                    [IsOnline] bit NOT NULL,
                    [IsAvailable] bit NOT NULL,
                    [CanReceiveProduction] bit NOT NULL,
                    [OccurredAt] datetimeoffset NOT NULL,
                    [Summary] nvarchar(500) NOT NULL,
                    [Payload] nvarchar(max) NOT NULL,
                    [Status] nvarchar(30) NOT NULL,
                    [AttemptCount] int NOT NULL,
                    [LastError] nvarchar(max) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [NextAttemptAt] datetimeoffset NOT NULL,
                    [SentAt] datetimeoffset NULL
                );
                CREATE UNIQUE INDEX [IX_MesEquipmentNotificationDeliveries_EventKey] ON [MesEquipmentNotificationDeliveries] ([EventKey]);
                CREATE INDEX [IX_MesEquipmentNotificationDeliveries_Status_NextAttemptAt] ON [MesEquipmentNotificationDeliveries] ([Status], [NextAttemptAt]);
            END;
            """, cancellationToken);

        if (!await db.AlertRules.AnyAsync(cancellationToken))
        {
            db.AlertRules.AddRange(
                new AlertRule
                {
                    RuleCode = "TEMP-HIGH",
                    Name = "高温告警",
                    Metric = AlertMetric.Temperature,
                    Comparison = AlertComparison.GreaterThan,
                    Threshold = 85,
                    WindowSeconds = 0,
                    DurationSeconds = 30,
                    Severity = AlertSeverity.Critical,
                    Description = "温度超过 85°C 且持续 30 秒"
                },
                new AlertRule
                {
                    RuleCode = "VIBRATION-HIGH",
                    Name = "机械振动异常",
                    Metric = AlertMetric.Vibration,
                    Comparison = AlertComparison.GreaterThan,
                    Threshold = 8,
                    WindowSeconds = 0,
                    ConsecutiveSamples = 5,
                    Severity = AlertSeverity.Warning,
                    Description = "振动超过 8 mm/s 且连续 5 次"
                },
                new AlertRule
                {
                    RuleCode = "DEVICE-OFFLINE",
                    Name = "设备离线",
                    Metric = AlertMetric.Heartbeat,
                    Comparison = AlertComparison.GreaterThan,
                    Threshold = 60,
                    WindowSeconds = 0,
                    DurationSeconds = 60,
                    Severity = AlertSeverity.Critical,
                    Description = "设备心跳超过 60 秒未更新"
                },
                new AlertRule
                {
                    RuleCode = "CAPACITY-LOW",
                    Name = "低产能告警",
                    Metric = AlertMetric.Capacity,
                    Comparison = AlertComparison.LessThan,
                    Threshold = 80,
                    WindowSeconds = 600,
                    Severity = AlertSeverity.Warning,
                    Description = "10 分钟产量低于额定产能的 80%"
                });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.AlertRules.AnyAsync(x => x.RuleCode == "CAPACITY-LOW", cancellationToken))
        {
            db.AlertRules.Add(new AlertRule
            {
                RuleCode = "CAPACITY-LOW",
                Name = "低产能告警",
                Metric = AlertMetric.Capacity,
                Comparison = AlertComparison.LessThan,
                Threshold = 80,
                WindowSeconds = 600,
                Severity = AlertSeverity.Warning,
                Description = "10 分钟产量低于额定产能的 80%"
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        if (await db.Equipments.AnyAsync(cancellationToken)) return;

        db.Equipments.AddRange(
            Create("LINE-A-01", "智能装配线 A1", "装配线", "主工厂", "一车间", "WC-ASSY-A", 120),
            Create("LINE-A-02", "智能装配线 A2", "装配线", "主工厂", "一车间", "WC-ASSY-A", 100),
            Create("TEST-01", "终检设备 01", "测试设备", "主工厂", "二车间", "WC-TEST-01", 80),
            Create("PACK-01", "自动包装机 01", "包装设备", "主工厂", "二车间", "WC-PACK-01", 150));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Equipment Create(string code, string name, string type, string factory, string workshop, string workCenter, decimal capacity) => new()
    {
        DeviceCode = code,
        Name = name,
        EquipmentType = type,
        Factory = factory,
        Workshop = workshop,
        WorkCenter = workCenter,
        RatedCapacityPerHour = capacity,
        Status = EquipmentStatus.Stopped,
        CanReceiveProduction = false,
        Scenario = SimulationScenario.Normal,
        SnapshotAt = DateTimeOffset.UtcNow,
        TemperatureCelsius = 25,
        CurrentAmps = 0,
        VibrationMillimetersPerSecond = 0,
        CycleSeconds = 3600m / capacity
    };
}
