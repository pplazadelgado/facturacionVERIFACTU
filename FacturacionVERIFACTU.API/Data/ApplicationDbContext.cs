using API.Data.Entities;
using FacturacionVERIFACTU.API.Data.Entities;
using FacturacionVERIFACTU.API.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.OpenApi.Validations;

namespace FacturacionVERIFACTU.API.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets se añadirán en el Bloque 2
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Cliente> Clientes => Set<Cliente>();
        public DbSet<Producto> Productos => Set<Producto>();
        public DbSet<SerieNumeracion> SeriesNumeracion => Set<SerieNumeracion>();
        public DbSet<CierreEjercicio> CierreEjercicios => Set<CierreEjercicio>();
        public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
        public DbSet<LineaPresupuesto> LineasPresupuesto => Set<LineaPresupuesto>();
        public DbSet<Albaran> Albaranes => Set<Albaran>();
        public DbSet<LineaAlbaran> LineasAlbaranes => Set<LineaAlbaran>();
        public DbSet<Factura> Facturas => Set<Factura>();
        public DbSet<LineaFactura> LineasFacturas => Set<LineaFactura>();
        public DbSet<TipoImpuesto> TiposImpuesto => Set<TipoImpuesto>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
        public DbSet<ReciboServicio> RecibosServicio => Set<ReciboServicio>();
       


        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            //Auto generar Chemas para nuestros tenants
            var nuevosTenants = ChangeTracker.Entries<Tenant>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity)
                .Where(t => string.IsNullOrEmpty(t.Schema))
                .ToList();

            foreach(var tenant in nuevosTenants)
            {
                //Generar schema basado en NIF
                var nifLimpio = new string(tenant.NIF
                    .Where(char.IsLetterOrDigit)
                    .ToArray())
                    .ToLower();

                //Limitar a 20 caracteres
                if (nifLimpio.Length > 20)
                    nifLimpio = nifLimpio.Substring(0, 20);

                var schemaBase = $"tenant_{nifLimpio}";

                //Asegurar que sea unico
                var schema = schemaBase;
                var contador = 1;

                while(await Tenants.AnyAsync(t => t.Schema == schema, cancellationToken))
                {
                    schema = $"{schemaBase}_{contador}";
                    contador++;
                }
                tenant.Schema = schema;
            }

            //Guardar cambios
            var result = await base.SaveChangesAsync(cancellationToken);

            //Crear schemas fisicos en PoftgresSQL para los nuevos tenants
            foreach(var tenant in nuevosTenants)
            {
                try
                {
                    await Database.ExecuteSqlRawAsync(
                         $"CREATE SCHEMA IF NOT EXISTS {tenant.Schema}",
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    //log error para no fallar la transaccion
                    Console.WriteLine($"Error creando schema {tenant.Schema}: {ex.Message}");
                }
            }

            return result;
        }
        
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Tenant>()
                .HasIndex(t => t.Schema)
                .IsUnique()
                .HasDatabaseName("IX_tenants_schema");

            modelBuilder.Entity<Tenant>()
               .HasIndex(t => t.NIF)
               .IsUnique();

            modelBuilder.Entity<Usuario>()
                .HasIndex(u => new { u.TenantId, u.Email })
                .IsUnique();

            modelBuilder.Entity<Cliente>()
                .HasIndex(c => new { c.TenantId, c.NIF })
                .IsUnique();

            modelBuilder.Entity<Producto>()
                .HasIndex(p => new { p.TenantId, p.Codigo })
                .IsUnique();

            modelBuilder.Entity<SerieNumeracion>()
                .HasIndex(s => new { s.TenantId, s.Codigo, s.Ejercicio, s.TipoDocumento })
                .IsUnique();

            modelBuilder.Entity<TipoImpuesto>()
                .HasIndex(t => new { t.TenantId, t.Nombre })
                .IsUnique();

            modelBuilder.Entity<Presupuesto>()
                .HasIndex(p => new { p.TenantId, p.Numero })
                .IsUnique();

            modelBuilder.Entity<Albaran>()
                .HasIndex(a => new { a.TenantId, a.Numero })
                .IsUnique();

            modelBuilder.Entity<Factura>()
                .HasIndex(f => new { f.TenantId, f.Numero })
                .IsUnique();

            modelBuilder.Entity<ReciboServicio>()
                .HasOne(r => r.Tenant)
                .WithMany()
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReciboServicio>()
                .HasIndex(r => r.NumeroRecibo)
                .IsUnique();

            // ==========================================
            // RELACIONES Y DELETE BEHAVIOR
            // ==========================================

            // Tenant -> Usuarios
            modelBuilder.Entity<Usuario>()
                .HasOne(u => u.Tenant)
                .WithMany(t => t.Usuarios)
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant -> Clientes
            modelBuilder.Entity<Cliente>()
                .HasOne(c => c.Tenant)
                .WithMany(t => t.Clientes)
                .HasForeignKey(c => c.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant -> Productos
            modelBuilder.Entity<Producto>()
                .HasOne(p => p.Tenant)
                .WithMany(t => t.Productos)
                .HasForeignKey(p => p.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            //Tenant -> TiposImpuesto
            modelBuilder.Entity<TipoImpuesto>()
                .HasOne(t => t.Tenant)
                .WithMany(tenant => tenant.TipoImpuestos)
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant -> Series
            modelBuilder.Entity<SerieNumeracion>()
                .HasOne(s => s.Tenant)
                .WithMany(t => t.Series)
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tenant -> Cierres
            // API/Data/AppDbContext.cs - En OnModelCreating

            modelBuilder.Entity<CierreEjercicio>(entity =>
            {
                // Índice único: Un solo cierre activo por ejercicio y tenant
                entity.HasIndex(e => new { e.TenantId, e.Ejercicio, e.EstaAbierto })
                    .HasFilter("esta_abierto = false") // Solo un cierre "cerrado" por ejercicio
                    .IsUnique();

                entity.HasOne(e => e.Tenant)
                    .WithMany()
                    .HasForeignKey(e => e.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Usuario)
                    .WithMany()
                    .HasForeignKey(e => e.UsuarioId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.UsuarioReapertura)
                    .WithMany()
                    .HasForeignKey(e => e.UsuarioReaperturaId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Cliente -> Presupuestos
            modelBuilder.Entity<Presupuesto>()
                .HasOne(p => p.Cliente)
                .WithMany(c => c.Presupuestos)
                .HasForeignKey(p => p.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cliente -> Albaranes
            modelBuilder.Entity<Albaran>()
                .HasOne(a => a.Cliente)
                .WithMany(c => c.Albaranes)
                .HasForeignKey(a => a.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cliente -> Facturas
            modelBuilder.Entity<Factura>()
                .HasOne(f => f.Cliente)
                .WithMany(c => c.Facturas)
                .HasForeignKey(f => f.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Presupuesto -> Albaranes (opcional)
            modelBuilder.Entity<Albaran>()
                .HasOne(a => a.Presupuesto)
                .WithMany(p => p.Albaranes)
                .HasForeignKey(a => a.PresupuestoId)
                .OnDelete(DeleteBehavior.SetNull);

            // Factura -> Albaranes (opcional)
            modelBuilder.Entity<Albaran>()
                .HasOne(a => a.Factura)
                .WithMany(f => f.Albaranes)
                .HasForeignKey(a => a.FacturaId)
                .OnDelete(DeleteBehavior.SetNull);

            // Presupuesto -> LineasPresupuesto
            modelBuilder.Entity<LineaPresupuesto>()
                .HasOne(l => l.Presupuesto)
                .WithMany(p => p.Lineas)
                .HasForeignKey(l => l.PresupuestoId)
                .OnDelete(DeleteBehavior.Cascade);

            // Albaran -> LineasAlbaran
            modelBuilder.Entity<LineaAlbaran>()
                .HasOne(l => l.Albaran)
                .WithMany(a => a.Lineas)
                .HasForeignKey(l => l.AlbaranId)
                .OnDelete(DeleteBehavior.Cascade);

            // Factura -> LineasFactura
            modelBuilder.Entity<LineaFactura>()
                .HasOne(l => l.Factura)
                .WithMany(f => f.Lineas)
                .HasForeignKey(l => l.FacturaId)
                .OnDelete(DeleteBehavior.Cascade);

            // Producto -> LineasPresupuesto
            modelBuilder.Entity<LineaPresupuesto>()
                .HasOne(l => l.Producto)
                .WithMany(p => p.LineasPresupuesto)
                .HasForeignKey(l => l.ProductoId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<LineaPresupuesto>()
                .HasOne(l => l.TipoImpuesto)
                .WithMany(t => t.LineasPresupuesto)
                .HasForeignKey(l => l.TipoImpuestoId)
                .OnDelete(DeleteBehavior.Restrict);


            // Producto -> LineasAlbaran
            modelBuilder.Entity<LineaAlbaran>()
                .HasOne(l => l.Producto)
                .WithMany(p => p.LineasAlbaran)
                .HasForeignKey(l => l.ProductoId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<LineaAlbaran>()
                .HasOne(l => l.TipoImpuesto)
                .WithMany(t => t.LineasAlbaran)
                .HasForeignKey(l => l.TipoImpuestoId)
                .OnDelete(DeleteBehavior.Restrict);

            // Producto -> LineasFactura
            modelBuilder.Entity<LineaFactura>()
                .HasOne(l => l.Producto)
                .WithMany(p => p.LineasFactura)
                .HasForeignKey(l => l.ProductoId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<LineaFactura>()
                .HasOne(l => l.TipoImpuesto)
                .WithMany(t => t.LineasFactura)
                .HasForeignKey(l => l.TipoImpuestoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Producto>()
                .HasOne(p => p.TipoImpuesto)
                .WithMany(t => t.Productos)
                .HasForeignKey(p => p.TipoImpuestoId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(rt => rt.Id);
                entity.HasIndex(rt => rt.Token);
                entity.Property(rt => rt.Token).IsRequired();

                entity.HasOne(rt => rt.Usuario)
                    .WithMany()
                    .HasForeignKey(rt => rt.UsuarioId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ==========================================
            // QUERY FILTERS POR TENANT
            // (Se activarán con el sistema de autenticación)
            // ==========================================
            /*
            modelBuilder.Entity<Usuario>().HasQueryFilter(u => u.TenantId == CurrentTenantId);
            modelBuilder.Entity<Cliente>().HasQueryFilter(c => c.TenantId == CurrentTenantId);
            modelBuilder.Entity<Producto>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
            modelBuilder.Entity<Presupuesto>().HasQueryFilter(p => p.TenantId == CurrentTenantId);
            modelBuilder.Entity<Albaran>().HasQueryFilter(a => a.TenantId == CurrentTenantId);
            modelBuilder.Entity<Factura>().HasQueryFilter(f => f.TenantId == CurrentTenantId);
            */
        }
    }
}
