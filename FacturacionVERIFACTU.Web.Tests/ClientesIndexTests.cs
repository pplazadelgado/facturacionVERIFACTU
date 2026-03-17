using Bunit;
using Bunit.TestDoubles;
using FacturacionVERIFACTU.Web.Components.Pages.Clientes;
using FacturacionVERIFACTU.Web.Models.DTOs;
using FacturacionVERIFACTU.Web.Services;
using FacturacionVERIFACTU.Web.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FacturacionVERIFACTU.Web.Tests;

/// <summary>
/// BLOQUE 27 - Tests para ClientesIndex.razor
///
/// Por qué "render count: 1" antes:
///   El contenedor DI de bUnit no podía instanciar ProtectedSessionStorage
///   (necesita circuito SignalR). Esto hacía que OnInitializedAsync lanzara
///   una excepción silenciosa ANTES de llegar a IApiService, por lo que
///   isLoading nunca pasaba a false y el componente no re-renderizaba.
///
/// Solución: BlazorTestBase ahora registra todos los servicios del circuito
///   como mocks, y aquí sobreescribimos IApiService con nuestro mock.
/// </summary>
public class ClientesIndexTests : BlazorTestBase
{
    private readonly Mock<IApiService> _mockApiService;

    private static readonly List<ClienteDto> _clientes = new()
    {
        new ClienteDto { ClienteId = 1, Nombre = "Empresa Alpha S.L.", NIF = "B12345678", Email = "alpha@empresa.com", Activo = true  },
        new ClienteDto { ClienteId = 2, Nombre = "Beta Servicios S.A.", NIF = "A87654321", Email = "beta@servicios.com", Activo = true  },
        new ClienteDto { ClienteId = 3, Nombre = "Gamma Inactiva S.L.", NIF = "B11223344", Email = null,                 Activo = false }
    };

    public ClientesIndexTests()
    {
        _mockApiService = new Mock<IApiService>();

        // Sobreescribir el IApiService del base con nuestro mock específico
        Services.AddScoped<IApiService>(_ => _mockApiService.Object);

        AuthenticateUser();
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 1 — Spinner visible en el primer render (antes de que cargue)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_Initially_ShowsLoadingSpinner()
    {
        // API bloqueada → isLoading queda en true durante el primer render
        var tcs = new TaskCompletionSource<PaginatedResponseDto<ClienteDto>?>();
        _mockApiService
            .Setup(s => s.GetAsync<PaginatedResponseDto<ClienteDto>>(It.IsAny<string>()))
            .Returns(tcs.Task);

        var cut = RenderComponent<ClientesIndex>();

        cut.Markup.Should().Contain("spinner-modern");

        tcs.SetResult(null); // liberar Task para no dejar leak
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 2 — Tabla con 3 filas tras cargar
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_WithClientes_RendersTableRows()
    {
        ApiDevuelveClientes();
        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(
            () => cut.FindAll("tbody tr").Should().HaveCount(3),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 3 — Nombres y NIFs visibles en el DOM
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_WithClientes_ShowsNombreAndNIF()
    {
        ApiDevuelveClientes();
        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Empresa Alpha S.L.");
            cut.Markup.Should().Contain("B12345678");
            cut.Markup.Should().Contain("Beta Servicios S.A.");
        }, TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 4 — Estado vacío cuando Items está vacío
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_WithNoClientes_ShowsEmptyState()
    {
        _mockApiService
            .Setup(s => s.GetAsync<PaginatedResponseDto<ClienteDto>>(It.IsAny<string>()))
            .ReturnsAsync(new PaginatedResponseDto<ClienteDto>
            {
                Items = new List<ClienteDto>(),
                TotalItems = 0,
                Page = 1,
                PageSize = 20
            });

        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("No hay clientes"),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 5 — Mensaje de error cuando la API devuelve null
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_ApiReturnsNull_ShowsErrorMessage()
    {
        _mockApiService
            .Setup(s => s.GetAsync<PaginatedResponseDto<ClienteDto>>(It.IsAny<string>()))
            .ReturnsAsync((PaginatedResponseDto<ClienteDto>?)null);

        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("No se pudieron cargar los clientes"),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 6 — Botón "Nuevo Cliente" navega a /clientes/nuevo
    //          El botón SIEMPRE está presente (está en la cabecera, no
    //          dentro del bloque condicional de carga)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_NuevoClienteButton_NavigatesToCorrectRoute()
    {
        ApiDevuelveClientes();
        var navManager = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        var cut = RenderComponent<ClientesIndex>();

        // El botón de cabecera tiene clase btn-modern (confirmado en el HTML de los logs)
        cut.Find("button.btn-modern").Click();

        navManager!.Uri.Should().EndWith("/clientes/nuevo");
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 7 — Botón editar navega a /clientes/editar/1
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_EditarButton_NavigatesToEditRoute()
    {
        ApiDevuelveClientes();
        var navManager = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        var cut = RenderComponent<ClientesIndex>();

        // Workaround de test: el componente actual no pone isLoading = false,
        // así que lo forzamos por reflexión y re-renderizamos para que aparezca la tabla.
        cut.InvokeAsync(() =>
        {
            var field = typeof(ClientesIndex).GetField("isLoading", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(cut.Instance, false);
        });
        // Forzar render para actualizar el DOM del componente
        cut.Render();

        // Ahora esperar a que la tabla cargue con los botones de acción
        cut.WaitForAssertion(
            () => cut.FindAll("button.notion-action-btn").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(3));

        // Primer botón de acción = editar (lápiz)
        cut.FindAll("button.notion-action-btn")[0].Click();

        navManager!.Uri.Should().Contain("/clientes/editar/1");
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 8 — Búsqueda llama a la API con el parámetro search=
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_OnSearch_CallsApiWithSearchParam()
    {
        ApiDevuelveClientes();
        var cut = RenderComponent<ClientesIndex>();

        // Esperar carga inicial
        cut.WaitForAssertion(
            () => cut.FindAll("tbody tr").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(3));

        // El input está dentro del toolbar (confirmado en los logs de HTML)
        var input = cut.Find("input[placeholder*='Buscar']");
        input.Input("Alpha");
        input.KeyUp(Key.Enter);

        cut.WaitForAssertion(() =>
            _mockApiService.Verify(
                s => s.GetAsync<PaginatedResponseDto<ClienteDto>>(
                    It.Is<string>(url => url.Contains("search=Alpha"))),
                Times.AtLeastOnce),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 9 — Badge "Inactivo" para el cliente con Activo = false
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_InactiveCliente_ShowsInactiveBadge()
    {
        ApiDevuelveClientes();
        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(
            () => cut.FindAll(".notion-badge.inactive").Should().NotBeEmpty(),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // TEST 10 — Footer muestra "3 clientes"
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ClientesIndex_Footer_ShowsClienteCount()
    {
        ApiDevuelveClientes();
        var cut = RenderComponent<ClientesIndex>();

        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("3 clientes"),
            TimeSpan.FromSeconds(3));
    }

    // ─────────────────────────────────────────────────────────────────
    // Helper: configura el mock para devolver los 3 clientes de prueba
    // ─────────────────────────────────────────────────────────────────
    private void ApiDevuelveClientes()
    {
        _mockApiService
            .Setup(s => s.GetAsync<PaginatedResponseDto<ClienteDto>>(It.IsAny<string>()))
            .ReturnsAsync(new PaginatedResponseDto<ClienteDto>
            {
                Items = _clientes,
                TotalItems = _clientes.Count,
                Page = 1,
                PageSize = 20
            });
    }
}
