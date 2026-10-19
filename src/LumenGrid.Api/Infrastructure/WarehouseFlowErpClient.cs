using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LumenGrid.Api.Contracts;

namespace LumenGrid.Api.Infrastructure;

public sealed class WarehouseFlowErpClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<WarehouseFlowErpClient> logger)
{
    private string? accessToken;
    private DateTimeOffset tokenExpiresAt;

    public async Task<IReadOnlyList<WarehouseFlowProductionOrderSnapshot>> GetProductionOrdersAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/integration/mes/production-orders", null, cancellationToken);
        return await ReadAsync<List<WarehouseFlowProductionOrderSnapshot>>(response, cancellationToken) ?? [];
    }

    public async Task<IReadOnlyList<WarehouseFlowProductionOrderEvent>> GetProductionOrderEventsAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/integration/mes/production-order-events?afterId={afterId}&limit={limit}", null, cancellationToken);
        return await ReadAsync<List<WarehouseFlowProductionOrderEvent>>(response, cancellationToken) ?? [];
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("WarehouseFlow ERP 调用失败 {Path} {StatusCode}: {Body}", path, (int)response.StatusCode, body);
        throw new WarehouseFlowErpException((int)response.StatusCode, $"WarehouseFlow ERP 调用失败（{(int)response.StatusCode}）：{body}");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(accessToken) && tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return accessToken;

        var userName = configuration["Erp:UserName"];
        var password = configuration["Erp:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("LumenGrid 未配置 ERP 账号，请设置 Erp:UserName 和 Erp:Password");

        using var response = await http.PostAsJsonAsync("/api/auth/token", new { userName, password }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"LumenGrid 登录 WarehouseFlow 失败（{(int)response.StatusCode}）：{body}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        accessToken = document.RootElement.GetProperty("accessToken").GetString();
        var expiresIn = document.RootElement.TryGetProperty("expiresIn", out var expiresElement) ? expiresElement.GetInt32() : 3600;
        tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
        return accessToken ?? throw new InvalidOperationException("WarehouseFlow 登录未返回 accessToken");
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
}

public sealed class WarehouseFlowErpException(int statusCode, string message) : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
}
