using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/alerts")]
public sealed class AlertsController(
    LumenGridDbContext db,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    AlertEvaluationService evaluation,
    EquipmentEventWriter eventWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EquipmentAlertResponse>>> List([FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        var query = db.EquipmentAlerts.AsNoTracking();
        if (activeOnly) query = query.Where(x => x.IsActive);
        var alerts = await query.OrderByDescending(x => x.TriggeredAt).Take(200).ToListAsync(cancellationToken);
        return Ok(alerts.Select(EquipmentAlertResponse.From).ToList());
    }

    [HttpPost("{alertCode}/acknowledge")]
    public Task<IActionResult> Acknowledge(string alertCode, AlertActionRequest request, CancellationToken cancellationToken) =>
        Change(alertCode, alert =>
        {
            if (alert.Status == AlertStatus.New)
                alert.Status = AlertStatus.Confirmed;
            alert.AcknowledgedAt ??= DateTimeOffset.UtcNow;
            alert.Handler = request.Operator;
        }, cancellationToken, EquipmentEventType.AlertAcknowledged);

    [HttpPost("{alertCode}/start-processing")]
    public Task<IActionResult> StartProcessing(string alertCode, AlertActionRequest request, CancellationToken cancellationToken) =>
        Change(alertCode, alert =>
        {
            if (alert.Status is AlertStatus.New or AlertStatus.Confirmed)
                alert.Status = AlertStatus.Processing;
            alert.AcknowledgedAt ??= DateTimeOffset.UtcNow;
            alert.Handler = request.Operator;
        }, cancellationToken, EquipmentEventType.AlertProcessing);

    [HttpPost("{alertCode}/resolve")]
    public Task<IActionResult> Resolve(string alertCode, AlertActionRequest request, CancellationToken cancellationToken) =>
        Change(alertCode, alert =>
        {
            alert.Status = AlertStatus.Resolved;
            alert.IsActive = false;
            alert.ResolvedAt = DateTimeOffset.UtcNow;
            alert.ClosedReason = string.IsNullOrWhiteSpace(request.Reason) ? "人工处理完成" : request.Reason;
            alert.Handler = request.Operator;
        }, cancellationToken, EquipmentEventType.AlertResolved, forgetCondition: true);

    [HttpPost("{alertCode}/close")]
    public Task<IActionResult> Close(string alertCode, AlertActionRequest request, CancellationToken cancellationToken) =>
        Change(alertCode, alert =>
        {
            if (alert.Status != AlertStatus.Resolved)
                throw new InvalidOperationException("只有已解决告警才能关闭");
            alert.Status = AlertStatus.Closed;
            alert.ClosedReason = string.IsNullOrWhiteSpace(request.Reason) ? "告警已关闭" : request.Reason;
            alert.Handler = request.Operator;
        }, cancellationToken, EquipmentEventType.AlertClosed, forgetCondition: true);

    private async Task<IActionResult> Change(
        string alertCode,
        Action<EquipmentAlert> change,
        CancellationToken cancellationToken,
        EquipmentEventType eventType,
        bool forgetCondition = false)
    {
        if (!ControlApiEnabled()) return NotFound();
        var alert = await db.EquipmentAlerts.SingleOrDefaultAsync(x => x.AlertCode == alertCode, cancellationToken);
        if (alert is null) return NotFound(new { message = "告警不存在" });
        try
        {
            change(alert);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        var equipment = await db.Equipments.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceCode == alert.DeviceCode, cancellationToken);
        eventWriter.AppendAlert(db, alert, eventType, DateTimeOffset.UtcNow, "operator", $"告警状态变更为 {alert.Status}", equipment);
        await db.SaveChangesAsync(cancellationToken);
        if (forgetCondition) evaluation.SuppressUntilClear(alert.RuleCode, alert.DeviceCode);
        return Ok(EquipmentAlertResponse.From(alert));
    }

    private bool ControlApiEnabled() => environment.IsDevelopment() || configuration.GetValue<bool>("Simulation:ControlApiEnabled");
}
