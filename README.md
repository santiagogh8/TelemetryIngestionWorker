# Telemetry Worker Lab (Lite) — Starter Scaffolding

**EXPLICACION**

1.	CONCURRENCIA Y/O PARALELISMO — Channel<T> para particionamiento por deviceId

	- He marcado las clases con sealed, porque permite al compilar realizar optimizaciones(Devirtualization, etc) y por ende la ejecucion es mas rapida
	- He usado clases de la libreria System.Collections.Concurrent (ConcurrentDictionary, etc), estas clases estan diseñadas para un mayor rendimiento a nivel de concurrencia, paralelismo y mejor manejo del garabage collector, con esto evita los bloqueos, garantiza la seguridad en los hilos(thread-safety) entre otras bondades
	- Los metodos en su mayoria son asincronos (Async - Task) porque el ejercicio requiere desacoplamiento total.
	- El ejercicio plantea un funcionamiento con datos masivos en tiempo real, he usado System.Threading.Channels (Channel) dado que usa el patron Productor-Consumidor y es altamente eficiente para una comunicacion con servicios en segundo plano y no genera bloqueos
	- Para poder registrar las estadisticas(Conteos, totales, etc) de los eventos procesados, he implementado una clase que usa estas clases
		System.Collections.Concurrent.ConcurrentDictionary y System.Threading.Interlocked, que permiten atomicidad a las operaciones y trabajan muy bien con variables que son compartidas por multiples hilos.
	- El uso de Polly 8+, en la seccion de RETRY POLICIES se explica las bondades a nivel de concurrencia y paralelismo

2.	FAULT TOLERANCE — Graceful shutdown, manejo de mensajes poison

	Apagado controlado (gestion de SIGTERM)
		- El Worker detecta "ApplicationStopping" mediante "IHostApplicationLifetime" en el "Program.cs".
		- Deja de aceptar nuevos mensajes inmediatamente.
		- Finaliza el trabajo en curso, todos los mensajes almacenados en búfer se procesan antes de la salida.
		- Para Kubernetes el Tiempo de espera es 30 seg. (terminationGracePeriodSeconds: 30).

	Gestion de mensajes erroneos (No se vuelven a poner en la cola porque consumen CPU, solo se registran y confirman)
		- Para JSON no válidos, estos se detectan durante la deserialización, se registra y se confirma.
		- Para el fallo por validación de semántica (combustible negativo, velocidad > 350 km/h, marcas de tiempo futuras) solo se confirma.

	Gestion de errores por mensaje
		- Los fallos en el metodo "ServicioPersistencia.PersistirEventoAsync()" no se vuelven a poner en cola ya que se reintentaron en Retry.
		- Los mensajes con formato incorrecto se confirman inmediatamente.
		- Cada fallo de mensaje se aisla, un fallo no bloquea a otros dispositivos.

	Mirar en el codigo
		- En "Program.cs" esta la configuración del apagado controlado
		- En "ConsumidorTelemetria.ProcesarMensajeAsync(...)" es donde detecta los mensajes erróneos
		- En "ConsumidorTelemetria.ProcesarEventoDispositivoAsync(...)" es donde gestiona las excepciones por mensaje

3.	SECURITY — Idempotencia, validación, sin secretos
	IDEMPOTENCIA: 	
		En vista que el ejercicio menciona que se puede usar el almacenamiento en memoria he usado un patron clasico "deduplicación en memoria a corto plazo". Consiste en almacenar los identificadores únicos (eventId) junto con su marca de tiempo en una colección y usar un temporizador en segundo plano para eliminar los registros que superen el límite de la ventana de tiempo
		
		Ventajas
		- Baja latencia el rendimiento es super rapido y mas usando clases como ConcurrentDictionary.
		- Consumo de memoria controlado porque hay un metodo que sirve para limpiar registros antiguos evitando que crezca el uso de RAM.
		- Idempotencia eficaz al tener Retries, el sistema descarta el duplicado de forma optima.
		- No se necesita instalar ninguna libreria o software adicional
		
		Desventajas
		- Perdida de estado por tener toda la informacion en memoria(El ejercicio lo permite o no es estricto en este punto).
		- Falla la escalabilidad horizontal, ya que la instancia A no sabra lo que hace la instancia B.
		- Posibles bloqueos temporales al momento de limpiar los registros antiguos, cuando se tenga millones de datos en el ConcurrentDictionary
	
	VALIDACION DE ENTRADAS:	
		Se ha realizado validaciones basicas para rechazar ciertos mensajes, he usado constantes con la finalidad de probar rangos, timestamps, etc. (Ejemplo: MaxLat = 90.0; MinLat = -90.0;)

4.	RETRY POLICIES — Exponential backoff + circuit breaker

	Dado que se requiere un aplicativo que trabaje con alto rendimiento, alta concurrencia, paralelismo masivo, sin bloqueos y gestione el exceso de basura en memoria "Garbage Collector overhead", la mejor opcion es Polly desde la version 8+ ya que ahora es una libreria nativa del ecosistema .NET que consigue los mejores resultados frente a otras opciones. Por ende he usado Retry y Circuit Breaker de Polly, incluso Jitter y Backoff Exponencial ya viene incluido por defecto.
	
	CONFIGURACION EN EJERCICIO
	Retry con Backoff Exponencial + Jitter
		- Numero maximo de intentos = 3.
		- Retraso base inicial = 200 milisegundos.
		- Se ha simulado una excepcion de tipo TimeoutException para que entre en funcionamiento.
	Circuit Breaker
		- Radio de falla = 100% de fallas.
		- Ventana de fallas = rango de 10 segundos.
		- Minimo de fallas = 5 (Equivale a exigir un mínimo de 5 fallos consecutivos para abrir el circuito).
		- Tiempo que el circuito esta abierto = 30 segundos.
		- Se ha simulado una excepcion de tipo TimeoutException para que entre en funcionamiento.

5.	CONTENEDOR Y DESPLIGUE — Dockerfile + K8s manifiesto
	Tomar en cuenta que para crear los archivos se ha considerado solo el conocimiento teorico, no se pudo probar por limitacion de ambiente.
	Se aplica algunas mejores practicas
	
	Dockerfile
		- [Linea 3 y 24] Se usa sdk:8.0-alpine tanto para Build y Runtime, esto genera un entorno de construccion estandar y ligero, evita posibles incompatibilidades 
		- [Linea 11 y 12] Se  aplica cache de capas docker porque se copia unicamente el .csproj, si luego modificas el codigo y no subes paquetes nuevos, este paso se puede saltar
		- [Linea 34] Instala localizacion regional Alpine, evita errores de fechas string.
		- [Linea 40] Usa un usuario non-root creado en las versiones .NET8.
		- [Linea 46 y 47] curl -f, hace que si al consumir la url da un error 500, para docker es un error 1 por ende no lo marca como saludable.
					
	K8s manifiesto
		- Uso del usuario app con UID 1654 que por seguridad no tiene permisos de root.
		- Al usar seccompProfile: runtime/default, se evita que el contenedor ejecute llamadas al sistema(Kernel de Linux) no permitidas.
		- Al usar automountServiceAccountToken: false, se evita que el contenedor tenga acceso al token de Kubernetes(atacante controle el cluster).
		- Usando matchLabels para que busque por nombre(app) y version(v1), evita que se levante un pod con una version diferente a la que se esta desplegando.
		- Al usar NetworkPolicy, una barrera cortafuegos declarativa a nivel de red interna en Kubernetes, evita que el pod pueda comunicarse con otros pods que no esten en la misma red, por ende se evita que un atacante pueda acceder a otros pods del cluster.
		Para el Worker solo debe aceptar conexiones por el puerto 8080, los demas son rechazados.


6.	TESTS — Idempotencia y concurrencia
	Se ha generado clases con metodos basicos para probar:
	
	- Idempotencia (IdempotenciaTest.cs)
		Se detectan los "EventId" duplicados y no se cuentan dos veces.
	- Concurrencia(ConcurrenciaTest.cs)
		Múltiples hilos que registran estadísticas simultáneamente producen recuentos correctos.
	- Validación (ValidadorEventoTest.cs)
		Se rechazan los eventos fuera de rango (combustible negativo, velocidades imposibles, marcas de tiempo futuras).



**Trade-offs & What I'd Do With More Time**


1. Idempotencia distribuida
	- Reemplazar el almacenamiento De-duplicidad en memoria con Redis.
	- Tiempo de vida (TTL) de 24 horas o configurable.
	- Resiste los reinicios de pods y las implementaciones de múltiples instancias.

2. Cola de mensajes fallidos (DLQ)
	- Enviar eventos erróneos/fallidos a la cola telemetry.events.
	- Actualmente los mensajes se registra pero se pierde al terminar el servicio.
	- Consumidor independiente para gestion de auditoría.

3. Metricas y observabilidad
	- Configuracion de métricas en Dynatrace o similares para monitorear el rendimiento de eventos, percentiles de latencia, estado del disyuntor, logs en general.

4. Estado persistente
	- Usar PostgreSQL para la deduplicación de eventos y contadores.
	- En BDD aplicar UNIQUE(eventId) para control de idempotencia.
	- Persistir las estadisticas tras los reinicios.

5. Procesamiento por lotes
	- Recopilar N eventos y guardar en la memoria persistente en una sola transacción.
	- Reducir la sobrecarga de los RPC y mejorar el rendimiento.

6. Pruebas de extremo a extremo
	- Implementar pruebas SAST y DAST
	- Pruebas de integración de Docker Compose (productor + broker + Worker).
	- Pruebas mas reales como inyectar retrasos, fallos y simular particiones de red.

7. Configuracion
	- Aplicativo 100% configurable (Capacidad del canal, el número de Workers del dispositivo, tamaño de las ventanas , etc).




**RECURSOS DEL APLICATIVO**
	
	RUTAS DE ARCHIVOS DOCKERFILE Y K8S MANIFESTO
	- ..\TelemetryIngestionWorker\Dockerfile
	- ..\TelemetryIngestionWorker\k8s\deployment.yaml

	RABBITMQ:
	http://localhost:15672
	guest
	guest

	APIs:
	https://localhost:56805/stats
	https://localhost:56805/health

**LEVANTAR APLICATIVOS EN VISUAL STUDIO (LOCAL)**

	- Descargar el proyecto desde Github o .zip
	- Abrir la solucion en Visual Studio (.Net8)
	- Compilar la solucion
	- Abrir Docker Desktop y dejar en running
	- En la consola Power Shell de Visual Studio ejecutar:
		docker compose up -d
		docker ps
	- Verificar si esta activo el sitio RABBITMQ, ingresa a:
		http://localhost:15672
		guest
		guest
	- Levanta El proyecto Producer y Worker en Visual Studio o si prefieres en el cmd
		Producer: Escribe en la cola RABBITMQ los mensajes
		Worker: Consume los mensajes de la cola RABBITMQ y los procesa





# Telemetry Worker Lab (Lite) — Starter Scaffolding

A trimmed-down exercise scoped to **~3–4 hours**. We provide the message
producer, the broker, and a bare worker that runs and exposes `/health`.
Everything else is yours.

> Read `CANDIDATE_BRIEF.md` for the full requirements. This file only covers
> how to run the scaffolding.

## What's provided

```
src/
  Producer/      # Done for you. Pushes telemetry events to RabbitMQ.
  Worker/        # Bare: runs, exposes /health, has the event contract. Build the rest.
docker-compose.yml   # RabbitMQ + management UI.
```

You provide the `Dockerfile` and Kubernetes manifests (see the brief).

## Prerequisites

- .NET 8 SDK
- Docker + Docker Compose

## Run the infrastructure

```bash
docker compose up -d
# RabbitMQ management UI: http://localhost:15672  (guest / guest)
```

## Run the producer (event source)

```bash
cd src/Producer
dotnet run -- --rate 200 --devices 50
#   --rate            events per second (default 100)
#   --devices         number of distinct deviceIds (default 20)
#   --duplicate-rate  fraction resent as duplicates (default 0.05)
#   --poison-rate     fraction of malformed messages (default 0.02)
```

The producer deliberately emits **duplicates** and **poison messages** so you
can demonstrate idempotency and poison-message handling.

## Run the worker

```bash
cd src/Worker
dotnet run
# Health: GET http://localhost:8080/health
```

## Notes

- Queue name is `telemetry.events`.
- Broker credentials must come from env vars / config — **do not hardcode them.**
- The producer targets RabbitMQ; keep using it.
