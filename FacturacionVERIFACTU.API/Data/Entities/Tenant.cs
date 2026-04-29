using FacturacionVERIFACTU.API.Data.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FacturacionVERIFACTU.API.Data.Entities
{
    public class Tenant
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        [Column("nombre")]
        public string Nombre { get; set; } = string.Empty;

        // ⚠️ ALIAS AÑADIDO PARA COMPATIBILIDAD CON AUTHCONTROLLER ⚠️
        [NotMapped]
        public string NombreEmpresa
        {
            get => Nombre;
            set => Nombre = value;
        }

        [Required]
        [MaxLength(20)]
        [Column("nif")]
        public string NIF { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        [Column("schema")] // ⬅️ ESTA PROPIEDAD DEBE EXISTIR
        public string Schema { get; set; } = string.Empty;

        [MaxLength(200)]
        [Column("direccion")]
        public string? Direccion { get; set; }

        [MaxLength(100)]
        [Column("ciudad")]
        public string? Ciudad { get; set; }

        [MaxLength(20)]
        [Column("codigo_postal")]
        public string? CodigoPostal { get; set; }

        [MaxLength(50)]
        [Column("provincia")]
        public string? Provincia { get; set; }

        [MaxLength(100)]
        [Column("email")]
        public string? Email { get; set; }

        [MaxLength(20)]
        [Column("telefono")]
        public string? Telefono { get; set; }

        [Column("activo")]
        public bool Activo { get; set; } = true;

        [Column("fecha_alta")]
        public DateTime FechaAlta { get; set; } = DateTime.UtcNow;

        // ⚠️ ALIAS AÑADIDO PARA COMPATIBILIDAD CON AUTHCONTROLLER ⚠️
        [NotMapped]
        public DateTime FechaCreacion
        {
            get => FechaAlta;
            set => FechaAlta = value;
        }

        // ============================================
        // DATOS ADICIONALES PARA PDFs
        // ============================================

        // Logo de la empresa (bytes de la imagen PNG/JPG)
        [Column("logo")]
        public byte[]? Logo { get; set; }

        // DATOS REGISTRO MERCANTIL (obligatorios en facturas)
        [MaxLength(200)]
        [Column("registro_mercantil")]
        public string? RegistroMercantil { get; set; } // Ej: "Registro Mercantil de Madrid"

        [MaxLength(50)]
        [Column("tomo")]
        public string? Tomo { get; set; }

        [MaxLength(50)]
        [Column("libro")]
        public string? Libro { get; set; }

        [MaxLength(50)]
        [Column("folio")]
        public string? Folio { get; set; }

        [MaxLength(50)]
        [Column("seccion")]
        public string? Seccion { get; set; }

        [MaxLength(50)]
        [Column("hoja")]
        public string? Hoja { get; set; }

        [MaxLength(50)]
        [Column("inscripcion")]
        public string? Inscripcion { get; set; }

        // ============================================
        // PLAN / SUSCRIPCIÓN
        // ============================================
        [MaxLength(20)]
        [Column("plan")]
        public string Plan { get; set; } = "Demo"; // Demo | Activo | Suspendido

        [Column("fecha_incicio_plan")]
        public DateTime FechaInicioPlan { get; set; } = DateTime.UtcNow;

        [Column("fecha_fin_plan")]
        public DateTime? FechaFinPlan { get; set; }

        [Column("precio_mensual", TypeName ="decimal(10,2)")]
        public decimal PrecioMensual { get; set; } = 0m;

        [MaxLength(500)]
        [Column("notas_admin")]
        public string? NotasAdmin {  get; set; }

        // Relaciones
        public ICollection<Usuario> Usuarios { get; set; } = new List<Usuario>();
        public ICollection<Cliente> Clientes { get; set; } = new List<Cliente>();
        public ICollection<Producto> Productos { get; set; } = new List<Producto>();
        public ICollection<SerieNumeracion> Series { get; set; } = new List<SerieNumeracion>();
        public ICollection<TipoImpuesto> TipoImpuestos { get; set; } = new List<TipoImpuesto>();
    }
}