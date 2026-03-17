using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.API.DTOs
{
    public class SerieDto
    {
        public int Id { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string TipoDocumento { get; set; } = string.Empty;
        public int Ejercicio { get; set; }
        public int ProximoNumero { get; set; }
        public bool Activo { get; set; }
        public bool Bloqueada { get; set; }
        public string Formato { get; set; } = string.Empty;

    }

    public class SerieCreateDto
    {
        [Required]
        [StringLength(10)]
        public string Codigo { get; set; } = string.Empty;
        [Required]
        [StringLength(100)]
        public string Descripcion { get; set; } = string.Empty;
        [Required]
        [StringLength(20)]
        public string TipoDocumento { get; set; } = string.Empty;
        [Range(2000, 9999)]
        public int Ejercicio { get; set; }
        [StringLength(50)]
        public string? Formato { get; set; }
        public bool? Activo { get; set; }
    }

    public class SerieUpdateDto
    {
        [StringLength(10)]
        public string? Codigo { get; set; }

        [Required]
        [StringLength(100)]
        public string Descripcion { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Formato { get; set; } = string.Empty;

        public bool Activo { get; set; }
        public bool Bloqueada { get; set; }
    }

    public class ActualizarProximoNumeroDto
    {
        /// <summary>
        /// Número desde el que se generará el siguiente documento.
        /// Debe ser mayor al número actual para evitar duplicados.
        /// </summary>
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "El próximo número debe ser mayor que 0.")]
        public int ProximoNumero { get; set; }
    }

}
