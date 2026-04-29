using FacturacionVERIFACTU.API.Data.Interfaces;

namespace FacturacionVERIFACTU.API.Middleware
{
    /// <summary>
    /// Middleware que extrae tenant_id del JWT y lo inyecta en ITenantContext
    /// </summary>
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
        {
            var path = context.Request.Path.Value ?? string.Empty;
            // Ignorar rutas del superadmin
            if (!path.StartsWith("/api/superadmin", StringComparison.OrdinalIgnoreCase))
            {
                // Extrae tenant_id del claim JWT
                var tenantIdClaim = context.User.FindFirst("tenant_id");

                if (tenantIdClaim != null && int.TryParse(tenantIdClaim.Value, out int tenantId))
                {
                    tenantContext.SetTenantId(tenantId);
                }
            }
            await _next(context);
        }
    }

    /// <summary>
    /// Extension method para registrar middleware
    /// </summary>
    public static class TenantMiddlewareExtensions
    {
        public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TenantMiddleware>();
        }
    }
}