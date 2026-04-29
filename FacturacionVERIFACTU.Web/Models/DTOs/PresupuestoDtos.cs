using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.Web.Models.DTOs;

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
    public decimal TotalRecargo { get; set; }
    public decimal PorcentajeRetencion { get; set; }
    public decimal CuotaRetencion { get; set; }
    public decimal TotalConRetencion { get; set; }
    public List<LineaPresupuestoResponseDto> Lineas { get; set; } = new();
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }
}

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

    public decimal? PorcentajeRetencion { get; set; }

    [Required]
    public List<LineaPresupuestoDto> Lineas { get; set; } = new();
}

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

public class LineaPresupuestoDto
{
    public int? Id { get; set; }

    public int? TipoImpuestoId { get; set; }

    [Required]
    [StringLength(500)]
    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal PrecioUnitario { get; set; }

    [Range(0, 100)]
    public decimal PorcentajeDescuento { get; set; }

    [Range(0, 100)]
    public decimal? IVA { get; set; }

    public decimal? RecargoEquivalencia { get; set; }

    public int? ArticuloId { get; set; }

    public string? ArticuloCodigo { get; set; }
}


public class LineaPresupuestoResponseDto
{
    public int Id { get; set; }
    public int Orden { get; set; }
    public int? TipoImpuestoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal PorcentajeDescuento { get; set; }
    public decimal ImporteDescuento { get; set; }
    public decimal BaseImponible { get; set; }
    public decimal IVA { get; set; }
    public decimal ImporteIVA { get; set; }
    public decimal RecargoEquivalencia { get; set; }
    public decimal ImporteRecargo {  get; set; }
    public decimal Total { get; set; }
    public int? ArticuloId { get; set; }
    public string? ArticuloCodigo { get; set; }
}

public class CambiarEstadoPresupuestoDto
{
    [Required]
    [StringLength(20)]
    public string NuevoEstado { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Motivo { get; set; }
}

public class ConvertirPresupuestoAFacturaDto
{
    [Required]
    public int SerieId { get; set; }

    public DateTime? FechaEmision { get; set; }

    [MaxLength(500)]
    public string? Observaciones { get; set; }


    public List<int>? LineasSeleccionadas { get; set; }

    public List<ModificarLineaDto>? LineasModificadas { get; set; }

    [Range(0, 100)]
    public decimal? PorcentajeRetencion { get; set; }
}

public class ModificarLineaDto
{
    [Required]
    public int LineaId { get; set; }

    public decimal? Cantidad { get; set; }

    public decimal? PrecioUnitario { get; set; }
}



public class ConvertirPresupuestosAFacturaDto
{
    [Required]
    [MinLength(1)]
    public List<int> PresupuestosIds { get; set; } = new();
    [Required]
    public int SerieId {  get; set; }
    public DateTime? FechaEmision { get; set; }
    [MaxLength(500)]
    public string? Observaciones { get; set; }

    [Range(0,100)]
    public decimal? PorcentajeRetencion { get; set; }
}

