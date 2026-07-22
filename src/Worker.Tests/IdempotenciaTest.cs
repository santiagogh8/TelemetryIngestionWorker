using Xunit;
using Telemetry.Worker.Services;

namespace Telemetry.Worker.Tests;

public class IdempotenciaTest
{
    [Fact]
    public void AlmacenDuplicados_RechazaSegundaOcurrenciaDelMismoEventId()
    {
        var store = new AlmacenDuplicado();
        var eventId = Guid.NewGuid();

        Assert.True(store.EsEventoNuevo(eventId));

        store.MarcarVisto(eventId);

        Assert.False(store.EsEventoNuevo(eventId));
    }

    [Fact]
    public void AlmacenDuplicados_PermiteDistintosEventIds()
    {
        var store = new AlmacenDuplicado();
        var eventId1 = Guid.NewGuid();
        var eventId2 = Guid.NewGuid();

        Assert.True(store.EsEventoNuevo(eventId1));
        store.MarcarVisto(eventId1);

        Assert.True(store.EsEventoNuevo(eventId2));
        store.MarcarVisto(eventId2);
    }
}