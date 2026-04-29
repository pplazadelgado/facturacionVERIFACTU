using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OfficeOpenXml.Utils;
using System.Collections.Immutable;

namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize(Roles ="SuperAdmin")]
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IJwtService _jwtService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            ApplicationDbContext context,
            IJwtService service,
            IConfiguration configuration,
            ILogger<AdminController> logger)
        {
            _context = context;
            _jwtService = service;
            _configuration = configuration;
            _logger = logger;
        }

        // ============================================================
        // DASHBOARD GLOBAL
        // ============================================================

        ///<summary>
        /// Resumen global del sistema para el dashboard superadmin
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<ActionResult<AdminDashboardDto>> GetDashBoard()
        {
            try
            {
                var hoy = DateTime.UtcNow;
                var inicioMes = new DateTime(hoy.Year, hoy.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                var hace7Dias = hoy.AddDays(-7);

                //Excluir tenant SuperAdmin interno
                var tenants = await _context.Tenants
                    .Where(t => t.NIF != "SUPERADMIN")
                    .ToListAsync();

                var totalEmpresas = tenants.Count;
                var empresasDemo = tenants.Count(t => t.Plan == "Demo");
                var empresasActivas = tenants.Count(t => t.Plan == "Activo");
                var empresasSuspendidas = tenants.Count(t => t.Plan == "Suspendido");

                //Demos proximas a vencer (7 dias)
                var demosVenceProximamente = tenants.Count(t =>
                    t.Plan == "Demo" &&
                    t.FechaFinPlan.HasValue &&
                    t.FechaFinPlan.Value <= hoy.AddDays(-7) &&
                    t.FechaFinPlan.Value >= hoy);

                //Facturas del mes en todo el sistema
                var facturasEsteMes = await _context.Facturas
                    .Where(f => f.FechaEmision >= inicioMes && f.Estado != "Anulada")
                    .GroupBy(f => 1)
                    .Select(g => new
                    {
                        Cantidad = g.Count(),
                        Volumen = g.Sum(f => f.Total)
                    })
                    .FirstOrDefaultAsync();

                //Ultimas altas (5 mas recientes)
                var ultimasAltas = await _context.Tenants
                    .Where(t => t.NIF != "SUPERADMIN")
                    .OrderByDescending(t => t.FechaAlta)
                    .Take(5)
                    .Select(t => new TenantResumenDto
                    {
                        Id = t.Id,
                        Nombre = t.Nombre,
                        NIF = t.NIF,
                        Plan = t.Plan,
                        FechaAlta = t.FechaAlta,
                        FechaFinPlan = t.FechaFinPlan
                    })
                    .ToListAsync();

                return Ok(new AdminDashboardDto
                {
                    TotalEmpresas = totalEmpresas,
                    EmpresasDemo = empresasDemo,
                    EmpresasActivas = empresasActivas,
                    EmpresasSuspendidas = empresasSuspendidas,
                    DemosVencenProximamente = demosVenceProximamente,
                    FacturasMesActual = facturasEsteMes?.Cantidad ?? 0,
                    VolumenMesActual = facturasEsteMes?.Volumen ?? 0m,
                    UltimasAltas = ultimasAltas
                });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al obtener dashboard admin");
                return StatusCode(500, new { mensaje = "Error al obtener el dashboard" });
            }
        }

        // ============================================================
        // LISTADO DE EMPRESAS
        // ============================================================

        ///<summary>
        /// Listado paginado de todas la empresas metricas
        /// </summary>
        [HttpGet("empresas")]
        public async Task<ActionResult<PaginatedResponseDto<TenantDetalleDto>>> GetEmpresas(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? plan = null,
            [FromQuery] string? search = null)
        {
            try
            {
                var query = _context.Tenants
                    .Where(t => t.NIF != "SUPERADMIN")
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(plan))
                    query = query.Where(t => t.Plan == plan);

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var s = search.ToLower();
                    query = query.Where(t =>
                        t.Nombre.ToLower().Contains(s) ||
                        t.NIF.ToLower().Contains(s));
                }

                var total = await query.CountAsync();

                var tenants = await query
                    .OrderByDescending(t => t.FechaAlta)
                    .Skip((page - 1) + pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var tenantIds = tenants.Select(t => t.Id).ToList();

                //Metricas por tenant en una sola consulta
                var facturasPorTenant = await _context.Facturas
                    .Where(f => tenantIds.Contains(f.TenantId) && f.Estado != "Anulada")
                    .GroupBy(f => f.TenantId)
                    .Select(g => new
                    {
                        TenantId = g.Key,
                        Cantidad = g.Count(),
                        Volumen = g.Sum(f => f.Total)
                    })
                    .ToListAsync();

                var usuariosPorTenant = await _context.Usuarios
                    .Where(u => tenantIds.Contains(u.TenantId) && u.Activo && u.Rol != "SuperAdmin")
                    .GroupBy(u => u.TenantId)
                    .Select(g => new { TenantId = g.Key, Cantidad = g.Count() })
                    .ToListAsync();

                // Último acceso por tenant (usuario más reciente)
                var ultimosAccesos = await _context.Usuarios
                    .Where(u => tenantIds.Contains(u.TenantId) &&
                                u.UltimoAcceso.HasValue &&
                                u.Rol != "SuperAdmin")
                    .GroupBy(u => u.TenantId)
                    .Select(g => new
                    {
                        TenantId = g.Key,
                        UltimoAcceso = g.Max(u => u.UltimoAcceso)
                    })
                    .ToListAsync();

                var items = tenants.Select(t => new TenantDetalleDto
                {
                    Id = t.Id,
                    Nombre = t.Nombre,
                    NIF = t.NIF,
                    Email = t.Email,
                    Telefono = t.Telefono,
                    Plan = t.Plan,
                    FechaAlta = t.FechaAlta,
                    FechaInicioPlan = t.FechaInicioPlan,
                    FechaFinPlan = t.FechaFinPlan,
                    PrecioMensual = t.PrecioMensual,
                    NotasAdmin = t.NotasAdmin,
                    Activo = t.Activo,
                    TotalFacturas = facturasPorTenant
                        .FirstOrDefault(f => f.TenantId == t.Id)?.Cantidad ?? 0,
                    VolumenFacturado = facturasPorTenant
                        .FirstOrDefault(f => f.TenantId == t.Id)?.Volumen ?? 0m,
                    UsuariosActivos = usuariosPorTenant
                        .FirstOrDefault(u => u.TenantId == t.Id)?.Cantidad ?? 0,
                    UltimoAcceso = ultimosAccesos
                        .FirstOrDefault(u => u.TenantId == t.Id)?.UltimoAcceso
                }).ToList();

                return Ok(new PaginatedResponseDto<TenantDetalleDto>
                {
                    Items = items,
                    TotalItems = total,
                    Page = page,
                    PageSize = pageSize
                });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener listado de empresas");
                return StatusCode(500, new { mensaje = "Error al obtener empresas" });
            }
        }

        // ============================================================
        // DETALLE DE EMPRESA
        // ============================================================

        /// <summary>
        /// Detalle complet de una empresa con actividad historica
        /// </summary>
        [HttpGet("empresas/{id}")]
        public async Task<ActionResult<TenantDetalleDto>> GetEmpresa(int id)
        {
            try
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Empresa no encontrada" });

                var facturas = await _context.Facturas
                    .Where(f => f.TenantId == id && f.Estado != "Anulada")
                    .ToListAsync();

                var usuarios = await _context.Usuarios
                    .Where(u => u.TenantId == id && u.Rol != "SuperAdmin")
                    .ToListAsync();

                var recibos = await _context.RecibosServicio
                    .Where(r => r.TenantId == id)
                    .OrderByDescending(r => r.FechaEmision)
                    .Select(r => new ReciboResumenDto
                    {
                        Id = r.Id,
                        NumeroRecibo = r.NumeroRecibo,
                        Concepto = r.Concepto,
                        PeriodoDesde = r.PeriodoDesde,
                        PeriodoHasta = r.PeriodoHasta,
                        ImporteTotal = r.ImporteBase + r.ImporteIva,
                        FechaEmision = r.FechaEmision
                    })
                    .ToListAsync();

                //Actividad mensual ultimos 6 meses
                var hace6Meses = DateTime.UtcNow.AddMonths(-6);
                var actividadMensual = facturas
                    .Where(f => f.FechaEmision >= hace6Meses)
                    .GroupBy(f => new { f.FechaEmision.Year, f.FechaEmision.Month })
                    .Select(g => new ActividadMensualDto
                    {
                        Año = g.Key.Year,
                        Mes = g.Key.Month,
                        Facturas = g.Count(),
                        Volumen = g.Sum(f => f.Total)
                    })
                    .OrderBy(a => a.Año).ThenBy(a => a.Mes)
                    .ToList();

                var dto = new TenantDetalleDto
                {
                    Id = tenant.Id,
                    Nombre = tenant.Nombre,
                    NIF = tenant.NIF,
                    Email = tenant.Email,
                    Telefono = tenant.Telefono,
                    Direccion = tenant.Direccion,
                    Plan = tenant.Plan,
                    FechaAlta = tenant.FechaAlta,
                    FechaInicioPlan = tenant.FechaInicioPlan,
                    FechaFinPlan = tenant.FechaFinPlan,
                    PrecioMensual = tenant.PrecioMensual,
                    NotasAdmin = tenant.NotasAdmin,
                    Activo = tenant.Activo,
                    TotalFacturas = facturas.Count,
                    VolumenFacturado = facturas.Sum(f => f.Total),
                    UsuariosActivos = usuarios.Count(u => u.Activo),
                    UltimoAcceso = usuarios
                        .Where(u => u.UltimoAcceso.HasValue)
                        .Max(u => u.UltimoAcceso),
                    Recibos = recibos,
                    ActividadMensual = actividadMensual
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener detalle empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al obtener empresa" });
            }
        }

        // ============================================================
        // GESTIÓN DE PLAN
        // ============================================================

        ///<sumary>
        /// Actualizar plan, precio y fechas de una empresa
        /// </sumary>
        [HttpPatch("empresas/{id}/plan")]
        public async Task<ActionResult> ActualizarPlan(int id, [FromBody] ActualizarPlanDto dto)
        {
            try
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Empresa no encontrada" });

                var planAnterior = tenant.Plan;

                tenant.Plan = dto.Plan;
                tenant.FechaInicioPlan = dto.FechaInicioPlan ?? tenant.FechaInicioPlan;
                tenant.FechaFinPlan = dto.FechaFinPlan;
                tenant.PrecioMensual = dto.PrecioMensual ?? tenant.PrecioMensual;

                if (!string.IsNullOrWhiteSpace(dto.NotasAdmin))
                    tenant.NotasAdmin = dto.NotasAdmin;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Plan de empresa {Id} actualizado: {Anterior} → {Nuevo}", id, planAnterior, dto.Plan);

                return Ok(new { mensaje = $"Plan actualizado a {dto.Plan}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar plan empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al actualizar el plan" });
            }
        }

        ///<sumary>
        /// Extender demo con un clic(añade N dias desde hoy)
        /// </sumary>
        [HttpPost("empresas/{id}/extender-demo")]
        public async Task<ActionResult> ExtenderDemo(int id, [FromBody] ExtenderDemoDto dto)
        {
            try
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Empresa no encontrada" });

                var nuevaFecha = (tenant.FechaFinPlan.HasValue && tenant.FechaFinPlan > DateTime.UtcNow)
                    ? tenant.FechaFinPlan.Value.AddDays(dto.Dias)
                    : DateTime.UtcNow.AddDays(dto.Dias);

                tenant.Plan = "Demo";
                tenant.FechaFinPlan = nuevaFecha;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Demo empresa {Id} extendida {Dias} dias. Nueva fecha: {Fecha}", id, dto.Dias, nuevaFecha);

                return Ok(new
                {
                    mensaje = $"Demo extendida {dto.Dias} dias.",
                    nuevaFechaFin = nuevaFecha
                });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al extender demo empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al extender demo" });
            }
        }

        /// <summary>
        /// Activar o suspender una empresa
        /// </summary>
        [HttpPatch("empresas/{id}/estado")]
        public async Task<ActionResult> CambiarEstado(int id, [FromBody] CambiarEstadoEmpresaDto dto)
        {
            try
            {
                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Empresa no econtrada" });

                tenant.Activo = dto.Activo;
                if (!dto.Activo)
                    tenant.Plan = "Suspdendido";

                await _context.SaveChangesAsync();

                _logger.LogInformation("Estado empresa {Id} cambiado a : {Estado}",
                    id, dto.Activo ? "Activo" : "Suspendido");

                return Ok(new { mensaje = dto.Activo ? "Empresa activada" : "Empresa suspendida" });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al cambiar estado" });
            }
        }

        // ============================================================
        // IMPERSONACIÓN
        // ============================================================

        ///<summary>
        /// Genera un token temporal para entrar como empresa
        /// </summary>
        [HttpPost("empresas/{id}/impersonar")]
        public async Task<ActionResult> Impersonar(int id)
        {
            try
            {
                var superAdminId = User.FindFirst("user_id")?.Value;

                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Emrpresa no encontrada" });

                //buscar el primer admin activo del tenant
                var adminTenant = await _context.Usuarios
                    .FirstOrDefaultAsync(u =>
                    u.TenantId == id &&
                    u.Rol == "Admin" &&
                    u.Activo);

                if (adminTenant == null)
                    return BadRequest(new { mensaje = "La empresa no tiene usuarios Admin activos " });

                //Token impersonacion (duracion corta: 2 horas)
                var token = await _jwtService.GenerateImpersonationToken(
                    adminTenant.Id,
                    adminTenant.Email,
                    id,
                    "Admin",
                    int.Parse(superAdminId ?? "0")
                    );

                _logger.LogWarning("SuperAdmin {SuperAdmin} impersonando en empresa {TenantId} {Nombre}",
                    superAdminId, id, tenant.Nombre);

                return Ok(new
                {
                    token,
                    empresa = tenant.Nombre,
                    usuario = adminTenant.Email,
                    expira = DateTime.UtcNow.AddHours(2)
                });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al impersonar empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al generar token de impersonacion" });
            }
        }


        // ============================================================
        // RECIBOS
        // ============================================================

        ///<summary>
        /// Crear un recibo de servicio para empresa
        /// </summary>
        [HttpPost("empresas/{id}/recibos")]
        public async Task<ActionResult<ReciboResumenDto>> CrearRecibo(int id, [FromBody] CrearReciboDto dto)
        {
            try
            {
                var superAdminId = int.Parse(User.FindFirst("user_id")?.Value ?? "0");

                var tenant = await _context.Tenants
                    .FirstOrDefaultAsync(t => t.Id == id && t.NIF != "SUPERADMIN");

                if (tenant == null)
                    return NotFound(new { mensaje = "Empresa no encontrada" });

                // Número correlativo global
                var ultimoNumero = await _context.RecibosServicio
                    .MaxAsync(r => (int?)r.NumeroRecibo) ?? 0;

                var importeIVA = Math.Round(dto.ImporteBase * (dto.PorcentajeIVA / 100), 2);
                var importeTotal = dto.ImporteBase + importeIVA;

                var recibo = new ReciboServicio
                {
                    TenantId = id,
                    NumeroRecibo = ultimoNumero + 1,
                    Concepto = dto.Concepto,
                    PeriodoDesde = dto.PeriodoDesde,
                    PeriodoHasta = dto.PeriodoHasta,
                    ImporteBase = dto.ImporteBase,
                    PorcentajeIva = dto.PorcentajeIVA,       // ← Iva no IVA
                    ImporteIva = importeIVA,                  // ← Iva no IVA
                    ImporteTotal = importeTotal,
                    FechaEmision = DateTime.UtcNow,
                    CreadoPorId = superAdminId
                };

                _context.RecibosServicio.Add(recibo);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Recibo #{Numero} creado para empresa {TenantId}. Total: {Total}€",
                    recibo.NumeroRecibo, id, importeTotal);

                return CreatedAtAction(nameof(GetRecibo),
                    new { id = recibo.Id },
                    new ReciboResumenDto
                    {
                        Id = recibo.Id,
                        NumeroRecibo = recibo.NumeroRecibo,
                        Concepto = recibo.Concepto,
                        PeriodoDesde = recibo.PeriodoDesde,
                        PeriodoHasta = recibo.PeriodoHasta,
                        ImporteTotal = recibo.ImporteBase + importeIVA,
                        FechaEmision = recibo.FechaEmision
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear recibo empresa {Id}", id);
                return StatusCode(500, new { mensaje = "Error al crear recibo" });
            }

        }

        /// <summary>
        /// Obtener un recibo por ID
        /// </summary>
        [HttpGet("recibos/{id}")]
        public async Task<ActionResult<ReciboResumenDto>> GetRecibo(int id)
        {
            var recibo = await _context.RecibosServicio
                .Include(r => r.Tenant)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (recibo == null)
                return NotFound(new { menseja = "Reicbo no encontrado" });

            return Ok(new ReciboResumenDto
            {
                Id = recibo.Id,
                NumeroRecibo = recibo.NumeroRecibo,
                Concepto = recibo.Concepto,
                PeriodoDesde = recibo.PeriodoDesde,
                PeriodoHasta = recibo.PeriodoHasta,
                ImporteTotal = recibo.ImporteIva + recibo.ImporteBase,
                FechaEmision = recibo.FechaEmision
            });
        }

        ///<summary>
        ///Descargar recibo en PDF
        /// </summary>
        [HttpGet("recibos/{id}/pdf")]
        public async Task<ActionResult> DescargarReciboPdf(int id)
        {
            try
            {
                var recibo = await _context.RecibosServicio
                    .Include(r => r.Tenant)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (recibo == null)
                    return NotFound(new { mensaje = "Recibo no encontrado" });

                var pdfService = HttpContext.RequestServices
                    .GetRequiredService<IRecibosPdfService>();

                var bytes = pdfService.GenerarReciboServicio(recibo, _configuration);
                var fileName = $"Recibo-{recibo.NumeroRecibo:D4}-{recibo.Tenant.Nombre}.pdf";

                return File(bytes, "application/pdf", fileName);

            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF recibo {Id}", id);
                return StatusCode(500, new { mensaje = "Error al generar PDF" });
            }
        }
    }
}
