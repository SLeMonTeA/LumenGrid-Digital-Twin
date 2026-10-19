using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using LumenGrid.Api.Infrastructure;
using LumenGrid.Api.Realtime;
using LumenGrid.Api.Simulation;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddDbContext<LumenGridDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LumenGridDb")));
builder.Services.AddHttpClient<WarehouseFlowErpClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Erp:BaseUrl"] ?? "http://localhost:5101");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<TraceForgeEquipmentNotificationClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Mes:BaseUrl"] ?? "http://localhost:5102");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.TryAddWithoutValidation("X-Integration-Key", builder.Configuration["Integration:SharedKey"] ?? "");
});
builder.Services.AddScoped<ErpProductionPlanSynchronizationService>();
builder.Services.AddScoped<MesEquipmentNotificationDeliveryService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SimulationControlState>();
builder.Services.AddSingleton<CapacityObservationTracker>();
builder.Services.AddSingleton<EquipmentEventWriter>();
builder.Services.AddSingleton<AlertEvaluationService>();
builder.Services.AddHostedService<EquipmentSimulationHostedService>();
builder.Services.AddHostedService<ErpProductionPlanSyncHostedService>();
builder.Services.AddHostedService<MesEquipmentNotificationDeliveryHostedService>();
builder.Services.AddCors(options => options.AddPolicy("LumenGridWeb", policy =>
    policy.WithOrigins("http://localhost:5175")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await LumenGridDbInitializer.EnsureCreatedAndSeedAsync(scope.ServiceProvider.GetRequiredService<LumenGridDbContext>());

app.UseCors("LumenGridWeb");
app.MapControllers();
app.MapHub<EquipmentHub>("/hubs/equipment");
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options.WithTitle("LumenGrid Equipment API"));
}

app.MapGet("/", () => Results.Ok(new
{
    service = "LumenGrid Equipment API",
    status = "running",
    endpoints = new[] { "/api/equipment", "/api/equipment/summary", "/api/events", "/api/integration/erp/production-plans", "/api/integration/mes/equipment-availability", "/api/integration/mes/execution-bindings", "/api/integration/mes/equipment-notification-deliveries", "/api/simulation", "/hubs/equipment" }
}));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
