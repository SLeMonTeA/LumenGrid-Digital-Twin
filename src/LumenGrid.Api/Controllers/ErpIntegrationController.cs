using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/integration/erp")]
public sealed class ErpIntegrationController(
    LumenGridDbContext db,
    EquipmentEventWriter eventWriter,
    ErpProductionPlanSynchronizationService synchronizer) : ControllerBase
{
    [HttpGet("production-plans")]
    public async Task<ActionResult<IReadOnlyList<ProductionPlanSnapshotResponse>>> ListPlans(
        [FromQuery] string? workCenter = null,
        [FromQuery] bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var plans = await db.ProductionPlanSnapshots.AsNoTracking()
            .OrderBy(x => x.RequiredAt)
            .ToListAsync(cancellationToken);
        if (activeOnly)
            plans = plans.Where(x => x.IsActive).ToList();
        if (!string.IsNullOrWhiteSpace(workCenter))
            plans = plans.Where(x => ProductionPlanSnapshotResponse.From(x).WorkCenters.Contains(workCenter, StringComparer.OrdinalIgnoreCase)).ToList();
        return Ok(plans.Select(ProductionPlanSnapshotResponse.From).ToList());
    }

    [HttpPost("production-plans")]
    public async Task<IActionResult> AcceptPlan(ErpProductionPlanRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventKey) || string.IsNullOrWhiteSpace(request.OrderNo))
            return BadRequest(new { message = "EventKey 和 OrderNo 不能为空" });
        if (request.PlannedQuantity < 0 || request.CompletedQuantity < 0 ||
            request.ReportedGoodQuantity < 0 || request.ReportedDefectQuantity < 0)
            return BadRequest(new { message = "生产计划数量不能为负数" });

        var eventKey = request.EventKey.Trim();
        var duplicate = await db.ProductionPlanSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(x => x.LastEventKey == eventKey, cancellationToken);
        if (duplicate is not null)
            return Ok(new { accepted = true, duplicate = true, plan = ProductionPlanSnapshotResponse.From(duplicate) });

        var orderNo = request.OrderNo.Trim();
        var plan = await db.ProductionPlanSnapshots
            .SingleOrDefaultAsync(x => x.OrderNo == orderNo, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var occurredAt = request.OccurredAt ?? now;
        var workCenters = (request.WorkCenters ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var eventType = request.EventType.Trim();
        var isCancelled = eventType.Equals("ProductionOrderCancelled", StringComparison.OrdinalIgnoreCase) ||
                          eventType.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);

        if (plan is null)
        {
            plan = new ProductionPlanSnapshot { OrderNo = orderNo };
            db.ProductionPlanSnapshots.Add(plan);
        }

        plan.ProductSku = request.ProductSku.Trim();
        plan.ProductName = request.ProductName.Trim();
        plan.Status = request.Status.Trim();
        plan.PlannedQuantity = request.PlannedQuantity;
        plan.CompletedQuantity = request.CompletedQuantity;
        plan.ReportedGoodQuantity = request.ReportedGoodQuantity;
        plan.ReportedDefectQuantity = request.ReportedDefectQuantity;
        plan.RequiredAt = request.RequiredAt;
        plan.PlannedStartAt = request.PlannedStartAt;
        plan.PlannedEndAt = request.PlannedEndAt;
        plan.WorkCentersJson = System.Text.Json.JsonSerializer.Serialize(workCenters);
        plan.LastEventKey = eventKey;
        plan.LastEventAt = occurredAt;
        plan.SyncedAt = now;
        plan.IsActive = !isCancelled;

        var equipment = await db.Equipments.AsNoTracking()
            .Where(x => workCenters.Contains(x.WorkCenter))
            .ToListAsync(cancellationToken);
        eventWriter.AppendPlan(
            db,
            plan,
            workCenters.FirstOrDefault() ?? string.Empty,
            isCancelled ? EquipmentEventType.ProductionPlanCancelled : plan.Id == 0 ? EquipmentEventType.ProductionPlanPublished : EquipmentEventType.ProductionPlanChanged,
            occurredAt,
            eventType);

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { accepted = true, duplicate = false, plan = ProductionPlanSnapshotResponse.From(plan), matchedEquipment = equipment.Count });
    }

    [HttpPost("production-plans/sync")]
    public async Task<ActionResult<ErpPlanSyncResult>> SyncPlans(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await synchronizer.SyncAsync(cancellationToken));
        }
        catch (WarehouseFlowErpException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ex.Message });
        }
    }

    [HttpGet("production-plans/sync-status")]
    public async Task<ActionResult<ErpPlanSyncStatusResponse>> SyncStatus(CancellationToken cancellationToken) =>
        Ok(await synchronizer.GetStatusAsync(cancellationToken));
}
