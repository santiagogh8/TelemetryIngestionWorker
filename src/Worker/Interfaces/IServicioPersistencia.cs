namespace Telemetry.Worker.Interfaces
{
    /// <summary>
    /// Servicio que simula persistencia fragil (≈20% fallas).
    /// Implementa retry con backoff exponencial mas jitter y un Circuit Breaker.
    /// </summary>
    public interface IServicioPersistencia
    {
        Task PersistirEventoAsync(Guid eventId, string deviceId, CancellationToken ct);
    }
}
