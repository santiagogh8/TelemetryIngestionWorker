using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Telemetry.Worker.Interfaces;

namespace Telemetry.Worker.Services;

/// <summary>
/// Servicio que simula persistencia fragil (aproximado 20% fallas).
/// Implementa retry con backoff exponencial mas jitter y un Circuit Breaker usando Polly v8.4.1.
/// </summary>
public sealed class ServicioPersistencia : IServicioPersistencia
{
    private readonly ILogger<ServicioPersistencia> _logger;
    private readonly ResiliencePipeline<bool> _pipeline;
    private readonly Random _rng = new();

    public ServicioPersistencia(ILogger<ServicioPersistencia> logger)
    {
        _logger = logger;

        _pipeline = new ResiliencePipelineBuilder<bool>()

            // Configuracion para Retry con Backoff Exponencial + Jitter Nativo por defecto
            .AddRetry(new RetryStrategyOptions<bool>
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(200), // Retraso base inicial

                // Evaluamos excepciones y retornos booleanos falsos de forma unificada
                ShouldHandle = new PredicateBuilder<bool>()
                    .Handle<TimeoutException>()
                    .HandleResult(resultado => resultado == false),

                OnRetry = argumento =>
                {
                    //AttemptNumber inicia en 0
                    _logger.LogWarning(
                        "Intento {Intento} de persistencia tras {Delay}ms",
                        argumento.AttemptNumber + 1,
                        argumento.RetryDelay.TotalMilliseconds);

                    return default; // Equivalente eficiente a ValueTask.CompletedTask
                }
            })

            // Configuracion Circuit Breaker secuencial
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<bool>
            {                
                FailureRatio = 1.0, // Un valor de 1.0 (100%) indica que todas las muestras analizadas deben fallar para saltar el circuito
                SamplingDuration = TimeSpan.FromSeconds(10), // Ventana para analizar fallos
                MinimumThroughput = 5, // Equivale a exigir un minimo de 5 fallos consecutivos para abrir el circuito
                BreakDuration = TimeSpan.FromSeconds(30), // Tiempo que el circuito se mantendra ABIERTO

                ShouldHandle = new PredicateBuilder<bool>()
                    .Handle<TimeoutException>()
                    .HandleResult(resultado => resultado == false)
            })
            .Build();
    }

    public async Task PersistirEventoAsync(Guid eventId, string deviceId, CancellationToken ct)
    {
        try
        {
            var resultado = await _pipeline.ExecuteAsync(async cancellationToken =>
            {
                // Simular aproximado de 20% de fallas
                if (_rng.NextDouble() < 0.2)
                {
                    throw new TimeoutException("Timeout en capa de persistencia");
                }

                // Pasamos el token de cancelación nativo de la ejecución al delay
                await Task.Delay(5, cancellationToken);
                return true;
            }, ct); // Inyectamos el CancellationToken (ct) principal aqui para propagarlo

            if (!resultado)
            {
                throw new InvalidOperationException($"No se pudo persistir {eventId} para {deviceId} después de reintentos");
            }
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit Breaker abierto al persistir {EventId} de {DeviceId}", eventId, deviceId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persistiendo {EventId} de {DeviceId}", eventId, deviceId);
            throw;
        }
    }
}
