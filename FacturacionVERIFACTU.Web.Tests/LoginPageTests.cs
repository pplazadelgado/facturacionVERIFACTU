using Bunit;
using Bunit.TestDoubles;
using FacturacionVERIFACTU.Web.Components.Pages;
using FacturacionVERIFACTU.Web.Models.DTOs;
using FacturacionVERIFACTU.Web.Services;
using FacturacionVERIFACTU.Web.Tests.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;

namespace FacturacionVERIFACTU.Web.Tests;

/// <summary>
/// BLOQUE 27 - Tests unitarios para el componente Login.razor
/// Cubre: renderizado, validación, login exitoso, login fallido y redirección.
/// </summary>
public class LoginPageTests : BlazorTestBase
{
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<AuthenticationStateProvider> _mockAuthStateProvider;

    public LoginPageTests()
    {
        _mockAuthService = new Mock<IAuthService>();
        _mockAuthStateProvider = new Mock<AuthenticationStateProvider>();

        // Registrar servicios mock en el contenedor DI de bUnit
        Services.AddScoped<IAuthService>(_ => _mockAuthService.Object);
        Services.AddScoped<AuthenticationStateProvider>(_ => _mockAuthStateProvider.Object);
    }

    // =========================================================
    // TEST 1: Renderizado inicial del formulario
    // =========================================================
    [Fact]
    public void Login_Renders_WithEmailAndPasswordFields()
    {
        // Arrange: usuario no autenticado
        ConfigurarUsuarioNoAutenticado();

        // Act
        var cut = RenderComponent<Login>();

        // Assert: el formulario debe tener los campos básicos
        cut.Find("h1").TextContent.Should().Contain("Iniciar sesión");
        cut.Find("input[type='text'], input:not([type])").Should().NotBeNull();
        cut.Find("input[type='password']").Should().NotBeNull();
    }

    // =========================================================
    // TEST 2: Botón de login está habilitado por defecto
    // =========================================================
    [Fact]
    public void Login_SubmitButton_IsEnabledByDefault()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        // Act
        var cut = RenderComponent<Login>();
        var button = cut.Find("button[type='submit']");

        // Assert
        button.HasAttribute("disabled").Should().BeFalse();
    }

    // =========================================================
    // TEST 3: Login exitoso redirige al home
    // =========================================================
    [Fact]
    public async Task Login_WithValidCredentials_NavigatesToHome()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        var loginResponse = new LoginResponse
        {
            AccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.test",
            User = new UserInfo { Email = "admin@empresa.com", NombreCompleto = "Admin" }
        };

        _mockAuthService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync(loginResponse);

        var navManager = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        var cut = RenderComponent<Login>();

        // Act: rellenar formulario y hacer submit
        cut.Find("input:not([type='password'])").Change("admin@empresa.com");
        cut.Find("input[type='password']").Change("MiPassword123!");
        await cut.Find("button[type='submit']").ClickAsync(new());

        // Assert: debe navegar fuera del login
        navManager!.History.Count.Should().BeGreaterThan(0);
    }

    // =========================================================
    // TEST 4: Login fallido muestra mensaje de error
    // =========================================================
    [Fact]
    public async Task Login_WithInvalidCredentials_ShowsErrorMessage()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        _mockAuthService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>()))
            .ReturnsAsync((LoginResponse?)null); // null = credenciales incorrectas

        var cut = RenderComponent<Login>();

        // Act
        cut.Find("input:not([type='password'])").Change("wrong@email.com");
        cut.Find("input[type='password']").Change("wrongpassword");
        await cut.Find("button[type='submit']").ClickAsync(new());

        // Assert: debe aparecer un mensaje de error
        var errorDiv = cut.Find(".alert-danger");
        errorDiv.Should().NotBeNull();
        errorDiv.TextContent.Should().Contain("incorrectos");
    }

    // =========================================================
    // TEST 5: Durante el login se muestra spinner de carga
    // =========================================================
    [Fact]
    public async Task Login_WhileLoading_ShowsSpinnerAndDisablesButton()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        // Simular una llamada lenta al servicio
        var tcs = new TaskCompletionSource<LoginResponse?>();
        _mockAuthService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>()))
            .Returns(tcs.Task);

        var cut = RenderComponent<Login>();

        // Act: iniciar submit sin completar la tarea
        cut.Find("input:not([type='password'])").Change("admin@empresa.com");
        cut.Find("input[type='password']").Change("password123");
        var clickTask = cut.Find("button[type='submit']").ClickAsync(new());

        // Assert: el botón debe estar deshabilitado mientras carga
        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("button[type='submit']");
            btn.HasAttribute("disabled").Should().BeTrue();
        });

        // Cleanup
        tcs.SetResult(null);
        await clickTask;
    }

    // =========================================================
    // TEST 6: Usuario ya autenticado es redirigido al home
    // =========================================================
    [Fact]
    public void Login_WhenAlreadyAuthenticated_RedirectsToHome()
    {
        // Arrange: simular usuario ya autenticado
        ConfigurarUsuarioAutenticado("admin@empresa.com");

        var navManager = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
        var cut = RenderComponent<Login>();

        // Assert: debe haber navegado al home
        cut.WaitForAssertion(() =>
        {
            navManager!.History.Should().Contain(h => h.Uri == "/");
        });
    }

    // =========================================================
    // TEST 7: Error de conexión muestra mensaje apropiado
    // =========================================================
    [Fact]
    public async Task Login_OnConnectionError_ShowsConnectionErrorMessage()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        _mockAuthService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>()))
            .ThrowsAsync(new HttpRequestException("No se puede conectar al servidor"));

        var cut = RenderComponent<Login>();

        // Act
        cut.Find("input:not([type='password'])").Change("admin@empresa.com");
        cut.Find("input[type='password']").Change("password123");
        await cut.Find("button[type='submit']").ClickAsync(new());

        // Assert
        var errorDiv = cut.Find(".alert-danger");
        errorDiv.TextContent.Should().Contain("Error de conexión");
    }

    // =========================================================
    // TEST 8: AuthService.LoginAsync es llamado con los datos correctos
    // =========================================================
    [Fact]
    public async Task Login_CallsAuthService_WithCorrectCredentials()
    {
        // Arrange
        ConfigurarUsuarioNoAutenticado();

        LoginRequest? capturedRequest = null;
        _mockAuthService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>()))
            .Callback<LoginRequest>(req => capturedRequest = req)
            .ReturnsAsync((LoginResponse?)null);

        var cut = RenderComponent<Login>();
        const string testEmail = "usuario@empresa.com";
        const string testPassword = "MiContraseña2024!";

        // Act
        cut.Find("input:not([type='password'])").Change(testEmail);
        cut.Find("input[type='password']").Change(testPassword);
        await cut.Find("button[type='submit']").ClickAsync(new());

        // Assert
        _mockAuthService.Verify(s => s.LoginAsync(It.IsAny<LoginRequest>()), Times.Once);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Email.Should().Be(testEmail);
        capturedRequest.Password.Should().Be(testPassword);
    }

    // =========================================================
    // Helpers privados
    // =========================================================

    private void ConfigurarUsuarioNoAutenticado()
    {
        var anonymousState = new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity())); // Sin autenticación

        _mockAuthStateProvider
            .Setup(p => p.GetAuthenticationStateAsync())
            .ReturnsAsync(anonymousState);
    }

    private void ConfigurarUsuarioAutenticado(string email)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, "Admin")
        };
        var identity = new ClaimsIdentity(claims, "jwt");
        var authenticatedState = new AuthenticationState(new ClaimsPrincipal(identity));

        _mockAuthStateProvider
            .Setup(p => p.GetAuthenticationStateAsync())
            .ReturnsAsync(authenticatedState);
    }
}
