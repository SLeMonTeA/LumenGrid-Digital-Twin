using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Infrastructure;

public sealed class ErpProductionPlanSynchronizationService(
    WarehouseFlowErpClient erp,
    LumenGridDbContext db,
    EquipmentEventWriter eventWriter)
{
    private const string StreamName = "WarehouseFlow-ProductionOrder";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<ErpPlanSyncResult> SyncAsync(CancellationToken cancellationToken)
    {
            await Gate.WaitAsync(cancellationToken);
        try
        {
            var snapshots = await erp.GetProductionOrdersAsync(cancellationToken);
            var cursor = await GetOrCreateCursorAsync(cancellationToken);
            var events = await erp.GetProductionOrderEventsAsync(cursor.LastEventId, 100, cancellationToken);
            var applied = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var integrationEvent in events)
            {
                var log = await db.ErpPlanSyncLogs.SingleOrDefaultAsync(x => x.EventKey == integrationEvent.EventKey, cancellationToken);
                if (log?.Status is ErpPlanSyncStatus.Succeeded or ErpPlanSyncStatus.Skipped)
                {
                    cursor.LastEventId = integrationEvent.EventId;
                    cursor.UpdatedAt = DateTimeOffset.UtcNow;
                    skipped++;
                    continue;
                }

                try
                {
                    if (integrationEvent.Snapshot is null)
                        throw new InvalidOperationException($"ERP 事件 {integrationEvent.EventKey} 未返回生产订单快照");

                    var current = await db.ProductionPlanSnapshots.SingleOrDefaultAsync(x => x.OrderNo == integrationEvent.AggregateNo, cancellationToken);
                    if (current is not null && current.LastEventAt > integrationEvent.OccurredAt)
                    {
                        log = MarkLog(log, integrationEvent, ErpPlanSyncStatus.Skipped, "事件早于当前计划快照，已跳过");
                        cursor.LastEventId = integrationEvent.EventId;
                        cursor.UpdatedAt = DateTimeOffset.UtcNow;
                        skipped++;
                    }
                    else
                    {
                        ApplySnapshot(integrationEvent.Snapshot, integrationEvent.EventKey, integrationEvent.EventType, integrationEvent.OccurredAt, current);
                        log = MarkLog(log, integrationEvent, ErpPlanSyncStatus.Succeeded, null);
                        cursor.LastEventId = integrationEvent.EventId;
                        cursor.UpdatedAt = DateTimeOffset.UtcNow;
                        applied++;
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    log = MarkLog(log, integrationEvent, ErpPlanSyncStatus.Failed, ex.Message);
                    await db.SaveChangesAsync(cancellationToken);
                    failed++;
                    break;
                }
            }

            var snapshotApplied = await BootstrapMissingSnapshotsAsync(snapshots, cancellationToken);
            return new(snapshots.Count, snapshotApplied, events.Count, applied, skipped, failed, cursor.LastEventId, DateTimeOffset.UtcNow);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<ErpPlanSyncStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var cursor = await db.ErpPlanSyncCursors.AsNoTracking().SingleOrDefaultAsync(x => x.StreamName == StreamName, cancellationToken);
        var logs = db.ErpPlanSyncLogs.AsNoTracking().Where(x => x.EventKey != string.Empty);
        var lastFailure = await logs.Where(x => x.Status == ErpPlanSyncStatus.Failed).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        return new(
            StreamName,
            cursor?.LastEventId ?? 0,
            cursor?.UpdatedAt,
            await logs.CountAsync(x => x.Status == ErpPlanSyncStatus.Succeeded, cancellationToken),
            await logs.CountAsync(x => x.Status == ErpPlanSyncStatus.Failed, cancellationToken),
            await logs.CountAsync(x => x.Status == ErpPlanSyncStatus.Skipped, cancellationToken),
            lastFailure?.CreatedAt,
            lastFailure?.LastError);
    }

    private async Task<int> BootstrapMissingSnapshotsAsync(
        IReadOnlyList<WarehouseFlowProductionOrderSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var orderNos = snapshots.Select(x => x.OrderNo).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (orderNos.Count == 0) return 0;
        var existing = await db.ProductionPlanSnapshots.Where(x => orderNos.Contains(x.OrderNo)).ToDictionaryAsync(x => x.OrderNo, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var applied = 0;
        foreach (var snapshot in snapshots)
        {
            if (existing.ContainsKey(snapshot.OrderNo)) continue;
            var eventKey = $"ERP-SNAPSHOT:{snapshot.OrderNo}:{snapshot.Status}:{snapshot.PlannedQuantity}:{snapshot.CompletedQuantity}:{snapshot.RequiredAt:O}";
            var plan = new ProductionPlanSnapshot { OrderNo = snapshot.OrderNo };
            db.ProductionPlanSnapshots.Add(plan);
            ApplySnapshot(snapshot, eventKey, "ProductionOrderSnapshot", DateTimeOffset.UtcNow, plan);
            applied++;
        }

        if (applied > 0) await db.SaveChangesAsync(cancellationToken);
        return applied;
    }

    private async Task<ErpPlanSyncCursor> GetOrCreateCursorAsync(CancellationToken cancellationToken)
    {
        var cursor = await db.ErpPlanSyncCursors.SingleOrDefaultAsync(x => x.StreamName == StreamName, cancellationToken);
        if (cursor is not null) return cursor;
        cursor = new ErpPlanSyncCursor { StreamName = StreamName };
        db.ErpPlanSyncCursors.Add(cursor);
        await db.SaveChangesAsync(cancellationToken);
        return cursor;
    }

    private void ApplySnapshot(
        WarehouseFlowProductionOrderSnapshot snapshot,
        string eventKey,
        string eventType,
        DateTimeOffset occurredAt,
        ProductionPlanSnapshot? plan)
    {
        plan ??= new ProductionPlanSnapshot { OrderNo = snapshot.OrderNo };
        if (plan.Id == 0 && !db.ChangeTracker.Entries<ProductionPlanSnapshot>().Any(x => ReferenceEquals(x.Entity, plan)))
            db.ProductionPlanSnapshots.Add(plan);

        var workCenters = snapshot.Operations
            .Select(x => x.WorkCenter)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var wasNew = string.IsNullOrWhiteSpace(plan.LastEventKey);
        plan.ProductSku = snapshot.ProductSku?.Trim() ?? string.Empty;
        plan.ProductName = snapshot.ProductName?.Trim() ?? string.Empty;
        plan.Status = snapshot.Status?.Trim() ?? string.Empty;
        plan.PlannedQuantity = snapshot.PlannedQuantity;
        plan.CompletedQuantity = snapshot.CompletedQuantity;
        plan.ReportedGoodQuantity = snapshot.ReportedGoodQuantity;
        plan.ReportedDefectQuantity = snapshot.ReportedDefectQuantity;
        plan.RequiredAt = snapshot.RequiredAt;
        plan.PlannedStartAt = null;
        plan.PlannedEndAt = null;
        plan.WorkCentersJson = JsonSerializer.Serialize(workCenters);
        plan.LastEventKey = eventKey;
        plan.LastEventAt = occurredAt;
        plan.SyncedAt = DateTimeOffset.UtcNow;
        plan.IsActive = !string.Equals(snapshot.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);

        if (wasNew)
        {
            eventWriter.AppendPlan(
                db,
                plan,
                workCenters.FirstOrDefault() ?? string.Empty,
                plan.IsActive ? EquipmentEventType.ProductionPlanPublished : EquipmentEventType.ProductionPlanCancelled,
                occurredAt,
                eventType);
        }
        else if (!plan.IsActive)
        {
            eventWriter.AppendPlan(db, plan, workCenters.FirstOrDefault() ?? string.Empty, EquipmentEventType.ProductionPlanCancelled, occurredAt, eventType);
        }
        else
        {
            eventWriter.AppendPlan(db, plan, workCenters.FirstOrDefault() ?? string.Empty, EquipmentEventType.ProductionPlanChanged, occurredAt, eventType);
        }
    }

    private ErpPlanSyncLog MarkLog(ErpPlanSyncLog? log, WarehouseFlowProductionOrderEvent integrationEvent, ErpPlanSyncStatus status, string? error)
    {
        log ??= new ErpPlanSyncLog
        {
            EventKey = integrationEvent.EventKey,
            EventType = integrationEvent.EventType,
            OrderNo = integrationEvent.AggregateNo,
            OccurredAt = integrationEvent.OccurredAt,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (log.Id == 0 && !db.ChangeTracker.Entries<ErpPlanSyncLog>().Any(x => ReferenceEquals(x.Entity, log)))
            db.ErpPlanSyncLogs.Add(log);
        log.Status = status;
        log.AttemptCount++;
        log.LastError = error;
        log.ProcessedAt = status == ErpPlanSyncStatus.Failed ? null : DateTimeOffset.UtcNow;
        return log;
    }
}

public sealed class ErpProductionPlanSyncHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ErpProductionPlanSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Erp:PlanSyncEnabled", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Erp:PlanSyncIntervalSeconds", 30)));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ErpProductionPlanSynchronizationService>();
                var result = await service.SyncAsync(stoppingToken);
                if (result.SnapshotApplied > 0 || result.EventsApplied > 0 || result.EventsFailed > 0)
                    logger.LogInformation("ERP 生产计划同步完成：快照 {SnapshotFetched}/{SnapshotApplied}，事件 {EventsFetched}/{EventsApplied}，失败 {EventsFailed}，游标 {LastEventId}", result.SnapshotFetched, result.SnapshotApplied, result.EventsFetched, result.EventsApplied, result.EventsFailed, result.LastEventId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "ERP 生产计划自动同步失败，将在下一轮重试"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
