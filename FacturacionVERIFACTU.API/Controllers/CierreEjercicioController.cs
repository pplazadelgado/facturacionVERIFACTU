// API/Controllers/CierreEjercicioController.cs
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class CierreEjercicioController : ControllerBase
    {
        private readonly ICierreEjercicioService _cierreEjercicioService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<CierreEjercicioController> _logger;

        public CierreEjercicioController(
            ICierreEjercicioService cierreEjercicioService,
            ITenantContext tenantContext,
            ILogger<CierreEjercicioController> logger)
        {
            _cierreEjercicioService = cierreEjercicioService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        /// <summary>
        /// Obtiene estadísticas previas al cierre de un ejercicio
        /// </summary>
        [HttpGet("estadisticas/{ejercicio}")]
        [ProducesResponseType(typeof(EstadisticasCierreDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<EstadisticasCierreDTO>> ObtenerEstadisticas(int ejercicio)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                _logger.LogInformation(
                    "Consultando estadísticas del ejercicio {Ejercicio} - Tenant {TenantId}",
                    ejercicio, tenantId);

                var estadisticas = await _cierreEjercicioService.ObtenerEstadisticasEjercicio(
                    ejercicio,
                    tenantId.Value);

                if (!estadisticas.PuedeCerrar)
                {
                    _logger.LogWarning(
                        "Ejercicio {Ejercicio} NO puede cerrarse. Facturas pendientes: {Pendientes}",
                        ejercicio, estadisticas.FacturasPendientes);
                }

                return Ok(estadisticas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas del ejercicio {Ejercicio}", ejercicio);
                return StatusCode(500, new { mensaje = "Error al obtener estadísticas" });
            }
        }

        /// <summary>
        /// Valida si un ejercicio puede cerrarse sin cerrar realmente
        /// </summary>
        [HttpGet("validar/{ejercicio}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> ValidarCierre(int ejercicio)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var estadisticas = await _cierreEjercicioService.ObtenerEstadisticasEjercicio(
                    ejercicio,
                    tenantId.Value);

                var resultado = new
                {
                    ejercicio,
                    puedeCerrar = estadisticas.PuedeCerrar,
                    razon = estadisticas.PuedeCerrar
                        ? "El ejercicio puede cerrarse"
                        : estadisticas.FacturasPendientes > 0
                            ? $"Hay {estadisticas.FacturasPendientes} factura(s) sin enviar a VERIFACTU"
                            : "No hay facturas en el ejercicio",
                    estadisticas
                };

                return Ok(resultado);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al validar cierre del ejercicio {Ejercicio}", ejercicio);
                return StatusCode(500, new { mensaje = "Error al validar cierre" });
            }
        }

        /// <summary>
        /// Cierra el ejercicio fiscal bloqueando facturas y generando informes
        /// </summary>
        [HttpPost("cerrar")]
        [ProducesResponseType(typeof(CierreRealizadoDTO), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CierreRealizadoDTO>> CerrarEjercicio([FromBody] CerrarEjercicioRequest request)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                // Obtener usuario del contexto (si tienes ITenantContext con método GetUserId)
                // Si no, podrías obtenerlo del claim directamente
                var userId = User.FindFirst("UserId")?.Value;
                if (!int.TryParse(userId, out int usuarioId))
                {
                    return Unauthorized(new { mensaje = "Usuario no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                _logger.LogInformation(
                    "Usuario {UsuarioId} iniciando cierre del ejercicio {Ejercicio} - Tenant {TenantId}",
                    usuarioId, request.Ejercicio, tenantId);

                var (exito, mensaje, resultado) = await _cierreEjercicioService.CerrarEjercicio(
                    request.Ejercicio,
                    tenantId.Value,
                    usuarioId);

                if (!exito)
                {
                    _logger.LogWarning(
                        "Fallo al cerrar ejercicio {Ejercicio}: {Mensaje}",
                        request.Ejercicio, mensaje);

                    return BadRequest(new { mensaje });
                }

                _logger.LogInformation(
                    "✅ Ejercicio {Ejercicio} cerrado exitosamente. ID Cierre: {CierreId}",
                    request.Ejercicio, resultado.CierreId);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = resultado.CierreId },
                    resultado);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validación al cerrar ejercicio {Ejercicio}", request.Ejercicio);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cerrar ejercicio {Ejercicio}", request.Ejercicio);
                return StatusCode(500, new { mensaje = "Error al cerrar ejercicio" });
            }
        }

        /// <summary>
        /// Reabre un ejercicio cerrado desbloqueando facturas
        /// OPERACIÓN CRÍTICA - Requiere motivo obligatorio
        /// </summary>
        [HttpPost("reabrir")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> ReabrirEjercicio([FromBody] ReabrirEjercicioRequest request)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var userId = User.FindFirst("UserId")?.Value;
                if (!int.TryParse(userId, out int usuarioId))
                {
                    return Unauthorized(new { mensaje = "Usuario no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                if (string.IsNullOrWhiteSpace(request.Motivo))
                {
                    return BadRequest(new { mensaje = "Debe especificar un motivo para la reapertura" });
                }

                _logger.LogWarning(
                    "⚠️ Usuario {UsuarioId} solicitando reapertura del cierre {CierreId}. Motivo: {Motivo}",
                    usuarioId, request.CierreId, request.Motivo);

                var (exito, mensaje) = await _cierreEjercicioService.ReabrirEjercicio(
                    request.CierreId,
                    request.Motivo,
                    usuarioId);

                if (!exito)
                {
                    return BadRequest(new { mensaje });
                }

                _logger.LogWarning(
                    "✅ Cierre {CierreId} reabierto exitosamente por usuario {UsuarioId}",
                    request.CierreId, usuarioId);

                return Ok(new { mensaje });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al reabrir cierre {CierreId}", request.CierreId);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reabrir cierre {CierreId}", request.CierreId);
                return StatusCode(500, new { mensaje = "Error al reabrir ejercicio" });
            }
        }

        /// <summary>
        /// Obtiene todos los cierres del tenant ordenados por ejercicio
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<CierreEjercicioDTO>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<CierreEjercicioDTO>>> ObtenerHistorial()
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var historial = await _cierreEjercicioService.ObtenerHistorialCierresDTO(tenantId.Value, page: 1, pageSize:100, ejercicio:null);

                _logger.LogInformation(
                     "Consultado historial de cierres. Total: {TotalItems}, Página: {Page}/{TotalPages}",
                        historial.TotalItems,   // ✅ Total de elementos
                        historial.Page,         // ✅ Página actual
                        historial.TotalPages);  // ✅ Total de páginas

                return Ok(historial);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener historial de cierres");
                return StatusCode(500, new { mensaje = "Error al obtener historial" });
            }
        }

        /// <summary>
        /// Obtiene detalles de un cierre específico
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(CierreEjercicioDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<CierreEjercicioDTO>> ObtenerPorId(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var cierre = await _cierreEjercicioService.ObtenerCierreDTO(id, tenantId.Value);

                if (cierre == null)
                {
                    return NotFound(new { mensaje = $"Cierre {id} no encontrado" });
                }

                return Ok(cierre);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener cierre {Id}", id);
                return StatusCode(500, new { mensaje = "Error al obtener cierre" });
            }
        }

        /// <summary>
        /// Descarga el libro de facturas en formato Excel
        /// </summary>
        [HttpGet("descargar/libro/{cierreId}")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> DescargarLibroFacturas(int cierreId)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var cierre = await _cierreEjercicioService.ObtenerCierreEntidad(cierreId, tenantId.Value);

                if (cierre == null || string.IsNullOrEmpty(cierre.RutaLibroFacturas))
                {
                    return NotFound(new { mensaje = "Archivo no encontrado" });
                }

                var filePath = cierre.RutaLibroFacturas;

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning(
                        "Archivo no existe en el sistema: {Ruta}",
                        filePath);
                    return NotFound(new { mensaje = "Archivo no existe en el servidor" });
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var fileName = Path.GetFileName(filePath);

                _logger.LogInformation(
                    "Descargando libro de facturas del cierre {CierreId}: {Archivo}",
                    cierreId, fileName);

                return File(
                    fileBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar libro de facturas del cierre {CierreId}", cierreId);
                return StatusCode(500, new { mensaje = "Error al descargar archivo" });
            }
        }

        /// <summary>
        /// Descarga el resumen trimestral de IVA en formato Excel
        /// </summary>
        [HttpGet("descargar/resumen/{cierreId}")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> DescargarResumenIVA(int cierreId)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var cierre = await _cierreEjercicioService.ObtenerCierreEntidad(cierreId, tenantId.Value);

                if (cierre == null || string.IsNullOrEmpty(cierre.RutaResumenIVA))
                {
                    return NotFound(new { mensaje = "Archivo no encontrado" });
                }

                var filePath = cierre.RutaResumenIVA;

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning(
                        "Archivo no existe en el sistema: {Ruta}",
                        filePath);
                    return NotFound(new { mensaje = "Archivo no existe en el servidor" });
                }

                var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
                var fileName = Path.GetFileName(filePath);

                _logger.LogInformation(
                    "Descargando resumen IVA del cierre {CierreId}: {Archivo}",
                    cierreId, fileName);

                return File(
                    fileBytes,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar resumen IVA del cierre {CierreId}", cierreId);
                return StatusCode(500, new { mensaje = "Error al descargar archivo" });
            }
        }

        /// <summary>
        /// Obtiene la lista de ejercicios con facturas (para selector)
        /// </summary>
        [HttpGet("ejercicios-disponibles")]
        [ProducesResponseType(typeof(List<int>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult<List<int>>> ObtenerEjerciciosDisponibles()
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                // Aquí necesitarías inyectar el contexto o crear un método en el servicio
                // Por simplicidad, asumo que tienes acceso al ApplicationDbContext
                // Si no, deberías crear un método en ICierreEjercicioService

                var ejercicios = await _cierreEjercicioService
                    .ObtenerEjerciciosDisponiblesAsync(tenantId.Value);

                return Ok(ejercicios);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener ejercicios disponibles");
                return StatusCode(500, new { mensaje = "Error al obtener ejercicios" });
            }
        }
    }

    // ========== DTOs de Request ==========
    public class CerrarEjercicioRequest
    {
        public int Ejercicio { get; set; }
    }

    public class ReabrirEjercicioRequest
    {
        public int CierreId { get; set; }
        public string Motivo { get; set; } = string.Empty;
    }
}

