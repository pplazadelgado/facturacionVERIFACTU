using FacturacionVERIFACTU.API.Data.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FacturacionVERIFACTU.API.Data.Services
{
    public interface IRecibosPdfService
    {
        byte[] GenerarReciboServicio(ReciboServicio recibo, IConfiguration config);
    }

    public class RecibosPdfService : IRecibosPdfService
    {
        public byte[] GenerarReciboServicio(ReciboServicio recibo, IConfiguration config)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // Datos del proveedor desde appsettings
            var provNombre = config["ProveedorSoftware:Nombre"] ?? "Proveedor Software";
            var provNIF = config["ProveedorSoftware:NIF"] ?? "";
            var provDireccion = config["ProveedorSoftware:Direccion"] ?? "";
            var provEmail = config["ProveedorSoftware:Email"] ?? "";
            var provTelefono = config["ProveedorSoftware:Telefono"] ?? "";

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(45);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Content().Column(col =>
                    {
                        // ── CABECERA ──────────────────────────────────
                        col.Item().Row(row =>
                        {
                            // Proveedor (izquierda)
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(provNombre)
                                    .FontSize(16).Bold().FontColor("#1e3a8a");
                                c.Item().Text($"NIF: {provNIF}").FontSize(9);
                                c.Item().Text(provDireccion).FontSize(9);
                                c.Item().Text(provEmail).FontSize(9);
                                c.Item().Text(provTelefono).FontSize(9);
                            });

                            // Número recibo (derecha)
                            row.ConstantItem(180).Column(c =>
                            {
                                c.Item().AlignRight().Text("RECIBO DE SERVICIO")
                                    .FontSize(14).Bold().FontColor("#1e3a8a");
                                c.Item().AlignRight()
                                    .Text($"Nº {recibo.NumeroRecibo:D4}")
                                    .FontSize(18).Bold();
                                c.Item().AlignRight()
                                    .Text($"Fecha: {recibo.FechaEmision:dd/MM/yyyy}")
                                    .FontSize(9).Italic();
                            });
                        });

                        col.Item().PaddingTop(15).LineHorizontal(1).LineColor("#cbd5e1");

                        // ── DATOS CLIENTE ─────────────────────────────
                        col.Item().PaddingTop(15).Column(c =>
                        {
                            c.Item().Text("CLIENTE").FontSize(11).Bold().FontColor("#475569");
                            c.Item().PaddingTop(5).Text(recibo.Tenant.Nombre)
                                .FontSize(13).Bold();
                            c.Item().Text($"NIF/CIF: {recibo.Tenant.NIF}").FontSize(9);
                            if (!string.IsNullOrEmpty(recibo.Tenant.Direccion))
                                c.Item().Text(recibo.Tenant.Direccion).FontSize(9);
                            if (!string.IsNullOrEmpty(recibo.Tenant.Email))
                                c.Item().Text(recibo.Tenant.Email).FontSize(9);
                        });

                        col.Item().PaddingTop(15).LineHorizontal(1).LineColor("#cbd5e1");

                        // ── PERÍODO ───────────────────────────────────
                        col.Item().PaddingTop(15).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("PERÍODO DE SERVICIO")
                                    .FontSize(9).Bold().FontColor("#475569");
                                c.Item().Text(
                                    $"{recibo.PeriodoDesde:dd/MM/yyyy} — {recibo.PeriodoHasta:dd/MM/yyyy}")
                                    .FontSize(11).Bold();
                            });
                        });

                        col.Item().PaddingTop(20);

                        // ── TABLA CONCEPTOS ───────────────────────────
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(5);   // Concepto
                                cols.ConstantColumn(100); // Base
                                cols.ConstantColumn(80);  // IVA%
                                cols.ConstantColumn(100); // Total
                            });

                            // Cabecera
                            table.Header(header =>
                            {
                                header.Cell().Background("#1e3a8a").Padding(8)
                                    .Text("Concepto").FontColor("#fff").FontSize(9).Bold();
                                header.Cell().Background("#1e3a8a").Padding(8).AlignRight()
                                    .Text("Base").FontColor("#fff").FontSize(9).Bold();
                                header.Cell().Background("#1e3a8a").Padding(8).AlignRight()
                                    .Text("IVA %").FontColor("#fff").FontSize(9).Bold();
                                header.Cell().Background("#1e3a8a").Padding(8).AlignRight()
                                    .Text("Total").FontColor("#fff").FontSize(9).Bold();
                            });

                            // Línea del recibo
                            table.Cell().Background("#f8fafc").BorderBottom(0.5f)
                                .BorderColor("#e2e8f0").Padding(8)
                                .Text(recibo.Concepto).FontSize(10);
                            table.Cell().Background("#f8fafc").BorderBottom(0.5f)
                                .BorderColor("#e2e8f0").Padding(8).AlignRight()
                                .Text(recibo.ImporteBase.ToString("N2") + " €").FontSize(10);
                            table.Cell().Background("#f8fafc").BorderBottom(0.5f)
                                .BorderColor("#e2e8f0").Padding(8).AlignRight()
                                .Text($"{recibo.PorcentajeIva:N0}%").FontSize(10);
                            table.Cell().Background("#f8fafc").BorderBottom(0.5f)
                                .BorderColor("#e2e8f0").Padding(8).AlignRight()
                                .Text(recibo.ImporteTotal.ToString("N2") + " €")
                                .FontSize(10).Bold();
                        });

                        // ── TOTALES ───────────────────────────────────
                        col.Item().PaddingTop(10).AlignRight().Column(c =>
                        {
                            c.Item().Row(r =>
                            {
                                r.ConstantItem(130).AlignRight()
                                    .Text("Base imponible:").FontSize(10);
                                r.ConstantItem(110).AlignRight()
                                    .Text(recibo.ImporteBase.ToString("C"))
                                    .FontSize(10).Bold();
                            });
                            c.Item().PaddingTop(4).Row(r =>
                            {
                                r.ConstantItem(130).AlignRight()
                                    .Text($"IVA ({recibo.PorcentajeIva:N0}%):").FontSize(10);
                                r.ConstantItem(110).AlignRight()
                                    .Text(recibo.ImporteIva.ToString("C"))
                                    .FontSize(10).Bold();
                            });
                            c.Item().PaddingTop(6).LineHorizontal(1).LineColor("#1e3a8a");
                            c.Item().PaddingTop(6).Row(r =>
                            {
                                r.ConstantItem(130).AlignRight()
                                    .Text("TOTAL:").FontSize(13).Bold();
                                r.ConstantItem(110).AlignRight()
                                    .Text(recibo.ImporteTotal.ToString("C"))
                                    .FontSize(13).Bold().FontColor("#1e3a8a");
                            });
                        });

                        // ── PIE ───────────────────────────────────────
                        col.Item().PaddingTop(40).LineHorizontal(0.5f).LineColor("#cbd5e1");
                        col.Item().PaddingTop(10).AlignCenter()
                            .Text("Este documento es un recibo de servicio de software. " +
                                  "No tiene validez como factura fiscal.")
                            .FontSize(8).FontColor("#94a3b8").Italic();
                    });
                });
            });

            return document.GeneratePdf();
        }
    }
}
