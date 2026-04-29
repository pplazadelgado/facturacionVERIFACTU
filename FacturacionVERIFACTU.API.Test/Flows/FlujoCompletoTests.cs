using API.Data.Entities;
using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Models;
using FacturacionVERIFACTU.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FacturacionVERIFACTU.API.Tests.Flows
{
    /// <summary>
    /// Tests de integración del flujo completo de negocio:
    /// Presupuesto → Albarán → Factura
    /// 
    /// Estos tests son más lentos que los unitarios porque tocan varios
    /// servicios y la BD en memoria. Son los más valiosos para detectar
    /// roturas en la cadena de conversión.
    /// </summary>
    public class FlujoCompletoTests
    {
        private readonly ApplicationDbContext _context;
        private readonly IPresupuestoService _presupuestoService;
        private readonly IAlbaranService _albaranService;
        private readonly IFacturaService _facturaService;

        private const int TenantId = 1;
        private const int ClienteId = 1;
        private const int SeriePresId = 1;   // serie presupuestos
        private const int SerieAlbId = 2;   // serie albaranes
        private const int SerieFacId = 3;   // serie facturas
        private const int ImpuestoId = 1;

        public FlujoCompletoTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"FlujoCompleto_{Guid.NewGuid()}")
                .Options;
            _context = new ApplicationDbContext(options);

            SembrarDatosBase();

            // ── Mocks comunes ────────────────────────────────────────────
            var aeatMock = new Mock<IAEATClient>();
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            ICacheService cache = new PassThroughCache();

            // ── Numeración: contador independiente por tipo de documento ──
            // Así cada servicio obtiene su propio número correlativo
            var contadorPres = 0;
            var contadorAlb = 0;
            var contadorFac = 0;

            var numeracionMock = new Mock<ISerieNumeracionService>();
            numeracionMock
                .Setup(s => s.ObtenerSiguienteNumeroAsync(
                    It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<int>(), DocumentTypes.PRESUPUESTO))
                .ReturnsAsync(() =>
                {
                    contadorPres++;
                    return ($"P-2024-{contadorPres:D3}", contadorPres);
                });
            numeracionMock
                .Setup(s => s.ObtenerSiguienteNumeroAsync(
                    It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<int>(), DocumentTypes.ALBARAN))
                .ReturnsAsync(() =>
                {
                    contadorAlb++;
                    return ($"A-2024-{contadorAlb:D3}", contadorAlb);
                });
            numeracionMock
                .Setup(s => s.ObtenerSiguienteNumeroAsync(
                    It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<int>(), DocumentTypes.FACTURA))
                .ReturnsAsync(() =>
                {
                    contadorFac++;
                    return ($"F-2024-{contadorFac:D3}", contadorFac);
                });

            // ── Construir servicios ───────────────────────────────────────
            _presupuestoService = new PresupuestoService(
                _context,
                numeracionMock.Object,
                new Mock<ILogger<PresupuestoService>>().Object,
                cache);

            _albaranService = new AlbaranService(
                _context,
                numeracionMock.Object,
                new Mock<ILogger<AlbaranService>>().Object);

            var verifactu = new VERIFACTUService(
                _context,
                aeatMock.Object,
                new Mock<ILogger<VERIFACTUService>>().Object);

            _facturaService = new FacturaService(
                _context,
                numeracionMock.Object,
                verifactu,
                null!,
                new Mock<ILogger<FacturaService>>().Object,
                scopeFactoryMock.Object,
                cache);
        }

        // ══════════════════════════════════════════════════════════════════
        // FLUJO 1: PRESUPUESTO → FACTURA (directo, sin albarán)
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Flujo_PresupuestoDirectoAFactura_TodoElCiclo()
        {
            // ── PASO 1: Crear presupuesto en estado Borrador ──────────────
            var presupuesto = await _presupuestoService.CrearPresupuestoAsync(TenantId,
                new PresupuestoCreateDto
                {
                    ClienteId = ClienteId,
                    SerieId = SeriePresId,
                    Lineas = new List<LineaPresupuestoDto>
                    {
                        new()
                        {
                            Descripcion        = "Desarrollo web",
                            Cantidad           = 10,
                            PrecioUnitario     = 100m,
                            PorcentajeDescuento = 0,
                            TipoImpuestoId     = ImpuestoId
                        }
                    }
                });

            presupuesto.Estado.Should().Be("Borrador");
            presupuesto.BaseImponible.Should().Be(1000m);
            presupuesto.TotalIVA.Should().Be(210m);
            presupuesto.Total.Should().Be(1210m);

            // ── PASO 2: Enviar el presupuesto al cliente ──────────────────
            var enviado = await _presupuestoService.CambiarEstadoAsync(TenantId, presupuesto.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Enviado" });

            enviado.Estado.Should().Be("Enviado");

            // ── PASO 3: El cliente acepta ─────────────────────────────────
            var aceptado = await _presupuestoService.CambiarEstadoAsync(TenantId, presupuesto.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Aceptado" });

            aceptado.Estado.Should().Be("Aceptado");

            // ── PASO 4: Convertir directamente a factura ──────────────────
            var factura = await _facturaService.ConvertirDesdePresupuestoAsync(TenantId, presupuesto.Id,
                new ConvertirPresupuestoAFacturaDto
                {
                    SerieId = SerieFacId
                });

            // Verificar que la factura se creó correctamente
            factura.Should().NotBeNull();
            factura.Estado.Should().Be("Emitida");
            factura.BaseImponible.Should().Be(1000m,
                because: "los totales deben conservarse del presupuesto");
            factura.TotalIVA.Should().Be(210m);
            factura.Total.Should().Be(1210m);
            factura.Huella.Should().NotBeNullOrEmpty(
                because: "toda factura debe tener huella VERIFACTU");

            // ── PASO 5: Verificar que el presupuesto quedó como Facturado ─
            var presupuestoFinal = await _presupuestoService.ObtenerPorIdAsync(TenantId, presupuesto.Id);
            presupuestoFinal!.Estado.Should().Be("Facturado",
                because: "al convertir a factura el presupuesto debe marcarse como Facturado");
        }

        // ══════════════════════════════════════════════════════════════════
        // FLUJO 2: PRESUPUESTO → ALBARÁN → FACTURA (flujo completo)
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Flujo_PresupuestoAlbaranFactura_TodoElCiclo()
        {
            // ── PASO 1: Crear y aceptar presupuesto ───────────────────────
            var presupuesto = await _presupuestoService.CrearPresupuestoAsync(TenantId,
                new PresupuestoCreateDto
                {
                    ClienteId = ClienteId,
                    SerieId = SeriePresId,
                    Lineas = new List<LineaPresupuestoDto>
                    {
                        new()
                        {
                            Descripcion    = "Instalación servidor",
                            Cantidad       = 1,
                            PrecioUnitario = 500m,
                            TipoImpuestoId = ImpuestoId
                        },
                        new()
                        {
                            Descripcion    = "Configuración red",
                            Cantidad       = 2,
                            PrecioUnitario = 150m,
                            TipoImpuestoId = ImpuestoId
                        }
                    }
                });

            await _presupuestoService.CambiarEstadoAsync(TenantId, presupuesto.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Enviado" });
            await _presupuestoService.CambiarEstadoAsync(TenantId, presupuesto.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Aceptado" });

            // ── PASO 2: Convertir a albarán ───────────────────────────────
            var albaran = await _albaranService.ConvertirDesdePresupuesto(TenantId, presupuesto.Id,
                new ConvertirPresupuestoDto
                {
                    SerieId = SerieAlbId
                });

            albaran.Should().NotBeNull();
            albaran.Estado.Should().Be("Pendiente");
            albaran.Lineas.Should().HaveCount(2,
                because: "el albarán debe copiar todas las líneas del presupuesto");
            albaran.BaseImponible.Should().Be(presupuesto.BaseImponible,
                because: "los totales del albarán deben coincidir con el presupuesto");

            // ── PASO 3: Marcar albarán como entregado ─────────────────────
            var albaranEntregado = await _albaranService.CambiarEstadoAsync(TenantId, albaran.Id,
                new CambiarEstadoAlbaranDto { NuevoEstado = "Entregado" });

            albaranEntregado.Estado.Should().Be("Entregado");

            // ── PASO 4: Convertir albarán a factura ───────────────────────
            var factura = await _facturaService.ConvertirDesdeAlbaranAsync(TenantId, albaran.Id,
                new ConvertirAlbaranesAFacturaDto
                {
                    AlbaranesIds = new List<int> { albaran.Id },
                    SerieId = SerieFacId
                });

            factura.Should().NotBeNull();
            factura.Estado.Should().Be("Emitida");
            factura.Huella.Should().NotBeNullOrEmpty();

            // ── PASO 5: Verificar referencias cruzadas ────────────────────
            // La factura debe saber de qué albaranes viene
            factura.AlbaranesIds.Should().Contain(albaran.Id,
                because: "la factura debe referenciar el albarán del que procede");
        }

        // ══════════════════════════════════════════════════════════════════
        // VALIDACIONES DE ESTADO — no se puede saltar pasos
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task Flujo_PresupuestoBorrador_NoPuedeConvertirseAAlbaran()
        {
            // Un presupuesto en Borrador NO puede convertirse directamente
            // Hay que pasar por Enviado → Aceptado primero
            var presupuesto = await _presupuestoService.CrearPresupuestoAsync(TenantId,
                new PresupuestoCreateDto
                {
                    ClienteId = ClienteId,
                    SerieId = SeriePresId,
                    Lineas = new List<LineaPresupuestoDto>
                    {
                        new() { Descripcion = "Servicio", Cantidad = 1,
                                PrecioUnitario = 100m, TipoImpuestoId = ImpuestoId }
                    }
                });

            Func<Task> accion = () => _albaranService.ConvertirDesdePresupuesto(
                TenantId, presupuesto.Id, new ConvertirPresupuestoDto { SerieId = SerieAlbId });

            await accion.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*Aceptado*",
                    because: "solo presupuestos Aceptados pueden convertirse a albarán");
        }

        [Fact]
        public async Task Flujo_EstadosPresupuesto_TransicionesInvalidas()
        {
            var presupuesto = await _presupuestoService.CrearPresupuestoAsync(TenantId,
                new PresupuestoCreateDto
                {
                    ClienteId = ClienteId,
                    SerieId = SeriePresId,
                    Lineas = new List<LineaPresupuestoDto>
                    {
                        new() { Descripcion = "Servicio", Cantidad = 1,
                                PrecioUnitario = 100m, TipoImpuestoId = ImpuestoId }
                    }
                });

            // No se puede pasar de Borrador directamente a Aceptado
            Func<Task> saltoInvalido = () => _presupuestoService.CambiarEstadoAsync(
                TenantId, presupuesto.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Aceptado" });

            await saltoInvalido.Should()
                .ThrowAsync<InvalidOperationException>(
                    because: "no se puede aceptar un presupuesto sin enviarlo antes");
        }

        [Fact]
        public async Task Flujo_HuellaEncadenada_SegundaFacturaTieneHuellaAnterior()
        {
            // Verificamos que el encadenamiento VERIFACTU funciona:
            // la huella de la factura 2 debe incluir la huella de la factura 1
            var dto = new PresupuestoCreateDto
            {
                ClienteId = ClienteId,
                SerieId = SeriePresId,
                Lineas = new List<LineaPresupuestoDto>
                {
                    new() { Descripcion = "Servicio", Cantidad = 1,
                            PrecioUnitario = 100m, TipoImpuestoId = ImpuestoId }
                }
            };

            // Crear dos presupuestos y convertirlos a facturas
            var pres1 = await CrearPresupuestoAceptadoAsync(dto);
            var pres2 = await CrearPresupuestoAceptadoAsync(dto);

            var factura1 = await _facturaService.ConvertirDesdePresupuestoAsync(TenantId, pres1.Id,
                new ConvertirPresupuestoAFacturaDto { SerieId = SerieFacId });

            var factura2 = await _facturaService.ConvertirDesdePresupuestoAsync(TenantId, pres2.Id,
                new ConvertirPresupuestoAFacturaDto { SerieId = SerieFacId });

            // La segunda factura debe encadenarse con la primera
            factura2.HuellaAnterior.Should().Be(factura1.Huella,
                because: "VERIFACTU exige que cada factura encadene con la anterior");
            factura2.Huella.Should().NotBe(factura1.Huella,
                because: "cada factura tiene su propia huella única");
        }

        // ══════════════════════════════════════════════════════════════════
        // HELPERS
        // ══════════════════════════════════════════════════════════════════

        private async Task<PresupuestoResponseDto> CrearPresupuestoAceptadoAsync(
            PresupuestoCreateDto dto)
        {
            var p = await _presupuestoService.CrearPresupuestoAsync(TenantId, dto);
            await _presupuestoService.CambiarEstadoAsync(TenantId, p.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Enviado" });
            await _presupuestoService.CambiarEstadoAsync(TenantId, p.Id,
                new CambiarEstadoPresupuestoDto { NuevoEstado = "Aceptado" });
            return p;
        }

        private void SembrarDatosBase()
        {
            _context.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Nombre = "Empresa Test SL",
                NIF = "B12345678",
                Schema = "test"
            });

            _context.Clientes.Add(new Cliente
            {
                Id = ClienteId,
                TenantId = TenantId,
                Nombre = "Cliente Test SA",
                NIF = "A87654321",
                TipoCliente = "B2B",
                Activo = true,
                RegimenRecargoEquivalencia = false,
                PorcentajeRetencionDefecto = 0
            });

            // Serie presupuestos
            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SeriePresId,
                TenantId = TenantId,
                Codigo = "P",
                Descripcion = "Presupuestos",
                TipoDocumento = DocumentTypes.PRESUPUESTO,
                ProximoNumero = 1,
                Ejercicio = DateTime.UtcNow.Year,
                Activo = true,
                Bloqueada = false
            });

            // Serie albaranes
            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieAlbId,
                TenantId = TenantId,
                Codigo = "A",
                Descripcion = "Albaranes",
                TipoDocumento = DocumentTypes.ALBARAN,
                ProximoNumero = 1,
                Ejercicio = DateTime.UtcNow.Year,
                Activo = true,
                Bloqueada = false
            });

            // Serie facturas
            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieFacId,
                TenantId = TenantId,
                Codigo = "F",
                Descripcion = "Facturas",
                TipoDocumento = DocumentTypes.FACTURA,
                ProximoNumero = 1,
                Ejercicio = DateTime.UtcNow.Year,
                Activo = true,
                Bloqueada = false
            });

            // Tipo de impuesto IVA 21%
            _context.TiposImpuesto.Add(new TipoImpuesto
            {
                Id = ImpuestoId,
                TenantId = TenantId,
                Nombre = "IVA 21%",
                PorcentajeIva = 21m,
                PorcentajeRecargo = 0m,
                Activo = true
            });

            _context.SaveChanges();
        }
    }

    // ── Stub de caché reutilizable ─────────────────────────────────────────
    file sealed class PassThroughCache : ICacheService
    {
        public Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
            => factory()!;
        public void Remove(string key) { }
        public void RemoveByPrefix(string prefix) { }
    }
}