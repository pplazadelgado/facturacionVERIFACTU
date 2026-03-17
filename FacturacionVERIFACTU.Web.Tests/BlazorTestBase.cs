using Bunit;
using Bunit.TestDoubles;
using FacturacionVERIFACTU.Web.Models.DTOs;
using FacturacionVERIFACTU.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using System.Security.Claims;

namespace FacturacionVERIFACTU.Web.Tests.Helpers;

/// <summary>
/// Clase base para todos los tests de componentes Blazor.
///
/// PROBLEMA RESUELTO: ProtectedSessionStorage es sealed → no se puede mockear con Moq.
/// Solución: registramos un IJSRuntime mock para que su constructor no falle,
/// y luego registramos la instancia real (que en bUnit nunca se llamará
/// porque IAuthService ya está mockeado y no llega a usarla).
/// </summary>
public abstract class BlazorTestBase : TestContext
{
    protected BlazorTestBase()
    {
        // ── 1. Soporte de autorización bUnit ────────────────────────
        Services.AddOptions();
        Services.AddAuthorizationCore();
        Services.AddLogging();

        // ── 2. IJSRuntime mock ──────────────────────────────────────
        // ProtectedSessionStorage necesita IJSRuntime en su constructor.
        // bUnit ya registra un FakeJSRuntime, pero lo hacemos explícito
        // para que ProtectedSessionStorage pueda instanciarse.
        var mockJs = new Mock<IJSRuntime>();
        Services.AddScoped<IJSRuntime>(_ => mockJs.Object);

        // ── 3. ProtectedSessionStorage — instancia real con JS mock ─
        // No podemos mockearla (sealed), pero como IAuthService está
        // mockeado nunca se llamará a sus métodos.
        Services.AddScoped<ProtectedSessionStorage>();

        // ── 4. IAuthService mockeado ────────────────────────────────
        // Evita que CustomAuthStateProvider intente usar SessionStorage.
        var mockAuth = new Mock<IAuthService>();
        mockAuth.Setup(s => s.GetTokenAsync()).ReturnsAsync((string?)null);
        mockAuth.Setup(s => s.IsAuthenticatedAsync()).ReturnsAsync(false);
        mockAuth.Setup(s => s.GetCurrentUserAsync()).ReturnsAsync((UserInfo?)null);
        Services.AddScoped<IAuthService>(_ => mockAuth.Object);

        // ── 5. AuthenticationStateProvider (anónimo por defecto) ────
        var anonState = new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity()));
        var mockAuthState = new Mock<AuthenticationStateProvider>();
        mockAuthState.Setup(p => p.GetAuthenticationStateAsync())
                     .ReturnsAsync(anonState);
        Services.AddScoped<AuthenticationStateProvider>(_ => mockAuthState.Object);

        // ── 6. TokenState y TokenHandler ───────────────────────────
        Services.AddScoped<TokenState>();
        Services.AddScoped<TokenHandler>();

        // ── 7. ConfirmDialogService (clase simple, instancia real) ──
        Services.AddScoped<ConfirmDialogService>();
    }

    /// <summary>
    /// Configura el contexto de autenticación como usuario autenticado.
    /// </summary>
    protected TestAuthorizationContext AuthenticateUser(
        string email = "test@empresa.com",
        string nombre = "Usuario Test",
        string tenantId = "1",
        string role = "Admin")
    {
        var authContext = this.AddTestAuthorization();
        authContext.SetAuthorized(email);
        authContext.SetClaims(
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, nombre),
            new Claim("TenantId", tenantId),
            new Claim(ClaimTypes.Role, role)
        );
        return authContext;
    }

    /// <summary>Configura el contexto como usuario NO autenticado.</summary>
    protected TestAuthorizationContext SetUnauthenticated()
    {
        var authContext = this.AddTestAuthorization();
        authContext.SetNotAuthorized();
        return authContext;
    }
}
