
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

namespace FacturacionVERIFACTU.API.Tests.Security
{
    /// <summary>
    /// Tests de aislamiento multi-tenant.
    /// Verifican que los datos de un tenant son completamente invisibles
    /// para otro tenant, incluso compartiendo la misma base de datos.
    /// </summary>
    public class MultiTenantTests
    {
        private readonly ApplicationDbContext _context;
        private readonly FacturaService _serviceA;   // servicio para Tenant A
        private readonly FacturaService _serviceB;   // servicio para Tenant B

        // IDs fijos para los dos tenants
        private const int TenantA = 1;
        private const int TenantB = 2;

        // IDs de datos de cada tenant
        private const int ClienteAId = 10;
        private const int ClienteBId = 20;
        private const int SerieAId = 10;
        private const int SerieBId = 20;
        private const int ImpuestoAId = 10;
        private const int ImpuestoBId = 20;

        public MultiTenantTests()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"MultiTenantTests_{Guid.NewGuid()}")
                .Options;
            _context = new ApplicationDbContext(options);

            // Sembrar datos de AMBOS tenants en la misma BD
            // Esto simula la situación real: misma BD, datos mezclados
            SembrarDatosAmbosTeants();

            // Creamos un servicio por tenant — en la app real el tenantId
            // viene del JWT, aquí lo pasamos directamente en cada llamada
            _serviceA = CrearServicio();
            _serviceB = CrearServicio();
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE AISLAMIENTO DE FACTURAS
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task ObtenerFacturas_TenantA_NoVeFacturasDeTenantB()
        {
            // Crear una factura para cada tenant
            await CrearFacturaParaTenant(TenantA, ClienteAId, SerieAId, ImpuestoAId);
            await CrearFacturaParaTenant(TenantB, ClienteBId, SerieBId, ImpuestoBId);

            // El tenant A pide SUS facturas
            var facturasA = await _serviceA.ObtenerTodosAsync(TenantA, null, null, null);

            // Solo debe ver la suya — nunca la del tenant B
            facturasA.Should().HaveCount(1,
                because: "cada tenant solo debe ver sus propias facturas");
            facturasA.Should().AllSatisfy(f =>
                f.TenantId.Should().Be(TenantA,
                    because: "ninguna factura de otro tenant debe filtrarse"));
        }

        [Fact]
        public async Task ObtenerFacturas_TenantB_NoVeFacturasDeTenantA()
        {
            // Creamos 3 facturas del tenant A y 1 del tenant B
            await CrearFacturaParaTenant(TenantA, ClienteAId, SerieAId, ImpuestoAId);
            await CrearFacturaParaTenant(TenantA, ClienteAId, SerieAId, ImpuestoAId);
            await CrearFacturaParaTenant(TenantA, ClienteAId, SerieAId, ImpuestoAId);
            await CrearFacturaParaTenant(TenantB, ClienteBId, SerieBId, ImpuestoBId);

            var facturasB = await _serviceB.ObtenerTodosAsync(TenantB, null, null, null);

            // El tenant B solo ve la suya, aunque haya 3 del tenant A en la misma BD
            facturasB.Should().HaveCount(1,
                because: "aunque haya más facturas de otros tenants en BD, solo ve las suyas");
        }

        [Fact]
        public async Task ObtenerFacturaPorId_TenantA_NoPuedeAccederAFacturaDeTenantB()
        {
            // Creamos una factura del tenant B
            var facturaB = await CrearFacturaParaTenant(TenantB, ClienteBId, SerieBId, ImpuestoBId);

            // El tenant A intenta acceder a esa factura por ID
            // NUNCA debe poder verla aunque conozca el ID
            var resultado = await _serviceA.ObtenerPorIdAsync(TenantA, facturaB.Id);

            resultado.Should().BeNull(
                because: "acceder a una factura de otro tenant con su ID debe devolver null");
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE AISLAMIENTO DE CLIENTES
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CrearFactura_TenantA_NoPuedeUsarClienteDeTenantB()
        {
            // El tenant A intenta crear una factura usando el cliente del tenant B
            // Esto simula un ataque: alguien que conoce un ClienteId ajeno
            var dto = new FacturaCreateDto
            {
                ClienteId = ClienteBId,  // ← cliente del tenant B
                SerieId = SerieAId,
                FechaEmision = DateTime.UtcNow,
                Lineas = new List<LineaFacturaDto>
                {
                    new() { Descripcion = "Servicio", Cantidad = 1,
                            PrecioUnitario = 100m, TipoImpuestoId = ImpuestoAId }
                }
            };

            Func<Task> accion = () => _serviceA.CrearFacturaAsync(TenantA, dto);

            // Debe fallar — el cliente no pertenece al tenant A
            await accion.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage($"*{ClienteBId}*",
                    because: "el servicio debe rechazar clientes de otros tenants");
        }

        // ══════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES
        // ══════════════════════════════════════════════════════════════════

        private void SembrarDatosAmbosTeants()
        {
            // ── Tenant A ──────────────────────────────────────────────────
            _context.Tenants.Add(new Tenant
            {
                Id = TenantA,
                Nombre = "Empresa A SL",
                NIF = "A11111111",
                Schema = "tenant_a"
            });
            _context.Clientes.Add(new Cliente
            {
                Id = ClienteAId,
                TenantId = TenantA,
                Nombre = "Cliente de A",
                NIF = "11111111A",
                TipoCliente = "B2B",
                Activo = true
            });
            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieAId,
                TenantId = TenantA,
                Codigo = "FA",
                Descripcion = "Facturas A",
                TipoDocumento = DocumentTypes.FACTURA,
                Activo = true,
                Bloqueada = false,
                ProximoNumero = 0,
                Ejercicio = DateTime.UtcNow.Year
            });
            _context.TiposImpuesto.Add(new TipoImpuesto
            {
                Id = ImpuestoAId,
                TenantId = TenantA,
                Nombre = "IVA 21% A",
                PorcentajeIva = 21m,
                PorcentajeRecargo = 0m,
                Activo = true
            });

            // ── Tenant B ──────────────────────────────────────────────────
            _context.Tenants.Add(new Tenant
            {
                Id = TenantB,
                Nombre = "Empresa B SL",
                NIF = "B22222222",
                Schema = "tenant_b"
            });
            _context.Clientes.Add(new Cliente
            {
                Id = ClienteBId,
                TenantId = TenantB,
                Nombre = "Cliente de B",
                NIF = "22222222B",
                TipoCliente = "B2B",
                Activo = true
            });
            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieBId,
                TenantId = TenantB,
                Codigo = "FB",
                Descripcion = "Facturas B",
                TipoDocumento = DocumentTypes.FACTURA,
                Activo = true,
                Bloqueada = false,
                ProximoNumero = 0,
                Ejercicio = DateTime.UtcNow.Year
            });
            _context.TiposImpuesto.Add(new TipoImpuesto
            {
                Id = ImpuestoBId,
                TenantId = TenantB,
                Nombre = "IVA 21% B",
                PorcentajeIva = 21m,
                PorcentajeRecargo = 0m,
                Activo = true
            });

            _context.SaveChanges();
        }

        /// <summary>
        /// Crea el servicio con todos sus mocks.
        /// El mismo servicio se usa para ambos tenants — el aislamiento
        /// lo da el tenantId que se pasa en cada llamada, no el servicio.
        /// </summary>
        private FacturaService CrearServicio()
        {
            var loggerMock = new Mock<ILogger<FacturaService>>();
            var verifactuLogger = new Mock<ILogger<VERIFACTUService>>();
            var aeatMock = new Mock<IAEATClient>();
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            var cacheMock = new Mock<ICacheService>();
            var numeracionMock = new Mock<ISerieNumeracionService>();

            // Numeración que devuelve números únicos cada vez
            var contador = 0;
            numeracionMock
                .Setup(s => s.ObtenerSiguienteNumeroAsync(
                    It.IsAny<int>(), It.IsAny<string>(),
                    It.IsAny<int>(), It.IsAny<string>()))
                .ReturnsAsync(() =>
                {
                    contador++;
                    return ($"F-2024-{contador:D3}", contador);
                });

            cacheMock
                .Setup(c => c.GetOrCreateAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<Task<List<TipoImpuesto>>>>(),
                    It.IsAny<TimeSpan?>()))
                .Returns((string _, Func<Task<List<TipoImpuesto>>> factory, TimeSpan? __)
                    => factory());

            var verifactu = new VERIFACTUService(_context, aeatMock.Object, verifactuLogger.Object);

            return new FacturaService(
                _context, numeracionMock.Object, verifactu,
                null!, loggerMock.Object, scopeFactoryMock.Object, cacheMock.Object);
        }

        /// <summary>
        /// Crea una factura real en BD para el tenant indicado y devuelve su ID.
        /// </summary>
        private async Task<FacturaResponseDto> CrearFacturaParaTenant(
            int tenantId, int clienteId, int serieId, int impuestoId)
        {
            var dto = new FacturaCreateDto
            {
                ClienteId = clienteId,
                SerieId = serieId,
                FechaEmision = DateTime.UtcNow,
                Lineas = new List<LineaFacturaDto>
                {
                    new()
                    {
                        Descripcion    = "Servicio de prueba",
                        Cantidad       = 1,
                        PrecioUnitario = 100m,
                        TipoImpuestoId = impuestoId
                    }
                }
            };

            return await _serviceA.CrearFacturaAsync(tenantId, dto);
        }
    }
}