using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Realtime;

namespace LumenGrid.Api.Simulation;

public sealed class EquipmentSimulationHostedService(
    IServiceScopeFactory scopeFactory,
    IHubContext<EquipmentHub> hub,
    IConfiguration configuration,
    SimulationControlState controlState,
    AlertEvaluationService alertEvaluation,
    EquipmentEventWriter eventWriter,
    ILogger<EquipmentSimulationHostedService> logger) : BackgroundService
{
    private readonly Dictionary<string, Random> randomByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly int randomSeed = configuration.GetValue("Simulation:RandomSeed", 20260920);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (controlState.IsPaused)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                continue;
            }

            var intervalSeconds = controlState.IntervalSeconds;
            var timeMultiplier = controlState.TimeMultiplier;
            await TickAsync(intervalSeconds, timeMultiplier, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    private async Task TickAsync(int intervalSeconds, decimal timeMultiplier, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LumenGridDbContext>();
            var equipment = await db.Equipments.ToListAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var signals = new List<SimulationSignalResponse>();

            foreach (var device in equipment)
            {
                var previousStatus = device.Status;
                var previousScenario = device.Scenario;
                var previousHeartbeat = device.LastHeartbeat;
                var previousCanReceiveProduction = device.CanReceiveProduction;
                var previousFaultCode = device.FaultCode;
                Advance(device, now, intervalSeconds * (double)timeMultiplier);
                if (device.SimulationEnabled)
                {
                    if (previousHeartbeat is null && device.LastHeartbeat is not null)
                        eventWriter.AppendEquipment(db, device, EquipmentEventType.DeviceOnline, now, "simulation", "设备上线");
                    if (previousStatus != device.Status ||
                        previousCanReceiveProduction != device.CanReceiveProduction ||
                        !string.Equals(previousFaultCode, device.FaultCode, StringComparison.Ordinal))
                    {
                        eventWriter.AppendEquipment(
                            db,
                            device,
                            device.Status == EquipmentStatus.Offline ? EquipmentEventType.DeviceOffline : EquipmentEventType.StatusChanged,
                            now,
                            "simulation",
                            $"设备状态由 {previousStatus} 变为 {device.Status}",
                            details: new { previousStatus, currentStatus = device.Status, previousFaultCode, currentFaultCode = device.FaultCode });
                    }
                    if (previousScenario != device.Scenario)
                        eventWriter.AppendEquipment(db, device, EquipmentEventType.ScenarioChanged, now, "simulation", $"模拟场景切换为 {device.Scenario}");
                    eventWriter.AppendEquipment(db, device, EquipmentEventType.MetricSample, now, "simulation", "设备指标采集");
                }
                if (device.SimulationEnabled)
                    signals.Add(SimulationSignalResponse.From(device, now));
            }

            var alertChanges = await alertEvaluation.EvaluateAsync(db, equipment, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            controlState.RecordBatch(signals, now);
            if (equipment.Count > 0)
            {
                await hub.Clients.All.SendAsync("EquipmentSnapshotChanged", equipment.Select(EquipmentSnapshotResponse.From), cancellationToken);
                await hub.Clients.All.SendAsync("SimulationSignalBatchChanged", controlState.Snapshot(20), cancellationToken);
                foreach (var alert in alertChanges.DistinctBy(x => x.AlertCode))
                    await hub.Clients.All.SendAsync("AlertChanged", EquipmentAlertResponse.From(alert), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LumenGrid 设备模拟器本轮更新失败");
        }
    }

    private void Advance(Equipment device, DateTimeOffset now, double seconds)
    {
        if (!device.SimulationEnabled) return;

        if (device.Scenario == SimulationScenario.Offline)
        {
            device.Status = EquipmentStatus.Offline;
            device.CanReceiveProduction = false;
            device.FaultCode = "OFFLINE";
            device.SnapshotAt = now;
            return;
        }

        if (device.Status == EquipmentStatus.Maintenance)
        {
            device.CanReceiveProduction = false;
            device.LastHeartbeat = now;
            device.SnapshotAt = now;
            return;
        }

        if (device.Status is EquipmentStatus.Stopped or EquipmentStatus.Offline or EquipmentStatus.Faulted)
            device.Status = EquipmentStatus.Starting;
        if (device.Status == EquipmentStatus.Starting)
            device.Status = EquipmentStatus.Running;

        var random = GetRandom(device.DeviceCode);
        var noise = (decimal)(random.NextDouble() - 0.5);
        var isDegraded = device.Scenario is SimulationScenario.HighTemperature or SimulationScenario.Vibration or SimulationScenario.CapacityDrop;
        device.Status = isDegraded ? EquipmentStatus.Degraded : EquipmentStatus.Running;
        device.CanReceiveProduction = true;
        device.LastHeartbeat = now;
        device.SnapshotAt = now;

        device.TemperatureCelsius = device.Scenario switch
        {
            SimulationScenario.HighTemperature => Math.Min(105, device.TemperatureCelsius + 2.5m + Math.Abs(noise)),
            SimulationScenario.CapacityDrop => Math.Max(60, 66 + noise),
            _ => MoveTowards(device.TemperatureCelsius, 65 + noise * 2, 1.5m)
        };
        device.CurrentAmps = device.Scenario == SimulationScenario.Vibration ? 13 + noise * 2 : 10 + noise;
        device.VibrationMillimetersPerSecond = device.Scenario == SimulationScenario.Vibration
            ? Math.Min(12, device.VibrationMillimetersPerSecond + 0.8m + Math.Abs(noise))
            : MoveTowards(device.VibrationMillimetersPerSecond, 2.2m, 0.35m);
        device.CycleSeconds = 3600m / device.RatedCapacityPerHour * (device.Scenario == SimulationScenario.CapacityDrop ? 1.35m : 1m);

        var outputFactor = device.Scenario == SimulationScenario.CapacityDrop ? 0.55m : device.Scenario == SimulationScenario.Vibration ? 0.85m : 1m;
        var produced = device.RatedCapacityPerHour / 3600m * (decimal)seconds * outputFactor;
        var defectRate = device.Scenario is SimulationScenario.HighTemperature or SimulationScenario.Vibration ? 0.08m : 0.02m;
        var defect = produced * defectRate;
        device.ProducedQuantity += produced;
        device.DefectQuantity += defect;
        device.GoodQuantity += produced - defect;
        device.FaultCode = device.Scenario switch
        {
            SimulationScenario.HighTemperature when device.TemperatureCelsius >= 85 => "HIGH_TEMPERATURE",
            SimulationScenario.Vibration when device.VibrationMillimetersPerSecond >= 8 => "HIGH_VIBRATION",
            _ => string.Empty
        };
        if (!string.IsNullOrEmpty(device.FaultCode))
        {
            device.Status = EquipmentStatus.Faulted;
            device.CanReceiveProduction = false;
        }
    }

    private Random GetRandom(string deviceCode)
    {
        if (randomByDevice.TryGetValue(deviceCode, out var random)) return random;
        var hash = 17;
        foreach (var character in deviceCode) hash = unchecked(hash * 31 + character);
        random = new Random(unchecked(randomSeed + hash));
        randomByDevice[deviceCode] = random;
        return random;
    }

    private static decimal MoveTowards(decimal current, decimal target, decimal step) =>
        Math.Abs(target - current) <= step ? target : current + Math.Sign(target - current) * step;
}
