using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Simulation;

/// <summary>
/// 用设备累计产量计算短窗口产能百分比。只保留进程内采样点，避免把阶段二的窗口计算升级成历史事件模型。
/// </summary>
public sealed class CapacityObservationTracker
{
    private readonly Dictionary<string, Queue<ProductionPoint>> samples = new(StringComparer.OrdinalIgnoreCase);

    public void Record(Equipment device, DateTimeOffset observedAt)
    {
        if (!device.SimulationEnabled || device.Status == EquipmentStatus.Maintenance)
        {
            samples.Remove(device.DeviceCode);
            return;
        }

        if (!samples.TryGetValue(device.DeviceCode, out var points))
            samples[device.DeviceCode] = points = new Queue<ProductionPoint>();
        if (points.Count > 0 && device.ProducedQuantity < points.Last().ProducedQuantity)
            points.Clear();
        if (points.Count == 0 || points.Last().ObservedAt != observedAt)
            points.Enqueue(new ProductionPoint(observedAt, device.ProducedQuantity));
        while (points.Count > 0 && observedAt - points.Peek().ObservedAt > TimeSpan.FromHours(1))
            points.Dequeue();
    }

    public bool TryGetCapacityRatio(
        Equipment device,
        DateTimeOffset observedAt,
        int windowSeconds,
        decimal targetCapacityPerHour,
        out decimal ratio)
    {
        ratio = 0;
        if (windowSeconds <= 0 || !samples.TryGetValue(device.DeviceCode, out var points) || points.Count < 2)
            return false;

        var window = TimeSpan.FromSeconds(windowSeconds);
        var oldest = points.FirstOrDefault(x => observedAt - x.ObservedAt >= window);
        if (oldest.ObservedAt == default || targetCapacityPerHour <= 0)
            return false;
        var elapsedHours = (decimal)(observedAt - oldest.ObservedAt).TotalHours;
        if (elapsedHours <= 0) return false;
        var actualPerHour = Math.Max(0, device.ProducedQuantity - oldest.ProducedQuantity) / elapsedHours;
        ratio = actualPerHour / targetCapacityPerHour * 100m;
        return true;
    }

    private readonly record struct ProductionPoint(DateTimeOffset ObservedAt, decimal ProducedQuantity);
}
