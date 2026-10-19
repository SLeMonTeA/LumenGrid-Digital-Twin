using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/integration/status")]
public sealed class IntegrationStatusController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IntegrationStatusResponse>> Get(CancellationToken cancellationToken)
    {
        var erpLogs = db.ErpPlanSyncLogs.AsNoTracking();
        var erpLastSucceeded = await erpLogs.Where(x => x.Status == ErpPlanSyncStatus.Succeeded).OrderByDescending(x => x.ProcessedAt).FirstOrDefaultAsync(cancellationToken);
        var erpLastFailed = await erpLogs.Where(x => x.Status == ErpPlanSyncStatus.Failed).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var notificationLogs = db.MesEquipmentNotificationDeliveries.AsNoTracking();
        var notificationLastSucceeded = await notificationLogs.Where(x => x.Status == MesNotificationDeliveryStatus.Sent).OrderByDescending(x => x.SentAt).FirstOrDefaultAsync(cancellationToken);
        var notificationLastFailed = await notificationLogs.Where(x => x.Status == MesNotificationDeliveryStatus.Failed).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);

        return Ok(new IntegrationStatusResponse(
            "LumenGrid",
            DateTimeOffset.UtcNow,
            [
                new IntegrationChannelStatus(
                    "erp-production-plan",
                    "inbound",
                    "WarehouseFlow",
                    0,
                    await erpLogs.CountAsync(x => x.Status == ErpPlanSyncStatus.Succeeded || x.Status == ErpPlanSyncStatus.Skipped, cancellationToken),
                    await erpLogs.CountAsync(x => x.Status == ErpPlanSyncStatus.Failed, cancellationToken),
                    erpLastSucceeded?.ProcessedAt,
                    erpLastFailed?.CreatedAt,
                    erpLastFailed?.LastError),
                new IntegrationChannelStatus(
                    "mes-equipment-notification",
                    "outbound",
                    "TraceForge",
                    await notificationLogs.CountAsync(x => x.Status == MesNotificationDeliveryStatus.Pending, cancellationToken),
                    await notificationLogs.CountAsync(x => x.Status == MesNotificationDeliveryStatus.Sent, cancellationToken),
                    await notificationLogs.CountAsync(x => x.Status == MesNotificationDeliveryStatus.Failed, cancellationToken),
                    notificationLastSucceeded?.SentAt,
                    notificationLastFailed?.CreatedAt,
                    notificationLastFailed?.LastError)
            ]));
    }
}
