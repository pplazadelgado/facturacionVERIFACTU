using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.API.DTOs
{
    /// <summary>
    /// DTO para crear/actualizar presupuesto
    /// </summary>
    public class PresupuestoCreateDto
    {
        [Required]
        public int ClienteId { get; set; }

        [Required]
        public int SerieId { get; set; }

        public DateTime? Fecha { get; set; }

        public DateTime? FechaValidez { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public decimal? PorcentajeRetencion { get; set; } // ⭐ NUEVO (opcional)

        [Required]
        public List<LineaPresupuestoDto> Lineas { get; set; } = new();
    }

    /// <summary>
    /// DTO para actualizar presupuesto existente
    /// </summary>
    public class PresupuestoUpdateDto
    {
        [Required]
        public int ClienteId { get; set; }

        public DateTime? FechaEmision { get; set; }

        public DateTime? FechaValidez { get; set; }

        [StringLength(500)]
        public string? Observaciones { get; set; }

        public decimal? PorcentajeRetencion { get; set; }

        [Required]
        public List<LineaPresupuestoDto> Lineas { get; set; } = new();
    }

    /// <summary>
    /// DTO de respuesta de presupuesto
    /// </summary>
    public class PresupuestoResponseDto
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public string NumeroPresupuesto { get; set; } = string.Empty;
        public int SerieId { get; set; } 
        public int Numero { get; set; }
        public int Ejercicio { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime? FechaValidez { get; set; }
        public int ClienteId { get; set; }
        public string? ClienteNombre { get; set; }
        public string Estado { get; set; } = "Borrador";
        public decimal BaseImponible { get; set; }
        public decimal TotalIVA { get; set; }
        public decimal Total { get; set; }
        public string? Observaciones { get; set; }

        public decimal TotalRecargo { get; set; } // ⭐ NUEVO
        public decimal PorcentajeRetencion { get; set; } // ⭐ NUEVO
        public decimal CuotaRetencion { get; set; } // ⭐ NUEVO
        public decimal TotalConRetencion { get; set; } // ⭐ NUEVO (calculado)
        public List<LineaPresupuestoResponseDto> Lineas { get; set; } = new();
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// DTO para línea de presupuesto
    /// </summary>
    public class LineaPresupuestoDto
    {
        public int? Id { get; set; }

        [Required]
        [StringLength(500)]
        public string Descripcion { get; set; } = string.Empty;

        public decimal Cantidad { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal PrecioUnitario { get; set; }

        [Range(0, 100)]
        public decimal PorcentajeDescuento { get; set; } = 0;

        public decimal? RecargoEquivalencia { get; set; } // ⭐ NUEVO (opcional)

        public int? ArticuloId { get; set; }
        public int? TipoImpuestoId { get; set; }
    }

    /// <summary>
    /// DTO de respuesta de línea de presupuesto
    /// </summary>
    public class LineaPresupuestoResponseDto
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
        public decimal ImporteIVA { get; set; }
        public decimal RecargoEquivalencia { get; set; }
        public decimal ImporteRecargo { get; set; }
        public decimal Total { get; set; }
        public int? ArticuloId { get; set; }
        public string? ArticuloCodigo { get; set; }
        public int? TipoImpuestoId { get; set; }
    }

    /// <summary>
    /// DTO para cambio de estado
    /// </summary>
    public class CambiarEstadoPresupuestoDto
    {
        [Required]
        [StringLength(20)]
        public string NuevoEstado { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Motivo { get; set; }
    }
}
