using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/simulation")]
public sealed class SimulationController(
    LumenGridDbContext db,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    SimulationControlState controlState,
    EquipmentEventWriter eventWriter) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult Status([FromQuery] int limit = 20)
    {
        if (!ControlApiEnabled()) return NotFound();
        return Ok(controlState.Snapshot(limit));
    }

    [HttpGet("signals")]
    public IActionResult Signals([FromQuery] int limit = 20)
    {
        if (!ControlApiEnabled()) return NotFound();
        return Ok(controlState.Snapshot(limit).RecentSignals);
    }

    [HttpPut("settings")]
    public IActionResult UpdateSettings(UpdateSimulationSettingsRequest request)
    {
        if (!ControlApiEnabled()) return NotFound();
        controlState.UpdateSettings(request.IntervalSeconds, request.TimeMultiplier);
        return Ok(controlState.Snapshot());
    }

    [HttpPost("pause")]
    public IActionResult Pause()
    {
        if (!ControlApiEnabled()) return NotFound();
        controlState.Pause();
        return Ok(controlState.Snapshot());
    }

    [HttpPost("resume")]
    public IActionResult Resume()
    {
        if (!ControlApiEnabled()) return NotFound();
        controlState.Resume();
        return Ok(controlState.Snapshot());
    }

    [HttpPost("devices/{deviceCode}/start")]
    public Task<IActionResult> Start(string deviceCode, CancellationToken cancellationToken) =>
        Update(deviceCode, device =>
        {
            device.SimulationEnabled = true;
            device.Scenario = SimulationScenario.Normal;
            device.Status = EquipmentStatus.Starting;
            device.CanReceiveProduction = false;
            device.FaultCode = string.Empty;
        }, cancellationToken, EquipmentEventType.DeviceStarted, "设备模拟启动");

    [HttpPost("devices/{deviceCode}/stop")]
    public Task<IActionResult> Stop(string deviceCode, CancellationToken cancellationToken) =>
        Update(deviceCode, device =>
        {
            device.SimulationEnabled = false;
            device.Scenario = SimulationScenario.Normal;
            device.Status = EquipmentStatus.Stopped;
            device.CanReceiveProduction = false;
            device.FaultCode = string.Empty;
        }, cancellationToken, EquipmentEventType.DeviceStopped, "设备模拟停止");

    [HttpPost("devices/{deviceCode}/scenarios/high-temperature")]
    public Task<IActionResult> HighTemperature(string deviceCode, CancellationToken cancellationToken) => SetScenario(deviceCode, SimulationScenario.HighTemperature, cancellationToken, device =>
    {
        device.TemperatureCelsius = Math.Max(device.TemperatureCelsius, 75);
    });

    [HttpPost("devices/{deviceCode}/scenarios/vibration")]
    public Task<IActionResult> Vibration(string deviceCode, CancellationToken cancellationToken) => SetScenario(deviceCode, SimulationScenario.Vibration, cancellationToken, device =>
    {
        device.VibrationMillimetersPerSecond = Math.Max(device.VibrationMillimetersPerSecond, 6);
    });

    [HttpPost("devices/{deviceCode}/scenarios/offline")]
    public Task<IActionResult> Offline(string deviceCode, CancellationToken cancellationToken) => SetScenario(deviceCode, SimulationScenario.Offline, cancellationToken);

    [HttpPost("devices/{deviceCode}/scenarios/capacity-drop")]
    public Task<IActionResult> CapacityDrop(string deviceCode, CancellationToken cancellationToken) => SetScenario(deviceCode, SimulationScenario.CapacityDrop, cancellationToken);

    [HttpPost("devices/{deviceCode}/recover")]
    public Task<IActionResult> Recover(string deviceCode, CancellationToken cancellationToken) =>
        Update(deviceCode, device =>
        {
            device.SimulationEnabled = true;
            device.Scenario = SimulationScenario.Normal;
            device.Status = EquipmentStatus.Starting;
            device.CanReceiveProduction = false;
            device.FaultCode = string.Empty;
            device.TemperatureCelsius = Math.Min(device.TemperatureCelsius, 70);
            device.VibrationMillimetersPerSecond = Math.Min(device.VibrationMillimetersPerSecond, 3);
        }, cancellationToken, EquipmentEventType.DeviceRecovered, "设备恢复");

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var devices = await db.Equipments.ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            ResetDevice(device);
            eventWriter.AppendEquipment(db, device, EquipmentEventType.DeviceReset, DateTimeOffset.UtcNow, "operator", "设备状态重置");
        }
        await db.SaveChangesAsync(cancellationToken);
        controlState.ClearSignals();
        return Ok(new { reset = true, count = devices.Count });
    }

    [HttpPost("presets/demo")]
    public async Task<IActionResult> LoadDemoPreset(CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var devices = await db.Equipments.ToListAsync(cancellationToken);
        foreach (var device in devices)
        {
            ResetDevice(device);
            eventWriter.AppendEquipment(db, device, EquipmentEventType.DeviceReset, DateTimeOffset.UtcNow, "operator", "加载演示预置前重置设备");
            device.SimulationEnabled = device.DeviceCode is "LINE-A-01" or "TEST-01" or "PACK-01";
            if (device.SimulationEnabled)
            {
                device.Status = EquipmentStatus.Starting;
                eventWriter.AppendEquipment(db, device, EquipmentEventType.DeviceStarted, DateTimeOffset.UtcNow, "operator", "加载演示预置并启动设备");
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        controlState.ClearSignals();
        controlState.Resume();
        return Ok(new { loaded = true, activeDevices = devices.Count(x => x.SimulationEnabled) });
    }

    private Task<IActionResult> SetScenario(string deviceCode, SimulationScenario scenario, CancellationToken cancellationToken, Action<Equipment>? prepare = null) =>
        Update(deviceCode, device =>
        {
            device.SimulationEnabled = true;
            device.Scenario = scenario;
            device.Status = scenario == SimulationScenario.Offline ? EquipmentStatus.Offline : EquipmentStatus.Running;
            device.CanReceiveProduction = scenario != SimulationScenario.Offline;
            prepare?.Invoke(device);
        }, cancellationToken, EquipmentEventType.ScenarioChanged, $"模拟场景切换为 {scenario}");

    private async Task<IActionResult> Update(
        string deviceCode,
        Action<Equipment> update,
        CancellationToken cancellationToken,
        EquipmentEventType eventType,
        string summary)
    {
        if (!ControlApiEnabled()) return NotFound();
        var device = await db.Equipments.SingleOrDefaultAsync(x => x.DeviceCode == deviceCode, cancellationToken);
        if (device is null) return NotFound(new { message = "设备不存在" });
        update(device);
        eventWriter.AppendEquipment(db, device, eventType, DateTimeOffset.UtcNow, "operator", summary);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { deviceCode = device.DeviceCode, status = device.Status, scenario = device.Scenario });
    }

    private bool ControlApiEnabled() => environment.IsDevelopment() || configuration.GetValue<bool>("Simulation:ControlApiEnabled");

    private static void ResetDevice(Equipment device)
    {
        device.SimulationEnabled = false;
        device.Scenario = SimulationScenario.Normal;
        device.Status = EquipmentStatus.Stopped;
        device.CanReceiveProduction = false;
        device.LastHeartbeat = null;
        device.SnapshotAt = DateTimeOffset.UtcNow;
        device.TemperatureCelsius = 25;
        device.CurrentAmps = 0;
        device.VibrationMillimetersPerSecond = 0;
        device.ProducedQuantity = 0;
        device.GoodQuantity = 0;
        device.DefectQuantity = 0;
        device.CycleSeconds = 3600m / device.RatedCapacityPerHour;
        device.FaultCode = string.Empty;
    }
}
