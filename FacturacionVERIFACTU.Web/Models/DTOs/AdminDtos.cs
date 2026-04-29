namespace FacturacionVERIFACTU.Web.Models.DTOs
{
    public class AdminDashboardDto
    {
        public int TotalEmpresas { get; set; }
        public int EmpresasDemo { get; set; }
        public int EmpresasActivas { get; set; }
        public int EmpresasSuspendidas { get; set; }
        public int DemosVencenProximamente { get; set; }
        public int FacturasMesActual { get; set; }
        public decimal VolumenMesActual { get; set; }
        public List<TenantResumenDto> UltimasAltas { get; set; } = new();
    }

    public class TenantResumenDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string NIF { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public DateTime FechaAlta { get; set; }
        public DateTime? FechaFinPlan { get; set; }
    }

    public class TenantDetalleDto
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string NIF { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Telefono { get; set; }
        public string? Direccion { get; set; }
        public string Plan { get; set; } = string.Empty;
        public bool Activo { get; set; }
        public DateTime FechaAlta { get; set; }
        public DateTime FechaInicioPlan { get; set; }
        public DateTime? FechaFinPlan { get; set; }
        public decimal PrecioMensual { get; set; }
        public string? NotasAdmin { get; set; }
        public int TotalFacturas { get; set; }
        public decimal VolumenFacturado { get; set; }
        public int UsuariosActivos { get; set; }
        public DateTime? UltimoAcceso { get; set; }
        public List<ReciboResumenDto> Recibos { get; set; } = new();
        public List<ActividadMensualDto> ActividadMensual { get; set; } = new();
    }

    public class ReciboResumenDto
    {
        public int Id { get; set; }
        public int NumeroRecibo { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public decimal ImporteTotal { get; set; }
        public DateTime FechaEmision { get; set; }
    }

    public class ActividadMensualDto
    {
        public int Año { get; set; }
        public int Mes { get; set; }
        public int Facturas { get; set; }
        public decimal Volumen { get; set; }
    }
}
