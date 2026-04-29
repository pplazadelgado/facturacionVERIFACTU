using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.Data.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FacturacionVERIFACTU.API.Services;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualBasic;


namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PresupuestosController : ControllerBase
    {
        private readonly IPresupuestoService _presupuestoService;
        private readonly ITenantContext _tenantContext;
        private readonly IPDFService _pdfService;
        private readonly ILogger<PresupuestosController> _logger;

        public PresupuestosController(
            IPresupuestoService presupuestoService,
            ITenantContext tenantContext,
            IPDFService pdfService,
            ILogger<PresupuestosController> logget)
        {
            _presupuestoService = presupuestoService;
            _tenantContext = tenantContext;
            _pdfService = pdfService;
            _logger = logget;
        }

        ///<summary>
        ///Obtiene todos los presupuestos del tenant
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<PresupuestoResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<PresupuestoResponseDto>>> ObtenerTodos(
            [FromQuery] string? estado = null,
            [FromQuery] int? ejercicio = null)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == 0 || tenantId == null)
                {
                    return Unauthorized(new { message = "Tenant no identificado" });
                }

                var presupuestos = await _presupuestoService.ObtenerTodosAsync(tenantId.Value, estado, ejercicio);
                return Ok(presupuestos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener los presupuestos");
                return StatusCode(500, new { mensaje = "Error al obtener l prespuestos" });
            }
        }

        ///<summary>
        ///Obtiene un presupuesto por id
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(PresupuestoResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PresupuestoResponseDto>> ObtenerPorId(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == 0 || tenantId == null)
                {
                    return Unauthorized(new { message = "Tenant no encontrado" });
                }

                var prespuesto = await _presupuestoService.ObtenerPorIdAsync(tenantId.Value, id);
                if (prespuesto == null)
                {
                    return NotFound(new { message = $"Presupuesto {id} no encontrado" });
                }

                return Ok(prespuesto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener presupuesto {Id}", id);
                return StatusCode(500, new { mensaje = "Error al obtener el presupuesto" });
            }
        }


        ///<summary>
        ///Crea un nuevo presupuesto
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(PresupuestoResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<PresupuestoResponseDto>> Crear(
            [FromBody] PresupuestoCreateDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == 0 || tenantId ==null)
                {
                    return Unauthorized(new { message = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var presupuesto = await _presupuestoService.CrearPresupuestoAsync(tenantId.Value, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = presupuesto.Id },
                    presupuesto);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al crear el prespuesto");
                return BadRequest(new { menasje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al crear el presupuesto");
                return StatusCode(500, new { mensaje = "Error al crear el prespuesto" });
            }
        }

        ///<sumary>
        ///Actualiza un presupuesto existente
        /// </sumary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(PresupuestoResponseDto),StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PresupuestoResponseDto>> Actualizar(
            int id,
            [FromBody] PresupuestoUpdateDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == 0 || tenantId == null)
                {
                    return Unauthorized(new { message = "Tenant no encontrado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var presupuesto = await _presupuestoService.ActualizarPresupuestoAsync(tenantId.Value, id, dto);

                return Ok(presupuesto);
                }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al actualizar prespuesto {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro al actualizar presupuesto {Id}", id);
                return StatusCode(500, new { mensaje = "Erro al actualizar presupuesto" });
            }            
        }


        /// <summary>
        /// Cambia el estado de un presupuesto
        /// Transiciones válidas:
        /// - Borrador → Enviado
        /// - Enviado → Aceptado/Rechazado
        /// </summary>
        [HttpPatch("{id}/estado")]
        [ProducesResponseType(typeof(PresupuestoResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<PresupuestoResponseDto>> CambiarEstado(
            int id,
            [FromBody] CambiarEstadoPresupuestoDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == 0 || tenantId == null)
                {
                    return Unauthorized(new { message = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var prespuesto = await _presupuestoService.CambiarEstadoAsync(tenantId.Value, id, dto);

                return Ok(prespuesto);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al cambiar el estado del prespuesto {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del presupuesto {Id}", id);
                return StatusCode(500, new { mensaje = "Error al cambiar el estado" });
            }
        }

        ///<summary>
        ///Elimina un presupuesto 
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> Eliminar(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == 0 || tenantId == null)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var eliminado = await _presupuestoService.EliminarAsync(tenantId.Value, id);

                if (!eliminado)
                {
                    return NotFound(new { mensaje = $"Presupuesto {id} no encontrado" });
                }

                return NoContent();
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al eliminar presupuesto {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar presupuesto {Id}", id);
                return StatusCode(500, new { mensaje = "Error al eliminar presepuesto" });
            }
        }


        ///<summary>
        ///Descarga el PDF del presupuesto
        /// </summary>
        [HttpGet("{id}/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DescargaPDF(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                    return Unauthorized(new { mensaje = "Tenant no identificado" });

                //Obtener elshema del tenant
                var tenantSchema = _tenantContext.GetTenantSchema();
                if (string.IsNullOrEmpty(tenantSchema))
                    return Unauthorized(new { mensaje = "Schema del tenant no encontrado" });

                //Generar PDF
                var bytes = await _pdfService.GenerarPDFPresupuesto(id, tenantId.Value);

                //Obtener datos del presupuesto paa el nombre del archivo
                var presupuesto = await _presupuestoService.ObtenerPorIdAsync(tenantId.Value, id);
                if (presupuesto == null)
                    return NotFound(new { mensaje = $"Presupuesto {id} no encontrado" });

                var nombreArchivo = $"Presupuesto-{presupuesto.SerieId}-{presupuesto.Numero}.pdf";

                return File(bytes, "application/pdf", nombreArchivo);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF del presupuesto {id}", id);
                return StatusCode(500, new { mensaje = "Error al generar el PDF del presupuesto" });
            }
        }
    }
}
