namespace Telemetry.Worker.Interfaces
{
    /// <summary>
    /// Almacén en memoria para detección de duplicados (idempotencia).
    /// Implementa una ventana deslizante basada en timestamp.
    /// Para producción, reemplazar por Redis u otra cache distribuida.
    /// </summary>
    public interface IAlmacenDuplicado
    {
        bool EsEventoNuevo(Guid eventId);
        void MarcarVisto(Guid eventId);
    }
}
