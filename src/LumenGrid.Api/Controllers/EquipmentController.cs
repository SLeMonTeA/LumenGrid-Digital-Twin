using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/equipment")]
public sealed class EquipmentController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EquipmentSnapshotResponse>>> List(CancellationToken cancellationToken)
    {
        var equipment = await db.Equipments.AsNoTracking().OrderBy(x => x.DeviceCode).ToListAsync(cancellationToken);
        return Ok(equipment.Select(EquipmentSnapshotResponse.From).ToList());
    }

    [HttpGet("summary")]
    public async Task<ActionResult<EquipmentDashboardSummary>> Summary(CancellationToken cancellationToken)
    {
        var equipment = await db.Equipments.AsNoTracking().ToListAsync(cancellationToken);
        var capacityLoss = equipment.Where(x => x.Status is EquipmentStatus.Degraded or EquipmentStatus.Faulted or EquipmentStatus.Offline)
            .Sum(x => x.RatedCapacityPerHour);
        return Ok(new EquipmentDashboardSummary(
            equipment.Count,
            equipment.Count(x => x.Status == EquipmentStatus.Running),
            equipment.Count(x => x.Status is EquipmentStatus.Stopped or EquipmentStatus.Offline),
            equipment.Count(x => x.Status is EquipmentStatus.Degraded or EquipmentStatus.Faulted),
            capacityLoss));
    }

    [HttpGet("{deviceCode}")]
    public async Task<ActionResult<EquipmentSnapshotResponse>> Get(string deviceCode, CancellationToken cancellationToken)
    {
        var equipment = await db.Equipments.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceCode == deviceCode, cancellationToken);
        return equipment is null ? NotFound(new { message = "设备不存在" }) : Ok(EquipmentSnapshotResponse.From(equipment));
    }
}
