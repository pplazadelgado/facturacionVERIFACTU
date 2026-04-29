using API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;


namespace FacturacionVERIFACTU.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FacturasController : ControllerBase
    {
        private readonly IFacturaService _facturaService;
        private readonly ITenantContext _tenantContext;
        private readonly IPDFService _pdfService;
        private readonly ILogger<FacturasController> _logger;

        public FacturasController(
            IFacturaService facturaService,
            ITenantContext tenanntContext,
            IPDFService pdfService,
            ILogger<FacturasController> logger)
        {
            _facturaService = facturaService;
            _tenantContext = tenanntContext;
            _pdfService = pdfService;
            _logger = logger;
        }


        /// <summary>
        /// Obtiene todas las facturas con filtros opcionales
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<FacturaResponseDto>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FacturaResponseDto>>> ObtenerTodos(
            [FromQuery] int? clienteId = null,
            [FromQuery] string? estado = null,
            [FromQuery] int? ejercicio = null)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontado" });
                }

                var facturas = await _facturaService.ObtenerTodosAsync(tenantId.Value, clienteId, estado, ejercicio);

                return Ok(facturas);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener facturas");
                return StatusCode(500, new { mensaje = "Error al obtener facturas" });
            }
        }


        ///<summary>
        /// Obtiene una factura por Id
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> ObtenerPorId(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });
                }

                var factura = await _facturaService.ObtenerPorIdAsync(tenantId.Value, id);

                if (factura == null)
                {
                    return NotFound(new { mensaje = $"Factura {id} no encontrada" });
                }

                return Ok(factura);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener factura {Id}", id);
                return StatusCode(500, new { mensaje = "Error al obtener factura " });
            }
        }


        ///<summary>
        /// Crear una nueva factura
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<FacturaResponseDto>> Crear([FromBody] FacturaCreateDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var factura = await _facturaService.CrearFacturaAsync(tenantId.Value, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = factura.Id },
                    factura);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error de validacion al crear la factura");
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al crear la factura");
                return StatusCode(500, new { mensaje = "Error al crear la factura" });
            }
        }


        ///<sumary>
        /// Actualiza una factura existente
        /// </sumary>
        [HttpPut("{id}")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> Actualizar(
            int id,
            [FromBody] FacturaUpdateDto dto)
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

                var factura = await _facturaService.ActualizarFacturaAsync(tenantId.Value, id, dto);

                return Ok(factura);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Erro de validacion al actualizar factura {Id}", id);
                return BadRequest(new {mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar factura {Id}", id);
                return StatusCode(500, new { mensaje = "Error al actulizar factura" });
            }
        }


        ///<summary>
        /// Marca una factura como pagada
        /// </summary>
        [HttpPatch("{id}/marcar-pagada")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> MarcarComoPagada(int id, [FromBody] MarcarComoPagadaDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized("Tenant no identificado");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var factura = await _facturaService.MarcarComoPagadaAsync(tenantId.Value, id, dto);

                return Ok(factura);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al marcar factura como pagada {Id}", id);
                return BadRequest(new {mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al marcar factura como pagada {Id}", id);
                return StatusCode(500, new { mensaje = "Error al marcar factura como pagada" });
            }
        }


        ///<summary>
        /// Anula una factura
        /// </summary>
        [HttpPatch("{id}/anular")]
        [ProducesResponseType(typeof(FacturaResponseDto),StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> Anular( int id, [FromBody] AnularFacturaDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var factura = await _facturaService.AnularFacturaAsync(tenantId.Value, id, dto);

                return Ok(factura);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al anular la factura {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al anular la factura {Id}", id);
                return StatusCode(500, new { mensaje = "Error al anular la factura" });
            }
        }

        ///<sumary>
        /// Elimina una factura
        /// </sumary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Eliminar(int id)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if(tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }

                var eliminado = await _facturaService.EliminarAsync(tenantId.Value, id);

                if (!eliminado)
                {
                    return NotFound(new { mensaje = $"Factura {id} no encontrada" });
                }

                return NoContent();
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Erro al eliminar la factura {Id}", id);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar la factura {Id}", id);
                return StatusCode(500, new { mensaje = "Error al eliminar la factura" });
            }
        }


        ///<summary>
        /// Convierte un presupuesto en factura
        /// </summary>
        [HttpPost("desde-presupuesto/{presupuestoId}")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> ConvertirDesdePresupuesto( int presupuestoId, [FromBody] ConvertirPresupuestoAFacturaDto dto)
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

                var factura = await _facturaService.ConvertirDesdePresupuestoAsync(tenantId.Value, presupuestoId, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = factura.Id },
                    factura);
            }
            catch(InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Erro al convertir presupuesto {PresupuestoId} a factura", presupuestoId);
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al convertir presupuesto {PresupuestoId} a factura", presupuestoId);
                return StatusCode(500, new { mensaje = "Error al convertir presupuesto a facturra" });
            }
        }


        ///<sumari>
        /// Convierte varios presupuestos en una factura
        /// </sumari>
        [HttpPost("desde-presupuestos")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<FacturaResponseDto>> ConvertirDesdePresupuestos(
            [FromBody] ConvertirPresupuestosAFacturaDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no identificado" });
                }
                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                // Llamada corregida: nombre correcto y pasar tenantId.Value (int)
                var factura = await _facturaService.ConvertirDesdePresupuestosAsync(tenantId.Value, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = factura.Id },
                    factura);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al convertir prespuestos a factura agrupada");
                return BadRequest(new { mensaje = ex.Message });
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al convertir presupuestos a factura agrupada");
                return StatusCode(500, new { mensaja = "Eror al convertir presupuestos a factura" });
            }
        }


        /// <summary>
        /// Convierte uno o más albaranes en factura
        /// </summary>
        [HttpPost("desde-albaranes")]
        [ProducesResponseType(typeof(FacturaResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FacturaResponseDto>> ConvertirDesdeAlbaranes(int albaranId,
            [FromBody] ConvertirAlbaranesAFacturaDto dto)
        {
            try
            {
                var tenantId = _tenantContext.GetTenantId();
                if (tenantId == null || tenantId == 0)
                {
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }

                var factura = await _facturaService.ConvertirDesdeAlbaranAsync(tenantId.Value,albaranId, dto);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = factura.Id },
                    factura);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Error al convertir albaranes a factura");
                return BadRequest(new { mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al convertir albaranes a factura");
                return StatusCode(500, new { mensaje = "Error al convertir albaranes a factura" });
            }
        }

        
        ///<summary>
        /// Descarga el PDF de la factura (incluye QR VERIFACTU si esta enviada)
        /// </summary>
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
                    return Unauthorized(new { mensaje = "Tenant no encontrado" });

                //Obtener el schema del tenant
                var tenantSchema = _tenantContext.GetTenantSchema();
                if (string.IsNullOrEmpty(tenantSchema))
                    return Unauthorized(new { mensaje = "Schema del tenant no encontrado" });

                //Generar PDF
                var bytes = await _pdfService.GenerarPDFFactura(id, tenantId.Value);

                //Obtener datos de la factura para el nombre del archivo
                var factura = await _facturaService.ObtenerPorIdAsync(tenantId.Value, id);
                if (factura == null)
                    return NotFound(new { mensaje = $"Factura {id} no encontrada" });

                var nombreArchivo = $"Factura-{factura.SerieCodigo}-{factura.Numero}.pdf";

                return File(bytes, "application/pdf", nombreArchivo);
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error al generar PDF de la factura {Id}", id);
                return StatusCode(500, new { mensaje = "Error al generar el PDF de la factura" });
            }
        }
    }
}
