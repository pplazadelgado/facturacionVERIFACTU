using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            ApplicationDbContext context,
            ITenantContext tenantContext,
            ILogger<DashboardController> logger)
        {
            _context = context;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        [HttpGet("resumen")]
        public async Task<ActionResult<ResumenDashboardDto>> GetResumen()
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var mesActual = DateTime.UtcNow.Month;
                var yearActual = DateTime.UtcNow.Year;
                var inicioMes = new DateTime(yearActual, mesActual, 1, 0, 0, 0, DateTimeKind.Utc);
                var finMes = inicioMes.AddMonths(1);

                var facturacionMesActual = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value
                             && f.FechaEmision >= inicioMes
                             && f.FechaEmision < finMes
                             && f.Estado == "Emitida")
                    .SumAsync(f => (decimal?)f.Total) ?? 0;

                var facturasPendientesPago = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value && f.Estado == "Emitida")
                    .CountAsync();

                var presupuestosPendientes = await _context.Presupuestos
                    .Where(p => p.TenantId == tenantId.Value
                             && (p.Estado == "Pendiente" || p.Estado == "Enviado" || p.Estado == "Borrador"))
                    .CountAsync();

                var seisMesesAtras = DateTime.UtcNow.AddMonths(-6);
                var clientesActivos = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value
                             && f.FechaEmision >= seisMesesAtras
                             && f.Estado != "Anulada")
                    .Select(f => f.ClienteId)
                    .Distinct()
                    .CountAsync();

                return Ok(new ResumenDashboardDto
                {
                    FacturacionMesActual = facturacionMesActual,
                    FacturasPendientesPago = facturasPendientesPago,
                    PresupuestosPendientes = presupuestosPendientes,
                    ClientesActivos = clientesActivos
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener resumen del dashboard");
                return StatusCode(500, "Error al obtener el resumen");
            }
        }

        [HttpGet("facturacion-mensual")]
        public async Task<ActionResult<List<FacturacionMensualDto>>> GetFacturacionMensual(
            [FromQuery] int? year = null)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var yearConsulta = year ?? DateTime.UtcNow.Year;
                var inicioYear = new DateTime(yearConsulta, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var finYear = inicioYear.AddYears(1);

                var facturas = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value
                             && f.Estado != "Anulada"
                             && f.FechaEmision >= inicioYear
                             && f.FechaEmision < finYear)
                    .Select(f => new { f.FechaEmision, f.Total })
                    .ToListAsync();

                var meses = Enumerable.Range(1, 12).Select(mes => new FacturacionMensualDto
                {
                    NumeroMes = mes,
                    Mes = new DateTime(yearConsulta, mes, 1).ToString("MMMM"),
                    Total = facturas
                        .Where(f => f.FechaEmision.Month == mes)
                        .Sum(f => f.Total),
                    CantidadFacturas = facturas
                        .Count(f => f.FechaEmision.Month == mes)
                }).ToList();

                return Ok(meses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener facturación mensual");
                return StatusCode(500, "Error al obtener facturación mensual");
            }
        }

        [HttpGet("clientes-top")]
        public async Task<ActionResult<List<ClienteTopDto>>> GetClientesTop(
            [FromQuery] int limit = 5,
            [FromQuery] int? year = null)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var query = _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value && f.Estado != "Anulada");

                if (year.HasValue)
                {
                    var inicioYear = new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var finYear = inicioYear.AddYears(1);
                    query = query.Where(f => f.FechaEmision >= inicioYear && f.FechaEmision < finYear);
                }

                var clientesTop = await query
                    .GroupBy(f => new { f.ClienteId, f.Cliente.Nombre, f.Cliente.NIF })
                    .Select(g => new ClienteTopDto
                    {
                        ClienteId = g.Key.ClienteId,
                        RazonSocial = g.Key.Nombre,
                        NIF = g.Key.NIF,
                        TotalFacturado = g.Sum(f => f.Total),
                        CantidadFacturas = g.Count(),
                        UltimaFactura = g.Max(f => f.FechaEmision)
                    })
                    .OrderByDescending(c => c.TotalFacturado)
                    .Take(limit)
                    .ToListAsync();

                return Ok(clientesTop);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener clientes top");
                return StatusCode(500, "Error al obtener clientes top");
            }
        }

        [HttpGet("productos-mas-vendidos")]
        public async Task<ActionResult<List<ProductoMasVendidoDto>>> GetProductosMasVendidos(
            [FromQuery] int limit = 10,
            [FromQuery] int? year = null)
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var query = _context.LineasFacturas
                    .Where(lf => lf.Factura.TenantId == tenantId.Value
                              && lf.Factura.Estado != "Anulada"
                              && lf.ProductoId != null);

                if (year.HasValue)
                {
                    var inicioYear = new DateTime(year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    var finYear = inicioYear.AddYears(1);
                    query = query.Where(lf => lf.Factura.FechaEmision >= inicioYear
                                           && lf.Factura.FechaEmision < finYear);
                }

                // ← Calcular importe en SQL directamente, sin usar TotalLinea [NotMapped]
                var productosMasVendidos = await query
                    .GroupBy(lf => new { lf.ProductoId, lf.Producto!.Descripcion })
                    .Select(g => new ProductoMasVendidoDto
                    {
                        ProductoId = g.Key.ProductoId!.Value,
                        Nombre = g.Key.Descripcion,
                        CantidadVendida = g.Sum(lf => lf.Cantidad),
                        TotalFacturado = g.Sum(lf => lf.BaseImponible + lf.ImporteIva + lf.ImporteRecargo),
                        PrecioMedio = g.Average(lf => lf.PrecioUnitario)
                    })
                    .OrderByDescending(p => p.CantidadVendida)
                    .Take(limit)
                    .ToListAsync();

                return Ok(productosMasVendidos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener productos más vendidos");
                return StatusCode(500, "Error al obtener productos más vendidos");
            }
        }

        [HttpGet("facturacion-comparativa")]
        public async Task<ActionResult<FacturacionComparativaDto>> GetFacturacionComparativa()
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var yearActual = DateTime.UtcNow.Year;
                var yearAnterior = yearActual - 1;

                var facturas = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value
                             && f.Estado != "Anulada"
                             && f.FechaEmision.Year >= yearAnterior
                             && f.FechaEmision.Year <= yearActual)
                    .Select(f => new { f.FechaEmision, f.Total })
                    .ToListAsync();

                var datosActual = Enumerable.Range(1, 12)
                    .Select(m => facturas
                        .Where(f => f.FechaEmision.Year == yearActual && f.FechaEmision.Month == m)
                        .Sum(f => f.Total))
                    .ToList();

                var datosAnterior = Enumerable.Range(1, 12)
                    .Select(m => facturas
                        .Where(f => f.FechaEmision.Year == yearAnterior && f.FechaEmision.Month == m)
                        .Sum(f => f.Total))
                    .ToList();

                return Ok(new FacturacionComparativaDto
                {
                    YearActual = yearActual,
                    YearAnterior = yearAnterior,
                    DatosActual = datosActual,
                    DatosAnterior = datosAnterior
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener facturación comparativa");
                return StatusCode(500, "Error al obtener facturación comparativa");
            }
        }

        [HttpGet("estadisticas-cobros")]
        public async Task<ActionResult<EstadisticasCobrosDto>> GetEstadisticasCobros()
        {
            var tenantId = _tenantContext.GetTenantId();
            if (tenantId == null) return Unauthorized();

            try
            {
                var facturas = await _context.Facturas
                    .Where(f => f.TenantId == tenantId.Value && f.Estado != "Anulada")
                    .Select(f => new { f.Total, f.Estado, f.FechaEmision })
                    .ToListAsync();

                var hoy = DateTime.UtcNow;

                return Ok(new EstadisticasCobrosDto
                {
                    TotalFacturado = facturas.Sum(f => f.Total),
                    TotalCobrado = facturas.Where(f => f.Estado == "Pagada").Sum(f => f.Total),
                    TotalPendiente = facturas.Where(f => f.Estado == "Emitida").Sum(f => f.Total),
                    TotalVencido = facturas.Where(f => f.Estado == "Emitida"
                                                    && f.FechaEmision.AddDays(30) < hoy).Sum(f => f.Total),
                    CantidadPagadas = facturas.Count(f => f.Estado == "Pagada"),
                    CantidadPendientes = facturas.Count(f => f.Estado == "Emitida"),
                    CantidadVencidas = facturas.Count(f => f.Estado == "Emitida"
                                                        && f.FechaEmision.AddDays(30) < hoy)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas de cobros");
                return StatusCode(500, "Error al obtener estadísticas de cobros");
            }
        }
    }
}