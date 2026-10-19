using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/integration/mes")]
public sealed class MesEquipmentIntegrationController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet("equipment-availability")]
    public async Task<ActionResult<MesEquipmentAvailabilityResponse>> Availability(
        [FromQuery] string? workCenter = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var equipmentQuery = db.Equipments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(workCenter))
            equipmentQuery = equipmentQuery.Where(x => x.WorkCenter == workCenter);
        var equipment = await equipmentQuery.OrderBy(x => x.DeviceCode).ToListAsync(cancellationToken);
        var activePlans = await db.ProductionPlanSnapshots.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var deviceCodes = equipment.Select(x => x.DeviceCode).ToList();
        var activeBindings = await db.ProductionExecutionBindings.AsNoTracking()
            .Where(x => x.IsActive && deviceCodes.Contains(x.DeviceCode))
            .OrderByDescending(x => x.LastEventAt)
            .ToListAsync(cancellationToken);
        var bindingsByDevice = activeBindings
            .GroupBy(x => x.DeviceCode)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var activeAlerts = await db.EquipmentAlerts.AsNoTracking()
            .Where(x => x.IsActive && deviceCodes.Contains(x.DeviceCode))
            .OrderBy(x => x.TriggeredAt)
            .ToListAsync(cancellationToken);
        var alertsByDevice = activeAlerts.GroupBy(x => x.DeviceCode)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        var items = equipment.Select(device =>
        {
            var alerts = alertsByDevice.TryGetValue(device.DeviceCode, out var matched) ? matched : [];
            bindingsByDevice.TryGetValue(device.DeviceCode, out var binding);
            var isOnline = device.LastHeartbeat is not null &&
                           now - device.LastHeartbeat.Value <= TimeSpan.FromSeconds(60) &&
                           device.Status != EquipmentStatus.Offline;
            var isAvailable = isOnline &&
                              device.CanReceiveProduction &&
                              device.Status is EquipmentStatus.Running or EquipmentStatus.Degraded;
            var currentCapacity = isAvailable
                ? device.RatedCapacityPerHour * (device.Status == EquipmentStatus.Degraded ? 0.8m : 1m)
                : 0;
            var target = ProductionPlanMatcher.Resolve(device, activePlans, now);
            return new MesEquipmentAvailabilityItem(
                device.DeviceCode,
                device.Name,
                device.WorkCenter,
                device.Status,
                isOnline,
                isAvailable,
                device.CanReceiveProduction,
                device.RatedCapacityPerHour,
                currentCapacity,
                target.CapacityPerHour,
                target.Source,
                target.ProductionOrderNos,
                binding?.WorkOrderNo ?? string.Empty,
                binding?.OperationCode ?? string.Empty,
                binding?.Status.ToString() ?? string.Empty,
                alerts.Count,
                alerts.Select(x => x.AlertCode).ToList(),
                device.LastHeartbeat,
                device.SnapshotAt,
                null);
        }).ToList();

        return Ok(new MesEquipmentAvailabilityResponse(
            now,
            workCenter?.Trim() ?? string.Empty,
            items,
            items.Sum(x => x.RatedCapacityPerHour),
            items.Sum(x => x.CurrentCapacityPerHour)));
    }

    [HttpGet("equipment-events")]
    public async Task<ActionResult<MesEquipmentEventBatchResponse>> Events(
        [FromQuery] long afterId = 0,
        [FromQuery] string? deviceCode = null,
        [FromQuery] string? workCenter = null,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(limit, 1, 500);
        var query = db.EquipmentEvents.AsNoTracking().Where(x => x.Id > afterId);
        if (!string.IsNullOrWhiteSpace(deviceCode))
            query = query.Where(x => x.DeviceCode == deviceCode);
        if (!string.IsNullOrWhiteSpace(workCenter))
            query = query.Where(x => x.WorkCenter == workCenter);

        var rows = await query.OrderBy(x => x.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        return Ok(new MesEquipmentEventBatchResponse(
            afterId,
            items.Count > 0 ? items[^1].Id : null,
            hasMore,
            items.Select(EquipmentEventResponse.From).ToList()));
    }
}

[ApiController]
[Route("api/events")]
public sealed class EquipmentEventSummaryController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<EquipmentEventSummaryResponse>> Summary(
        [FromQuery] string? deviceCode = null,
        [FromQuery] string? workCenter = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.EquipmentEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(deviceCode)) query = query.Where(x => x.DeviceCode == deviceCode);
        if (!string.IsNullOrWhiteSpace(workCenter)) query = query.Where(x => x.WorkCenter == workCenter);
        if (from is not null) query = query.Where(x => x.OccurredAt >= from);
        if (to is not null) query = query.Where(x => x.OccurredAt <= to);

        var events = await query.Select(x => new { x.EventType, x.OccurredAt }).ToListAsync(cancellationToken);
        return Ok(new EquipmentEventSummaryResponse(
            DateTimeOffset.UtcNow,
            events.Count == 0 ? null : events.Min(x => x.OccurredAt),
            events.Count == 0 ? null : events.Max(x => x.OccurredAt),
            events.Count,
            events.GroupBy(x => x.EventType.ToString()).ToDictionary(x => x.Key, x => (long)x.Count())));
    }
}
