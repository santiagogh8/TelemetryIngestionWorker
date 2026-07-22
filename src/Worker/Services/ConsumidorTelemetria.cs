using System.Text.Json;
using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Telemetry.Worker.Contracts;
using Telemetry.Worker.Interfaces;

namespace Telemetry.Worker.Services;

/// <summary>
/// Consumidor RabbitMQ que:
/// - Particiona por deviceId (mismo dispositivo -> procesamiento secuencial)
/// - Maneja dispositivos diferentes en paralelo
/// - Detecta y registra mensajes poison (JSON invalido / semantica)
/// - Soporta apagado ordenado (graceful shutdown)
/// </summary>
public sealed class ConsumidorTelemetria : IConsumidorTelemetria
{
    private readonly IConnection _conexion;
    private readonly IValidadorEvento _validador;
    private readonly IAlmacenDuplicado _dedupe;
    private readonly IServicioPersistencia _persistencia;
    private readonly IAlmacenEstadistica _estadisticas;
    private readonly ILogger<ConsumidorTelemetria> _logger;

    // Canales
    private readonly Channel<(ulong, ReadOnlyMemory<byte>)> _mensajesEntrantes;
    private readonly Dictionary<string, Channel<(ulong, TelemetryEvent)>> _canalesPorDispositivo = new();
    private readonly SemaphoreSlim _lockCanales = new(1, 1);

    private const int CapacidadCanal = 100;

    public ConsumidorTelemetria(
        IConnection conexion,
        IValidadorEvento validador,
        IAlmacenDuplicado dedupe,
        IServicioPersistencia persistencia,
        IAlmacenEstadistica estadistica,
        ILogger<ConsumidorTelemetria> logger)
    {
        _conexion = conexion;
        _validador = validador;
        _dedupe = dedupe;
        _persistencia = persistencia;
        _estadisticas = estadistica;
        _logger = logger;

        _mensajesEntrantes = Channel.CreateBounded<(ulong, ReadOnlyMemory<byte>)>(new BoundedChannelOptions(CapacidadCanal) { FullMode = BoundedChannelFullMode.Wait });
    }

    public async Task IniciarAsync(CancellationToken ct)
    {
        using var canal = _conexion.CreateModel();
        canal.BasicQos(0, 1, false);
        canal.QueueDeclare(TelemetryEvent.QueueName, durable: true, exclusive: false, autoDelete: false);

        var consumidor = new AsyncEventingBasicConsumer(canal);

        consumidor.Received += async (model, ea) =>
        {
            if (ct.IsCancellationRequested)
            {
                canal.BasicNack(ea.DeliveryTag, false, true);
                return;
            }

            try
            {
                await _mensajesEntrantes.Writer.WriteAsync((ea.DeliveryTag, new ReadOnlyMemory<byte>(ea.Body.ToArray())), ct);
            }
            catch (OperationCanceledException)
            {
                canal.BasicNack(ea.DeliveryTag, false, true);
            }
        };

        canal.BasicConsume(TelemetryEvent.QueueName, autoAck: false, consumidor);
        _logger.LogInformation("Consumidor iniciado para la cola '{QueueName}'", TelemetryEvent.QueueName);

        await DespachadorAsync(canal, ct);

        _mensajesEntrantes.Writer.Complete();
        _logger.LogInformation("Shutdown del consumidor completo");
    }

    private async Task DespachadorAsync(IModel canal, CancellationToken ct)
    {
        try
        {
            await foreach (var (deliveryTag, body) in _mensajesEntrantes.Reader.ReadAllAsync(ct))
            {
                _ = ProcesarMensajeAsync(canal, deliveryTag, body, ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Despachador cancelado");
        }
    }

    private async Task ProcesarMensajeAsync(IModel canal, ulong deliveryTag, ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        try
        {
            TelemetryEvent? evento = null;
            try
            {
                evento = JsonSerializer.Deserialize<TelemetryEvent>(body.Span);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Mensaje poison (JSON invalido): {Error}", ex.Message);
                _estadisticas.RegistrarEventoPoison();
                canal.BasicAck(deliveryTag, false);
                return;
            }

            if (evento == null)
            {
                _logger.LogWarning("Evento deserializado es nulo");
                _estadisticas.RegistrarEventoPoison();
                canal.BasicAck(deliveryTag, false);
                return;
            }

            var (esValido, error) = _validador.Validar(evento);
            if (!esValido)
            {
                _logger.LogWarning("Evento invalido: {Error}", error);
                _estadisticas.RegistrarEventoPoison();
                canal.BasicAck(deliveryTag, false);
                return;
            }

            if (!_dedupe.EsEventoNuevo(evento.EventId))
            {
                _logger.LogDebug("Evento duplicado ignorado: {EventId}", evento.EventId);
                _estadisticas.RegistrarEventoDuplicado();
                canal.BasicAck(deliveryTag, false);
                return;
            }

            _dedupe.MarcarVisto(evento.EventId);

            await EnrutarAColaDispositivoAsync(evento, deliveryTag, canal, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Procesamiento cancelado");
            canal.BasicNack(deliveryTag, false, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado procesando mensaje");
            canal.BasicNack(deliveryTag, false, true);
        }
    }

    private async Task EnrutarAColaDispositivoAsync(TelemetryEvent evento, ulong deliveryTag, IModel canal, CancellationToken ct)
    {
        await _lockCanales.WaitAsync(ct);
        try
        {
            if (!_canalesPorDispositivo.TryGetValue(evento.DeviceId, out var canalDisp))
            {
                canalDisp = Channel.CreateBounded<(ulong, TelemetryEvent)>(new BoundedChannelOptions(CapacidadCanal) { FullMode = BoundedChannelFullMode.Wait });
                _canalesPorDispositivo[evento.DeviceId] = canalDisp;

                _ = ProcesarEventoDispositivoAsync(evento.DeviceId, canalDisp, canal, ct);
            }

            await canalDisp.Writer.WriteAsync((deliveryTag, evento), ct);
        }
        finally
        {
            _lockCanales.Release();
        }
    }

    private async Task ProcesarEventoDispositivoAsync(string deviceId, Channel<(ulong, TelemetryEvent)> canalDisp, IModel canal, CancellationToken ct)
    {
        try
        {
            await foreach (var (deliveryTag, evento) in canalDisp.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _persistencia.PersistirEventoAsync(evento.EventId, deviceId, ct);

                    _estadisticas.RegistrarEventoValido(deviceId);
                    _estadisticas.RegistrarEventoProcesado();

                    canal.BasicAck(deliveryTag, false);

                    _logger.LogDebug("Evento procesado {EventId} para {DeviceId}", evento.EventId, deviceId);
                }
                catch (OperationCanceledException)
                {
                    canal.BasicNack(deliveryTag, false, true);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error procesando evento {EventId} para {DeviceId}", evento.EventId, deviceId);
                    // No se vuelven a encolar: ya intentamos reintentos internos y Circuit Breaker puede haberse abierto
                    canal.BasicNack(deliveryTag, false, false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker de dispositivo {DeviceId} cancelado", deviceId);
        }
    }
}