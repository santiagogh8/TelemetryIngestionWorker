using Xunit;
using Telemetry.Worker.Services;
using Telemetry.Worker.Contracts;

namespace Telemetry.Worker.Tests;

public class ValidadorEventoTest
{
    private readonly ValidadorEvento _validator = new();

    [Fact]
    public void Validate_RejectsNullEvent()
    {
        var (isValid, error) = _validator.Validar(null);
        Assert.False(isValid);
        Assert.Contains("null", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RejectsNegativeFuelLevel()
    {
        var evt = new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            DeviceId = "device-1",
            Timestamp = DateTimeOffset.UtcNow,
            Lat = 0.0,
            Lng = 0.0,
            SpeedKph = 50,
            FuelLevel = -0.1,
            EngineOn = true
        };

        var (isValid, error) = _validator.Validar(evt);
        Assert.False(isValid);
        Assert.Contains("FuelLevel", error);
    }

    [Fact]
    public void Validate_RejectsImpossiblyHighSpeeds()
    {
        var evt = new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            DeviceId = "device-1",
            Timestamp = DateTimeOffset.UtcNow,
            Lat = 0.0,
            Lng = 0.0,
            SpeedKph = 9999,
            FuelLevel = 0.5,
            EngineOn = true
        };

        var (isValid, error) = _validator.Validar(evt);
        Assert.False(isValid);
        Assert.Contains("SpeedKph", error);
    }

    [Fact]
    public void Validate_AcceptsValidEvent()
    {
        var evt = new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            DeviceId = "vehicle-1234",
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
            Lat = -0.1807,
            Lng = -78.4678,
            SpeedKph = 82.5,
            FuelLevel = 0.43,
            EngineOn = true
        };

        var (isValid, error) = _validator.Validar(evt);
        Assert.True(isValid);
    }
}