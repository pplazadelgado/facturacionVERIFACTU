using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.Web.Models.DTOs;

/// <summary>
/// DTO de respuesta de albarán (debe coincidir con el backend)
/// </summary>
public class AlbaranResponseDto
{
    public int Id { get; set; }
    public int TenantId { get; set; }
    public int ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public int SerieId { get; set; }  // ⭐ CORREGIDO: int, no string
    public string? SerieCodigo { get; set; }
    public int? PresupuestoId { get; set; }
    public string? PresupuestoNumero { get; set; }
    public string Numero { get; set; } = string.Empty;  // ⭐ CORREGIDO: string, no int (formato: "2025-0001")
    public int Ejercicio { get; set; }
    public DateTime FechaEmision { get; set; }
    public DateTime? FechaEntrega { get; set; }
    public decimal BaseImponible { get; set; }
    public decimal TotalIVA { get; set; }
    public decimal TotalRecargo { get; set; }
    public decimal Total { get; set; }
    public string Estado { get; set; } = "Pendiente";
    public string? DireccionEntrega { get; set; }
    public string? Observaciones { get; set; }
    public bool Facturado { get; set; }
    public int? FacturaId { get; set; }
    public List<LineaAlbaranResponseDto> Lineas { get; set; } = new();
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
}

/// <summary>
/// DTO para crear albarán
/// </summary>
public class AlbaranCreateDto
{
    [Required]
    public int ClienteId { get; set; }

    [Required]
    public int SerieId { get; set; }  // ⭐ CORREGIDO: int, no string

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

    public decimal? RecargoEquivalencia { get; set; }

    public int? ProductoId { get; set; }

    public int? TipoImpuestoId { get; set; }
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
    public decimal ImporteIva { get; set; }  // ⭐ CORREGIDO: ImporteIva, no ImporteIVA
    public decimal RecargoEquivalencia { get; set; }
    public decimal ImporteRecargo { get; set; }
    public decimal Importe { get; set; }  // ⭐ AÑADIDO
    public decimal TotalLinea { get; set; }  // ⭐ AÑADIDO
    public int? ProductoId { get; set; }
    public string? ProductoCodigo { get; set; }
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
public class ConvertirPresupuestoAAlbaranDto
{
    [Required]
    public int SerieId { get; set; }  // ⭐ CORREGIDO: int, no string

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

/// <summary>
/// DTO para convertir albaranes a factura
/// </summary>
public class ConvertirAlbaranesAFacturaDto
{
    [Required(ErrorMessage = "Debe seleccionar al menos un albarán")]
    [MinLength(1, ErrorMessage = "Debe seleccionar al menos un albarán")]
    public List<int> AlbaranesIds { get; set; } = new();

    [Required(ErrorMessage = "La serie es obligatoria")]
    public int SerieId { get; set; }  // ⭐ CORREGIDO: int, no string

    public DateTime? FechaEmision { get; set; }

    [MaxLength(500)]
    public string? Observaciones { get; set; }

    [Range(0, 100)]
    public decimal? PorcentajeRetencion { get; set; }
}
