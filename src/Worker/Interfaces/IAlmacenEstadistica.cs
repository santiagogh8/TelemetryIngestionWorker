using Telemetry.Worker.Contracts;

namespace Telemetry.Worker.Interfaces
{
    /// <summary>
    /// Almacen en memoria para estadisticas del pipeline.
    /// Thread-safe.
    /// </summary>
    public interface IAlmacenEstadistica
    {
        void RegistrarEventoValido(string deviceId);
        void RegistrarEventoPoison();
        void RegistrarEventoDuplicado();
        void RegistrarEventoProcesado();
        UltimaEstadistica ObtenerUltimaEstadistica();
    }
}
