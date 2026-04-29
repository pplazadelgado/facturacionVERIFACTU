namespace FacturacionVERIFACTU.API.Domain
{
    /// <summary>
    /// Lógica pura de cálculo de importes de una línea de documento.
    /// Al no depender de base de datos ni servicios externos,
    /// es directamente testeable sin ninguna infraestructura.
    /// </summary>
    public static class CalculadoraLineas
    {
        public static ResultadoLinea Calcular(
            decimal cantidad,
            decimal precioUnitario,
            decimal porcentajeDescuento,
            decimal porcentajeIva,
            decimal porcentajeRecargo)
        {
            var subtotal = Math.Round(cantidad * precioUnitario, 2);
            var importeDescuento = Math.Round(subtotal * (porcentajeDescuento / 100), 2);
            var baseImponible = Math.Round(subtotal - importeDescuento, 2);
            var importeIva = Math.Round(baseImponible * (porcentajeIva / 100), 2);
            var importeRecargo = Math.Round(baseImponible * (porcentajeRecargo / 100), 2);
            var importe = Math.Round(baseImponible + importeIva + importeRecargo, 2);

            return new ResultadoLinea(
                subtotal,
                importeDescuento,
                baseImponible,
                importeIva,
                importeRecargo,
                importe);
        }
    }

    /// <summary>
    /// Record = clase inmutable pensada para transportar datos.
    /// C# genera automáticamente Equals, GetHashCode y ToString.
    /// </summary>
    public record ResultadoLinea(
        decimal Subtotal,
        decimal ImporteDescuento,
        decimal BaseImponible,
        decimal ImporteIva,
        decimal ImporteRecargo,
        decimal Importe);
}
