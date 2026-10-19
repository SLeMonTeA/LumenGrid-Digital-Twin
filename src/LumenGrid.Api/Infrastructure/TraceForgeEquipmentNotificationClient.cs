using System.Net.Http.Json;
using LumenGrid.Api.Contracts;

namespace LumenGrid.Api.Infrastructure;

public sealed class TraceForgeEquipmentNotificationClient(HttpClient http, ILogger<TraceForgeEquipmentNotificationClient> logger)
{
    private readonly string integrationKey = http.DefaultRequestHeaders.TryGetValues("X-Integration-Key", out var values)
        ? values.FirstOrDefault() ?? string.Empty
        : string.Empty;

    public async Task PublishAsync(MesEquipmentNotificationRequest request, CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/integration/lumengrid/equipment-notifications")
        {
            Content = JsonContent.Create(request)
        };
        if (!string.IsNullOrWhiteSpace(integrationKey))
            httpRequest.Headers.TryAddWithoutValidation("X-Integration-Key", integrationKey);
        using var response = await http.SendAsync(httpRequest, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("TraceForge 设备通知投递失败 {EventKey} {StatusCode}: {Body}", request.EventKey, (int)response.StatusCode, body);
        throw new TraceForgeNotificationException((int)response.StatusCode, $"TraceForge 设备通知接口失败（{(int)response.StatusCode}）：{body}");
    }
}

public sealed class TraceForgeNotificationException(int statusCode, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
}
