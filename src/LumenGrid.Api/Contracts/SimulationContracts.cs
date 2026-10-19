using LumenGrid.Api.Domain;

namespace LumenGrid.Api.Contracts;

public sealed record UpdateSimulationSettingsRequest(int IntervalSeconds, decimal TimeMultiplier);

public sealed record SimulationSignalResponse(
    string DeviceCode,
    DateTimeOffset SentAt,
    EquipmentStatus Status,
    SimulationScenario Scenario,
    decimal TemperatureCelsius,
    decimal CurrentAmps,
    decimal VibrationMillimetersPerSecond,
    decimal ProducedQuantity,
    decimal GoodQuantity,
    decimal DefectQuantity,
    string FaultCode)
{
    public static SimulationSignalResponse From(Equipment equipment, DateTimeOffset sentAt) => new(
        equipment.DeviceCode,
        sentAt,
        equipment.Status,
        equipment.Scenario,
        equipment.TemperatureCelsius,
        equipment.CurrentAmps,
        equipment.VibrationMillimetersPerSecond,
        equipment.ProducedQuantity,
        equipment.GoodQuantity,
        equipment.DefectQuantity,
        equipment.FaultCode);
}

public sealed record SimulationStatusResponse(
    bool Paused,
    int IntervalSeconds,
    decimal TimeMultiplier,
    DateTimeOffset? LastTickAt,
    IReadOnlyList<SimulationSignalResponse> LastBatch,
    IReadOnlyList<SimulationSignalResponse> RecentSignals);
