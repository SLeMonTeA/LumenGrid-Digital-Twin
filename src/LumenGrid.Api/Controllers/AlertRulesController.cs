using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LumenGrid.Api.Contracts;
using LumenGrid.Api.Domain;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Simulation;

namespace LumenGrid.Api.Controllers;

[ApiController]
[Route("api/alert-rules")]
public sealed class AlertRulesController(
    LumenGridDbContext db,
    IWebHostEnvironment environment,
    IConfiguration configuration,
    EquipmentEventWriter eventWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AlertRuleResponse>>> List(CancellationToken cancellationToken)
    {
        var rules = await db.AlertRules.AsNoTracking().OrderBy(x => x.RuleCode).ToListAsync(cancellationToken);
        return Ok(rules.Select(AlertRuleResponse.From).ToList());
    }

    [HttpGet("{ruleCode}")]
    public async Task<ActionResult<AlertRuleResponse>> Get(string ruleCode, CancellationToken cancellationToken)
    {
        var rule = await db.AlertRules.AsNoTracking().SingleOrDefaultAsync(x => x.RuleCode == ruleCode, cancellationToken);
        return rule is null ? NotFound(new { message = "告警规则不存在" }) : Ok(AlertRuleResponse.From(rule));
    }

    [HttpPost]
    public async Task<ActionResult<AlertRuleResponse>> Create(AlertRuleRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        if (string.IsNullOrWhiteSpace(request.RuleCode) || string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "规则编码和名称不能为空" });
        if (request.Threshold < 0 || request.WindowSeconds < 0 || request.DurationSeconds < 0 || request.ConsecutiveSamples < 0)
            return BadRequest(new { message = "阈值、统计窗口、持续时间和连续采样次数不能为负数" });
        if (await db.AlertRules.AnyAsync(x => x.RuleCode == request.RuleCode, cancellationToken))
            return Conflict(new { message = "规则编码已存在" });

        var rule = FromRequest(request);
        db.AlertRules.Add(rule);
        eventWriter.AppendRule(db, rule, DateTimeOffset.UtcNow, "operator", "创建告警规则");
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/alert-rules/{rule.RuleCode}", AlertRuleResponse.From(rule));
    }

    [HttpPut("{ruleCode}")]
    public async Task<ActionResult<AlertRuleResponse>> Update(string ruleCode, AlertRuleRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var rule = await db.AlertRules.SingleOrDefaultAsync(x => x.RuleCode == ruleCode, cancellationToken);
        if (rule is null) return NotFound(new { message = "告警规则不存在" });
        if (request.Threshold < 0 || request.WindowSeconds < 0 || request.DurationSeconds < 0 || request.ConsecutiveSamples < 0)
            return BadRequest(new { message = "阈值、统计窗口、持续时间和连续采样次数不能为负数" });

        rule.Name = request.Name;
        rule.Metric = request.Metric;
        rule.Comparison = request.Comparison;
        rule.Threshold = request.Threshold;
        rule.WindowSeconds = request.WindowSeconds;
        rule.DurationSeconds = request.DurationSeconds;
        rule.ConsecutiveSamples = request.ConsecutiveSamples;
        rule.Severity = request.Severity;
        rule.Enabled = request.Enabled;
        rule.WorkCenter = request.WorkCenter ?? string.Empty;
        rule.Description = request.Description ?? string.Empty;
        eventWriter.AppendRule(db, rule, DateTimeOffset.UtcNow, "operator", "修改告警规则");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(AlertRuleResponse.From(rule));
    }

    [HttpPut("{ruleCode}/enabled")]
    public async Task<ActionResult<AlertRuleResponse>> SetEnabled(string ruleCode, SetAlertRuleEnabledRequest request, CancellationToken cancellationToken)
    {
        if (!ControlApiEnabled()) return NotFound();
        var rule = await db.AlertRules.SingleOrDefaultAsync(x => x.RuleCode == ruleCode, cancellationToken);
        if (rule is null) return NotFound(new { message = "告警规则不存在" });
        rule.Enabled = request.Enabled;
        eventWriter.AppendRule(db, rule, DateTimeOffset.UtcNow, "operator", request.Enabled ? "启用告警规则" : "停用告警规则");
        await db.SaveChangesAsync(cancellationToken);
        return Ok(AlertRuleResponse.From(rule));
    }

    private bool ControlApiEnabled() => environment.IsDevelopment() || configuration.GetValue<bool>("Simulation:ControlApiEnabled");

    private static AlertRule FromRequest(AlertRuleRequest request) => new()
    {
        RuleCode = request.RuleCode.Trim(),
        Name = request.Name.Trim(),
        Metric = request.Metric,
        Comparison = request.Comparison,
        Threshold = request.Threshold,
        WindowSeconds = request.WindowSeconds,
        DurationSeconds = request.DurationSeconds,
        ConsecutiveSamples = request.ConsecutiveSamples,
        Severity = request.Severity,
        Enabled = request.Enabled,
        WorkCenter = request.WorkCenter?.Trim() ?? string.Empty,
        Description = request.Description?.Trim() ?? string.Empty
    };
}
