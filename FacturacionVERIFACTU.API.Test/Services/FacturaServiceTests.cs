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

namespace FacturacionVERIFACTU.API.Tests.Services
{
    // Stub de caché que siempre ejecuta el factory — los tests necesitan datos reales de BD
    file sealed class PassThroughCache : ICacheService
    {
        public Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
            => factory()!;
        public void Remove(string key) { }
        public void RemoveByPrefix(string prefix) { }
    }

    public class FacturaServiceTests
    {
        // ── Instancias compartidas por todos los tests de esta clase ───────
        private readonly ApplicationDbContext _context;
        private readonly FacturaService _service;

        // IDs fijos para los datos de prueba — así los tests son legibles
        private const int TenantId = 1;
        private const int ClienteId = 1;
        private const int SerieId = 1;
        private const int ImpuestoId = 1;

        public FacturaServiceTests()
        {
            // BD nueva e independiente para cada instancia de test
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"FacturaTests_{Guid.NewGuid()}")
                .Options;
            _context = new ApplicationDbContext(options);

            // Sembrar datos base que necesitan TODOS los tests
            // Esto equivale a tener un estado inicial limpio y conocido
            SembrarDatosBase();

            // Mocks de las dependencias que no queremos probar aquí
            var loggerMock = new Mock<ILogger<FacturaService>>();
            var numeracionMock = new Mock<ISerieNumeracionService>();
            var verifactuLoggerMock = new Mock<ILogger<VERIFACTUService>>();
            var aeatMock = new Mock<IAEATClient>();
            var scopeFactoryMock = new Mock<IServiceScopeFactory>();
            ICacheService cacheService = new PassThroughCache();

            // ── Configurar el mock de numeración ──────────────────────────
            // Aquí aprendemos algo nuevo: Setup() define QUÉ devuelve el mock
            // cuando se llama con ciertos parámetros.
            // It.IsAny<T>() significa "acepta cualquier valor de tipo T"
            numeracionMock
                .Setup(s => s.ObtenerSiguienteNumeroAsync(
                    It.IsAny<int>(),     // tenantId
                    It.IsAny<string>(),  // serie
                    It.IsAny<int>(),     // ejercicio
                    It.IsAny<string>()   // tipoDocumento
                ))
                .ReturnsAsync(("F-2024-001", 1));  // siempre devuelve este número

            // ── Construir VERIFACTUService real (con BD en memoria) ────────
            var verifactuService = new VERIFACTUService(
                _context,
                aeatMock.Object,
                verifactuLoggerMock.Object);

            // ── Construir el servicio que vamos a testear ─────────────────
            _service = new FacturaService(
                _context,
                numeracionMock.Object,
                verifactuService,
                null!,               // AEATClient — no se usa en los tests que haremos
                loggerMock.Object,
                scopeFactoryMock.Object,
                cacheService);
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE VALIDACIÓN — comprueban que el servicio rechaza datos malos
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CrearFactura_ClienteNoExiste_LanzaExcepcion()
        {
            // Intentamos crear una factura para un cliente que NO existe en BD
            var dto = CrearDtoBasico(clienteId: 999); // ID que no existe

            // Func<Task> es necesario para que FluentAssertions pueda
            // capturar excepciones de métodos async
            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);

            await accion.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*999*"); // el mensaje debe mencionar el ID
        }

        [Fact]
        public async Task CrearFactura_SinLineas_LanzaExcepcion()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>(); // sin líneas

            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);

            await accion.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*línea*");
        }

        [Fact]
        public async Task CrearFactura_CantidadCeroSinDescripcion_LanzaExcepcion()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
    {
        new() { Descripcion = "", Cantidad = 0, PrecioUnitario = 0 }
    };

            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);
            await accion.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task CrearFactura_CantidadCeroConDescripcion_EsLineaTextoValida()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
    {
        new() { Descripcion = "── Trabajos de instalación ──", Cantidad = 0, PrecioUnitario = 0 },
        new() { Descripcion = "Mano de obra", Cantidad = 1, PrecioUnitario = 100, TipoImpuestoId = ImpuestoId }
    };

            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);
            await accion.Should().NotThrowAsync(
                because: "cantidad 0 con descripción es una línea de texto válida");
        }

        [Fact]
        public async Task CrearFactura_CantidadNegativa_AbonoValido()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
    {
        new() { Descripcion = "Abono servicio", Cantidad = -1,
                PrecioUnitario = 500m, TipoImpuestoId = ImpuestoId }
    };

            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);
            await accion.Should().NotThrowAsync(
                because: "cantidades negativas son válidas para abonos");
        }

        [Fact]
        public async Task CrearFactura_PrecioNegativo_LanzaExcepcion()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
            {
                new() { Descripcion = "Servicio", Cantidad = 1, PrecioUnitario = -50 }
            };

            Func<Task> accion = () => _service.CrearFacturaAsync(TenantId, dto);

            await accion.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*negativo*");
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE CREACIÓN CORRECTA
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public async Task CrearFactura_DatosCorrectos_GuardaEnBD()
        {
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
            {
                new()
                {
                    Descripcion      = "Servicio de consultoría",
                    Cantidad         = 2,
                    PrecioUnitario   = 500m,
                    TipoImpuestoId   = ImpuestoId
                }
            };

            var resultado = await _service.CrearFacturaAsync(TenantId, dto);

            // Verificar el DTO de respuesta
            resultado.Should().NotBeNull();
            resultado.Numero.Should().Be("F-2024-001");
            resultado.Estado.Should().Be("Emitida");

            // Verificar que realmente se guardó en BD
            // Esto es importante: no basta con que el DTO sea correcto,
            // tiene que persistir en la base de datos
            var facturaEnBD = await _context.Facturas.FirstOrDefaultAsync();
            facturaEnBD.Should().NotBeNull();
            facturaEnBD!.TenantId.Should().Be(TenantId);
        }

        [Fact]
        public async Task CrearFactura_DatosCorrectos_CalculaTotalesCorrectamente()
        {
            // 2 unidades × 500 € = 1000 € base, IVA 21% = 210 €, total 1210 €
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
            {
                new()
                {
                    Descripcion    = "Consultoría",
                    Cantidad       = 2,
                    PrecioUnitario = 500m,
                    TipoImpuestoId = ImpuestoId
                }
            };

            var resultado = await _service.CrearFacturaAsync(TenantId, dto);

            resultado.BaseImponible.Should().Be(1000m);
            resultado.TotalIVA.Should().Be(210m);
            resultado.Total.Should().Be(1210m);
        }

        [Fact]
        public async Task CrearFactura_ClienteB2C_TipoFacturaF2()
        {
            // Los clientes B2C sin NIF generan facturas simplificadas (F2)
            // Añadimos un cliente B2C específico para este test
            var clienteB2C = new Cliente
            {
                Id = 99,
                TenantId = TenantId,
                Nombre = "Cliente Particular",
                NIF = "",           // sin NIF
                TipoCliente = "B2C",
                Activo = true
            };
            _context.Clientes.Add(clienteB2C);
            await _context.SaveChangesAsync();

            var dto = CrearDtoBasico(clienteId: 99);
            dto.Lineas = new List<LineaFacturaDto>
            {
                new() { Descripcion = "Producto", Cantidad = 1,
                        PrecioUnitario = 50m, TipoImpuestoId = ImpuestoId }
            };

            var resultado = await _service.CrearFacturaAsync(TenantId, dto);

            resultado.TipoFacturaVERIFACTU.Should().Be("F2",
                because: "un cliente B2C sin NIF genera factura simplificada");
        }

        [Fact]
        public async Task CrearFactura_GeneraHuellaVERIFACTU()
        {
            // Toda factura creada debe tener huella VERIFACTU — es obligatorio por ley
            var dto = CrearDtoBasico();
            dto.Lineas = new List<LineaFacturaDto>
            {
                new() { Descripcion = "Servicio", Cantidad = 1,
                        PrecioUnitario = 100m, TipoImpuestoId = ImpuestoId }
            };

            var resultado = await _service.CrearFacturaAsync(TenantId, dto);

            resultado.Huella.Should().NotBeNullOrEmpty(
                because: "VERIFACTU exige huella en todas las facturas");
        }

        // ══════════════════════════════════════════════════════════════════
        // MÉTODOS AUXILIARES
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Siembra los datos mínimos que necesitan todos los tests.
        /// Se ejecuta una vez al construir la clase de test.
        /// </summary>
        private void SembrarDatosBase()
        {
            // Tenant
            _context.Tenants.Add(new Tenant
            {
                Id = TenantId,
                Nombre = "Empresa Test SL",
                NIF = "B12345678",
                Schema = "test"
            });

            // Cliente B2B con NIF (genera facturas F1)
            _context.Clientes.Add(new Cliente
            {
                Id = ClienteId,
                TenantId = TenantId,
                Nombre = "Cliente Test SA",
                NIF = "A87654321",
                TipoCliente = "B2B",
                Activo = true
            });

            _context.SeriesNumeracion.Add(new SerieNumeracion
            {
                Id = SerieId,
                TenantId = TenantId,
                Codigo = "A",
                Descripcion = "Serie Test",
                TipoDocumento = "Factura",
                Ejercicio = DateTime.UtcNow.Year
            });

            _context.TiposImpuesto.Add(new TipoImpuesto
            {
                Id = ImpuestoId,
                TenantId = TenantId,
                Nombre = "IVA 21%",
                PorcentajeIva = 21m
            });

            _context.SaveChanges();
        }

        private FacturaCreateDto CrearDtoBasico(int clienteId = ClienteId) => new FacturaCreateDto
        {
            ClienteId = clienteId,
            SerieId = SerieId,
            FechaEmision = DateTime.UtcNow,
            Lineas = new List<LineaFacturaDto>
            {
                new() { Descripcion = "Servicio test", Cantidad = 1, PrecioUnitario = 100m, TipoImpuestoId = ImpuestoId }
            }
        };
    }
}