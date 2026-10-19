using System.Text.Json;
using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Simulation;

public sealed record ProductionTarget(
    decimal CapacityPerHour,
    string Source,
    IReadOnlyList<string> ProductionOrderNos);

public static class ProductionPlanMatcher
{
    public static ProductionTarget Resolve(
        Equipment device,
        IReadOnlyList<ProductionPlanSnapshot> plans,
        DateTimeOffset at)
    {
        var matched = plans
            .Where(x => x.IsActive && IsInWindow(x, at) && HasWorkCenter(x, device.WorkCenter))
            .Select(x => new
            {
                x.OrderNo,
                Rate = PlannedRate(x)
            })
            .Where(x => x.Rate > 0)
            .ToList();

        if (matched.Count == 0)
            return new ProductionTarget(device.RatedCapacityPerHour, "RatedCapacity", []);

        return new ProductionTarget(
            matched.Sum(x => x.Rate),
            "ProductionPlan",
            matched.Select(x => x.OrderNo).ToList());
    }

    private static bool IsInWindow(ProductionPlanSnapshot plan, DateTimeOffset at) =>
        (plan.PlannedStartAt is null || at >= plan.PlannedStartAt.Value) &&
        (plan.PlannedEndAt is null || at <= plan.PlannedEndAt.Value);

    private static bool HasWorkCenter(ProductionPlanSnapshot plan, string workCenter)
    {
        var workCenters = JsonSerializer.Deserialize<List<string>>(plan.WorkCentersJson) ?? [];
        return workCenters.Any(x => x.Equals(workCenter, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal PlannedRate(ProductionPlanSnapshot plan)
    {
        if (plan.PlannedStartAt is null || plan.PlannedEndAt is null) return 0;
        var hours = (decimal)(plan.PlannedEndAt.Value - plan.PlannedStartAt.Value).TotalHours;
        if (hours <= 0) return 0;
        var remaining = Math.Max(0, plan.PlannedQuantity - plan.CompletedQuantity);
        return remaining / hours;
    }
}
