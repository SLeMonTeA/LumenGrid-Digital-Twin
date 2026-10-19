using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Infrastructure;

public sealed record MesNotificationDeliveryBatchResult(int Attempted, int Sent, int Failed, DateTimeOffset CompletedAt);

public sealed class MesEquipmentNotificationDeliveryService(
    TraceForgeEquipmentNotificationClient mes,
    LumenGridDbContext db,
    ILogger<MesEquipmentNotificationDeliveryService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<MesNotificationDeliveryBatchResult> DeliverPendingAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var deliveries = await db.MesEquipmentNotificationDeliveries
                .Where(x => x.Status != MesNotificationDeliveryStatus.Sent && x.NextAttemptAt <= now)
                .OrderBy(x => x.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);
            var sent = 0;
            var failed = 0;
            foreach (var delivery in deliveries)
            {
                delivery.AttemptCount++;
                try
                {
                    await mes.PublishAsync(new MesEquipmentNotificationRequest(
                        delivery.EventKey,
                        delivery.EventId,
                        delivery.EventType,
                        delivery.DeviceCode,
                        delivery.WorkCenter,
                        delivery.EquipmentStatus,
                        delivery.IsOnline,
                        delivery.IsAvailable,
                        delivery.CanReceiveProduction,
                        delivery.OccurredAt,
                        delivery.Summary,
                        delivery.Payload), cancellationToken);
                    delivery.Status = MesNotificationDeliveryStatus.Sent;
                    delivery.LastError = null;
                    delivery.SentAt = DateTimeOffset.UtcNow;
                    sent++;
                }
                catch (Exception ex) when (ex is TraceForgeNotificationException or HttpRequestException or TaskCanceledException)
                {
                    delivery.Status = MesNotificationDeliveryStatus.Failed;
                    delivery.LastError = ex.Message;
                    var delaySeconds = Math.Min(900, Math.Max(10, Math.Pow(2, Math.Min(delivery.AttemptCount, 7)) * 10));
                    delivery.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                    failed++;
                    logger.LogWarning(ex, "MES 设备通知投递失败，将在 {NextAttemptAt} 重试：{EventKey}", delivery.NextAttemptAt, delivery.EventKey);
                }
            }

            if (deliveries.Count > 0) await db.SaveChangesAsync(cancellationToken);
            return new(deliveries.Count, sent, failed, DateTimeOffset.UtcNow);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<int> RetryFailedAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var failed = await db.MesEquipmentNotificationDeliveries
                .Where(x => x.Status == MesNotificationDeliveryStatus.Failed)
                .ToListAsync(cancellationToken);
            foreach (var delivery in failed)
            {
                delivery.Status = MesNotificationDeliveryStatus.Pending;
                delivery.NextAttemptAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            return failed.Count;
        }
        finally
        {
            Gate.Release();
        }
    }
}

public sealed class MesEquipmentNotificationDeliveryHostedService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MesEquipmentNotificationDeliveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Mes:NotificationDeliveryEnabled", true)) return;
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Mes:NotificationDeliveryIntervalSeconds", 15)));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<MesEquipmentNotificationDeliveryService>();
                var result = await service.DeliverPendingAsync(stoppingToken);
                if (result.Attempted > 0)
                    logger.LogInformation("MES 设备通知 Outbox 处理完成：尝试 {Attempted}，成功 {Sent}，失败 {Failed}", result.Attempted, result.Sent, result.Failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "MES 设备通知 Outbox 处理失败，将在下一轮重试"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
