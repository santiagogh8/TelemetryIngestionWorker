using System.Collections.Concurrent;
using Telemetry.Worker.Contracts;
using Telemetry.Worker.Interfaces;

namespace Telemetry.Worker.Services;

/// <summary>
/// Almacen en memoria para estadisticas del pipeline.
/// Thread-safe.
/// </summary>
public sealed class AlmacenEstadistica : IAlmacenEstadistica
{
    private readonly ConcurrentDictionary<string, long> _porDispositivo = new();
    private long _totalPoison;
    private long _totalDuplicados;
    private long _totalProcesados;

    public void RegistrarEventoValido(string deviceId)
    {
        _porDispositivo.AddOrUpdate(deviceId, 1, (_, valor) => valor + 1);
    }

    public void RegistrarEventoPoison() => Interlocked.Increment(ref _totalPoison);
    public void RegistrarEventoDuplicado() => Interlocked.Increment(ref _totalDuplicados);
    public void RegistrarEventoProcesado() => Interlocked.Increment(ref _totalProcesados);

    public UltimaEstadistica ObtenerUltimaEstadistica()
    {
        return new UltimaEstadistica
        {
            EventoPorDispositivo = _porDispositivo.ToDictionary(k => k.Key, v => v.Value),
            TotalEventoPoison = Interlocked.Read(ref _totalPoison),
            TotalEventoDuplicado = Interlocked.Read(ref _totalDuplicados),
            TotalEventoProcesado = Interlocked.Read(ref _totalProcesados),
            TotalEventoValido = _porDispositivo.Values.Sum(),
            CapturadoEn = DateTimeOffset.UtcNow
        };
    }
}