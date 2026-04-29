using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.API.DTOs
{

    /// <summary>
    /// Dto para crear factura
    /// </summary>
    public class FacturaCreateDto
    {
        [Required(ErrorMessage = "El cliente es obligatorio")]
        public int ClienteId { get; set; }

        [Required(ErrorMessage = "La serie es obligatoria")]
        public int SerieId { get; set; }

        public int? AlbaranId { get; set; }

        public DateTime? FechaEmision { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Required(ErrorMessage = "Debe incluir al menos una linea")]
        [MinLength(1, ErrorMessage = "Debe incluir al menos una linea")]
        public List<LineaFacturaDto> Lineas { get; set; } = new();

        //VERIFACTU
        [MaxLength(2)]
        public string? TipoFacturaVERIFACTU { get; set; }  //F1,F2,F3 calculada desde backend

        //Solo si es rectificativa
        [MaxLength(50)]
        public string? NumeroFacturaRectificada { get; set; }

        [MaxLength(20)]
        public string? TipoRectificacion { get; set; }

        [Range(0, 100)]
        public decimal? PorcentajeRetencion { get; set; }
    }

    ///<summary>
    ///Dto para actuliar factura existente
    /// </summary>
    public class FacturaUpdateDto
    {
        [Required]
        public int ClienteId { get; set; }

        public DateTime? FechaEmision { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Required]
        public List<LineaFacturaDto> Lineas { get; set; } = new();

        [Range(0, 100)]
        public decimal? PorcentajeRetencion { get; set; }
    }

    ///<summary>
    ///Dto de respuesta de factura
    /// </summary>
    public class FacturaResponseDto
    {
        public int Id { get; set; }
        public int TenantId { get; set; }
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string? ClienteNIF { get; set; }
        public int SerieId { get; set; }
        public string SerieCodigo { get; set; }
        public string Numero { get; set; } = string.Empty;
        public int Ejercicio { get; set; }
        public DateTime FechaEmision { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal TotalIVA { get; set; }
        public decimal TotalRecargo { get; set; }
        public decimal PorcentajeRetencion { get; set; }
        public decimal CuotaRetencion { get; set; }
        public decimal Total { get; set; }
        public string Estado { get; set; } = string.Empty;
        public bool Bloqueada { get; set; }
        public string? Observaciones { get; set; }

        // VERIFACTU
        public string? Huella { get; set; }
        public string? HuellaAnterior { get; set; }
        public bool EnviadaVERIFACTU { get; set; }
        public DateTime? FechaEnvioVERIFACTU { get; set; }
        public string? TipoFacturaVERIFACTU { get; set; }
        public string? UrlVERIFACTU { get; set; }
        public string? QRBase64 { get; set; }  // QR como Base64 para mostrar en frontend

        // Rectificativas
        public string? NumeroFacturaRectificada {  get; set; }
        public string? TipoRectificacion {  get; set; }

        //Relaciones
        public List<int> AlbaranesIds { get; set; }

        //Lineas
        public List<LineaFacturaResponseDto> Lineas { get; set; } = new();

        //Auditoria
        public DateTime FechaCreaccion { get; set; }
        public DateTime? FechaModificacion { get; set; }

    }

    /// <summary>
    /// DTO para línea de factura
    /// </summary>
    public class LineaFacturaDto
    {
        public int? Id { get; set; }

        public int? ProductoId { get; set; }

        [Required(ErrorMessage = "La descripción es obligatoria")]
        [MaxLength(200)]
        public string Descripcion { get; set; } = string.Empty;

        public decimal Cantidad { get; set; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "El precio no puede ser negativo")]
        public decimal PrecioUnitario { get; set; }

        [Range(0, 100)]
        public decimal PorcentajeDescuento { get; set; } = 0;

        [Range(0, 100)]
        public decimal? RecargoEquivalencia { get; set; }
        public int? TipoImpuestoId { get; set; }
    }

    /// <summary>
    /// DTO de respuesta de línea de factura
    /// </summary>
    public class LineaFacturaResponseDto
    {
        public int Id { get; set; }
        public int Orden { get; set; }
        public int? ProductoId { get; set; }
        public string? ProductoCodigo { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public decimal PrecioUnitario { get; set; }
        public decimal PorcentajeDescuento { get; set; }
        public decimal ImporteDescuento { get; set; }
        public decimal BaseImponible { get; set; }
        public decimal RecargoEquivalencia { get; set; } 
        public decimal ImporteIva { get; set; }
        public decimal ImporteRecargo { get; set; }
        public decimal Importe { get; set; }
        public decimal TotalLinea {  get; set; }
        public int? TipoImpuestoId { get;set; }
    }

    /// <summary>
    /// DTO para listado simplificado de facturas
    /// </summary>
    public class FacturaListItemDto
    {
        public int Id { get; set; }
        public string Numero { get; set; } = string.Empty;
        public DateTime FechaEmision { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string ClienteNombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public bool EnviadaVERIFACTU { get; set; }
    }

    /// <summary>
    /// DTO para marcar factura como pagada
    /// </summary>
    public class MarcarComoPagadaDto
    {
        [Required]
        public DateTime FechaPago { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "El importe debe ser mayor a 0")]
        public decimal Importe { get; set; }

        [MaxLength(200)]
        public string? FormaPago { get; set; } // Transferencia, Efectivo, Tarjeta, etc.

        [MaxLength(500)]
        public string? Observaciones { get; set; }
    }


    /// <summary>
    /// DTO para anular factura
    /// </summary>
    public class AnularFacturaDto
    {
        [Required(ErrorMessage = "El motivo es obligatorio")]
        [MaxLength(500)]
        public string Motivo { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO para convertir presupuesto a factura
    /// </summary>
    public class ConvertirPresupuestoAFacturaDto
    {
        [Required(ErrorMessage = "La serie es obligatoria")]
        public int SerieId { get; set; }

        public DateTime? FechaEmision { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

       

        /// <summary>
        /// IDs de líneas del presupuesto a incluir (si null, todas)
        /// </summary>
        public List<int>? LineasSeleccionadas { get; set; }

        /// <summary>
        /// Permite modificar cantidades/precios de líneas específicas
        /// </summary>
        public List<ModificarLineaDto>? LineasModificadas { get; set; }

        [Range(0, 100)]
        public decimal? PorcentajeRetencion { get; set; }
    }

    /// <summary>
    /// DTO para convertir varios presupuestos en factura agrupada
    /// </summary>
    public class ConvertirPresupuestosAFacturaDto
    {
        [Required(ErrorMessage = "Debe seleccionar al menos un presupuesto")]
        [MinLength(1, ErrorMessage = "Debe seleccionar al menos un presupuesto")]
        public List<int> PresupuestosIds { get; set; } = new();

        [Required(ErrorMessage = "La serie es obligatoria")]
        public int SerieId { get; set; }

        public DateTime? FechaEmision { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

        [Required]
        [MaxLength(2)]
        public string TipoFacturaVERIFACTU { get; set; } = "F1";

        [Range(0, 100)]
        public decimal? PorcentajeRetencion { get; set; }
    }

    /// <summary>
    /// DTO para modificar línea durante conversión
    /// </summary>
    public class ModificarLineaDto
    {
        [Required]
        public int LineaId { get; set; } // ID de la línea del presupuesto

        public decimal? Cantidad { get; set; }

        public decimal? PrecioUnitario { get; set; }
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
        public int SerieId { get; set; }

        public DateTime? FechaEmision { get; set; }

        [MaxLength(500)]
        public string? Observaciones { get; set; }

       

        [Range(0, 100)]
        public decimal? PorcentajeRetencion { get; set; }
    }
}