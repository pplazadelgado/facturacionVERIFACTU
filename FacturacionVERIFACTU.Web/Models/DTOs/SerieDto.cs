using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.Web.Models.DTOs
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
        public string Codigo { get;set; }
        public string Descripcion { get; set; }
        public string TipoDocumento { get; set; }
        public int Ejercicio { get; set; }
        public string Formato { get; set; } = "{SERIE}-{NUMERO}/{EJERCICIO}";
        public bool Activo { get; set; }
    }

    public class SerieUpdateDto
    {
        public string? Codigo { get; set; }
        public string Descripcion { get; set; }
        public string Formato { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public bool Bloqueada { get; set; }
        public int? ProximoNumero { get; set; }
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
