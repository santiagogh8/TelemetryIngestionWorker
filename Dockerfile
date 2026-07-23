# 1. ETAPA DE COMPILACION (Build Stage)
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS builder
WORKDIR /src

# Variables para acelerar la compilacion y evitar descargas innecesarias
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    NUGET_XMLDOC_MODE=skip

# Copiar el archivo del proyecto y restaurar dependencias aprovechando la cache
COPY ["src/Worker/Worker.csproj", "Worker/"]
RUN dotnet restore "Worker/Worker.csproj"

# Copiar el codigo fuente restante y compilar directamente a produccion
COPY src/Worker/ Worker/
WORKDIR /src/Worker
RUN dotnet publish -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false \
    /p:PublishReadyToRun=true

# 2. ETAPA DE EJECUCION (Runtime Stage)
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app

# Habilitar soporte de caracteres regionales y fechas en Linux Alpine
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    LC_ALL=en_US.UTF-8 \
    LANG=en_US.UTF-8

# Instalar libreria requerida para la globalizacion de .NET en Alpine
RUN apk add --no-cache icu-libs

# Copiar los binarios y asignar los permisos al usuario nativo 'app' en un solo paso
COPY --from=builder --chown=app:app /app/publish .

# Cambiar al usuario no-root nativo preconfigurado por Microsoft
USER app

# Exponer el puerto estandar
EXPOSE 8080

# Prueba de vida optimizada con curl para detectar cuelgues de la app
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Comando definitivo que arranca el Worker Service
ENTRYPOINT ["dotnet", "Worker.dll"]
