using Telemetry.Worker.Contracts;

namespace Telemetry.Worker.Interfaces
{
    /// <summary>
    /// Validador de eventos de telemetria.
    /// Contiene validaciones semanticas (rangos, timestamps, etc.).
    /// </summary>
    public interface IValidadorEvento
    {
        /// <summary>
        /// Valida un evento de telemetria.
        /// Retorna (esValido, mensajeError).
        /// </summary>
        (bool EsValido, string? MensajeError) Validar(TelemetryEvent? evento);
    }
}
