using API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Services;
using FacturacionVERIFACTU.API.DTOs;
using FacturacionVERIFACTU.API.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.SymbolStore;
using System.Xml.Linq;


namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IAlbaranService
    {
        Task<AlbaranResponseDto> CrearAlbaranAsync(int tenantId, AlbaranCreateDto dto);
        Task<AlbaranResponseDto> ActualizarAlbaranAsync(int tenantId, int id, AlbaranUpdateDto dto);
        Task<AlbaranResponseDto> CambiarEstadoAsync(int tenantId, int id, CambiarEstadoAlbaranDto dto);
        Task<AlbaranResponseDto> ObtenerPorIdAsync(int tenantId, int id);
        Task<List<AlbaranResponseDto>> ObtenerTodosAsync(int tenantId, string? estado = null, int? ejercicio = null);
        Task<bool> EliminarAsync(int tenantId, int id);
        Task<AlbaranResponseDto> ConvertirDesdePresupuesto(int tenantId, int presupuestoId, ConvertirPresupuestoDto dto);
    }

    public class AlbaranService : IAlbaranService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISerieNumeracionService _numeracionService;
        private readonly ILogger<AlbaranService> _logger;

        public AlbaranService(ApplicationDbContext context, ISerieNumeracionService numeracionService, ILogger<AlbaranService> logger)
        {
            _context = context;
            _numeracionService = numeracionService;
            _logger = logger;
        }

        public async Task<AlbaranResponseDto> CrearAlbaranAsync(int tenantId, AlbaranCreateDto dto)
        {
            //Validar si el cliente existe
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.TenantId == tenantId);

            if (cliente == null)
                throw new InvalidOperationException($"Cliente {dto.ClienteId} no encontrado");

            //Validar serie
            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == dto.SerieId && s.TenantId == tenantId);
            if (serie == null)
                throw new InvalidOperationException($"Serie {dto.SerieId} no encontrada");

            //Obtener el siguiente numero
            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serie.Codigo, ejercicio, DocumentTypes.ALBARAN);

            //Crear albaran
            var albaran = new Albaran
            {
                TenantId = tenantId,
                ClienteId = dto.ClienteId,
                SerieId = dto.SerieId,
                PresupuestoId = dto.PresupuestoId,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                FechaEntrega = dto.FechaEntrega?.ToUniversalTime(),
                Estado = "Pendiente",
                DireccionEntrega = dto.DireccionEntrega,
                Observaciones = dto.Observaciones,
                FechaCreacion = DateTime.UtcNow
            };

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

            //Agregar lineas con sistema de tipos de impuesto
            var productosDict = await ObtenerProductosAsync(
                lineasFiltradas.Where(l => l.ProductoId.HasValue).Select(l => l.ProductoId!.Value),
                tenantId);
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

                var linea = new LineaAlbaran
                {
                    Orden = orden++,
                    Descripcion = lineaDto.Descripcion,
                    Cantidad = lineaDto.Cantidad,
                    PrecioUnitario = lineaDto.PrecioUnitario,
                    PorcentajeDescuento = lineaDto.PorcentajeDescuento,
                    IvaPercentSnapshot = iva,
                    RePercentSnapshot = recargo,
                    TipoImpuestoId = tipoImpuesto.Id,
                    ProductoId = lineaDto.ProductoId,
                };

                CalcularLinea(linea);
                albaran.Lineas.Add(linea);
            }

            CalcularTotalesAlbaran(albaran);

            _context.Albaranes.Add(albaran);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Creado albaran {numero} para tenant {TenantId}",
                numeroCompleto, tenantId);

            return await MapearAResponseDto(albaran);
        }

        public async Task<AlbaranResponseDto> ActualizarAlbaranAsync(int tenantId, int id, AlbaranUpdateDto dto)
        {
            var albaran = await _context.Albaranes
                .Include(a => a.Lineas)
                .Include(a => a.Cliente)
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);

            if (albaran == null)
                throw new InvalidOperationException($"Albaran {id} no encontrado");

            //Solo se pueden editar albaranes en estado pendiente
            if (albaran.Estado != "Pendiente")
                throw new InvalidOperationException($"No se puede modificar un albaran en estado {albaran.Estado}");

            // ⭐ SI SE CAMBIÓ EL CLIENTE, RECARGAR
            Cliente clienteActual = albaran.Cliente;

            if (dto.ClienteId != albaran.ClienteId)
            {
                clienteActual = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Id == dto.ClienteId && c.TenantId == tenantId);

                if (clienteActual == null)
                    throw new InvalidOperationException($"Cliente {dto.ClienteId} no encontrado");

                albaran.ClienteId = dto.ClienteId;

                _logger.LogInformation(
                    "Albaran {Id}: Cliente cambiado a {Cliente}",
                    id, clienteActual.Nombre);
            }

            //Actualizar datos
            albaran.FechaEmision = (dto.FechaEmision ?? albaran.FechaEmision).ToUniversalTime();
            albaran.FechaEntrega = dto.FechaEntrega?.ToUniversalTime();
            albaran.DireccionEntrega = dto.DireccionEntrega;
            albaran.Observaciones = dto.Observaciones;
            albaran.FechaModificacion = DateTime.UtcNow;

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

            //Actualizacion inteligente de lineas
            var lineasDtoIds = lineasFiltradas
                .Where(l => l.Id.HasValue)
                .Select(l => l.Id.Value)
                .ToList();

            var lineasAEliminar = albaran.Lineas
                .Where(l => !lineasDtoIds.Contains(l.Id))
                .ToList();

            foreach (var lineaAEliminar in lineasAEliminar)
            {
                albaran.Lineas.Remove(lineaAEliminar);
                _context.LineasAlbaranes.Remove(lineaAEliminar);
            }

            var productosDict = await ObtenerProductosAsync(
                lineasFiltradas.Where(l => l.ProductoId.HasValue).Select(l => l.ProductoId!.Value),
                tenantId);
            var fechaReferencia = (dto.FechaEmision ?? albaran.FechaEmision).ToUniversalTime();
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
                    //Actualizar linea existente
                    var lineaExistente = albaran.Lineas
                        .FirstOrDefault(l => l.Id == lineaDto.Id.Value);

                    if (lineaExistente != null)
                    {
                        lineaExistente.Orden = orden++;
                        lineaExistente.Descripcion = lineaDto.Descripcion;
                        lineaExistente.Cantidad = lineaDto.Cantidad;
                        lineaExistente.PrecioUnitario = lineaDto.PrecioUnitario;
                        lineaExistente.PorcentajeDescuento = lineaDto.PorcentajeDescuento;
                        lineaExistente.IvaPercentSnapshot = iva;
                        lineaExistente.RePercentSnapshot = recargo;
                        lineaExistente.TipoImpuestoId = tipoImpuesto.Id;
                        lineaExistente.ProductoId = lineaDto.ProductoId;

                        CalcularLinea(lineaExistente);
                    }
                }
                else
                {
                    //Crear linea nueva
                    var lineaNueva = new LineaAlbaran
                    {
                        AlbaranId = albaran.Id,
                        Orden = orden++,
                        Descripcion = lineaDto.Descripcion,
                        Cantidad = lineaDto.Cantidad,
                        PrecioUnitario = lineaDto.PrecioUnitario,
                        PorcentajeDescuento = lineaDto.PorcentajeDescuento,
                        IvaPercentSnapshot = iva,
                        RePercentSnapshot = recargo,
                        TipoImpuestoId = tipoImpuesto.Id,
                        ProductoId = lineaDto.ProductoId
                    };

                    CalcularLinea(lineaNueva);
                    albaran.Lineas.Add(lineaNueva);
                }
            }

            //Recalcular totales
            CalcularTotalesAlbaran(albaran);

            await _context.SaveChangesAsync();

            _logger.LogInformation("Actualizado albaran {Numero} para Tenant {TenantId}",
                albaran.Numero, tenantId);

            return await MapearAResponseDto(albaran);
        }

        public async Task<AlbaranResponseDto> CambiarEstadoAsync(int tenantId, int id, CambiarEstadoAlbaranDto dto)
        {
            var albaran = await _context.Albaranes
                .Include(a => a.Lineas)
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);

            if (albaran == null)
                throw new InvalidOperationException($"Albaran {id} no encontrado");

            //Validar transicion de estado
            ValidarTransicionEstado(albaran.Estado, dto.NuevoEstado);

            var estadoAnterior = albaran.Estado;
            albaran.Estado = dto.NuevoEstado;
            albaran.FechaModificacion = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cambiado estado de albaran {Numero} de {EstadoAnterior} a {EstadoNuevo}",
                albaran.Numero, estadoAnterior, dto.NuevoEstado);

            return await MapearAResponseDto(albaran);
        }


        public async Task<AlbaranResponseDto> ObtenerPorIdAsync(int tenantId, int id)
        {
            var albaran = await _context.Albaranes
                .Include(a => a.Lineas)
                .Include(a => a.Cliente)
                .Include(a => a.Presupuesto)
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);

            if (albaran == null) return null;

            return await MapearAResponseDto(albaran);
        }

        public async Task<List<AlbaranResponseDto>> ObtenerTodosAsync(int tenantId, string? estado = null, int? ejercicio = null)
        {
            var query = _context.Albaranes
                .Where(a => a.TenantId == tenantId)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(estado))
                query = query.Where(a => a.Estado == estado);

            if (ejercicio.HasValue)
                query = query.Where(a => a.Ejercicio == ejercicio.Value);

            return await query
                .OrderByDescending(a => a.FechaEmision)
                .Select(a => new AlbaranResponseDto
                {
                    Id = a.Id,
                    TenantId = a.TenantId,
                    ClienteId = a.ClienteId,
                    ClienteNombre = a.Cliente.Nombre,
                    SerieId = a.SerieId,
                    SerieCodigo = a.Serie.Codigo,
                    PresupuestoId = a.PresupuestoId,
                    PresupuestoNumero = a.Presupuesto.Numero,
                    Numero = a.Numero,
                    Ejercicio = a.Ejercicio,
                    FechaEmision = a.FechaEmision,
                    FechaEntrega = a.FechaEntrega,
                    BaseImponible = a.BaseImponible,
                    TotalIVA = a.TotalIVA,
                    TotalRecargo = a.TotalRecargo,
                    Total = a.Total,
                    Estado = a.Estado,
                    DireccionEntrega = a.DireccionEntrega,
                    Observaciones = a.Observaciones,
                    Facturado = a.Facturado,
                    FacturaId = a.FacturaId,
                    Lineas = new List<LineaAlbaranResponseDto>(), // vacío en listado
                    FechaCreacion = a.FechaCreacion,
                    FechaModificacion = a.FechaModificacion
                })
                .ToListAsync();
        }


        public async Task<bool> EliminarAsync(int tenantId, int id)
        {
            var albaran = await _context.Albaranes
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);

            if (albaran == null) return false;

            //Solo se pueden eliminar albaranes en estado pendiente
            if (albaran.Estado != "Pendiente")
                throw new InvalidOperationException($"No se puede eliminar un albaran en estado {albaran.Estado}");

            _context.Albaranes.Remove(albaran);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Eliminado albaran {Numero} del tenant {TenantId}",
                albaran.Id, tenantId);

            return true;

        }

        public async Task<AlbaranResponseDto> ConvertirDesdePresupuesto(int tenantId, int presupuestoId, ConvertirPresupuestoDto dto)
        {
            //Obtener presupuesto CON CLIENTE
            var presupuesto = await _context.Presupuestos
                .Include(p => p.Lineas)
                .Include(p => p.Cliente)
                .FirstOrDefaultAsync(p => p.Id == presupuestoId && p.TenantId == tenantId);

            if (presupuesto == null)
                throw new InvalidOperationException($"Presupuesto {presupuestoId} no encontrado");

            //Presupuesto debe estar aceptado
            if (presupuesto.Estado != "Aceptado")
                throw new InvalidOperationException($"Solo se pueden convertir presupuestos en estado Aceptado. Estado actual: {presupuesto.Estado}");

            //Obtener serie para albaranes
            var serieAlbaran = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s =>
                s.TenantId == tenantId &&
                s.TipoDocumento == DocumentTypes.ALBARAN &&
                s.Activo);

            if (serieAlbaran == null)
                throw new InvalidOperationException("No hay series activas para albaranes");

            // ⭐ CARGAR CONFIGURACIÓN FISCAL DEL CLIENTE
            var cliente = presupuesto.Cliente;

            //Obtener siguiente numero
            var ejercicio = dto.FechaEmision?.Year ?? DateTime.UtcNow.Year;
            var (numeroCompleto, numero) = await _numeracionService
                .ObtenerSiguienteNumeroAsync(tenantId, serieAlbaran.Codigo, ejercicio, DocumentTypes.ALBARAN);

            //Crear albaran
            var albaran = new Albaran
            {
                TenantId = tenantId,
                ClienteId = presupuesto.ClienteId,
                SerieId = serieAlbaran.Id,
                PresupuestoId = presupuesto.Id,
                Numero = numeroCompleto,
                Ejercicio = ejercicio,
                FechaEmision = (dto.FechaEmision ?? DateTime.UtcNow).ToUniversalTime(),
                FechaEntrega = dto.FechaEntrega?.ToUniversalTime(),
                Estado = "Pendiente",
                DireccionEntrega = dto.DireccionEntrega ?? presupuesto.Cliente?.Direccion,
                Observaciones = dto.Observaciones ?? presupuesto.Observaciones,
                FechaCreacion = DateTime.UtcNow
            };

            //Copiar lineas del presupuesto
            var lineasACopiar = presupuesto.Lineas.AsEnumerable();

            //Filtrar lineas seleccionadas si se especifican
            if (dto.LineasSeleccionadas != null && dto.LineasSeleccionadas.Any())
            {
                lineasACopiar = lineasACopiar.Where(l => dto.LineasSeleccionadas.Contains(l.Id));
            }

            int orden = 1;
            foreach (var lineaPresupuesto in lineasACopiar.OrderBy(l => l.Orden))
            {
                // ⭐ MANTENER CONFIGURACIÓN FISCAL DEL PRESUPUESTO usando snapshots
                decimal iva = lineaPresupuesto.IvaPercentSnapshot;
                decimal recargo = cliente.RegimenRecargoEquivalencia
                    ? lineaPresupuesto.RePercentSnapshot
                    : 0m;

                var lineaAlbaran = new LineaAlbaran
                {
                    Orden = orden++,
                    Descripcion = lineaPresupuesto.Descripcion,
                    Cantidad = lineaPresupuesto.Cantidad,
                    PrecioUnitario = lineaPresupuesto.PrecioUnitario,
                    PorcentajeDescuento = lineaPresupuesto.PorcentajeDescuento,
                    IvaPercentSnapshot = iva,
                    RePercentSnapshot = recargo,
                    TipoImpuestoId = lineaPresupuesto.TipoImpuestoId,
                    ProductoId = lineaPresupuesto.ProductoId
                };

                CalcularLinea(lineaAlbaran);
                albaran.Lineas.Add(lineaAlbaran);
            }

            //Calcular totales
            CalcularTotalesAlbaran(albaran);

            _context.Albaranes.Add(albaran);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Creado albaran {NumeroAlbaran} desde presupuesto {NumeroPresupuesto} para tenant {TenantId}",
                numeroCompleto, presupuesto.Numero, tenantId);

            return await MapearAResponseDto(albaran);
        }


        #region Metodos privados

        private void CalcularLinea(LineaAlbaran linea)
        {
            var subtotal = Math.Round(linea.Cantidad * linea.PrecioUnitario, 2);
            linea.ImporteDescuento = Math.Round(subtotal * (linea.PorcentajeDescuento / 100), 2);
            linea.BaseImponible = Math.Round(subtotal - linea.ImporteDescuento, 2);
            linea.ImporteIva = Math.Round(linea.BaseImponible * (linea.IvaPercentSnapshot / 100), 2);
            linea.ImporteRecargo = Math.Round(linea.BaseImponible * (linea.RePercentSnapshot / 100), 2);
            linea.Importe = Math.Round(linea.BaseImponible + linea.ImporteIva + linea.ImporteRecargo, 2);
        }

        private void CalcularTotalesAlbaran(Albaran albaran)
        {
            albaran.BaseImponible = Math.Round(albaran.Lineas.Sum(l => l.BaseImponible), 2);
            albaran.TotalIVA = Math.Round(albaran.Lineas.Sum(l => l.ImporteIva), 2);
            albaran.TotalRecargo = Math.Round(albaran.Lineas.Sum(l => l.ImporteRecargo), 2);

            // Los albaranes NO tienen retención
            albaran.Total = Math.Round(albaran.BaseImponible + albaran.TotalIVA + albaran.TotalRecargo, 2);
        }

        private async Task<List<TipoImpuesto>> ObtenerTiposImpuestoActivosAsync(int tenantId, DateTime fechaReferencia)
        {
            return await _context.TiposImpuesto
                .Where(t => t.TenantId == tenantId
                    && t.Activo
                    && (t.FechaInicio == null || t.FechaInicio <= fechaReferencia)
                    && (t.FechaFin == null || t.FechaFin >= fechaReferencia))
                .OrderBy(t => t.Orden.HasValue ? 0 : 1)
                .ThenBy(t => t.Orden)
                .ThenBy(t => t.Id)
                .ToListAsync();
        }

        private (TipoImpuesto? TipoImpuesto, decimal Iva, decimal Recargo) ResolverTipoImpuesto(
            List<TipoImpuesto> tiposImpuestoActivos,
            int? tipoImpuestoId,
            Producto? producto)
        {
            if (!tiposImpuestoActivos.Any())
            {
                throw new InvalidOperationException("Nohay tipos de impuesto vigentes");
            }
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

            var iva = tipoImpuesto?.PorcentajeIva
                ?? 0m;

            tipoImpuesto ??= tiposImpuestoActivos.First();

            return (tipoImpuesto, tipoImpuesto.PorcentajeIva, tipoImpuesto.PorcentajeRecargo);
        }

        private void ValidarTransicionEstado(string estadoActual, string nuevoEstado)
        {
            var transicionesPermitidas = new Dictionary<string, List<string>>
            {
                {"Pendiente", new List<string>{"Entregado", "Anulado"} },
                {"Entregado", new List<string>{"Facturado", "Anulado"} },
                {"Aceptado", new List<string>{"Facturado"} },
                {"Facturado", new List<string> ()}, //Estado final
                {"Anulado", new List<string>() } //Estado final
            };

            if (!transicionesPermitidas.ContainsKey(estadoActual))
                throw new InvalidOperationException($"Estado actual '{estadoActual}' no valido");

            if (!transicionesPermitidas[estadoActual].Contains(nuevoEstado))
                throw new InvalidOperationException($"No se puede cambiar de '{estadoActual}' a '{nuevoEstado}'");
        }

        private async Task<AlbaranResponseDto> MapearAResponseDto(Albaran albaran)
        {
            if (albaran.Cliente == null)
            {
                albaran.Cliente = await _context.Clientes
                    .FirstOrDefaultAsync(c => c.Id == albaran.ClienteId && c.TenantId == albaran.TenantId);
            }

            var serie = await _context.SeriesNumeracion
                .FirstOrDefaultAsync(s => s.Id == albaran.SerieId && s.TenantId == albaran.TenantId);

            return new AlbaranResponseDto
            {
                Id = albaran.Id,
                TenantId = albaran.TenantId,
                ClienteId = albaran.ClienteId,
                ClienteNombre = albaran.Cliente?.Nombre,
                SerieId = albaran.SerieId,
                SerieCodigo = serie?.Codigo,
                PresupuestoId = albaran.PresupuestoId,
                PresupuestoNumero = albaran.Presupuesto?.Numero,
                Numero = albaran.Numero,
                Ejercicio = albaran.Ejercicio,
                FechaEmision = albaran.FechaEmision,
                FechaEntrega = albaran.FechaEntrega,
                BaseImponible = albaran.BaseImponible,
                TotalIVA = albaran.TotalIVA,
                TotalRecargo = albaran.TotalRecargo,
                Total = albaran.Total,
                Estado = albaran.Estado,
                DireccionEntrega = albaran.DireccionEntrega,
                Observaciones = albaran.Observaciones,
                Facturado = albaran.Facturado,
                FacturaId = albaran.FacturaId,
                Lineas = albaran.Lineas.Select(l => new LineaAlbaranResponseDto
                {
                    Id = l.Id,
                    Orden = l.Orden,
                    Descripcion = l.Descripcion,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.PrecioUnitario,
                    PorcentajeDescuento = l.PorcentajeDescuento,
                    ImporteDescuento = l.ImporteDescuento,
                    BaseImponible = l.BaseImponible,
                    IVA = l.IvaPercentSnapshot,
                    ImporteIva = l.ImporteIva,
                    RecargoEquivalencia = l.RePercentSnapshot,
                    ImporteRecargo = l.ImporteRecargo,
                    Importe = l.Importe,
                    TipoImpuestoId = l.TipoImpuestoId,
                    ProductoId = l.ProductoId
                }).OrderBy(l => l.Orden).ToList(),
                FechaCreacion = albaran.FechaCreacion,
                FechaModificacion = albaran.FechaModificacion
            };
        }

        private async Task<Dictionary<int, Producto>> ObtenerProductosAsync(IEnumerable<int> productosIds, int tenantId)
        {
            var ids = productosIds.Distinct().ToList();
            if (!ids.Any())
            {
                return new Dictionary<int, Producto>();
            }

            return await _context.Productos
                .Where(p => ids.Contains(p.Id) && p.TenantId == tenantId)
                .ToDictionaryAsync(p => p.Id);
        }

        #endregion
    }
}
