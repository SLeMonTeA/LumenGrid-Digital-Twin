using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/integration/mes/equipment-notification-deliveries")]
public sealed class MesNotificationDeliveryController(
    LumenGridDbContext db,
    MesEquipmentNotificationDeliveryService deliveryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MesNotificationDeliveryResponse>>> List(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = db.MesEquipmentNotificationDeliveries.AsNoTracking().AsQueryable();
        if (Enum.TryParse<MesNotificationDeliveryStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        return Ok(rows.Select(MesNotificationDeliveryResponse.From).ToList());
    }

    [HttpPost("retry")]
    public async Task<IActionResult> Retry(CancellationToken cancellationToken) =>
        Ok(new { reset = await deliveryService.RetryFailedAsync(cancellationToken) });
}
