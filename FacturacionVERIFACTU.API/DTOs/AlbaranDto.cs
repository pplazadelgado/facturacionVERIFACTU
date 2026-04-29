using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography.Pkcs;

namespace FacturacionVERIFACTU.API.DTOs
{
    /// <summary>
    /// DTO para crear albarán
    /// </summary>
    public class AlbaranCreateDto
    {
        [Required]
        public int ClienteId { get; set; }

        [Required]
        public int SerieId { get; set; }

        public int? PresupuestoId { get; set; }

        public DateTime? FechaEmision { get; set; }

        public DateTime? FechaEntrega { get; set; }

        [MaxLength(200)]
        public string? DireccionEntrega { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Required]
        public List<LineaAlbaranDto> Lineas { get; set; } = new();
    }

    /// <summary>
    /// DTO para actualizar albarán existente
    /// </summary>
    public class AlbaranUpdateDto
    {
        [Required]
        public int ClienteId { get; set; }

        public DateTime? FechaEmision { get; set; }

        public DateTime? FechaEntrega { get; set; }

        [MaxLength(200)]
        public string? DireccionEntrega { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Required]
        public List<LineaAlbaranDto> Lineas { get; set; } = new();
    }

    /// <summary>
    /// DTO de respuesta de albarán
    /// </summary>
    public class AlbaranResponseDto
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int ClienteId { get; set; }
        public string? ClienteNombre { get; set; }
        public int SerieId { get; set; }
        public string? SerieCodigo { get; set; }
        public int? PresupuestoId { get; set; }
        public string? PresupuestoNumero { get; set; }
        public string Numero { get; set; } = string.Empty;
        public int Ejercicio { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaEntrega { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal TotalIVA { get; set; }
        public decimal Total { get; set; }
        public string Estado { get; set; } = "Pendiente";
        public string? DireccionEntrega { get; set; }
        public string? Observaciones { get; set; }
        public bool Facturado { get; set; }
        public int? FacturaId { get; set; }

        public decimal TotalRecargo { get; set; } // ⭐ NUEVO

        public List<LineaAlbaranResponseDto> Lineas { get; set; } = new();
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// DTO para línea de albarán
    /// </summary>
    public class LineaAlbaranDto
    {
        public int? Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Descripcion { get; set; } = string.Empty;

        public decimal Cantidad { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal PrecioUnitario { get; set; }

        [Range(0, 100)]
        public decimal PorcentajeDescuento { get; set; } = 0;

        public decimal? RecargoEquivalencia { get; set; } // ⭐ NUEVO (opcional)

        public int? ProductoId { get; set; }

        public int? TipoImpuestoId {  get; set; }
    }

    /// <summary>
    /// DTO de respuesta de línea de albarán
    /// </summary>
    public class LineaAlbaranResponseDto
    {
        public int Id { get; set; }
        public int Orden { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal PorcentajeDescuento { get; set; }
        public decimal ImporteDescuento { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal IVA { get; set; }
        public decimal ImporteIva { get; set; }
        public decimal ImporteRecargo {  get; set; }
        public decimal Importe { get; set; }
        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public decimal RecargoEquivalencia { get; set; }
        public decimal TotalLinea {  get; set; }
        public int? TipoImpuestoId { get; set; }

    }

    /// <summary>
    /// DTO para cambio de estado
    /// </summary>
    public class CambiarEstadoAlbaranDto
    {
        [Required]
        [MaxLength(20)]
        public string NuevoEstado { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Motivo { get; set; }
    }

    /// <summary>
    /// DTO para convertir presupuesto a albarán
    /// </summary>
    public class ConvertirPresupuestoDto
    {
        [Required]
        public int SerieId {  get; set; }
        public DateTime? FechaEmision { get; set; }

        public DateTime? FechaEntrega { get; set; }

        [MaxLength(200)]
        public string? DireccionEntrega { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        /// <summary>
        /// IDs de líneas del presupuesto a incluir (si null, todas)
        /// </summary>
        public List<int>? LineasSeleccionadas { get; set; }
    }
}