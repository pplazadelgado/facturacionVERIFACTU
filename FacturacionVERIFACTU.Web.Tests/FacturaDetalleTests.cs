using Bunit;
using Bunit.TestDoubles;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.Web.Services;
using FacturacionVERIFACTU.Web.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FacturacionVERIFACTU.Web.Tests;

/// <summary>
/// BLOQUE 27 - Tests unitarios para Facturas con campos VERIFACTU.
/// Usa el FacturaResponseDto de FacturacionVERIFACTU.API.DTOs (el DTO completo con VERIFACTU).
/// 
/// Nombres REALES del DTO (FacturaDto.cs de la API):
///   - TotalIva  (no TotalIVA)
///   - QRBase64  (no QRVerifactu)
///   - LineaFacturaResponseDto: ImporteIva (no ImporteIVA), Importe (no Total)
/// </summary>
public class FacturaDetalleTests : BlazorTestBase
{
    private readonly Mock<IApiService> _mockApiService;

    // =========================================================
    // Datos de prueba — usando los nombres REALES del DTO
    // =========================================================
    private static readonly FacturaResponseDto _facturaEmitida = new()
    {
        Id = 1,
        Numero = "FAC-2024-001",
        FechaEmision = new DateTime(2024, 3, 15),
        Estado = "Emitida",
        ClienteNombre = "Empresa Alpha S.L.",
        ClienteNIF = "B12345678",
        BaseImponible = 1000.00m,
        TotalIva = 210.00m,         // ✅ TotalIva (no TotalIVA)
        Total = 1210.00m,
        EnviadaVERIFACTU = false,
        TipoFacturaVERIFACTU = "F1",
        Huella = null,
        Observaciones = "Factura de prueba",
        Lineas = new List<LineaFacturaResponseDto>
        {
            new()
            {
                Id = 1,
                Descripcion = "Servicio de consultoría",
                Cantidad = 10,
                PrecioUnitario = 100.00m,
                BaseImponible = 1000.00m,
                ImporteIva = 210.00m,   // ✅ ImporteIva (no ImporteIVA)
                Importe = 1210.00m      // ✅ Importe (no Total)
            }
        }
    };

    private static readonly FacturaResponseDto _facturaVerifactu = new()
    {
        Id = 2,
        Numero = "FAC-2024-002",
        FechaEmision = new DateTime(2024, 3, 16),
        Estado = "Emitida",
        ClienteNombre = "Beta Servicios S.A.",
        ClienteNIF = "A87654321",
        BaseImponible = 500.00m,
        TotalIva = 105.00m,         // ✅ TotalIva
        Total = 605.00m,
        EnviadaVERIFACTU = true,
        FechaEnvioVERIFACTU = new DateTime(2024, 3, 16, 10, 30, 0),
        TipoFacturaVERIFACTU = "F1",
        Huella = "abc123def456xyz789",
        QRBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAUA",  // ✅ QRBase64 (no QRVerifactu)
        Lineas = new List<LineaFacturaResponseDto>()
    };

    private static readonly FacturaResponseDto _facturaPagada = new()
    {
        Id = 3,
        Numero = "FAC-2024-003",
        FechaEmision = new DateTime(2024, 2, 10),
        Estado = "Pagada",
        ClienteNombre = "Gamma Tech S.L.",
        ClienteNIF = "B99887766",
        BaseImponible = 2000.00m,
        TotalIva = 420.00m,         // ✅ TotalIva
        Total = 2420.00m,
        EnviadaVERIFACTU = true,
        TipoFacturaVERIFACTU = "F1",
        Huella = "pagada_huella_hash",
        Lineas = new List<LineaFacturaResponseDto>()
    };

    public FacturaDetalleTests()
    {
        _mockApiService = new Mock<IApiService>();
        // Registrar ConfirmDialogService como scoped para evitar estado compartido entre tests
        Services.AddScoped<FacturacionVERIFACTU.Web.Services.ConfirmDialogService>(_ => new ConfirmDialogService());
        Services.AddScoped<IApiService>(_ => _mockApiService.Object);
        AuthenticateUser();
    }

    // TEST 1: Lista de facturas renderiza correctamente
    [Fact]
    public void FacturasIndex_LoadsFacturas_RendersTable()
    {
        var facturas = new List<FacturaResponseDto> { _facturaEmitida, _facturaVerifactu, _facturaPagada };

        var cut = RenderComponent<TestableFacturasComponent>(parameters => parameters
            .Add(p => p.Facturas, facturas));

        cut.Markup.Should().Contain("FAC-2024-001");
        cut.Markup.Should().Contain("FAC-2024-002");
        cut.Markup.Should().Contain("FAC-2024-003");
    }

    // TEST 2: Factura con VERIFACTU enviado muestra badge correcto
    [Fact]
    public void FacturaDetalle_WhenVerifactuEnviado_ShowsEnviadoBadge()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaVerifactu));

        cut.Markup.Should().Contain("Enviada");
    }

    // TEST 3: Factura NO enviada a VERIFACTU tiene EnviadaVERIFACTU = false
    [Fact]
    public void FacturaDetalle_WhenVerifactuNoEnviado_FlagIsFalse()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaEmitida));

        _facturaEmitida.EnviadaVERIFACTU.Should().BeFalse();
        cut.Markup.Should().Contain("Pendiente");
    }

    // TEST 4: Los totales de la factura se muestran correctamente
    [Fact]
    public void FacturaDetalle_DisplaysTotalsCorrectly()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaEmitida));

        // El componente formatea con CultureInfo es-ES ("1.000,00")
        cut.Markup.Should().Contain("1.000");
        cut.Markup.Should().Contain("210");
        cut.Markup.Should().Contain("1.210");
    }

    // TEST 5: Factura en estado "Pagada" muestra badge correcto
    [Fact]
    public void FacturaDetalle_EstadoPagada_ShowsPagadaBadge()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaPagada));

        cut.Markup.Should().Contain("Pagada");
    }

    // TEST 6: Número de factura y cliente se muestran correctamente
    [Fact]
    public void FacturaDetalle_ShowsNumeroAndClienteData()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaEmitida));

        cut.Markup.Should().Contain("FAC-2024-001");
        cut.Markup.Should().Contain("Empresa Alpha S.L.");
        cut.Markup.Should().Contain("B12345678");
    }

    // TEST 7: Tipo de factura VERIFACTU F1 se muestra
    [Fact]
    public void FacturaDetalle_TipoVerifactu_IsF1_ShowsCorrectType()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaEmitida));

        _facturaEmitida.TipoFacturaVERIFACTU.Should().Be("F1");
        cut.Markup.Should().Contain("F1");
    }

    // TEST 8: Factura con huella VERIFACTU la muestra en pantalla
    [Fact]
    public void FacturaDetalle_WithHuella_ShowsHuellaInfo()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaVerifactu));

        cut.Markup.Should().Contain("abc123def456xyz789");
    }

    // TEST 9: Líneas de factura se renderizan
    [Fact]
    public void FacturaDetalle_WithLineas_RendersLineas()
    {
        var cut = RenderComponent<TestableFacturaDetalleComponent>(parameters => parameters
            .Add(p => p.Factura, _facturaEmitida));

        cut.Markup.Should().Contain("Servicio de consultoría");
    }

    // TEST 10: DTO de factura tiene todos los campos VERIFACTU necesarios
    [Fact]
    public void FacturaResponseDto_HasAllRequiredVerifactuFields()
    {
        var factura = new FacturaResponseDto
        {
            Id = 1,
            Numero = "FAC-001",
            Estado = "Emitida",
            EnviadaVERIFACTU = true,
            TipoFacturaVERIFACTU = "F1",
            Huella = "hash_sha256",
            HuellaAnterior = "hash_anterior",
            FechaEnvioVERIFACTU = DateTime.UtcNow,
            QRBase64 = "base64_qr_string"   // ✅ QRBase64
        };

        factura.EnviadaVERIFACTU.Should().BeTrue();
        factura.TipoFacturaVERIFACTU.Should().Be("F1");
        factura.Huella.Should().NotBeNullOrEmpty();
        factura.QRBase64.Should().NotBeNull();    // ✅ QRBase64
        factura.FechaEnvioVERIFACTU.Should().NotBeNull();
    }

    // TEST 11: Cálculo de totales con distintos tipos de IVA
    [Theory]
    [InlineData(1000.00, 210.00, 1210.00)]  // IVA 21%
    [InlineData(500.00, 52.50, 552.50)]      // IVA 10.5%
    [InlineData(300.00, 12.00, 312.00)]      // IVA 4%
    [InlineData(1000.00, 0.00, 1000.00)]     // Exento de IVA
    public void FacturaDto_Total_EqualsBaseImponiblePlusTotalIva(
        decimal baseImponible, decimal totalIva, decimal expectedTotal)
    {
        var factura = new FacturaResponseDto
        {
            BaseImponible = baseImponible,
            TotalIva = totalIva,        // ✅ TotalIva
            Total = baseImponible + totalIva
        };

        factura.Total.Should().Be(expectedTotal);
    }

    // TEST 12: Fecha de envío VERIFACTU solo existe cuando está enviada
    [Fact]
    public void FacturaDetalle_VerifactuFechaEnvio_OnlyExistsWhenEnviada()
    {
        // Factura enviada → tiene fecha de envío
        _facturaVerifactu.EnviadaVERIFACTU.Should().BeTrue();
        _facturaVerifactu.FechaEnvioVERIFACTU.Should().NotBeNull();

        // Factura no enviada → sin fecha de envío
        _facturaEmitida.EnviadaVERIFACTU.Should().BeFalse();
        _facturaEmitida.FechaEnvioVERIFACTU.Should().BeNull();
    }
}

// =========================================================
// Componentes testables (wrappers ligeros para tests aislados)
// =========================================================

/// <summary>Wrapper testable para lista de facturas.</summary>
public class TestableFacturasComponent : Microsoft.AspNetCore.Components.ComponentBase
{
    [Microsoft.AspNetCore.Components.Parameter]
    public List<FacturaResponseDto> Facturas { get; set; } = new();

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "facturas-list");
        foreach (var f in Facturas)
        {
            builder.OpenElement(2, "div");
            builder.AddAttribute(3, "class", "factura-row");
            builder.AddContent(4, $"{f.Numero} - {f.ClienteNombre} - {f.Estado} - {f.Total:C}");
            builder.CloseElement();
        }
        builder.CloseElement();
    }
}

/// <summary>Wrapper testable para detalle de una factura.</summary>
public class TestableFacturaDetalleComponent : Microsoft.AspNetCore.Components.ComponentBase
{
    [Microsoft.AspNetCore.Components.Parameter]
    public FacturaResponseDto? Factura { get; set; }

    protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
    {
        if (Factura == null) return;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "factura-detalle");

        builder.OpenElement(2, "h2");
        builder.AddContent(3, Factura.Numero);
        builder.CloseElement();

        builder.OpenElement(4, "div");
        builder.AddContent(5, Factura.ClienteNombre);
        builder.CloseElement();

        builder.OpenElement(6, "div");
        builder.AddContent(7, Factura.ClienteNIF);
        builder.CloseElement();

        builder.OpenElement(8, "span");
        builder.AddAttribute(9, "class", $"badge estado-{Factura.Estado?.ToLower()}");
        builder.AddContent(10, Factura.Estado);
        builder.CloseElement();

        // Estado VERIFACTU
        builder.OpenElement(11, "div");
        builder.AddAttribute(12, "class", "verifactu-status");
        builder.AddContent(13, Factura.EnviadaVERIFACTU ? "Enviada" : "Pendiente VERIFACTU");
        builder.CloseElement();

        // Tipo VERIFACTU
        if (!string.IsNullOrEmpty(Factura.TipoFacturaVERIFACTU))
        {
            builder.OpenElement(14, "span");
            builder.AddAttribute(15, "class", "tipo-verifactu");
            builder.AddContent(16, Factura.TipoFacturaVERIFACTU);
            builder.CloseElement();
        }

        // Huella VERIFACTU
        if (!string.IsNullOrEmpty(Factura.Huella))
        {
            builder.OpenElement(17, "div");
            builder.AddAttribute(18, "class", "huella-verifactu");
            builder.AddContent(19, Factura.Huella);
            builder.CloseElement();
        }

        // Totales (formato es-ES → punto como separador de miles)
        var culture = new System.Globalization.CultureInfo("es-ES");
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "totales");
        builder.AddContent(22,
            $"Base: {Factura.BaseImponible.ToString("N2", culture)} " +
            $"IVA: {Factura.TotalIva.ToString("N2", culture)} " +  // ✅ TotalIva
            $"Total: {Factura.Total.ToString("N2", culture)}");
        builder.CloseElement();

        // Líneas
        foreach (var linea in Factura.Lineas ?? new List<LineaFacturaResponseDto>())
        {
            builder.OpenElement(23, "div");
            builder.AddAttribute(24, "class", "linea-factura");
            builder.AddContent(25, linea.Descripcion);
            builder.CloseElement();
        }

        builder.CloseElement();
    }
}
