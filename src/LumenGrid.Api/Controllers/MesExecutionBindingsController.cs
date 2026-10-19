using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/integration/mes/execution-bindings")]
public sealed class MesExecutionBindingsController(
    LumenGridDbContext db,
    EquipmentEventWriter eventWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MesExecutionBindingResponse>>> List(
        [FromQuery] string? deviceCode = null,
        [FromQuery] string? workOrderNo = null,
        [FromQuery] string? workCenter = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = db.ProductionExecutionBindings.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(deviceCode)) query = query.Where(x => x.DeviceCode == deviceCode);
        if (!string.IsNullOrWhiteSpace(workOrderNo)) query = query.Where(x => x.WorkOrderNo == workOrderNo);
        if (!string.IsNullOrWhiteSpace(workCenter)) query = query.Where(x => x.WorkCenter == workCenter);
        if (activeOnly) query = query.Where(x => x.IsActive);

        var bindings = await query
            .OrderByDescending(x => x.LastEventAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
        return Ok(bindings.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<MesExecutionBindingWriteResponse>> Publish(
        MesExecutionBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.EventKey)) return BadRequest(new { message = "EventKey 不能为空" });
        if (string.IsNullOrWhiteSpace(request.DeviceCode)) return BadRequest(new { message = "DeviceCode 不能为空" });
        if (string.IsNullOrWhiteSpace(request.WorkOrderNo)) return BadRequest(new { message = "WorkOrderNo 不能为空" });
        if (string.IsNullOrWhiteSpace(request.OperationCode)) return BadRequest(new { message = "OperationCode 不能为空" });
        if (request.ReportedGoodQuantity is < 0 || request.ReportedDefectQuantity is < 0)
            return BadRequest(new { message = "报工数量不能为负数" });

        var eventKey = request.EventKey.Trim();
        var duplicate = (await db.ProductionExecutionBindings.AsNoTracking().ToListAsync(cancellationToken))
            .FirstOrDefault(x => x.EventKey == eventKey || HasProcessedEventKey(x, eventKey));
        if (duplicate is not null)
            return Ok(new MesExecutionBindingWriteResponse(false, ToResponse(duplicate)) with { Duplicate = true });

        var device = await db.Equipments.SingleOrDefaultAsync(x => x.DeviceCode == request.DeviceCode.Trim(), cancellationToken);
        if (device is null) return NotFound(new { message = $"设备不存在：{request.DeviceCode}" });
        if (!string.IsNullOrWhiteSpace(request.WorkCenter) &&
            !string.Equals(request.WorkCenter.Trim(), device.WorkCenter, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { message = $"设备 {device.DeviceCode} 不属于工作中心 {request.WorkCenter}" });

        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;
        var status = ResolveStatus(request.EventType, request.Status);
        var sameBinding = await db.ProductionExecutionBindings
            .SingleOrDefaultAsync(x => x.DeviceCode == device.DeviceCode
                && x.WorkOrderNo == request.WorkOrderNo.Trim()
                && x.OperationCode == request.OperationCode.Trim(), cancellationToken);
        if (sameBinding is null)
        {
            var anotherActive = await db.ProductionExecutionBindings
                .Where(x => x.DeviceCode == device.DeviceCode && x.IsActive)
                .FirstOrDefaultAsync(cancellationToken);
            if (anotherActive is not null && status is ExecutionBindingStatus.Bound or ExecutionBindingStatus.Running)
                return Conflict(new { message = $"设备 {device.DeviceCode} 已绑定 {anotherActive.WorkOrderNo}/{anotherActive.OperationCode}" });

            sameBinding = new ProductionExecutionBinding
            {
                EventKey = eventKey,
                ProcessedEventKeysJson = JsonSerializer.Serialize(new[] { eventKey }),
                DeviceCode = device.DeviceCode,
                WorkCenter = device.WorkCenter,
                WorkOrderNo = request.WorkOrderNo.Trim(),
                OperationCode = request.OperationCode.Trim(),
                OperationName = request.OperationName?.Trim() ?? string.Empty,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ProductionExecutionBindings.Add(sameBinding);
        }

        sameBinding.Status = status;
        sameBinding.EventKey = eventKey;
        var processedKeys = ReadProcessedEventKeys(sameBinding);
        if (!processedKeys.Contains(eventKey, StringComparer.Ordinal)) processedKeys.Add(eventKey);
        sameBinding.ProcessedEventKeysJson = JsonSerializer.Serialize(processedKeys);
        sameBinding.LastEventAt = occurredAt;
        sameBinding.StartedAt = request.StartedAt ?? sameBinding.StartedAt ?? (status == ExecutionBindingStatus.Running ? occurredAt : null);
        sameBinding.PausedAt = status == ExecutionBindingStatus.Paused ? occurredAt : sameBinding.PausedAt;
        sameBinding.CompletedAt = status is ExecutionBindingStatus.Completed or ExecutionBindingStatus.Cancelled ? occurredAt : sameBinding.CompletedAt;
        sameBinding.ReportedGoodQuantity = request.ReportedGoodQuantity ?? sameBinding.ReportedGoodQuantity;
        sameBinding.ReportedDefectQuantity = request.ReportedDefectQuantity ?? sameBinding.ReportedDefectQuantity;
        sameBinding.IsActive = status is not (ExecutionBindingStatus.Completed or ExecutionBindingStatus.Cancelled);

        var eventType = ResolveEventType(request.EventType, status);
        eventWriter.AppendExecution(db, sameBinding, device, eventType, occurredAt, request.EventType?.Trim() ?? string.Empty);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new MesExecutionBindingWriteResponse(false, ToResponse(sameBinding)));
    }

    private static ExecutionBindingStatus ResolveStatus(string? eventType, string? requestedStatus)
    {
        if (Enum.TryParse<ExecutionBindingStatus>(requestedStatus, true, out var explicitStatus)) return explicitStatus;
        var value = eventType?.Trim() ?? string.Empty;
        if (value.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return ExecutionBindingStatus.Cancelled;
        if (value.Contains("complete", StringComparison.OrdinalIgnoreCase)) return ExecutionBindingStatus.Completed;
        if (value.Contains("pause", StringComparison.OrdinalIgnoreCase)) return ExecutionBindingStatus.Paused;
        if (value.Contains("start", StringComparison.OrdinalIgnoreCase) || value.Contains("run", StringComparison.OrdinalIgnoreCase)) return ExecutionBindingStatus.Running;
        return ExecutionBindingStatus.Bound;
    }

    private static EquipmentEventType ResolveEventType(string? eventType, ExecutionBindingStatus status)
    {
        var value = eventType?.Trim() ?? string.Empty;
        if (value.Contains("cancel", StringComparison.OrdinalIgnoreCase) || status == ExecutionBindingStatus.Cancelled) return EquipmentEventType.ProductionOperationCancelled;
        if (value.Contains("complete", StringComparison.OrdinalIgnoreCase) || status == ExecutionBindingStatus.Completed) return EquipmentEventType.ProductionOperationCompleted;
        if (value.Contains("pause", StringComparison.OrdinalIgnoreCase) || status == ExecutionBindingStatus.Paused) return EquipmentEventType.ProductionOperationPaused;
        if (value.Contains("start", StringComparison.OrdinalIgnoreCase) || status == ExecutionBindingStatus.Running) return EquipmentEventType.ProductionOperationStarted;
        return EquipmentEventType.ProductionDeviceBound;
    }

    private static MesExecutionBindingResponse ToResponse(ProductionExecutionBinding binding) => new(
        binding.Id,
        binding.EventKey,
        binding.DeviceCode,
        binding.WorkCenter,
        binding.WorkOrderNo,
        binding.OperationCode,
        binding.OperationName,
        binding.Status.ToString(),
        binding.StartedAt,
        binding.PausedAt,
        binding.CompletedAt,
        binding.ReportedGoodQuantity,
        binding.ReportedDefectQuantity,
        binding.LastEventAt,
        binding.IsActive);

    private static bool HasProcessedEventKey(ProductionExecutionBinding binding, string eventKey) =>
        ReadProcessedEventKeys(binding).Contains(eventKey, StringComparer.Ordinal);

    private static List<string> ReadProcessedEventKeys(ProductionExecutionBinding binding)
    {
        List<string> keys;
        try
        {
            keys = JsonSerializer.Deserialize<List<string>>(binding.ProcessedEventKeysJson) ?? [];
        }
        catch (JsonException)
        {
            keys = [];
        }
        if (binding.EventKey.Length > 0 && !keys.Contains(binding.EventKey, StringComparer.Ordinal)) keys.Add(binding.EventKey);
        return keys;
    }
}
