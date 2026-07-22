namespace Telemetry.Worker.Contracts
{
    public sealed class UltimaEstadistica
    {
        public required Dictionary<string, long> EventoPorDispositivo { get; init; }
        public long TotalEventoPoison { get; init; }
        public long TotalEventoDuplicado { get; init; }
        public long TotalEventoProcesado { get; init; }
        public long TotalEventoValido { get; init; }
        public DateTimeOffset CapturadoEn { get; init; }
    }
}
