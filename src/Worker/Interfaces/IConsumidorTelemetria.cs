namespace Telemetry.Worker.Interfaces
{
    /// <summary>
    /// Inicia el consumo de la telemetria
    /// </summary>
    public interface IConsumidorTelemetria
    {
        Task IniciarAsync(CancellationToken ct);
    }
}
