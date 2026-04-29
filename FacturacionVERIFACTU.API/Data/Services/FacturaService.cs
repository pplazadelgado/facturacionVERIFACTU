// API/Data/Services/FacturaService.cs
using API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Models;
using FacturacionVERIFACTU.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Xml.Linq;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IFacturaService
    {
        Task<FacturaResponseDto> CrearFacturaAsync(int tenantId, FacturaCreateDto dto);
        Task<FacturaResponseDto> ActualizarFacturaAsync(int tenantId, int id, FacturaUpdateDto dto);
        Task<FacturaResponseDto> MarcarComoPagadaAsync(int tenantId, int id, MarcarComoPagadaDto dto);
        Task<FacturaResponseDto> AnularFacturaAsync(int tenantId, int id, AnularFacturaDto dto);
        Task<FacturaResponseDto> ObtenerPorIdAsync(int tenantId, int id);
        Task<List<FacturaResponseDto>> ObtenerTodosAsync(int tenantId, int? id, string? estado, int? ejercicio);
        Task<bool> EliminarAsync(int tenantId, int id);
        Task<FacturaResponseDto> ConvertirDesdePresupuestoAsync(int tenantId, int id, ConvertirPresupuestoAFacturaDto dto);
        Task<FacturaResponseDto> ConvertirDesdePresupuestosAsync(int tenantId, ConvertirPresupuestosAFacturaDto dto);
        Task<FacturaResponseDto> ConvertirDesdeAlbaranAsync(int tenantId, int id, ConvertirAlbaranesAFacturaDto dto);
    }

    public class FacturaService : IFacturaService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISerieNumeracionService _numeracionService;
        private readonly VERIFACTUService _verifactuService;
        private readonly IAEATClient _verifactuHttpClient;
        private readonly ILogger<FacturaService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ICacheService _cacheService;

        public FacturaService(
            ApplicationDbContext context,
            ISerieNumeracionService numeracionService,
            VERIFACTUService verifactuService,
            IAEATClient verifactuHttpClient,
            ILogger<FacturaService> logger,
            IServiceScopeFactory scopeFactory,
            ICacheService cacheService)
        {
            _context = context;
            _numeracionService = numeracionService;
            _verifactuService = verifactuService;
            _verifactuHttpClient = verifactuHttpClient;
            _logger = logger;
            _scopeFactory = scopeFactory;
            _cacheService = cacheService;
        }

        // ====================================================================
        // CREAR FACTURA
        // ====================================================================
        // API/Services/FacturaService.cs - ACTUALIZAR CrearFacturaAsync

        public async Task<FacturaResponseDto> CrearFacturaAsync(int tenantId, FacturaCreateDto dto)
        {
            // Validar y cargar cliente CON configuración fiscal
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.TenantId == tenantId);

            if (cliente == null)
                throw new InvalidOperationException($"Cliente {dto.ClienteId} no encontrado");

            var lineasFiltradas = dto.Lineas
                .Where(l => l.ProductoId.HasValue || !string.IsNullOrWhiteSpace(l.Descripcion))
                .ToList();

            if (!lineasFiltradas.Any())
            {
                throw new InvalidOperationException("Debe incluir al menos una línea válida.");
            }

            foreach (var linea in lineasFiltradas)
            {
                var esLineaTexto = (linea.Cantidad == 0 || linea.Cantidad == null)
                   && !string.IsNullOrWhiteSpace(linea.Descripcion);

                if (!esLineaTexto && linea.Cantidad == 0)
                {
                    throw new InvalidOperationException("La cantidad no puede ser cero sin descripción.");
                }

                if (linea.PrecioUnitario < 0)
                {
                    throw new InvalidOperationException("El precio unitario no puede ser negativo.");
                }

                if (linea.PorcentajeDescuento < 0 || linea.PorcentajeDescuento > 100)
                {
                    throw new InvalidOperationException("El porcentaje de descuento debe estar entre 0 y 100.");
                }
            }


            // Validar productos
            var lineasProductoIds = lineasFiltradas
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, Producto> productosDict = new();

            if (lineasProductoIds.Any())
            {
                var productos = await _context.Productos
                    .Where(p => p.TenantId == tenantId && lineasProductoIds.Contains(p.Id))
                    .ToListAsync();

                if (productos.Count != lineasProductoIds.Count)
                {
                    var idFaltante = lineasProductoIds.First(id => !productos.Any(p => p.Id == id));
                    throw new InvalidOperationException($"El producto con ID {idFaltante} no fue encontrado.");
                }

                productosDict = productos.ToDictionary(p => p.Id);
            }

            // Validar serie
            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == dto.SerieId && s.TenantId == tenantId);

            if (serie == null)
                throw new InvalidOperationException($"Serie {dto.SerieId} no encontrada");

            if (serie.Bloqueada)
                throw new InvalidOperationException("La serie está bloqueada. No se pueden crear facturas");

            // Obtener siguiente número
            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serie.Codigo, ejercicio, DocumentTypes.FACTURA);

            // ⭐ APLICAR CONFIGURACIÓN FISCAL DEL CLIENTE
            var porcentajeRetencion = dto.PorcentajeRetencion
                ?? cliente.PorcentajeRetencionDefecto;

            // Crear factura
            var factura = new Factura
            {
                TenantId = tenantId,
                ClienteId = dto.ClienteId,
                SerieId = dto.SerieId,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                Estado = "Emitida",
                Observaciones = dto.Observaciones,
                TipoFacturaVERIFACTU = DeterminarTipoFacturaVerifactu(cliente),
                NumeroFacturaRectificada = string.IsNullOrWhiteSpace(dto.NumeroFacturaRectificada)
                    ? null
                    : dto.NumeroFacturaRectificada,
               TipoRectificacion = string.IsNullOrWhiteSpace(dto.TipoRectificacion)
                    ? null                           // ← null en lugar de ""
                    : dto.TipoRectificacion,

                PorcentajeRetencion = porcentajeRetencion, // ⭐ DESDE CLIENTE
                Bloqueada = false,
                ActualizadoEn = DateTime.UtcNow
            };

            // ⭐ AGREGAR LÍNEAS CON CONFIGURACIÓN FISCAL AUTOMÁTICA
            var fechaReferencia = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime();
            var tiposImpuestoActivos = await ObtenerTiposImpuestoActivosAsync(tenantId, fechaReferencia);
            int orden = 1;
            foreach (var lineaDto in lineasFiltradas)
            {
                Producto? producto = null;
                if (lineaDto.ProductoId.HasValue && productosDict.ContainsKey(lineaDto.ProductoId.Value))
                {
                    producto = productosDict[lineaDto.ProductoId.Value];
                }

                var (tipoImpuesto, iva, recargo) = ResolverTipoImpuesto(
                    tiposImpuestoActivos,
                    lineaDto.TipoImpuestoId,
                    producto);

                if (!cliente.RegimenRecargoEquivalencia)
                {
                    recargo = 0m;
                }

                var linea = new LineaFactura
                {
                    Orden = orden++,
                    Descripcion = lineaDto.Descripcion,
                    Cantidad = lineaDto.Cantidad,
                    PrecioUnitario = lineaDto.PrecioUnitario,
                    IvaPercentSnapshot = iva,
                    RePercentSnapshot = recargo,
                    TipoImpuestoId = tipoImpuesto.Id,
                    ProductoId = lineaDto.ProductoId
                };

                CalcularLineaFactura(linea);
                factura.Lineas.Add(linea);
            }

            // Calcular totales
            CalcularTotalesFactura(factura);
            ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

            // Log de configuración fiscal aplicada
            if (cliente.RegimenRecargoEquivalencia)
            {
                _logger.LogInformation(
                    "Factura {Numero}: Aplicado recargo equivalencia. Total recargo: {TotalRecargo}€",
                    numeroCompleto, factura.TotalRecargo);
            }

            if (porcentajeRetencion > 0)
            {
                _logger.LogInformation(
                    "Factura {Numero}: Aplicada retención {Porcentaje}%. Cuota: {Cuota}€",
                    numeroCompleto, porcentajeRetencion, factura.CuotaRetencion);
            }

            // Obtener hash de la factura anterior
            var facturaAnterior = await _context.Facturas
                .Where(f => f.SerieId == dto.SerieId && f.TenantId == tenantId)
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            factura.HuellaAnterior = facturaAnterior?.Huella;

            // Aplicar VERIFACTU
            await AplicarVERIFACTUAsync(factura);

            _context.Facturas.Add(factura);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Creada factura {Numero} para tenant {TenantId}", numeroCompleto, tenantId);

            // Enviar a AEAT en background
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnviarFacturaAEATAsync(factura.Id, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar factura {FacturaId} a AEAT", factura.Id);
                }
            });

            return await MapearAResponseDto(factura);
        }


        // ====================================================================
        // ACTUALIZAR FACTURA
        // ====================================================================
        // API/Services/FacturaService.cs - ActualizarFacturaAsync COMPLETO

        public async Task<FacturaResponseDto> ActualizarFacturaAsync(int tenantId, int id, FacturaUpdateDto dto)
        {
            var factura = await _context.Facturas
                .Include(f => f.Lineas)
                .Include(f => f.Serie)
                .Include(f => f.Cliente) // ⭐ IMPORTANTE: Incluir cliente
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (factura == null)
                throw new InvalidOperationException($"Factura {id} no encontrada");

            // Validaciones de bloqueo
            if (factura.Bloqueada)
                throw new InvalidOperationException(
                    "No se puede modificar una factura de un ejercicio cerrado. " +
                    "Debe reabrir el ejercicio primero.");

            if (factura.Estado != "Emitida")
                throw new InvalidOperationException($"No se puede modificar una factura en estado {factura.Estado}");

            if (factura.EnviadaVERIFACTU)
                throw new InvalidOperationException($"No se puede modificar una factura enviada a AEAT");

            if (factura.Serie.Bloqueada)
                throw new InvalidOperationException("No se puede modificar una factura de un ejercicio cerrado");

            // ⭐ SI SE CAMBIÓ EL CLIENTE, RECARGAR CONFIGURACIÓN FISCAL
            Cliente clienteActual = factura.Cliente;

            if (dto.ClienteId != factura.ClienteId)
            {
                clienteActual = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.TenantId == tenantId);

                if (clienteActual == null)
                    throw new InvalidOperationException($"Cliente {dto.ClienteId} no encontrado");

                factura.ClienteId = dto.ClienteId;

                // ⭐ RECALCULAR RETENCIÓN CON NUEVO CLIENTE
                factura.PorcentajeRetencion = dto.PorcentajeRetencion
                    ?? clienteActual.PorcentajeRetencionDefecto;

                _logger.LogInformation(
                    "Factura {Id}: Cliente cambiado a {Cliente}. Nueva retención: {Retencion}%",
                    id, clienteActual.Nombre, factura.PorcentajeRetencion);
            }
            else
            {
                // ⭐ ACTUALIZAR RETENCIÓN SI SE ESPECIFICÓ EN EL DTO
                if (dto.PorcentajeRetencion.HasValue)
                {
                    factura.PorcentajeRetencion = dto.PorcentajeRetencion.Value;
                }
            }

            var lineasFiltradas = dto.Lineas
                .Where(l => l.ProductoId.HasValue || !string.IsNullOrWhiteSpace(l.Descripcion))
                .ToList();

            if (!lineasFiltradas.Any())
            {
                throw new InvalidOperationException("Debe incluir al menos una línea válida.");
            }

            foreach (var linea in lineasFiltradas)
            {

                if (linea.PrecioUnitario < 0)
                {
                    throw new InvalidOperationException("El precio unitario no puede ser negativo.");
                }

                if (linea.PorcentajeDescuento < 0 || linea.PorcentajeDescuento > 100)
                {
                    throw new InvalidOperationException("El porcentaje de descuento debe estar entre 0 y 100.");
                }
            }

            // Validar productos
            var lineasProductoIds = lineasFiltradas
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, Producto> productosDict = new();

            if (lineasProductoIds.Any())
            {
                var productos = await _context.Productos
                    .Where(p => p.TenantId == tenantId && lineasProductoIds.Contains(p.Id))
                    .ToListAsync();

                if (productos.Count != lineasProductoIds.Count)
                {
                    var idFaltante = lineasProductoIds.First(id => !productos.Any(p => p.Id == id));
                    throw new InvalidOperationException($"El producto con ID {idFaltante} no fue encontrado.");
                }

                productosDict = productos.ToDictionary(p => p.Id);
            }

            // Actualizar datos básicos
            factura.FechaEmision = (dto.FechaEmision ?? factura.FechaEmision).ToUniversalTime();
            factura.Observaciones = dto.Observaciones;
            factura.ActualizadoEn = DateTime.UtcNow;

            // Actualización inteligente de líneas
            var lineasDtoIds = lineasFiltradas
                .Where(l => l.Id.HasValue)
                .Select(l => l.Id.Value)
                .ToList();

            var lineasAEliminar = factura.Lineas
                .Where(l => !lineasDtoIds.Contains(l.Id))
                .ToList();

            foreach (var lineaAEliminar in lineasAEliminar)
            {
                factura.Lineas.Remove(lineaAEliminar);
                _context.LineasFacturas.Remove(lineaAEliminar);
            }

            var fechaReferencia = (dto.FechaEmision ?? factura.FechaEmision).ToUniversalTime();
            var tiposImpuestoActivos = await ObtenerTiposImpuestoActivosAsync(tenantId, fechaReferencia);

            int orden = 1;
            foreach (var lineaDto in lineasFiltradas)
            {
                Producto? producto = null;
                if (lineaDto.ProductoId.HasValue && productosDict.ContainsKey(lineaDto.ProductoId.Value))
                {
                    producto = productosDict[lineaDto.ProductoId.Value];
                }

                var (tipoImpuesto, iva, recargo) = ResolverTipoImpuesto(
                    tiposImpuestoActivos,
                    lineaDto.TipoImpuestoId,
                    producto);

                if (!clienteActual.RegimenRecargoEquivalencia)
                {
                    recargo = 0m;
                }

                if (lineaDto.Id.HasValue && lineaDto.Id.Value > 0)
                {
                    // Actualizar línea existente
                    var lineaExistente = factura.Lineas
                        .FirstOrDefault(l => l.Id == lineaDto.Id.Value);

                    if (lineaExistente != null)
                    {
                        lineaExistente.Orden = orden++;
                        lineaExistente.Descripcion = lineaDto.Descripcion;
                        lineaExistente.Cantidad = lineaDto.Cantidad;
                        lineaExistente.PrecioUnitario = lineaDto.PrecioUnitario;
                        lineaExistente.IvaPercentSnapshot = iva;
                        lineaExistente.RePercentSnapshot = recargo;
                        lineaExistente.TipoImpuestoId = tipoImpuesto.Id;
                        lineaExistente.ProductoId = lineaDto.ProductoId;
                      
                        CalcularLineaFactura(lineaExistente);
                    }
                }
                else
                {
                    // Crear línea nueva
                    var lineaNueva = new LineaFactura
                    {
                        FacturaId = factura.Id,
                        Orden = orden++,
                        Descripcion = lineaDto.Descripcion,
                        Cantidad = lineaDto.Cantidad,
                        PrecioUnitario = lineaDto.PrecioUnitario,
                        IvaPercentSnapshot = iva,
                        RePercentSnapshot = recargo,
                        TipoImpuestoId = tipoImpuesto?.Id,
                        ProductoId = lineaDto.ProductoId,
                    };

                    CalcularLineaFactura(lineaNueva);
                    factura.Lineas.Add(lineaNueva);
                }
            }

            // Recalcular totales
            CalcularTotalesFactura(factura);

            // Log de cambios fiscales
            _logger.LogInformation(
                "Factura {Numero} actualizada. Cliente en recargo: {Recargo}. Retención: {Retencion}%",
                factura.Numero, clienteActual.RegimenRecargoEquivalencia, factura.PorcentajeRetencion);

            // Regenerar hash VERIFACTU
            await AplicarVERIFACTUAsync(factura);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Actualizada factura {Numero} para tenant {TenantID}", factura.Numero, tenantId);

            return await MapearAResponseDto(factura);
        }

        // ====================================================================
        // MARCAR COMO PAGADA
        // ====================================================================
        public async Task<FacturaResponseDto> MarcarComoPagadaAsync(int tenantId, int id, MarcarComoPagadaDto dto)
        {
            var factura = await _context.Facturas
                .Include(f => f.Lineas)
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (factura == null)
                throw new InvalidOperationException($"Factura {id} no encontrada");

            if (factura.Estado == "Anulada")
                throw new InvalidOperationException("No se puede marcar como pagada una factura anulada");

            if (factura.Estado == "Pagada")
                throw new InvalidOperationException("La factura ya está marcada como pagada");

            // Validar importe
            if (dto.Importe != factura.Total)
            {
                _logger.LogWarning(
                    "Factura {FacturaId}: Importe pagado ({Importe}) diferente del total({Total})",
                    id, dto.Importe, factura.Total);
            }

            var estadoAnterior = factura.Estado;
            factura.Estado = "Pagada";
            factura.ActualizadoEn = DateTime.UtcNow; // ⭐ NUEVO

            // Agregar información del pago para observaciones
            if (!string.IsNullOrEmpty(dto.Observaciones) || !string.IsNullOrEmpty(dto.FormaPago))
            {
                factura.Observaciones = (factura.Observaciones ?? "") +
                    $"\n[PAGADA] {dto.FechaPago:yyyy-MM-dd}: {dto.FormaPago ?? "Sin especificar"}" +
                    (string.IsNullOrEmpty(dto.Observaciones) ? "" : $" - {dto.Observaciones}");
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cambiado estado de factura {Numero} de {EstadoAnterior} a Pagada",
                factura.Numero, estadoAnterior);

            return await MapearAResponseDto(factura);
        }


        // ====================================================================
        // ANULAR FACTURA
        // ====================================================================
        public async Task<FacturaResponseDto> AnularFacturaAsync(int tenantId, int id, AnularFacturaDto dto)
        {
            var factura = await _context.Facturas
                .Include(f => f.Lineas)
                .Include(f => f.Serie)
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (factura == null)
                throw new InvalidOperationException($"Factura {id} no encontrada");

            // ⭐ VALIDAR BLOQUEO POR CIERRE
            if (factura.Bloqueada)
                throw new InvalidOperationException(
                    "No se puede anular una factura de un ejercicio cerrado. " +
                    "Debe crear una factura rectificativa.");

            if (factura.Serie.Bloqueada)
                throw new InvalidOperationException("No se puede anular una factura de un ejercicio cerrado. " +
                    "Debe crear una factura rectificativa");

            if (factura.Estado == "Anulada")
                throw new InvalidOperationException("La factura ya está anulada");

            if (factura.EnviadaVERIFACTU)
            {
                throw new InvalidOperationException("La factura ya fue enviada a AEAT. " +
                    "Debe crear una factura rectificativa en lugar de anularla");
            }

            var estadoAnterior = factura.Estado;
            factura.Estado = "Anulada";
            factura.ActualizadoEn = DateTime.UtcNow; // ⭐ NUEVO
            factura.Observaciones = (factura.Observaciones ?? "") +
                $"\n[ANULADA] {DateTime.UtcNow:yyyy-MM-dd}: {dto.Motivo}";

            await _context.SaveChangesAsync();

            _logger.LogWarning("Anulada factura {Numero}. Motivo: {Motivo}",
                factura.Numero, dto.Motivo);

            return await MapearAResponseDto(factura);
        }


        // ====================================================================
        // OBTENER POR ID
        // ====================================================================
        public async Task<FacturaResponseDto> ObtenerPorIdAsync(int tenantId, int id)
        {
            var factura = await _context.Facturas
                .Include(f => f.Lineas)
                .Include(f => f.Cliente)
                .Include(f => f.Serie)
                .Include(f => f.Albaranes)
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (factura == null) return null;

            return await MapearAResponseDto(factura);
        }


        // ====================================================================
        // OBTENER TODOS
        // ====================================================================
        public async Task<List<FacturaResponseDto>> ObtenerTodosAsync(
            int tenantId,
            int? clienteId = null,
            string? estado = null,
            int? ejercicio = null)
                {
                    var query = _context.Facturas
                        .Where(f => f.TenantId == tenantId)
                        .AsNoTracking();

                    if (clienteId.HasValue)
                        query = query.Where(f => f.ClienteId == clienteId.Value);
                    if (!string.IsNullOrEmpty(estado))
                        query = query.Where(f => f.Estado == estado);
            if (ejercicio.HasValue)
                query = query.Where(f => f.Ejercicio == ejercicio.Value);

                    return await query
                        .OrderByDescending(f => f.FechaEmision)
                        .Select(f => new FacturaResponseDto
                        {
                            Id = f.Id,
                            TenantId = f.TenantId,
                            ClienteId = f.ClienteId,
                            ClienteNombre = f.Cliente.Nombre,
                            ClienteNIF = f.Cliente.NIF,
                            SerieId = f.SerieId,
                            SerieCodigo = f.Serie.Codigo,
                            Numero = f.Numero,
                            Ejercicio = f.Ejercicio,
                            FechaEmision = f.FechaEmision,
                            BaseImponible = f.BaseImponible,
                            TotalIVA = f.TotalIVA,
                            TotalRecargo = f.TotalRecargo ?? 0m,
                            PorcentajeRetencion = f.PorcentajeRetencion ?? 0m,
                            CuotaRetencion = f.CuotaRetencion ?? 0m,
                            Total = f.Total,
                            Estado = f.Estado,
                            Bloqueada = f.Bloqueada,
                            Observaciones = f.Observaciones,
                            Huella = f.Huella,
                            HuellaAnterior = f.HuellaAnterior,
                            EnviadaVERIFACTU = f.EnviadaVERIFACTU,
                            FechaEnvioVERIFACTU = f.FechaEnvioVERIFACTU,
                            TipoFacturaVERIFACTU = f.TipoFacturaVERIFACTU,
                            UrlVERIFACTU = f.UrlVERIFACTU,
                            QRBase64 = f.QRVerifactu != null ? Convert.ToBase64String(f.QRVerifactu) : null,
                            NumeroFacturaRectificada = f.NumeroFacturaRectificada,
                            TipoRectificacion = f.TipoRectificacion,
                            AlbaranesIds = f.Albaranes.Select(a => a.Id).ToList(),
                            Lineas = new List<LineaFacturaResponseDto>() // vacío en listado
                        })
                        .ToListAsync();
                }


        // ====================================================================
        // ELIMINAR
        // ====================================================================
        public async Task<bool> EliminarAsync(int tenantId, int id)
        {
            var factura = await _context.Facturas
                .Include(f => f.Serie)
                .FirstOrDefaultAsync(f => f.Id == id && f.TenantId == tenantId);

            if (factura == null) return false;

            // ⭐ VALIDAR BLOQUEO
            if (factura.Bloqueada)
                throw new InvalidOperationException(
                    "No se puede eliminar una factura de un ejercicio cerrado.");

            // Solo se pueden eliminar facturas en estado emitida y no enviada
            if (factura.Estado != "Emitida")
                throw new InvalidOperationException($"No se puede eliminar una factura en estado {factura.Estado}.");

            if (factura.EnviadaVERIFACTU)
                throw new InvalidOperationException("No se puede eliminar una factura ya enviada a AEAT");

            _context.Facturas.Remove(factura);
            await _context.SaveChangesAsync();

            _logger.LogWarning("Eliminada factura {numero} del tenant {TenantId}",
                factura.Numero, tenantId);

            return true;
        }


        // ====================================================================
        // CONVERTIR DESDE PRESUPUESTO
        // ====================================================================
        // API/Services/FacturaService.cs - ConvertirDesdePresupuestoAsync

        public async Task<FacturaResponseDto> ConvertirDesdePresupuestoAsync(
            int tenantId, int presupuestoId, ConvertirPresupuestoAFacturaDto dto)
        {
            // Obtener presupuesto CON CLIENTE
            var presupuesto = await _context.Presupuestos
                .Include(p => p.Lineas)
                .Include(p => p.Cliente) // ⭐ IMPORTANTE
                .FirstOrDefaultAsync(p => p.Id == presupuestoId && p.TenantId == tenantId);

            if (presupuesto == null)
                throw new InvalidOperationException($"Presupuesto {presupuestoId} no encontrado");

            if (presupuesto.Estado != "Aceptado")
                throw new InvalidOperationException($"Solo se pueden convertir presupuestos en estado aceptado");

            if (presupuesto.FacturaId != null)
                throw new InvalidOperationException("Este presupuesto ya tiene una factura asociada");

            // Validar serie
            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == dto.SerieId && s.TenantId == tenantId);

            if (serie == null)
                throw new InvalidOperationException($"Serie {dto.SerieId} no encontrada");

            if (serie.Bloqueada)
                throw new InvalidOperationException("La serie está bloqueada");

            // ⭐ CARGAR CONFIGURACIÓN FISCAL DEL CLIENTE
            var cliente = presupuesto.Cliente;

            // Obtener siguiente número
            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serie.Codigo, ejercicio, DocumentTypes.FACTURA);

            // Crear factura
            var factura = new Factura
            {
                TenantId = tenantId,
                ClienteId = presupuesto.ClienteId,
                SerieId = serie.Id,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                Estado = "Emitida",
                Observaciones = dto.Observaciones ?? presupuesto.Observaciones,
                TipoFacturaVERIFACTU = DeterminarTipoFacturaVerifactu(cliente),
                // ⭐ MANTENER O APLICAR RETENCIÓN
                PorcentajeRetencion = dto.PorcentajeRetencion
                     ?? presupuesto.PorcentajeRetencion  // Del presupuesto
                     ?? cliente.PorcentajeRetencionDefecto, // O del cliente
                Bloqueada = false,
                ActualizadoEn = DateTime.UtcNow
            };

            // Cargar productos para configuración fiscal
            var lineasACopiar = presupuesto.Lineas.AsEnumerable();

            if (dto.LineasSeleccionadas != null && dto.LineasSeleccionadas.Any())
            {
                lineasACopiar = lineasACopiar.Where(l => dto.LineasSeleccionadas.Contains(l.Id));
            }

            var productosIds = lineasACopiar
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, Producto> productosDict = new();

            if (productosIds.Any())
            {
                var productos = await _context.Productos
                    .Where(p => productosIds.Contains(p.Id) && p.TenantId == tenantId)
                    .ToListAsync();

                productosDict = productos.ToDictionary(p => p.Id);
            }

            int orden = 1;
            foreach (var lineaPresupuesto in lineasACopiar.OrderBy(l => l.Orden))
            {
                // Aplicar modificaciones si existen
                var cantidad = lineaPresupuesto.Cantidad;
                var precioUnitario = lineaPresupuesto.PrecioUnitario;

                if (dto.LineasModificadas != null)
                {
                    var modificacion = dto.LineasModificadas
                        .FirstOrDefault(m => m.LineaId == lineaPresupuesto.Id);

                    if (modificacion != null)
                    {
                        cantidad = modificacion.Cantidad ?? cantidad;
                        precioUnitario = modificacion.PrecioUnitario ?? precioUnitario;
                    }
                }

                // ⭐ MANTENER CONFIGURACIÓN FISCAL DEL PRESUPUESTO
                decimal iva = lineaPresupuesto.IvaPercentSnapshot;
                decimal recargo = cliente.RegimenRecargoEquivalencia
                    ? lineaPresupuesto.RePercentSnapshot
                    : 0m;

                var lineaFactura = new LineaFactura
                {
                    Orden = orden++,
                    Descripcion = lineaPresupuesto.Descripcion,
                    Cantidad = cantidad,
                    PrecioUnitario = precioUnitario,
                    IvaPercentSnapshot = iva,
                    RePercentSnapshot = recargo,
                    TipoImpuestoId = lineaPresupuesto.TipoImpuestoId,
                    ProductoId = lineaPresupuesto.ProductoId
                };

                CalcularLineaFactura(lineaFactura);
                factura.Lineas.Add(lineaFactura);
            }

            // Calcular totales
            CalcularTotalesFactura(factura);
            ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

            _logger.LogInformation(
                "Factura desde presupuesto {Presupuesto}. Recargo: {Recargo}€, Retención: {Retencion}€",
                presupuesto.Numero, factura.TotalRecargo, factura.CuotaRetencion);

            // Obtener hash de la factura anterior
            var facturaAnterior = await _context.Facturas
                .Where(f => f.SerieId == dto.SerieId && f.TenantId == tenantId)
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            factura.HuellaAnterior = facturaAnterior?.Huella;

            // Aplicar VERIFACTU
            await AplicarVERIFACTUAsync(factura);

            _context.Facturas.Add(factura);
            await _context.SaveChangesAsync();

            // Vincular presupuesto con factura
            presupuesto.FacturaId = factura.Id;
            presupuesto.Estado = "Facturado";
            await _context.SaveChangesAsync();

            _logger.LogInformation("Creada factura {NumeroFactura} desde presupuesto {NumeroPresupuesto}",
                numeroCompleto, presupuesto.Numero);

            // Enviar a AEAT en background
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnviarFacturaAEATAsync(factura.Id, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar factura {FacturaId} a AEAT", factura.Id);
                }
            });

            return await MapearAResponseDto(factura);
        }

        // ====================================================================
        // CONVERTIR DESDE PRESUPUESTOS (AGRUPADA)
        // ====================================================================
        public async Task<FacturaResponseDto> ConvertirDesdePresupuestosAsync(
            int tenantId, ConvertirPresupuestosAFacturaDto dto)
        {
            var presupuestos = await _context.Presupuestos
                .Include(p => p.Lineas)
                .Include(p => p.Cliente)
                .Where(p => dto.PresupuestosIds.Contains(p.Id) && p.TenantId == tenantId)
                .ToListAsync();

            if (presupuestos.Count != dto.PresupuestosIds.Count)
                throw new InvalidOperationException("Algunos presupuestos no fuero encontrados");

            var clienteId = presupuestos.First().ClienteId;
            if (presupuestos.Any(p => p.ClienteId != clienteId))
                throw new InvalidOperationException("Todos los presupuestos deben de ser del mismo cliente");

            if (presupuestos.Any(p => p.Estado != "Aceptado"))
                throw new InvalidOperationException("Solo se pueden convertir presupuestos en estado aceptado");

            if (presupuestos.Any(p => p.FacturaId != null))
                throw new InvalidOperationException("Algurnos presupuestos ya estan facturados");

            var cliente = presupuestos.First().Cliente;

            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == dto.SerieId && s.TenantId == tenantId);

            if (serie == null)
                throw new InvalidOperationException($"Serie {dto.SerieId} no encontrada");

            if (serie.Bloqueada)
                throw new InvalidOperationException("La serie esta bloqueada");

            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serie.Codigo, ejercicio, DocumentTypes.FACTURA);

            var factura = new Factura
            {
                TenantId = tenantId,
                ClienteId = clienteId,
                SerieId = serie.Id,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                Estado = "Emitida",
                Observaciones = dto.Observaciones,
                TipoFacturaVERIFACTU = DeterminarTipoFacturaVerifactu(cliente),
                PorcentajeRetencion = dto.PorcentajeRetencion ?? presupuestos.First().PorcentajeRetencion ?? cliente.PorcentajeRetencionDefecto,
                Bloqueada = false,
                ActualizadoEn = DateTime.UtcNow
            };

            var productosIds = presupuestos
                .SelectMany(p => p.Lineas)
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, Producto> productosDict = new();

            if (productosIds.Any())
            {
                var productos = await _context.Productos
                    .Where(p => productosIds.Contains(p.Id))
                    .ToListAsync();

                productosDict = productos.ToDictionary(p => p.Id);
            }

            var orden = 1;
            foreach (var lineaPresupuesto in presupuestos.SelectMany(p => p.Lineas).OrderBy(l => l.Orden))
            {
                decimal iva = lineaPresupuesto.IvaPercentSnapshot;
                decimal recargo = cliente.RegimenRecargoEquivalencia
                    ? lineaPresupuesto.RePercentSnapshot
                    : 0m; 

             

                var lineaFactura = new LineaFactura
                {
                    Orden = orden++,
                    Descripcion = lineaPresupuesto.Descripcion,
                    Cantidad = lineaPresupuesto.Cantidad,
                    PrecioUnitario = lineaPresupuesto.PrecioUnitario,
                    IvaPercentSnapshot = iva,
                    RePercentSnapshot = recargo,
                    TipoImpuestoId = lineaPresupuesto.TipoImpuestoId,
                    ProductoId = lineaPresupuesto.ProductoId
                };

                CalcularLineaFactura(lineaFactura);
                factura.Lineas.Add(lineaFactura);
            }

            CalcularTotalesFactura(factura);
            ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

            var facturaAnterior = await _context.Facturas
                .Where(f => f.SerieId == dto.SerieId && f.TenantId == tenantId)
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            factura.HuellaAnterior = facturaAnterior?.Huella;

            await AplicarVERIFACTUAsync(factura);

            _context.Facturas.Add(factura);
            await _context.SaveChangesAsync();

            foreach (var presupuesto in presupuestos)
            {
                presupuesto.FacturaId = factura.Id;
                presupuesto.Estado = "Facturado";
            }

            await _context.SaveChangesAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    await EnviarFacturaAEATAsync(factura.Id, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar factura {FacturaId} a AEAT", factura.Id);
                }
            });

            return await MapearAResponseDto(factura);
        }


        // ====================================================================
        // CONVERTIR DESDE ALBARANES
        // ====================================================================
        // API/Services/FacturaService.cs - ConvertirDesdeAlbaranAsync

        public async Task<FacturaResponseDto> ConvertirDesdeAlbaranAsync(
            int tenantId, int id, ConvertirAlbaranesAFacturaDto dto)
        {
            // Validar albaranes CON CLIENTE
            var albaranes = await _context.Albaranes
                .Include(a => a.Lineas)
                .Include(a => a.Cliente) // ⭐ IMPORTANTE
                .Where(a => dto.AlbaranesIds.Contains(a.Id) && a.TenantId == tenantId)
                .ToListAsync();

            if (albaranes.Count != dto.AlbaranesIds.Count)
                throw new InvalidOperationException("Algunos albaranes no fueron encontrados");

            // Validar que todos sean del mismo cliente
            var clienteId = albaranes.First().ClienteId;
            if (albaranes.Any(a => a.ClienteId != clienteId))
                throw new InvalidOperationException("Todos los albaranes deben ser del mismo cliente");

            if (albaranes.Any(a => a.Facturado || a.FacturaId != null))
                throw new InvalidOperationException("Algunos albaranes ya están facturados");

            // ⭐ OBTENER CLIENTE CON CONFIGURACIÓN FISCAL
            var cliente = albaranes.First().Cliente;

            // Validar serie
            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == dto.SerieId && s.TenantId == tenantId);

            if (serie == null)
                throw new InvalidOperationException($"Serie {dto.SerieId} no encontrada");

            if (serie.Bloqueada)
                throw new InvalidOperationException("La serie está bloqueada");

            // Obtener siguiente número
            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serie.Codigo, ejercicio, "FACTURA");

            // Crear Factura
            var factura = new Factura
            {
                TenantId = tenantId,
                ClienteId = clienteId,
                SerieId = serie.Id,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                Estado = "Emitida",
                Observaciones = dto.Observaciones ??
                    $"Factura generada desde albaranes: {string.Join(", ", albaranes.Select(a => a.Numero))}",
                TipoFacturaVERIFACTU = DeterminarTipoFacturaVerifactu(cliente),

                // ⭐ APLICAR RETENCIÓN DEL CLIENTE
                PorcentajeRetencion = dto.PorcentajeRetencion
                    ?? cliente.PorcentajeRetencionDefecto,

                Bloqueada = false,
                ActualizadoEn = DateTime.UtcNow
            };

            // Cargar productos para posibles recálculos
            var productosIds = albaranes
                .SelectMany(a => a.Lineas)
                .Where(l => l.ProductoId.HasValue)
                .Select(l => l.ProductoId.Value)
                .Distinct()
                .ToList();

            Dictionary<int, Producto> productosDict = new();

            if (productosIds.Any())
            {
                var productos = await _context.Productos
                    .Where(p => productosIds.Contains(p.Id) && p.TenantId == tenantId)
                    .ToListAsync();

                productosDict = productos.ToDictionary(p => p.Id);
            }

            // Copiar líneas de todos los albaranes
            int orden = 1;
            foreach (var albaran in albaranes.OrderBy(a => a.FechaEmision))
            {
                foreach (var lineaAlbaran in albaran.Lineas.OrderBy(l => l.Orden))
                {
                    // ⭐ MANTENER CONFIGURACIÓN FISCAL DEL ALBARÁN
                    decimal iva = lineaAlbaran.IvaPercentSnapshot;
                    decimal recargo = cliente.RegimenRecargoEquivalencia
                        ? lineaAlbaran.RePercentSnapshot
                        : 0m;

                    var lineaFactura = new LineaFactura
                    {
                        Orden = orden++,
                        Descripcion = $"[{albaran.Numero}] {lineaAlbaran.Descripcion}",
                        Cantidad = lineaAlbaran.Cantidad,
                        PrecioUnitario = lineaAlbaran.PrecioUnitario,
                        IvaPercentSnapshot = iva,
                        RePercentSnapshot = recargo,
                        TipoImpuestoId = lineaAlbaran.TipoImpuestoId,
                        ProductoId = lineaAlbaran.ProductoId
                    };

                    CalcularLineaFactura(lineaFactura);
                    factura.Lineas.Add(lineaFactura);
                }
            }

            // Calcular totales
            CalcularTotalesFactura(factura);
            ValidarTipoFacturaVerifactu(factura.TipoFacturaVERIFACTU);

            _logger.LogInformation(
                "Factura desde albaranes. Recargo: {Recargo}€, Retención: {Retencion}€",
                factura.TotalRecargo, factura.CuotaRetencion);

            // Obtener hash de la factura anterior
            var facturaAnterior = await _context.Facturas
                .Where(f => f.SerieId == dto.SerieId && f.TenantId == tenantId)
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            factura.HuellaAnterior = facturaAnterior?.Huella;

            // Aplicar VERIFACTU
            await AplicarVERIFACTUAsync(factura);

            _context.Facturas.Add(factura);
            await _context.SaveChangesAsync();

            // Vincular Albaranes con factura
            foreach (var albaran in albaranes)
            {
                albaran.FacturaId = factura.Id;
                albaran.Facturado = true;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Creada factura {NumeroFactura} desde albaranes [{Albaranes}]",
                numeroCompleto, string.Join(", ", albaranes.Select(a => a.Numero)));

            // Enviar a AEAT en background
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnviarFacturaAEATAsync(factura.Id, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al enviar factura {FacturaId} a AEAT", factura.Id);
                }
            });

            return await MapearAResponseDto(factura);
        }

        #region Métodos privados

        // ====================================================================
        // ⭐ CALCULAR TOTALES FACTURA - NORMATIVA ESPAÑOLA
        // ====================================================================
        private void CalcularTotalesFactura(Factura factura)
        {
            // Base Imponible = Σ(Cantidad × Precio) de todas las líneas
            factura.BaseImponible = factura.Lineas.Sum(l => l.BaseImponible);

            // Total IVA = Σ(Base línea × IVA% línea)
            factura.TotalIVA = Math.Round(
                factura.Lineas.Sum(l => l.ImporteIva), 2);

            // Total Recargo = Σ(Base línea × Recargo% línea)
            factura.TotalRecargo = Math.Round(
                factura.Lineas.Sum(l => l.ImporteRecargo), 2);

            // Cuota Retención = Base Imponible Total × Retención%
            factura.CuotaRetencion = Math.Round(
                factura.BaseImponible * (factura.PorcentajeRetencion ?? 0m) / 100, 2);

            // TOTAL FACTURA = Base + IVA + Recargo - Retención
            factura.Total = factura.BaseImponible
                          + factura.TotalIVA
                          + factura.TotalRecargo
                          - factura.CuotaRetencion ?? 0m;
        }

        private void CalcularLineaFactura(LineaFactura linea)
        {
            var subtotal = Math.Round(linea.Cantidad * linea.PrecioUnitario, 2);
            linea.BaseImponible = Math.Round(subtotal, 2);
            linea.ImporteIva = Math.Round(linea.BaseImponible * (linea.IvaPercentSnapshot / 100), 2);
            linea.ImporteRecargo = Math.Round(subtotal * (linea.RePercentSnapshot / 100), 2);
            linea.Importe = Math.Round(linea.BaseImponible + linea.ImporteIva + linea.ImporteRecargo, 2);
        }
        private async Task<List<TipoImpuesto>> ObtenerTiposImpuestoActivosAsync(int tenantId, DateTime fechaReferencia)
        {
            var cacheKey = $"tipos_impuesto:{tenantId}:{fechaReferencia:yyyy-MM-dd}";

            return await _cacheService.GetOrCreateAsync(
                cacheKey,
                async () => await _context.TiposImpuesto
                    .Where(t => t.TenantId == tenantId
                        && t.Activo
                        && (t.FechaInicio == null || t.FechaInicio <= fechaReferencia)
                        && (t.FechaFin == null || t.FechaFin >= fechaReferencia))
                    .OrderBy(t => t.Orden)
                    .ToListAsync(),
                TimeSpan.FromMinutes(60)
            ) ?? new List<TipoImpuesto>();
        }

        private (TipoImpuesto? TipoImpuesto, decimal Iva, decimal Recargo) ResolverTipoImpuesto(
            List<TipoImpuesto> tiposImpuestoActivos,
            int? tipoImpuestoId,
            Producto? producto)
        {
            TipoImpuesto? tipoImpuesto = null;

            if (tipoImpuestoId.HasValue)
            {
                tipoImpuesto = tiposImpuestoActivos.FirstOrDefault(t => t.Id == tipoImpuestoId.Value);
                if (tipoImpuesto == null)
                    throw new InvalidOperationException("Tipo de impuesto no válido o inactivo");
            }
            else if (producto?.TipoImpuestoId.HasValue == true)
            {
                tipoImpuesto = tiposImpuestoActivos.FirstOrDefault(t => t.Id == producto.TipoImpuestoId.Value);
                if (tipoImpuesto == null)
                    throw new InvalidOperationException("Tipo de impuesto del producto no válido o inactivo");
            }

            tipoImpuesto ??= tiposImpuestoActivos.First();

            return (tipoImpuesto, tipoImpuesto.PorcentajeIva, tipoImpuesto.PorcentajeRecargo);
        }

        private static readonly HashSet<string> TiposFacturaVerifactuPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            "F1",
            "F2",
            "F3",
            "R1",
            "R2",
            "R3",
            "R4"
        };

        private static string DeterminarTipoFacturaVerifactu(Cliente cliente)
        {
            if (cliente == null)
            {
                return "F2";
            }

            var tieneIdentificacion = !string.IsNullOrWhiteSpace(cliente.NIF)
                || !string.Equals(cliente.TipoCliente, "B2C", StringComparison.OrdinalIgnoreCase);

            return tieneIdentificacion ? "F1" : "F2";
        }

        private static void ValidarTipoFacturaVerifactu(string? tipoFactura)
        {
            if (string.IsNullOrWhiteSpace(tipoFactura) || !TiposFacturaVerifactuPermitidos.Contains(tipoFactura))
            {
                throw new InvalidOperationException(
                   $"TipoFacturaVERIFACTU inválido: {tipoFactura ?? "<vacío>"}.");
            }
        }

        // ====================================================================
        // APLICAR VERIFACTU
        // ====================================================================
        public async Task AplicarVERIFACTUAsync(Factura factura)
        {
            if (factura.Tenant == null)
            {
                factura.Tenant = await _context.Tenants.FindAsync(factura.TenantId);
            }

            if (factura.Cliente == null)
            {
                factura.Cliente = await _context.Clientes.FindAsync(factura.ClienteId);
            }

            if (factura.Serie == null)
            {
                factura.Serie = await _context.SeriesNumeracion.FindAsync(factura.SerieId);
            }

            _verifactuService.ProcesarFacturaVERIFACTU(factura, factura.HuellaAnterior ?? "");
        }


        // ====================================================================
        // ENVIAR FACTURA A AEAT (Background)
        // ====================================================================
        private async Task EnviarFacturaAEATAsync(int facturaId, int tenantId)
        {
            try
            {
                // Crear un nuevo Scope para evitar problemas con DBContext
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var verifactuService = scope.ServiceProvider.GetRequiredService<VERIFACTUService>();

                var factura = await context.Facturas
                    .Include(f => f.Tenant)
                    .Include(f => f.Cliente)
                    .Include(f => f.Serie)
                    .Include(f => f.Lineas)
                    .FirstOrDefaultAsync(f => f.Id == facturaId);

                if (factura == null)
                {
                    _logger.LogWarning("Factura {FacturaId} no encontrada para envío a AEAT", facturaId);
                    return;
                }

                // Enviar a AEAT
                var resultado = await verifactuService.ProcesarYEnviarFactura(facturaId);

                // Actualizar estado
                factura.EnviadaVERIFACTU = resultado;
                factura.FechaEnvioVERIFACTU = DateTime.UtcNow;

                await context.SaveChangesAsync();

                _logger.LogInformation("Factura {FacturaId} enviada a AEAT: {Estado}",
                    facturaId, resultado ? "OK" : "ERROR");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error crítico al enviar factura {FacturaId} a AEAT", facturaId);
            }
        }


        // ====================================================================
        // MAPEAR A RESPONSE DTO
        // ====================================================================
        private async Task<FacturaResponseDto> MapearAResponseDto(Factura factura)
        {
            // Cargar relaciones si no están cargadas
            if (factura.Cliente == null)
            {
                factura.Cliente = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Id == factura.ClienteId && c.TenantId == factura.TenantId);
            }

            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == factura.SerieId && s.TenantId == factura.TenantId);

            // Cargar albaranes si existen
            var albaranesIds = await _context.Albaranes
                .Where(a => a.FacturaId == factura.Id)
                .Select(a => a.Id)
                .ToListAsync();

            return new FacturaResponseDto
            {
                Id = factura.Id,
                TenantId = factura.TenantId,
                ClienteId = factura.ClienteId,
                ClienteNombre = factura.Cliente?.Nombre,
                ClienteNIF = factura.Cliente?.NIF,
                SerieId = factura.SerieId,
                SerieCodigo = serie?.Codigo,
                Numero = factura.Numero,
                Ejercicio = factura.Ejercicio,
                FechaEmision = factura.FechaEmision,

                // ⭐ TOTALES ACTUALIZADOS
                BaseImponible = factura.BaseImponible,
                TotalIVA = factura.TotalIVA,
                TotalRecargo = factura.TotalRecargo ?? 0m, // ⭐ NUEVO
                PorcentajeRetencion = factura.PorcentajeRetencion ?? 0m, // ⭐ NUEVO
                CuotaRetencion = factura.CuotaRetencion ?? 0m, // ⭐ NUEVO
                Total = factura.Total,

                Estado = factura.Estado,
                Bloqueada = factura.Bloqueada, // ⭐ NUEVO
                Observaciones = factura.Observaciones,

                Huella = factura.Huella,
                HuellaAnterior = factura.HuellaAnterior,
                EnviadaVERIFACTU = factura.EnviadaVERIFACTU,
                FechaEnvioVERIFACTU = factura.FechaEnvioVERIFACTU,
                TipoFacturaVERIFACTU = factura.TipoFacturaVERIFACTU,
                UrlVERIFACTU = factura.UrlVERIFACTU,
                QRBase64 = factura.QRVerifactu != null
                    ? Convert.ToBase64String(factura.QRVerifactu)
                    : null,
                NumeroFacturaRectificada = factura.NumeroFacturaRectificada,
                TipoRectificacion = factura.TipoRectificacion,
                AlbaranesIds = albaranesIds,

                Lineas = factura.Lineas.Select(l => new LineaFacturaResponseDto
                {
                    Id = l.Id,
                    Orden = l.Orden,
                    ProductoId = l.ProductoId,
                    ProductoCodigo = l.Producto?.Codigo,
                    Descripcion = l.Descripcion,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    PorcentajeDescuento = 0,
                    ImporteDescuento = 0,
                    BaseImponible = l.BaseImponible,
                    //IVA = l.IvaPercentSnapshot,
                    RecargoEquivalencia = l.RePercentSnapshot, // ⭐ NUEVO
                    ImporteIva = l.ImporteIva,
                    ImporteRecargo = l.ImporteRecargo, // ⭐ NUEVO
                    Importe = l.Importe,
                    TipoImpuestoId = l.TipoImpuestoId
                }).OrderBy(l => l.Orden).ToList(),

                FechaCreacion = DateTime.UtcNow,
                FechaModificacion = factura.ActualizadoEn // ⭐ NUEVO
            };
        }

        #endregion
    }
}
