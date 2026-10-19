using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Realtime;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/maintenance-tasks")]
public sealed class MaintenanceTasksController(
    LumenGridDbContext db,
    IHubContext<EquipmentHub> hub,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    EquipmentEventWriter eventWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MaintenanceTaskResponse>>> List([FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = db.MaintenanceTasks.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.Status == MaintenanceStatus.Created || x.Status == MaintenanceStatus.InProgress);
        var tasks = await query.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(cancellationToken);
        return Ok(tasks.Select(MaintenanceTaskResponse.From).ToList());
    }

    [HttpGet("{taskNo}")]
    public async Task<ActionResult<MaintenanceTaskResponse>> Get(string taskNo, CancellationToken cancellationToken)
    {
        var task = await db.MaintenanceTasks.AsNoTracking().SingleOrDefaultAsync(x => x.TaskNo == taskNo, cancellationToken);
        return task is null ? NotFound(new { message = "维护任务不存在" }) : Ok(MaintenanceTaskResponse.From(task));
    }

    [HttpPost]
    public async Task<ActionResult<MaintenanceTaskResponse>> Create(CreateMaintenanceTaskRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        if (string.IsNullOrWhiteSpace(request.DeviceCode))
            return BadRequest(new { message = "设备编码不能为空" });
        var device = await db.Equipments.SingleOrDefaultAsync(x => x.DeviceCode == request.DeviceCode, cancellationToken);
        if (device is null) return NotFound(new { message = "设备不存在" });
        if (!string.IsNullOrWhiteSpace(request.AlertCode) && !await db.EquipmentAlerts.AnyAsync(x => x.AlertCode == request.AlertCode, cancellationToken))
            return NotFound(new { message = "关联告警不存在" });

        var task = new MaintenanceTask
        {
            TaskNo = $"MT-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}",
            DeviceCode = device.DeviceCode,
            AlertCode = request.AlertCode?.Trim() ?? string.Empty,
            Symptom = string.IsNullOrWhiteSpace(request.Symptom) ? device.FaultCode : request.Symptom.Trim(),
            Technician = request.Technician?.Trim() ?? string.Empty,
            Status = MaintenanceStatus.Created,
            RestoreEquipment = true
        };
        device.Status = EquipmentStatus.Maintenance;
        device.CanReceiveProduction = false;
        if (!string.IsNullOrWhiteSpace(task.AlertCode))
        {
            var alert = await db.EquipmentAlerts.SingleAsync(x => x.AlertCode == task.AlertCode, cancellationToken);
            if (alert.Status is AlertStatus.New or AlertStatus.Confirmed)
            {
                alert.Status = AlertStatus.Processing;
                eventWriter.AppendAlert(db, alert, EquipmentEventType.AlertProcessing, DateTimeOffset.UtcNow, "maintenance", "创建维护任务后进入处理中", device);
            }
            alert.Handler = task.Technician;
        }
        db.MaintenanceTasks.Add(task);
        eventWriter.AppendMaintenance(db, task, device, EquipmentEventType.MaintenanceCreated, DateTimeOffset.UtcNow, "operator", "创建维护任务");
        await db.SaveChangesAsync(cancellationToken);
        await Publish(task);
        return Created($"/api/maintenance-tasks/{task.TaskNo}", MaintenanceTaskResponse.From(task));
    }

    [HttpPost("{taskNo}/start")]
    public async Task<ActionResult<MaintenanceTaskResponse>> Start(string taskNo, AlertActionRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var task = await db.MaintenanceTasks.SingleOrDefaultAsync(x => x.TaskNo == taskNo, cancellationToken);
        if (task is null) return NotFound(new { message = "维护任务不存在" });
        if (task.Status != MaintenanceStatus.Created)
            return Conflict(new { message = "当前维护任务不能开始" });
        task.Status = MaintenanceStatus.InProgress;
        task.StartedAt ??= DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Operator)) task.Technician = request.Operator;
        var device = await db.Equipments.SingleAsync(x => x.DeviceCode == task.DeviceCode, cancellationToken);
        device.Status = EquipmentStatus.Maintenance;
        device.CanReceiveProduction = false;
        eventWriter.AppendMaintenance(db, task, device, EquipmentEventType.MaintenanceStarted, DateTimeOffset.UtcNow, "operator", "开始维护");
        await db.SaveChangesAsync(cancellationToken);
        await Publish(task);
        return Ok(MaintenanceTaskResponse.From(task));
    }

    [HttpPost("{taskNo}/complete")]
    public async Task<ActionResult<MaintenanceTaskResponse>> Complete(string taskNo, CompleteMaintenanceTaskRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var task = await db.MaintenanceTasks.SingleOrDefaultAsync(x => x.TaskNo == taskNo, cancellationToken);
        if (task is null) return NotFound(new { message = "维护任务不存在" });
        if (task.Status is not (MaintenanceStatus.Created or MaintenanceStatus.InProgress))
            return Conflict(new { message = "当前维护任务不能完成" });

        var now = DateTimeOffset.UtcNow;
        task.Status = MaintenanceStatus.Completed;
        task.StartedAt ??= now;
        task.CompletedAt = now;
        task.Cause = request.Cause?.Trim() ?? string.Empty;
        task.ReplacedPart = request.ReplacedPart?.Trim() ?? string.Empty;
        task.Result = request.Result?.Trim() ?? "维修完成";
        task.Technician = string.IsNullOrWhiteSpace(request.Technician) ? task.Technician : request.Technician.Trim();
        task.RestoreEquipment = request.RestoreEquipment;

        var device = await db.Equipments.SingleAsync(x => x.DeviceCode == task.DeviceCode, cancellationToken);
        device.Status = request.RestoreEquipment && device.SimulationEnabled ? EquipmentStatus.Starting : EquipmentStatus.Stopped;
        device.Scenario = SimulationScenario.Normal;
        device.CanReceiveProduction = false;
        device.FaultCode = string.Empty;
        device.TemperatureCelsius = Math.Min(device.TemperatureCelsius, 70);
        device.VibrationMillimetersPerSecond = Math.Min(device.VibrationMillimetersPerSecond, 3);

        if (!string.IsNullOrWhiteSpace(task.AlertCode))
        {
            var alert = await db.EquipmentAlerts.SingleOrDefaultAsync(x => x.AlertCode == task.AlertCode, cancellationToken);
            if (alert is not null && alert.IsActive)
            {
                alert.Status = AlertStatus.Resolved;
                alert.IsActive = false;
                alert.ResolvedAt = now;
                alert.ClosedReason = "维护完成，设备恢复";
                alert.Handler = task.Technician;
                eventWriter.AppendAlert(db, alert, EquipmentEventType.AlertResolved, now, "maintenance", "维护完成，设备恢复", device);
            }
        }
        eventWriter.AppendMaintenance(db, task, device, EquipmentEventType.MaintenanceCompleted, now, "operator", "维护完成");
        await db.SaveChangesAsync(cancellationToken);
        await Publish(task);
        return Ok(MaintenanceTaskResponse.From(task));
    }

    private async Task Publish(MaintenanceTask task) =>
        await hub.Clients.All.SendAsync("MaintenanceChanged", MaintenanceTaskResponse.From(task));

    private bool ControlApiEnabled() => environment.IsDevelopment() || configuration.GetValue<bool>("Simulation:ControlApiEnabled");
}
