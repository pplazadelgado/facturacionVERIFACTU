using FacturacionVERIFACTU.API.Domain;
using FluentAssertions;
using Xunit;

namespace FacturacionVERIFACTU.API.Tests.Domain
{
    public class CalculadoraLineasTests
    {
        // ══════════════════════════════════════════════════════════
        // [Fact] = un test que siempre se ejecuta igual, sin parámetros
        // Nombre del método = descripción de lo que estás probando
        // Patrón: Metodo_Escenario_ResultadoEsperado
        // ══════════════════════════════════════════════════════════

        [Fact]
        public void Calcular_LineaSimple_SinDescuentoSinRecargo_DevuelveTotalesCorrectos()
        {
            // ARRANGE: preparamos los datos de entrada
            // 2 unidades × 100 € = 200 € base, IVA 21% = 42 €, total 242 €
            decimal cantidad = 2m;
            decimal precio = 100m;
            decimal descuento = 0m;
            decimal iva = 21m;
            decimal recargo = 0m;

            // ACT: ejecutamos el método que queremos probar
            var resultado = CalculadoraLineas.Calcular(cantidad, precio, descuento, iva, recargo);

            // ASSERT: verificamos que cada campo tiene el valor esperado
            // .Should().Be() es la sintaxis de FluentAssertions, más legible que Assert.Equal()
            resultado.Subtotal.Should().Be(200m);
            resultado.ImporteDescuento.Should().Be(0m);
            resultado.BaseImponible.Should().Be(200m);
            resultado.ImporteIva.Should().Be(42m);
            resultado.ImporteRecargo.Should().Be(0m);
            resultado.Importe.Should().Be(242m);
        }

        [Fact]
        public void Calcular_ConDescuento10Porciento_ReduceBaseImponible()
        {
            // 1 unidad × 100 € con 10 % descuento
            // → base 90 €, IVA 21% sobre 90 = 18,90 €, total 108,90 €
            var resultado = CalculadoraLineas.Calcular(
                cantidad: 1m,
                precioUnitario: 100m,
                porcentajeDescuento: 10m,
                porcentajeIva: 21m,
                porcentajeRecargo: 0m);

            resultado.ImporteDescuento.Should().Be(10m,
                because: "el 10% de 100€ son 10€ de descuento");
            resultado.BaseImponible.Should().Be(90m,
                because: "100€ - 10€ de descuento = 90€ de base");
            resultado.ImporteIva.Should().Be(18.90m,
                because: "21% de 90€ = 18,90€");
            resultado.Importe.Should().Be(108.90m);
        }

        [Fact]
        public void Calcular_ConRecargoEquivalencia_SumaRecargoAlImporteTotal()
        {
            // Cliente con recargo de equivalencia: IVA 21% + recargo 5,2%
            // Base 100 € → IVA 21 € + recargo 5,20 € = total 126,20 €
            var resultado = CalculadoraLineas.Calcular(
                cantidad: 1m,
                precioUnitario: 100m,
                porcentajeDescuento: 0m,
                porcentajeIva: 21m,
                porcentajeRecargo: 5.2m);

            resultado.ImporteIva.Should().Be(21m);
            resultado.ImporteRecargo.Should().Be(5.20m);
            resultado.Importe.Should().Be(126.20m);
        }

        // ══════════════════════════════════════════════════════════
        // [Theory] + [InlineData] = el MISMO test con distintos datos
        // xUnit lo ejecuta una vez por cada [InlineData]
        // Muy útil para probar los tipos de IVA sin repetir código
        // ══════════════════════════════════════════════════════════

        [Theory]
        [InlineData(0, 0)]      // exento
        [InlineData(4, 4)]      // superreducido (alimentos básicos)
        [InlineData(10, 10)]     // reducido
        [InlineData(21, 21)]     // general
        public void Calcular_DistintosIVA_SobreBase100_IvaEsElPorcentajeExacto(
            decimal porcentajeIva, decimal ivaEsperado)
        {
            // Con base 100 € el importe del IVA coincide numéricamente con el porcentaje
            var resultado = CalculadoraLineas.Calcular(
                cantidad: 1m,
                precioUnitario: 100m,
                porcentajeDescuento: 0m,
                porcentajeIva: porcentajeIva,
                porcentajeRecargo: 0m);

            resultado.ImporteIva.Should().Be(ivaEsperado);
            resultado.Importe.Should().Be(100m + ivaEsperado);
        }

        [Fact]
        public void Calcular_PrecioConDecimales_RedondeoA2Decimales()
        {
            // 3 unidades × 1,999 € = 5,997 € → redondeado a 6,00 €
            var resultado = CalculadoraLineas.Calcular(
                cantidad: 3m,
                precioUnitario: 1.999m,
                porcentajeDescuento: 0m,
                porcentajeIva: 21m,
                porcentajeRecargo: 0m);

            resultado.Subtotal.Should().Be(6.00m);

            // Ningún campo debe tener más de 2 decimales
            resultado.Importe.Should().Be(Math.Round(resultado.Importe, 2),
                because: "los importes siempre se almacenan con 2 decimales");
        }

        [Fact]
        public void Calcular_DescuentoTotal100Porciento_BaseImponibleCero()
        {
            // Caso extremo: descuento del 100% → base 0 → IVA 0 → total 0
            var resultado = CalculadoraLineas.Calcular(
                cantidad: 1m,
                precioUnitario: 500m,
                porcentajeDescuento: 100m,
                porcentajeIva: 21m,
                porcentajeRecargo: 0m);

            resultado.ImporteDescuento.Should().Be(500m);
            resultado.BaseImponible.Should().Be(0m);
            resultado.ImporteIva.Should().Be(0m);
            resultado.Importe.Should().Be(0m);
        }
    }
}