using Xunit;
using Telemetry.Worker.Services;
using Telemetry.Worker.Contracts;

namespace Telemetry.Worker.Tests;

public class ConcurrenciaTest
{
    [Fact]
    public async Task AlmacenEstadisticas_ContadoresThreadSafe()
    {
        var store = new AlmacenEstadistica();
        var tasks = new List<Task>();
        int iterations = 1000;

        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    store.RegistrarEventoValido($"device-{j % 5}");
                    store.RegistrarEventoProcesado();
                }
            }));
        }

        await Task.WhenAll(tasks);

        var snapshot = store.ObtenerUltimaEstadistica();
        Assert.Equal(10 * iterations, snapshot.TotalEventoProcesado);
        Assert.Equal(10 * iterations, snapshot.TotalEventoValido);
        Assert.Equal(5, snapshot.EventoPorDispositivo.Count);
    }

    [Fact]
    public void ValidadorEvento_EsThreadSafe()
    {
        var validator = new ValidadorEvento();
        var validEvent = new TelemetryEvent
        {
            EventId = Guid.NewGuid(),
            DeviceId = "device-1",
            Timestamp = DateTimeOffset.UtcNow,
            Lat = 0.0,
            Lng = 0.0,
            SpeedKph = 50,
            FuelLevel = 0.5,
            EngineOn = true
        };

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var (isValid, _) = validator.Validar(validEvent);
                    Assert.True(isValid);
                }
            }));

        Assert.True(Task.WaitAll(tasks.ToArray(), 5000));
    }
}