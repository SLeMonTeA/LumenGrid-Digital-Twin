using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EquipmentEventsController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EquipmentEventResponse>>> List(
        [FromQuery] string? deviceCode = null,
        [FromQuery] string? workCenter = null,
        [FromQuery] string? productionOrderNo = null,
        [FromQuery] EquipmentEventType? eventType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var query = db.EquipmentEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(deviceCode))
            query = query.Where(x => x.DeviceCode == deviceCode);
        if (!string.IsNullOrWhiteSpace(workCenter))
            query = query.Where(x => x.WorkCenter == workCenter);
        if (!string.IsNullOrWhiteSpace(productionOrderNo))
            query = query.Where(x => x.ProductionOrderNo == productionOrderNo);
        if (eventType is not null)
            query = query.Where(x => x.EventType == eventType);
        if (from is not null)
            query = query.Where(x => x.OccurredAt >= from);
        if (to is not null)
            query = query.Where(x => x.OccurredAt <= to);

        var events = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
        return Ok(events.Select(EquipmentEventResponse.From).ToList());
    }
}

[ApiController]
[Route("api/equipment")]
public sealed class EquipmentHistoryController(LumenGridDbContext db) : ControllerBase
{
    [HttpGet("{deviceCode}/events/page")]
    public async Task<ActionResult<EquipmentEventPageResponse>> DeviceEventPage(
        string deviceCode,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int limit = 40,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(limit, 1, 200);
        var query = db.EquipmentEvents.AsNoTracking().Where(x => x.DeviceCode == deviceCode);
        if (from is not null) query = query.Where(x => x.OccurredAt >= from);
        if (to is not null) query = query.Where(x => x.OccurredAt <= to);
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            if (!TryDecodeCursor(cursor, out var cursorAt, out var cursorId))
                return BadRequest(new { message = "事件游标无效" });
            query = query.Where(x => x.OccurredAt < cursorAt || (x.OccurredAt == cursorAt && x.Id < cursorId));
        }

        var rows = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        var nextCursor = hasMore && items.Count > 0 ? EncodeCursor(items[^1]) : null;
        return Ok(new EquipmentEventPageResponse(items.Select(EquipmentEventResponse.From).ToList(), nextCursor, pageSize));
    }

    [HttpGet("{deviceCode}/events")]
    public async Task<ActionResult<IReadOnlyList<EquipmentEventResponse>>> DeviceEvents(
        string deviceCode,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] EquipmentEventType? eventType = null,
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var query = db.EquipmentEvents.AsNoTracking().Where(x => x.DeviceCode == deviceCode);
        if (from is not null) query = query.Where(x => x.OccurredAt >= from);
        if (to is not null) query = query.Where(x => x.OccurredAt <= to);
        if (eventType is not null) query = query.Where(x => x.EventType == eventType);
        var events = await query
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToListAsync(cancellationToken);
        return Ok(events.Select(EquipmentEventResponse.From).ToList());
    }

    [HttpGet("{deviceCode}/history")]
    public async Task<ActionResult<EquipmentHistoryReplayResponse>> Replay(
        string deviceCode,
        [FromQuery] DateTimeOffset? at = null,
        CancellationToken cancellationToken = default)
    {
        var replayAt = at ?? DateTimeOffset.UtcNow;
        var events = await db.EquipmentEvents.AsNoTracking()
            .Where(x => x.DeviceCode == deviceCode && x.OccurredAt <= replayAt)
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var snapshotEvent = events
            .Select(x => (Event: x, Snapshot: EquipmentEventWriter.ReadSnapshot(x)))
            .LastOrDefault(x => x.Snapshot is not null);

        return Ok(new EquipmentHistoryReplayResponse(
            deviceCode,
            replayAt,
            snapshotEvent.Snapshot is not null,
            snapshotEvent.Snapshot,
            events.Count,
            snapshotEvent.Event?.EventId,
            snapshotEvent.Event?.EventType,
            snapshotEvent.Event?.OccurredAt));
    }

    private static string EncodeCursor(EquipmentEvent record)
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{record.OccurredAt.UtcDateTime.Ticks}|{record.Id}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset occurredAt, out long id)
    {
        occurredAt = default;
        id = 0;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split('|', 2);
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ||
                !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                return false;
            occurredAt = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
