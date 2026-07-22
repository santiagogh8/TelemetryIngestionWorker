using System.Collections.Concurrent;
using Telemetry.Worker.Interfaces;

namespace Telemetry.Worker.Services;

/// <summary>
/// Almacén en memoria para detección de duplicados (idempotencia).
/// Implementa una ventana deslizante basada en timestamp.
/// Para producción, reemplazar por Redis u otra cache distribuida.
/// </summary>
public sealed class AlmacenDuplicado : IAlmacenDuplicado, IDisposable
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _vistos = new();
    private readonly int _ventanaSegundos = 3600; // 1 hora
    private readonly Timer _timerLimpieza;

    public AlmacenDuplicado()
    {
        // Limpieza periódica cada 10 minutos
        _timerLimpieza = new Timer(_ => LimpiarAntiguos(), null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public bool EsEventoNuevo(Guid eventId) => !_vistos.ContainsKey(eventId);

    public void MarcarVisto(Guid eventId) => _vistos[eventId] = DateTimeOffset.UtcNow;

    private void LimpiarAntiguos()
    {
        var corte = DateTimeOffset.UtcNow.AddSeconds(-_ventanaSegundos);
        var claves = _vistos.Where(kvp => kvp.Value < corte).Select(kvp => kvp.Key).ToList();
        foreach (var k in claves) _vistos.TryRemove(k, out _);
    }

    public void Dispose() => _timerLimpieza.Dispose();
}