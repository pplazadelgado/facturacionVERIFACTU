using Microsoft.EntityFrameworkCore;
using FacturacionVERIFACTU.API.Data;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Interfaces;
using FacturacionVERIFACTU.API.DTOs;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;


namespace FacturacionVERIFACTU.API.Data.Services
{
    public class CierreEjercicioService : ICierreEjercicioService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<CierreEjercicioService> _logger;

        public CierreEjercicioService(
            ApplicationDbContext context,
            ILogger<CierreEjercicioService> logger)
        {
            _context = context;
            _logger = logger;
        }

        #region Metodos publicos de la interface

        ///<summary>
        ///Obtiene estadidsticas del ejercicio antes de cerrar
        /// </summary>
        public async Task<EstadisticasCierreDTO> ObtenerEstadisticasEjercicio(int ejercicio, int tenantId)
        {
            var facturas = await _context.Facturas
                .Where(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId)
                .ToListAsync();

            var estadisticas = new EstadisticasCierreDTO
            {
                Ejercicio = ejercicio,
                TotalFacturas = facturas.Count,
                FacturasEnviadas = facturas.Count(f => f.EnviadaVERIFACTU),
                FacturasPendientes = facturas.Count(f => !f.EnviadaVERIFACTU),
                TotalBase = facturas.Sum(f => f.BaseImponible),
                TotalIVA = facturas.Sum(f=> f.TotalIVA),
                TotalRecargo = facturas.Sum(f => f.CuotaRecargo),
                TotalRetencion = facturas.Sum(f => f.CuotaRetencion ?? 0),
                TotalGeneral = facturas.Sum(f => f.Total)
            };

            //Resumen trimestral
            estadisticas.ResumenTrimestral = facturas
                .GroupBy(f => (f.FechaEmision.Month - 1) / 3 + 1)
                .Select(g => new ResumenTrimestreDTO
                {
                    Trimestre = g.Key,
                    NumFacturas = g.Count(),
                    BaseImponible = g.Sum(f => f.BaseImponible),
                    TotalIva = g.Sum(f => f.TotalIVA),
                    Recargo = g.Sum(f => f.CuotaRecargo),
                    Total = g.Sum(f => f.Total)
                })
                .OrderBy(r => r.Trimestre)
                .ToList();

            return estadisticas;
        }

        ///<summary>
        ///Cierra el ejercicio fiscal
        /// </summary>
        public async Task<(bool existo, string mensaje, CierreRealizadoDTO resultado)> CerrarEjercicio(
            int ejercicio, int tenantId, int usuarioId)
        {
            try
            {
                _logger.LogInformation($"Incicioando cierre del ejercicio {ejercicio} - Tenant {tenantId}");


                // 1. Validar
                var (esValido, mensajeValidacion) = await ValidarCierre(ejercicio, tenantId);
                if (!esValido)
                {
                    return (false, mensajeValidacion, null);
                }

                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    //2. Bloquear facturas
                    await BloquearFacturasEjercicio(ejercicio, tenantId);

                    //3. Calcular estadisticas
                    var cierre = await CalcularEstadisticasCierre(ejercicio, tenantId, usuarioId);

                    //4.Generar archivos
                    cierre.RutaLibroFacturas = await GenerarLibroFacturas(ejercicio, tenantId);
                    cierre.RutaResumenIVA = await GenerarResumenIVA(ejercicio, tenantId);

                    //5. Guardar cierre
                    _context.CierreEjercicios.Add(cierre);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();

                    _logger.LogInformation($"Ejercicio {ejercicio} cerrado exitosamente: Id cierre: {cierre.Id}");

                    var mensaje = $"Ejercicio {ejercicio} cerrado exitosamente. {cierre.TotalFacturas} facturas bloqueadas";
                    var resultado = MapearACierreRealizadoDTO(cierre, mensaje);

                    return (true, mensaje, resultado);
                }
                catch(Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, $"Error al cerrar ejercicio {ejercicio}");
                return (false, $"Error al cerrar ejercicio: {ex.Message}", null);
            }
        }


        ///<summary>
        ///Reabre ejercicio cerrado
        /// </summary>
        public async Task<(bool existo, string mensaje)> ReabrirEjercicio(
            int cierreId, string motivo, int usuarioId)
        {
            try
            {
                var cierre = await _context.CierreEjercicios
                    .FirstOrDefaultAsync(c => c.Id == cierreId);

                if (cierre == null)
                    return (false, "Cierre no encontrado");

                if (cierre.EstaAbierto)
                    return (false, "El ejercicio ya esta abierto");

                if (string.IsNullOrEmpty(motivo))
                    return (false, "Debe especificar un motivo para la reapertura");

                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    //1. Desbloquear factuas
                    var facturas = await _context.Facturas
                        .Where(f => f.FechaEmision.Year == cierre.Ejercicio && f.TenantId == cierre.TenantId)
                        .ToListAsync();

                    foreach( var factura in facturas)
                    {
                        factura.Bloqueada = false;
                        factura.ActualizadoEn = DateTime.UtcNow;
                    }

                    //2. Actualizar cierre
                    cierre.EstaAbierto = true;
                    cierre.MotivoReapertura = motivo;
                    cierre.FechaReapertura = DateTime.UtcNow;
                    cierre.UsuarioReaperturaId = usuarioId;
                    cierre.ActualizadoEn = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _logger.LogWarning(
                        $"Ejercicio {cierre.Ejercicio} reabierto por usuario {usuarioId}. " +
                        $"Motivo: {motivo}. {facturas.Count} facturas desbloqueadas");

                    return (true, $"Ejercicio {cierre.Ejercicio} reabierto correctamente. {facturas.Count} facturas desbloqueadas correctamente");

                }
                catch(Exception ex)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, $"Error al reabrir cierre {cierreId}");
                return (false, $"Error al reabrir ejercicio: {ex.Message}");
            }
        }


        ///<summary>
        ///Obtiene historial de cierres con DTOs
        /// </summary>
        public async Task<CierreEjercicioDTO> ObtenerCierreDTO(int cierreId, int tenantId)
        {
            var cierre = await _context.CierreEjercicios
                .Include(c => c.Usuario)
                .Include(c => c.UsuarioReapertura)
                .FirstOrDefaultAsync(c => c.Id == cierreId && c.TenantId == tenantId);

            return cierre != null ? MapearACierreDTO(cierre) : null;
        }

        /// <summary>
        /// Devuelve los años que tienen facturas registradas para el tenant.
        /// Si no hay ninguno, incluye el año actual como mínimo.
        /// </summary>
        public async Task<List<int>> ObtenerEjerciciosDisponiblesAsync(int tenantId)
        {
            var ejercicios = await _context.Facturas
                .Where(f => f.TenantId == tenantId)
                .Select(f => f.FechaEmision.Year)
                .Distinct()
                .OrderByDescending(a => a)
                .ToListAsync();

            // Si no hay facturas todavía, devolver el año actual como mínimo
            if (!ejercicios.Any())
                ejercicios.Add(DateTime.UtcNow.Year);

            return ejercicios;
        }


        ///<summary>
        ///Obtiene la entidad completa para uso interno 
        /// </summary>
        public async Task<CierreEjercicio> ObtenerCierreEntidad (int cierreId, int tenantId)
        {
            return await _context.CierreEjercicios
                .FirstOrDefaultAsync(c => c.Id == cierreId && c.TenantId == tenantId);
        }

        /// <summary>
        /// Obtiene un historial paginado de los cierres de ejercicio.
        /// </summary>
        public async Task<PaginatedResponseDto<CierreEjercicioDTO>> ObtenerHistorialCierresDTO(int tenantId, int page, int pageSize, int? ejercicio = null)
        {
            var query = _context.CierreEjercicios
                .Include(c => c.Usuario)
                .Include(c => c.UsuarioReapertura)
                .Where(c => c.TenantId == tenantId)
                .AsNoTracking();

            if (ejercicio.HasValue)
            {
                query = query.Where(c => c.Ejercicio == ejercicio.Value);
            }

            var totalItems = await query.CountAsync();

            var cierres = await query
                .OrderByDescending(c => c.Ejercicio)
                .ThenByDescending(c => c.FechaCierre)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var cierreDtos = cierres.Select(MapearACierreDTO).ToList();

            return new PaginatedResponseDto<CierreEjercicioDTO>
            {
                Items = cierreDtos,
                TotalItems = totalItems,
                Page = page,
                PageSize = pageSize
            };
        }

        #endregion

        #region validaciones

        ///<sumary>
        ///Valida que el ejercicio puede cerrarse
        /// </sumary>
        public async Task<(bool esValido, string mensaje)> ValidarCierre(int ejercicio, int tenantId)
        {
            //1. Verifica que no esta ya cerrado
            var cierreExiste = await _context.CierreEjercicios
                .FirstOrDefaultAsync(c => c.Ejercicio == ejercicio  && c.TenantId == tenantId && !c.EstaAbierto);

            if (cierreExiste != null)
                return (false, $"El ejercicio {ejercicio} ya esta cerrado");

            // 2. Verficar que todas las facturas estan enviadas a VERIFACTU
            var facturasPendientes = await _context.Facturas
                .Where(f => f.FechaEmision.Year == ejercicio
                        && f.TenantId == tenantId
                        && f.EnviadaVERIFACTU == false)
                .CountAsync();

            if (facturasPendientes > 0)
            {
                return (false, $"Hay {facturasPendientes} fatura(s) sin enviar a VERIFACTU");
            }

            //3. Verifcar que hay al menos una factura
            var totalFacturas = await _context.Facturas
                .CountAsync(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId);

            if (totalFacturas == 0)
                return (false, $"No hay facturas registradas en el ejercicio {ejercicio}");

            return (true, "Validacion exitosa");
        }

        #endregion

        #region Operaciones sobre facturas

        ///<summary>
        /// Bloquea todas las facturas del ejercicio
        /// </summary>
        private async Task BloquearFacturasEjercicio(int ejercicio, int tenantId)
        {
            var facturas = await _context.Facturas
                .Where(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId)
                .ToListAsync();

            foreach(var factura in facturas)
            {
                factura.Bloqueada = true;
                factura.ActualizadoEn = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation($"Bloqueadas {facturas.Count} facturas del ejercicio {ejercicio}");
        }

        ///<summary>
        /// Calcula las estadisticas para el cierre
        /// </summary>
        private async Task<CierreEjercicio> CalcularEstadisticasCierre(
            int ejercicio, int tenantId, int usuarioId)
        {
            var facturas = await _context.Facturas
                .Where(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId)
                .ToListAsync();

            //Calcular hash final (ultima factura del ejercicio)
            var ultimaFactura = facturas
                .OrderByDescending(f => f.FechaEmision)
                .ThenByDescending(f => f.Numero)
                .FirstOrDefault();

            var cierre = new CierreEjercicio
            {
                TenantId = tenantId,
                Ejercicio = ejercicio,
                FechaCierre = DateTime.UtcNow,
                UsuarioId = usuarioId,
                HashFinal = ultimaFactura?.Huella ?? "",
                TotalFacturas = facturas.Count,
                TotalBaseImponible = facturas.Sum(f => f.BaseImponible),
                TotalIVA = facturas.Sum(f => f.TotalIVA),
                TotalRecargo = facturas.Sum(f => f.CuotaRecargo),
                TotalRetencion = facturas.Sum(f => f.CuotaRetencion ?? 0m),
                TotalImporte = facturas.Sum(f => f.Total),
                EnviadoVERIFACTU = false,
                EstaAbierto = false

            };

            return cierre;
        }

        #endregion


        #region Generacion de archivos Excel

        ///<summary>
        ///G Genera el libro de facturas en Excel
        /// </summary>
        private async Task<string> GenerarLibroFacturas (int ejercicio, int tenantId)
        {
            var facturas = await _context.Facturas
                .Include(f => f.Cliente)
                .Include(f => f.Serie)
                .Where(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId)
                .OrderBy(f => f.FechaEmision)
                .ThenBy(f => f.Numero)
                .ToListAsync();

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add($"Facturas {ejercicio}");

            //Encabezados
            var headers = new[]
            {
                "Fecha", "Serie", "Numero", "Cliente", "NIF/CIF",
                "Base Imponible", "IVA", "Recargo", "Retencion", "Total",
                "Tipo Factura", "Estado VERIFACTU", "HUELLA"
            };

            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i +1].Value =headers[i];
                worksheet.Cells[1, i + 1].Style.Font.Bold = true;
                worksheet.Cells[1, i + 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[1, i + 1].Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                worksheet.Cells[1, i + 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
            }

            //Datos
            int row = 2;
            foreach(var factura in facturas)
            {
                worksheet.Cells[row, 1].Value = factura.FechaEmision.ToString("dd/MM/yyyy");
                worksheet.Cells[row, 2].Value = factura.Serie?.Descripcion ?? "";
                worksheet.Cells[row, 3].Value = factura.Numero;
                worksheet.Cells[row, 4].Value = factura.Cliente?.Nombre ?? "";
                worksheet.Cells[row, 5].Value = factura.Cliente?.NIF ?? "";
                worksheet.Cells[row, 6].Value = factura.BaseImponible;
                worksheet.Cells[row, 7].Value = factura.TotalIVA;
                worksheet.Cells[row, 8].Value = factura.CuotaRecargo;
                worksheet.Cells[row, 9].Value = factura.CuotaRetencion;
                worksheet.Cells[row, 10].Value = factura.Total;
                worksheet.Cells[row, 11].Value = factura.TipoFacturaVERIFACTU;
                worksheet.Cells[row, 12].Value = factura.EnviadaVERIFACTU;
                worksheet.Cells[row, 13].Value = factura.Huella ?? "";

                //formato moneda
                for (int col = 6; col <= 10; col++)
                {
                    worksheet.Cells[row, col].Style.Numberformat.Format = "#,##0.00 €";
                }

                row++;
            }

            //Fila totales

            worksheet.Cells[row, 5].Value = "TOTALES:";
            worksheet.Cells[row, 5].Style.Font.Bold = true;
            worksheet.Cells[row, 6].Formula = $"SUM(F2:F{row - 1})";
            worksheet.Cells[row, 7].Formula = $"SUM(G2:G{row - 1})";
            worksheet.Cells[row, 8].Formula = $"SUM(H2:H{row - 1})";
            worksheet.Cells[row, 9].Formula = $"SUM(I2:I{row - 1})";
            worksheet.Cells[row, 10].Formula = $"SUM(J2:J{row - 1})";

            for(int col = 5; col <= 10; col++)
            {
                worksheet.Cells[row, col].Style.Font.Bold = true;
                worksheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(Color.LightYellow);

                if (col >= 6)
                {
                    worksheet.Cells[row, col].Style.Numberformat.Format = "#,##0.00 €";
                }
            }

            //Ajustar columnas
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            //Guardar archivo
            var fileName = $"LibroFacturas_{ejercicio}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine("Archivos", "Cierres", fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var fileInfo = new FileInfo(filePath);
            await package.SaveAsAsync(fileInfo);

            _logger.LogInformation($"Libro de facturas generado: {filePath}");

            return filePath;
        }

        /// <summary>
        /// Genera resumen trimestral de IVA en Excel
        /// </summary>
        private async Task<string> GenerarResumenIVA(int ejercicio, int tenantId)
        {
            var facturas = await _context.Facturas
                .Where(f => f.FechaEmision.Year == ejercicio && f.TenantId == tenantId)
                .ToListAsync();

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add($"Resumen IVA {ejercicio}");

            worksheet.Cells["A1:E1"].Merge = true;
            worksheet.Cells["A1"].Value = $"RESUMEN TRIMESTRAL DE IVA - EJERCICIO {ejercicio}";
            worksheet.Cells["A1"].Style.Font.Size = 14;
            worksheet.Cells["A1"].Style.Font.Bold = true;
            worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            worksheet.Cells["A1"].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            worksheet.Row(1).Height = 30;

            // Encabezados
            var headers = new[] { "Trimestre", "Nº Facturas", "Base Imponible", "Cuota IVA", "Total" };
            for (int col = 1; col <= 5; col++)
            {
                worksheet.Cells[3, col].Value = headers[col - 1];
                worksheet.Cells[3, col].Style.Font.Bold = true;
                worksheet.Cells[3, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[3, col].Style.Fill.BackgroundColor.SetColor(Color.LightBlue);
                worksheet.Cells[3, col].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                worksheet.Cells[3, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            //Datos por trimestre
            var datosTrimestre = facturas
                .GroupBy(f => (f.FechaEmision.Month - 1) / 3 + 1)
                .Select(g => new
                {
                    Trimestre = g.Key,
                    NumFacturas = g.Count(),
                    Base = g.Sum(f => f.BaseImponible),
                    IVA = g.Sum(f => f.TotalIVA),
                    Total = g.Sum(f => f.Total)
                })
                .OrderBy(d => d.Trimestre)
                .ToList();

            int row = 4;
            foreach( var trimestre in datosTrimestre)
            {
                worksheet.Cells[row, 1].Value = $"{trimestre.Trimestre}º Trimestre";
                worksheet.Cells[row, 2].Value = trimestre.NumFacturas;
                worksheet.Cells[row, 3].Value = trimestre.Base;
                worksheet.Cells[row, 4].Value = trimestre.IVA;
                worksheet.Cells[row, 5].Value = trimestre.Total;

                // Formato
                worksheet.Cells[row, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                for (int col = 3; col <= 5; col++)
                {
                    worksheet.Cells[row, col].Style.Numberformat.Format = "#,##0.00 €";
                }

                row++;
            }

            // Fila vacia
            row++;

            //Totales anuales
            worksheet.Cells[row, 1].Value = "TOTAL ANUAL";
            worksheet.Cells[row, 1].Style.Font.Bold = true;
            worksheet.Cells[row, 1].Style.Font.Size = 12;
            worksheet.Cells[row, 2].Formula = $"SUM(B4:B{row - 2})";
            worksheet.Cells[row, 3].Formula = $"SUM(C4:C{row - 2})";
            worksheet.Cells[row, 4].Formula = $"SUM(D4:D{row - 2})";
            worksheet.Cells[row, 5].Formula = $"SUM(E4:E{row - 2})";

            for (int col = 1; col <= 5; col++)
            {
                worksheet.Cells[row, col].Style.Font.Bold = true;
                worksheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                worksheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(Color.LightGreen);
                worksheet.Cells[row, col].Style.Border.BorderAround(ExcelBorderStyle.Medium);

                if (col == 2)
                {
                    worksheet.Cells[row, col].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }
                else if (col >= 3)
                {
                    worksheet.Cells[row, col].Style.Numberformat.Format = "#,##0.00 €";
                }
            }


            // Ajustar columnas
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            // Ancho mínimo para legibilidad
            worksheet.Column(1).Width = 15;
            worksheet.Column(2).Width = 12;
            worksheet.Column(3).Width = 16;
            worksheet.Column(4).Width = 14;
            worksheet.Column(5).Width = 16;

            // Guardar archivo
            var fileName = $"ResumenIVA_{ejercicio}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine("Archivos", "Cierres", fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var fileInfo = new FileInfo(filePath);
            await package.SaveAsAsync(fileInfo);

            _logger.LogInformation($"Resumen IVA generado: {filePath}");

            return filePath;
        }

        #endregion

        #region Metodos de Mapeo

        ///<summary>
        /// Mapea entidad CierreEjercicio a DTO
        /// </summary>
        private CierreEjercicioDTO MapearACierreDTO(CierreEjercicio cierre)
        {
            return new CierreEjercicioDTO
            {
                Id = cierre.Id,
                TentantId = cierre.TenantId,
                Ejercicio = cierre.Ejercicio,
                FechaCierre = cierre.FechaCierre,
                UsuarioId = cierre.UsuarioId,
                NombreUsuario = cierre.Usuario?.NombreCompleto ?? "",

                HashFinal = cierre.HashFinal,
                EnviadoVERIFACTU = cierre.EnviadoVERIFACTU,
                FechaEnvio = cierre.FechaEnvio,

                TotalFactuas = cierre.TotalFacturas,
                TotalBaseImponible = cierre.TotalBaseImponible,
                TotalIVA = cierre.TotalIVA,
                TotalRecargo = cierre.TotalRecargo,
                TotalRetencion = cierre.TotalRetencion,
                TotalImporte = cierre.TotalImporte,

                RutaLibroFacturas = cierre.RutaLibroFacturas,
                RutaResumenIVA = cierre.RutaResumenIVA,

                EstaAbierto = cierre.EstaAbierto,
                MotivoReapertura = cierre.MotivoReapertura,
                FechaReapertura = cierre.FechaReapertura,
                UsuarioReaperturaId = cierre.UsuarioReaperturaId,
                NombreUsuarioReapertura = cierre.UsuarioReapertura?.NombreCompleto ?? "",

                CreadoEn = cierre.CreadoEn,
                ActualizadoEn = cierre.ActualizadoEn
            };
        }

        /// <summary>
        /// Mapea resultado de cierre a DTO de respuesta
        /// </summary>
        private CierreRealizadoDTO MapearACierreRealizadoDTO(CierreEjercicio cierre, string mensaje)
        {
            return new CierreRealizadoDTO
            {
                CierreId = cierre.Id,
                Ejercicio = cierre.Ejercicio,
                Mensaje = mensaje,
                HashFinal = cierre.HashFinal,
                Estadisticas = new EstadisticasDTO
                {
                    TotalFacturas = cierre.TotalFacturas,
                    TotalBaseImponible = cierre.TotalBaseImponible,
                    TotalIVA = cierre.TotalIVA,
                    TotalRecargo = cierre.TotalRecargo,
                    TotalRetencion = cierre.TotalRetencion,
                    TotalImporte = cierre.TotalImporte
                },
                Archivos = new ArchivosGeneradosDTO
                {
                    LibroFacturas = cierre.RutaLibroFacturas ?? "",
                    ResumenIVA = cierre.RutaResumenIVA ?? ""
                }
            };
        }

        #endregion
    }


}
