using RabbitMQ.Client;
using Telemetry.Worker.Interfaces;
using Telemetry.Worker.Services;

var builder = WebApplication.CreateBuilder(args);

var rabbitMqHost = builder.Configuration["RabbitMQ:Host"] ?? Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
var rabbitMqUser = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? builder.Configuration["RabbitMQ:User"] ?? "guest";
var rabbitMqPass = Environment.GetEnvironmentVariable("RABBITMQ_PASS") ?? builder.Configuration["RabbitMQ:Pass"] ?? "guest";

// Registrar conexion RabbitMQ
builder.Services.AddSingleton<IConnection>(sp =>
{
    var factory = new ConnectionFactory
    {
        HostName = rabbitMqHost,
        UserName = rabbitMqUser,
        Password = rabbitMqPass,
        DispatchConsumersAsync = true
    };
    return factory.CreateConnection();
});

// Registrar servicios
builder.Services.AddSingleton<IValidadorEvento, ValidadorEvento>();
builder.Services.AddSingleton<IAlmacenDuplicado, AlmacenDuplicado>();
builder.Services.AddSingleton<IServicioPersistencia, ServicioPersistencia>();
builder.Services.AddSingleton<IAlmacenEstadistica, AlmacenEstadistica>();
builder.Services.AddSingleton<IConsumidorTelemetria, ConsumidorTelemetria>();

builder.Services.AddLogging(cfg =>
{
    cfg.AddConsole();
    cfg.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Endpoint de salud
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }));

// Endpoint de estadisticas — inyecta el almacen de estadisticas
app.MapGet("/stats", (IAlmacenEstadistica stats) =>
{
    var snapshot = stats.ObtenerUltimaEstadistica();
    return Results.Ok(new
    {
        timestamp = snapshot.CapturadoEn,
        totalProcesados = snapshot.TotalEventoProcesado,
        totalValidos = snapshot.TotalEventoValido,
        totalPoison = snapshot.TotalEventoPoison,
        totalDuplicados = snapshot.TotalEventoDuplicado,
        porDispositivo = snapshot.EventoPorDispositivo
    });
});

// Iniciar consumidor en background
var consumidor = app.Services.GetRequiredService<IConsumidorTelemetria>();
var hostLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();

_ = Task.Run(async () =>
{
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime.ApplicationStopping);
    try
    {
        await consumidor.IniciarAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        // Esperado en apagado
    }
});

app.Run();
