using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Data.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;



namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AlbaranesController : ControllerBase
    {
        private readonly IAlbaranService _albaranService;
        private readonly ITenantContext _tenantContext;
        private readonly IPDFService _pdfService;
        private readonly ILogger<AlbaranesController> _logger;

        public AlbaranesController(
            IAlbaranService albaranService,
            ITenantContext tenantContext,
            IPDFService pdfService,
            ILogger<AlbaranesController> logger)
        {
            _albaranService = albaranService;
            _tenantContext = tenantContext;
            _pdfService = pdfService;
            _logger = logger;
        }

        ///<summary>
        ///Obtiene todos los albaranes
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<AlbaranResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<AlbaranResponseDto>>> ObtenerTodos([FromQuery] string? estado = null, [FromQuery] int? ejercicio = null)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var albaranes = await _albaranService.ObtenerTodosAsync(tenantId.Value, estado, ejercicio);
                return Ok(albaranes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener albaranes");
                return StatusCode(500, new { mensaje = "Error al obtener albaranes" });
            }
        }

        ///<summary>
        ///Obtiene un albaran por Id
        ///</summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(AlbaranResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AlbaranResponseDto>> ObtenerPorId(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var albaran = await _albaranService.ObtenerPorIdAsync(tenantId.Value, id);

                if (albaran == null)
                {
                    return NotFound(new { mensaje = $"Albaran {id} no encontrado" });
                }

                return Ok(albaran);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener albaran {Id}", id);
                return StatusCode(500, new { mensaje = "Error al obtener el albaran" });
            }

        }

        ///<summary>
        ///Cra un nuevo albaran
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(AlbaranResponseDto),StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<AlbaranResponseDto>> Crear([FromBody] AlbaranCreateDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var albaran = await _albaranService.CrearAlbaranAsync(tenantId.Value, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = albaran.Id },
                    albaran);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al crear el albaran");
                return BadRequest(new {mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear el albaran");
                return StatusCode(500, new { mensaje = "Error al crear el albaran" });
            }
        }


        ///<summary>
        ///Actualiza un albaran existente
        /// </summary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(AlbaranResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AlbaranResponseDto>> Actualizar(
            int id,
            [FromBody] AlbaranUpdateDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var albaran = await _albaranService.ActualizarAlbaranAsync(tenantId.Value, id, dto);

                return Ok(albaran);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validadacion al actualizar albaran {Id}", id);
                return BadRequest(new {mensaje = ex.Message});
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar albaran {Id}", id);
                return StatusCode(500, new { mensaje = "Error al actualizar albaran" });
            }
        }

        /// <summary>
        /// Cambia el estado de un albarán.
        /// Transiciones válidas: Pendiente → Entregado/Anulado, Entregado → Facturado
        /// </summary>
        [HttpPatch("{id}/estado")]
        [ProducesResponseType(typeof(AlbaranResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AlbaranResponseDto>> CambiarEstdo(int id, [FromBody] CambiarEstadoAlbaranDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var albaran = await _albaranService.CambiarEstadoAsync(tenantId.Value, id, dto);

                return Ok(albaran);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al cambiar estado del albaran {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al cambiar estado del albaran {Id}", id);
                return StatusCode(500, new { mensaje = "Error al cambiar estado" });
            }
        }

        ///<summary>
        /// Elimina un albaran
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Eliminar (int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });
                }

                var eliminado = await _albaranService.EliminarAsync(tenantId.Value, id);

                if (!eliminado)
                {
                    return NotFound(new { mensaje = $"Albaran {id} no encontrado" });
                }

                return NoContent();
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al elminiar el albaran {Id}", id);
                return BadRequest(new {mensaje = ex.Message});
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar albaran {Id}", id);
                return StatusCode(500, new { mensaje = "Error al eliminar el albaran" });
            }
        }


        ///<sumary>
        ///Convierte un presupuesto en albaran 
        /// </sumary>
        [HttpPost("desde-presupuesto/{presupuestoId}")]
        [ProducesResponseType(typeof(AlbaranResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<AlbaranResponseDto>> ConvertirDesdePresupuesto(int presupuestoId, [FromBody] ConvertirPresupuestoDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var albaran = await _albaranService.ConvertirDesdePresupuesto(tenantId.Value, presupuestoId, dto);
                return CreatedAtAction(nameof(ObtenerPorId), new { id = albaran.Id }, albaran);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al convertir presupuesto {PresupuestoId} a albaran", presupuestoId);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al convertir presupuesto {PresupuestoId} a albaran", presupuestoId);
                return StatusCode(500, new { mensaje = "Error al convertir presupuesto a albaran" });
            }
        }

        ///<summary>
        ///Descarga el PDF del albarán
        ///</summary>
        [HttpGet("{id}/pdf")]
        [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> DescargarPDF(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                // Obtener el schema del tenant para pasarlo al servicio PDF
                var tenantSchema = _tenantContext.GetTenantSchema();
                if (string.IsNullOrEmpty(tenantSchema))
                {
                    return Unauthorized(new { mensaje = "Schema del tenant no encontrado" });
                }

                // Generar PDF
                var bytes = await _pdfService.GenerarPDFAlbaran(id, tenantId.Value);

                // Obtener datos del albarán para el nombre del archivo
                var albaran = await _albaranService.ObtenerPorIdAsync(tenantId.Value, id);
                if (albaran == null)
                {
                    return NotFound(new { mensaje = $"Albarán {id} no encontrado" });
                }

                var nombreArchivo = $"Albaran-{albaran.SerieCodigo}-{albaran.Numero}.pdf";

                return File(bytes, "application/pdf", nombreArchivo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF del albarán {Id}", id);
                return StatusCode(500, new { mensaje = "Error al generar el PDF del albarán" });
            }
        }
    }
}
