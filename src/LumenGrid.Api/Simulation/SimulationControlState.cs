using LumenGrid.Api.Contracts;

namespace LumenGrid.Api.Simulation;

/// <summary>
/// 仅保存模拟信号中心的运行控制和最近信号窗口，不承担阶段三的历史事件存储职责。
/// </summary>
public sealed class SimulationControlState(IConfiguration configuration)
{
    private const int MaxRecentSignals = 100;
    private readonly object syncRoot = new();
    private bool paused;
    private int intervalSeconds = Math.Clamp(configuration.GetValue("Simulation:IntervalSeconds", 2), 1, 60);
    private decimal timeMultiplier = Math.Clamp(configuration.GetValue("Simulation:TimeMultiplier", 1m), 0.1m, 10m);
    private DateTimeOffset? lastTickAt;
    private List<SimulationSignalResponse> lastBatch = [];
    private readonly LinkedList<SimulationSignalResponse> recentSignals = [];

    public bool IsPaused
    {
        get { lock (syncRoot) return paused; }
    }

    public int IntervalSeconds
    {
        get { lock (syncRoot) return intervalSeconds; }
    }

    public decimal TimeMultiplier
    {
        get { lock (syncRoot) return timeMultiplier; }
    }

    public void Pause()
    {
        lock (syncRoot) paused = true;
    }

    public void Resume()
    {
        lock (syncRoot) paused = false;
    }

    public void UpdateSettings(int requestedIntervalSeconds, decimal requestedTimeMultiplier)
    {
        lock (syncRoot)
        {
            intervalSeconds = Math.Clamp(requestedIntervalSeconds, 1, 60);
            timeMultiplier = Math.Clamp(requestedTimeMultiplier, 0.1m, 10m);
        }
    }

    public void RecordBatch(IReadOnlyList<SimulationSignalResponse> batch, DateTimeOffset tickAt)
    {
        lock (syncRoot)
        {
            lastTickAt = tickAt;
            lastBatch = batch.ToList();
            foreach (var signal in batch)
            {
                recentSignals.AddFirst(signal);
                while (recentSignals.Count > MaxRecentSignals)
                    recentSignals.RemoveLast();
            }
        }
    }

    public void ClearSignals()
    {
        lock (syncRoot)
        {
            lastTickAt = null;
            lastBatch = [];
            recentSignals.Clear();
        }
    }

    public SimulationStatusResponse Snapshot(int limit = 20)
    {
        lock (syncRoot)
        {
            var recent = recentSignals.Take(Math.Clamp(limit, 1, MaxRecentSignals)).ToList();
            return new SimulationStatusResponse(paused, intervalSeconds, timeMultiplier, lastTickAt, lastBatch.ToList(), recent);
        }
    }
}
