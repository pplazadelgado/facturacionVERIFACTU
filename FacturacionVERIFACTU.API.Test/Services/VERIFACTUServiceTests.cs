using API.Data.Entities;
using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FacturacionVERIFACTU.API.Tests.Services
{
    public class VERIFACTUServiceTests
    {
        // ── Dependencias del servicio ──────────────────────────────────────
        private readonly VERIFACTUService _service;
        private readonly ApplicationDbContext _context;

        public VERIFACTUServiceTests()
        {
            // BASE DE DATOS EN MEMORIA
            // Cada test crea una BD nueva e independiente gracias al Guid.
            // Si usáramos siempre el mismo nombre, los datos de un test
            // contaminarían al siguiente.
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
                .Options;
            _context = new ApplicationDbContext(options);

            // MOCKS con Moq
            // Mock<T> crea un objeto falso que implementa la interfaz T.
            // Por defecto todos sus métodos no hacen nada (void) o devuelven null.
            var loggerMock = new Mock<ILogger<VERIFACTUService>>();
            var aeatClientMock = new Mock<IAEATClient>();

            // Construimos el servicio real con la BD en memoria y los mocks
            _service = new VERIFACTUService(_context, aeatClientMock.Object, loggerMock.Object);
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE GenerarHuellaFactura
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public void GenerarHuella_Mismosdatos_SiempreProduceMismaHuella()
        {
            // La huella es determinista: misma entrada = misma salida.
            // Esto es crítico para VERIFACTU — si cambia algo, la huella cambia.
            var factura = CrearFacturaDePrueba();

            var huella1 = _service.GenerarHuellaFactura(factura, "");
            var huella2 = _service.GenerarHuellaFactura(factura, "");

            huella1.Should().Be(huella2,
                because: "el algoritmo SHA-256 es determinista");
        }

        [Fact]
        public void GenerarHuella_FacturaNula_LanzaArgumentNullException()
        {
            // Verificamos que el método protege contra nulls correctamente.
            // El método Assert de xunit para excepciones es:
            // () => es una lambda que ejecuta el código que debe explotar
            var accion = () => _service.GenerarHuellaFactura(null!, "");

            accion.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void GenerarHuella_CambiaNIF_ProduceHuellaDistinta()
        {
            // Si cambia cualquier dato de la factura, la huella debe cambiar.
            // Esto garantiza la integridad: no se puede modificar una factura
            // sin que se detecte.
            var factura1 = CrearFacturaDePrueba();
            var factura2 = CrearFacturaDePrueba();
            factura2.Tenant!.NIF = "B99999999";  // NIF diferente

            var huella1 = _service.GenerarHuellaFactura(factura1, "");
            var huella2 = _service.GenerarHuellaFactura(factura2, "");

            huella1.Should().NotBe(huella2,
                because: "cambiar el NIF debe producir una huella completamente diferente");
        }

        [Fact]
        public void GenerarHuella_ConHuellaAnterior_EsDistintaASinHuellaAnterior()
        {
            // El encadenamiento: la huella de la factura 2 incluye la huella
            // de la factura 1. Si no fuera así, no habría cadena y alguien
            // podría insertar facturas falsas en medio.
            var factura = CrearFacturaDePrueba();

            var huellaEncadenada = _service.GenerarHuellaFactura(factura, "huella_factura_anterior");
            var huellaLibre = _service.GenerarHuellaFactura(factura, "");

            huellaEncadenada.Should().NotBe(huellaLibre,
                because: "la huella anterior forma parte del cálculo");
        }

        [Fact]
        public void GenerarHuella_DevuelveCadenaBase64NoVacia()
        {
            // Verificamos el formato: debe ser Base64 y no puede estar vacío.
            var factura = CrearFacturaDePrueba();

            var huella = _service.GenerarHuellaFactura(factura, "");

            huella.Should().NotBeNullOrEmpty();
            // Intentar convertir de Base64 no debe lanzar excepción
            var accion = () => Convert.FromBase64String(huella);
            accion.Should().NotThrow(because: "la huella debe ser Base64 válido");
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE ValidarHuella
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public void ValidarHuella_HuellaCorrecta_DevuelveTrue()
        {
            var factura = CrearFacturaDePrueba();

            // Primero generamos la huella correcta
            var huellaCorrecta = _service.GenerarHuellaFactura(factura, "");

            // Luego validamos que la reconoce como correcta
            var esValida = _service.ValidarHuella(factura, "", huellaCorrecta);

            esValida.Should().BeTrue();
        }

        [Fact]
        public void ValidarHuella_HuellaManipulada_DevuelveFalse()
        {
            // Simulamos que alguien modificó la factura después de firmarla.
            // La validación debe detectarlo.
            var factura = CrearFacturaDePrueba();
            var huellaOriginal = _service.GenerarHuellaFactura(factura, "");

            // Modificamos la factura (como haría un atacante)
            factura.Total = 999999m;

            var esValida = _service.ValidarHuella(factura, "", huellaOriginal);

            esValida.Should().BeFalse(
                because: "modificar el total después de firmar debe invalidar la huella");
        }

        // ══════════════════════════════════════════════════════════════════
        // TESTS DE ValidarCadenaFacturas
        // ══════════════════════════════════════════════════════════════════

        [Fact]
        public void ValidarCadena_ListaVacia_DevuelveTrue()
        {
            // Caso límite: sin facturas no hay nada que validar
            var resultado = _service.ValidarCadenaFacturas(new List<Factura>());
            resultado.Should().BeTrue();
        }

        [Fact]
        public void ValidarCadena_CadenaCorrecta_DevuelveTrue()
        {
            // Construimos una cadena de 2 facturas encadenadas correctamente
            var factura1 = CrearFacturaDePrueba(numero: "F-2024-001");
            factura1.HuellaAnterior = "";
            factura1.Huella = _service.GenerarHuellaFactura(factura1, "");

            var factura2 = CrearFacturaDePrueba(numero: "F-2024-002");
            factura2.HuellaAnterior = factura1.Huella;
            factura2.Huella = _service.GenerarHuellaFactura(factura2, factura1.Huella!);

            var resultado = _service.ValidarCadenaFacturas(
                new List<Factura> { factura1, factura2 });

            resultado.Should().BeTrue();
        }

        [Fact]
        public void ValidarCadena_FacturaManipulada_DevuelveFalse()
        {
            // Si alguien modifica una factura en medio de la cadena,
            // la validación debe detectarlo
            var factura1 = CrearFacturaDePrueba(numero: "F-2024-001");
            factura1.HuellaAnterior = "";
            factura1.Huella = _service.GenerarHuellaFactura(factura1, "");

            var factura2 = CrearFacturaDePrueba(numero: "F-2024-002");
            factura2.HuellaAnterior = factura1.Huella;
            factura2.Huella = _service.GenerarHuellaFactura(factura2, factura1.Huella!);

            // Manipulamos la factura 1 después de encadenar
            factura1.Total = 99999m;

            var resultado = _service.ValidarCadenaFacturas(
                new List<Factura> { factura1, factura2 });

            resultado.Should().BeFalse(
                because: "modificar una factura rompe la cadena entera");
        }

        // ══════════════════════════════════════════════════════════════════
        // MÉTODO AUXILIAR — fábrica de facturas de prueba
        // En los tests necesitas crear objetos con datos válidos repetidamente.
        // En lugar de repetir código, un método privado actúa como "fábrica".
        // ══════════════════════════════════════════════════════════════════

        private static Factura CrearFacturaDePrueba(string numero = "F-2024-001")
        {
            return new Factura
            {
                Id = 1,
                Numero = numero,
                FechaEmision = new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                TotalIVA = 21.00m,
                Total = 121.00m,
                TipoFacturaVERIFACTU = "F1",
                Tenant = new FacturacionVERIFACTU.API.Data.Entities.Tenant
                {
                    NIF = "B12345678",
                    Nombre = "Empresa Test SL"
                },
                Cliente = new FacturacionVERIFACTU.API.Data.Entities.Cliente
                {
                    NIF = "12345678Z",
                    Nombre = "Cliente Test",
                    TipoCliente = "B2B"
                },
                Serie = new FacturacionVERIFACTU.API.Data.Entities.SerieNumeracion
                {
                    Codigo = "A"
                }
            };
        }
    }
}
