using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FacturacionVERIFACTU.API.Data.Entities
{
    [Table("recibos_servicio")]
    public class ReciboServicio
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("tenant_id")]
        public int TenantId {  get; set; }

        [Required]
        [Column("numero_recibo")]
        public int NumeroRecibo {  get; set; }//Correlativo global

        [Required]
        [MaxLength(200)]
        public string Concepto { get; set; } = string.Empty;

        [Column("periodo_dsde")]
        public DateTime PeriodoDesde { get; set; }

        [Column("periodo_hasta")]
        public DateTime PeriodoHasta { get; set; }

        [Column("importe_base", TypeName ="decimal(10,2)")]
        public decimal ImporteBase { get; set; }

        [Column("porcentaje_iva", TypeName = "decimal(5,2)")]
        public decimal PorcentajeIva { get; set; } = 21m;

        [Column("importe_iva", TypeName = "decimal(10,2)")]
        public decimal ImporteIva { get; set; }

        [Column("importe_total", TypeName = "decimal(10,2)")]
        public decimal ImporteTotal { get; set; }

        [Column("fecha_emision")]
        public DateTime FechaEmision { get; set; } = DateTime.UtcNow;

        [Column("creado_por_id")]
        public int CreadoPorId {  get; set; }  //ID del superadmin

        //Relacciones
        [ForeignKey("TenantId")]
        public Tenant Tenant { get; set; } = null;
    }
}
