using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Realtime;

namespace LumenGrid.Api.Simulation;

/// <summary>
/// 阶段二的最小规则引擎：支持阈值持续时间或连续采样次数，不执行脚本，也不引入消息中间件。
/// </summary>
public sealed class AlertEvaluationService(
    ILogger<AlertEvaluationService> logger,
    CapacityObservationTracker capacityTracker,
    EquipmentEventWriter eventWriter)
{
    private readonly Dictionary<string, ConditionState> conditionStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> suppressedUntilClear = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<EquipmentAlert>> EvaluateAsync(
        LumenGridDbContext db,
        IReadOnlyList<Equipment> equipment,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rules = await db.AlertRules.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
        var activePlans = await db.ProductionPlanSnapshots.AsNoTracking().Where(x => x.IsActive).ToListAsync(cancellationToken);
        var activeAlerts = await db.EquipmentAlerts.Where(x => x.IsActive).ToListAsync(cancellationToken);
        var activeByKey = activeAlerts.ToDictionary(x => Key(x.RuleCode, x.DeviceCode), StringComparer.OrdinalIgnoreCase);
        var changed = new List<EquipmentAlert>();
        var enabledRuleCodes = rules.Select(x => x.RuleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in equipment)
            capacityTracker.Record(device, now);

        foreach (var alert in activeAlerts.Where(x => !enabledRuleCodes.Contains(x.RuleCode)))
        {
            Resolve(alert, now, "规则已停用");
            eventWriter.AppendAlert(db, alert, EquipmentEventType.AlertResolved, now, "rule-engine", "规则已停用", equipment.FirstOrDefault(x => x.DeviceCode.Equals(alert.DeviceCode, StringComparison.OrdinalIgnoreCase)));
            changed.Add(alert);
        }

        foreach (var device in equipment)
        {
            foreach (var active in activeAlerts.Where(x => x.DeviceCode.Equals(device.DeviceCode, StringComparison.OrdinalIgnoreCase)))
            {
                if (!device.SimulationEnabled)
                {
                    Resolve(active, now, "设备模拟已停止");
                    eventWriter.AppendAlert(db, active, EquipmentEventType.AlertResolved, now, "rule-engine", "设备模拟已停止", device);
                    changed.Add(active);
                }
            }

            foreach (var rule in rules)
            {
                if (!string.IsNullOrWhiteSpace(rule.WorkCenter) && !rule.WorkCenter.Equals(device.WorkCenter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var key = Key(rule.RuleCode, device.DeviceCode);
                if (!device.SimulationEnabled)
                {
                    conditionStates.Remove(key);
                    suppressedUntilClear.Remove(key);
                    continue;
                }

                var observation = ReadObservation(rule, device, activePlans, now);
                if (!observation.Ready)
                {
                    conditionStates.Remove(key);
                    continue;
                }
                if (!observation.Matches)
                {
                    conditionStates.Remove(key);
                    suppressedUntilClear.Remove(key);
                    if (activeByKey.TryGetValue(key, out var recovered))
                    {
                        Resolve(recovered, now, "条件已恢复");
                        eventWriter.AppendAlert(db, recovered, EquipmentEventType.AlertResolved, now, "rule-engine", "条件已恢复", device);
                        changed.Add(recovered);
                        activeByKey.Remove(key);
                    }
                    continue;
                }

                var state = conditionStates.TryGetValue(key, out var existing)
                    ? existing
                    : conditionStates[key] = new ConditionState(now);
                state.ConsecutiveSamples++;

                if (activeByKey.TryGetValue(key, out var activeAlert))
                {
                    activeAlert.CurrentValue = observation.Value;
                    activeAlert.LastObservedAt = now;
                    changed.Add(activeAlert);
                    continue;
                }

                if (suppressedUntilClear.Contains(key)) continue;

                if (!MeetsTrigger(rule, state, now)) continue;

                var alert = new EquipmentAlert
                {
                    RuleCode = rule.RuleCode,
                    DeviceCode = device.DeviceCode,
                    WorkCenter = device.WorkCenter,
                    Metric = rule.Metric,
                    Severity = rule.Severity,
                    CurrentValue = observation.Value,
                    Threshold = rule.Threshold,
                    TriggeredAt = now,
                    LastObservedAt = now,
                    Status = AlertStatus.New,
                    IsActive = true
                };
                db.EquipmentAlerts.Add(alert);
                activeByKey[key] = alert;
                eventWriter.AppendAlert(db, alert, EquipmentEventType.AlertTriggered, now, "rule-engine", $"触发告警 {rule.Name}", device);
                changed.Add(alert);
                logger.LogInformation("设备 {DeviceCode} 触发告警 {RuleCode}，当前值 {CurrentValue}", device.DeviceCode, rule.RuleCode, observation.Value);
            }
        }

        return changed;
    }

    public void SuppressUntilClear(string ruleCode, string deviceCode)
    {
        var key = Key(ruleCode, deviceCode);
        conditionStates.Remove(key);
        suppressedUntilClear.Add(key);
    }

    private static bool MeetsTrigger(AlertRule rule, ConditionState state, DateTimeOffset now) =>
        (rule.DurationSeconds <= 0 || now - state.ConditionSince >= TimeSpan.FromSeconds(rule.DurationSeconds))
        && (rule.ConsecutiveSamples <= 0 || state.ConsecutiveSamples >= rule.ConsecutiveSamples);

    private (decimal Value, bool Matches, bool Ready) ReadObservation(
        AlertRule rule,
        Equipment device,
        IReadOnlyList<ProductionPlanSnapshot> activePlans,
        DateTimeOffset now)
    {
        if (rule.Metric == AlertMetric.Capacity)
        {
            var target = ProductionPlanMatcher.Resolve(device, activePlans, now);
            if (!capacityTracker.TryGetCapacityRatio(device, now, rule.WindowSeconds, target.CapacityPerHour, out var ratio))
                return (0, false, false);
            return (ratio, Compare(rule, ratio), true);
        }

        var value = rule.Metric switch
        {
            AlertMetric.Temperature => device.TemperatureCelsius,
            AlertMetric.Vibration => device.VibrationMillimetersPerSecond,
            AlertMetric.Heartbeat => device.LastHeartbeat is null ? 999999m : (decimal)Math.Max(0, (now - device.LastHeartbeat.Value).TotalSeconds),
            _ => 0
        };
        return (value, Compare(rule, value), true);
    }

    private static bool Compare(AlertRule rule, decimal value) => rule.Comparison switch
    {
        AlertComparison.GreaterThan => value > rule.Threshold,
        AlertComparison.LessThan => value < rule.Threshold,
        _ => false
    };

    private static void Resolve(EquipmentAlert alert, DateTimeOffset now, string reason)
    {
        if (!alert.IsActive) return;
        alert.Status = AlertStatus.Resolved;
        alert.IsActive = false;
        alert.ResolvedAt = now;
        alert.LastObservedAt = now;
        alert.ClosedReason = reason;
    }

    private static string Key(string ruleCode, string deviceCode) => $"{ruleCode}::{deviceCode}";

    private sealed class ConditionState(DateTimeOffset conditionSince)
    {
        public DateTimeOffset ConditionSince { get; } = conditionSince;
        public int ConsecutiveSamples { get; set; }
    }
}
