using Telemetry.Worker.Contracts;
using Telemetry.Worker.Interfaces;

namespace Telemetry.Worker.Services;

/// <summary>
/// Validador de eventos de telemetria.
/// Contiene validaciones semanticas (rangos, timestamps, etc.).
/// </summary>
public sealed class ValidadorEvento : IValidadorEvento
{
    // Tolerancia de reloj: no aceptar eventos muy futuros (1 hora)
    private static readonly DateTimeOffset MaximoTiempoFuturo = DateTimeOffset.UtcNow.AddHours(1);
    private const double MaxCombustible = 1.0;
    private const double MinCombustible = 0.0;
    private const double MaxVelocidad = 350.0;
    private const double MaxLat = 90.0;
    private const double MinLat = -90.0;
    private const double MaxLng = 180.0;
    private const double MinLng = -180.0;

    public (bool EsValido, string? MensajeError) Validar(TelemetryEvent? evento)
    {
        if (evento == null)
            return (false, "Evento es nulo");

        if (evento.EventId == Guid.Empty)
            return (false, "EventId esta vacio");

        if (string.IsNullOrWhiteSpace(evento.DeviceId))
            return (false, "DeviceId vacio o espacion en blanco");

        // Timestamp: no muy en el futuro (tolerancia de 1 hora)
        if (evento.Timestamp > MaximoTiempoFuturo)
            return (false, $"Timestamp demasiado en el futuro: {evento.Timestamp}");

        // Combustible
        if (evento.FuelLevel < MinCombustible || evento.FuelLevel > MaxCombustible)
            return (false, $"FuelLevel fuera de rango [0, 1]: {evento.FuelLevel}");

        // Velocidad
        if (evento.SpeedKph < 0 || evento.SpeedKph > MaxVelocidad)
            return (false, $"SpeedKph fuera de rango [0, {MaxVelocidad}]: {evento.SpeedKph}");

        // Latitud / Longitud
        if (evento.Lat < MinLat || evento.Lat > MaxLat)
            return (false, $"Lat fuera de rango [-90, 90]: {evento.Lat}");

        if (evento.Lng < MinLng || evento.Lng > MaxLng)
            return (false, $"Lng fuera de rango [-180, 180]: {evento.Lng}");

        return (true, null);
    }
}