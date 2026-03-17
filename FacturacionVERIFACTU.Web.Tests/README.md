# BLOQUE 27: Testing Blazor con bUnit 🧪

Proyecto de tests unitarios para los componentes Blazor de FacturacionVERIFACTU.
**Duración**: 3 días (19-21 Marzo) | **Framework**: bUnit + xUnit + Moq

---

## 📁 Estructura del Proyecto

```
FacturacionVERIFACTU.Web.Tests/
├── FacturacionVERIFACTU.Web.Tests.csproj    ← Proyecto de tests
├── Helpers/
│   └── BlazorTestBase.cs                    ← Clase base con configuración común
├── LoginPageTests.cs                        ← 8 tests para Login.razor
├── ClientesIndexTests.cs                    ← 10 tests para ClientesIndex.razor
├── FacturaDetalleTests.cs                   ← 12 tests para Facturas + VERIFACTU
└── README.md
```

---

## 🔧 Paso 1: Crear el proyecto en Visual Studio 2022

### Opción A - Desde Visual Studio:
1. Click derecho en la solución → **Agregar** → **Nuevo proyecto**
2. Selecciona **xUnit Test Project (.NET)**
3. Nombre: `FacturacionVERIFACTU.Web.Tests`
4. Framework: **.NET 8.0**

### Opción B - Desde terminal (Package Manager Console o terminal):
```bash
cd tu-ruta-solucion
dotnet new xunit -n FacturacionVERIFACTU.Web.Tests --framework net8.0
```

---

## 📦 Paso 2: Instalar NuGet Packages

### Desde NuGet Package Manager (Visual Studio):
Herramientas → Administrador de paquetes NuGet → Consola:

```powershell
# En la consola de NuGet, selecciona el proyecto FacturacionVERIFACTU.Web.Tests

Install-Package bunit -Version 1.28.9
Install-Package Moq -Version 4.20.72
Install-Package FluentAssertions -Version 6.12.0
Install-Package Microsoft.AspNetCore.Components.Authorization -Version 8.0.0
```

### O directamente editando el .csproj (ya incluido):
El archivo `.csproj` ya tiene todas las referencias configuradas correctamente.

---

## 🔗 Paso 3: Añadir referencia al proyecto Web

En Visual Studio:
1. Click derecho en `FacturacionVERIFACTU.Web.Tests`
2. **Agregar** → **Referencia de proyecto**
3. Selecciona `FacturacionVERIFACTU.Web`

O en el .csproj (ya incluido):
```xml
<ProjectReference Include="..\FacturacionVERIFACTU.Web\FacturacionVERIFACTU.Web.csproj" />
```

---

## ▶️ Paso 4: Ejecutar los tests

### Desde Visual Studio:
- **Test** → **Ejecutar todas las pruebas** (Ctrl+R, A)
- O abrir **Explorador de pruebas** (Test → Explorador de pruebas)

### Desde terminal:
```bash
dotnet test FacturacionVERIFACTU.Web.Tests/FacturacionVERIFACTU.Web.Tests.csproj -v normal
```

---

## 📋 Tests incluidos

### LoginPageTests.cs (8 tests)
| Test | Descripción |
|------|-------------|
| `Login_Renders_WithEmailAndPasswordFields` | Verifica que el formulario renderiza correctamente |
| `Login_SubmitButton_IsEnabledByDefault` | El botón no está deshabilitado al inicio |
| `Login_WithValidCredentials_NavigatesToHome` | Login exitoso redirige a `/` |
| `Login_WithInvalidCredentials_ShowsErrorMessage` | Credenciales wrongas muestran error |
| `Login_WhileLoading_ShowsSpinnerAndDisablesButton` | Spinner visible durante la petición |
| `Login_WhenAlreadyAuthenticated_RedirectsToHome` | Usuario ya autenticado se redirige |
| `Login_OnConnectionError_ShowsConnectionErrorMessage` | Error de red se muestra al usuario |
| `Login_CallsAuthService_WithCorrectCredentials` | AuthService recibe los datos correctos |

### ClientesIndexTests.cs (10 tests)
| Test | Descripción |
|------|-------------|
| `ClientesIndex_Initially_ShowsLoadingSpinner` | Spinner visible al iniciar carga |
| `ClientesIndex_WithClientes_RendersTable` | Tabla renderiza con los clientes |
| `ClientesIndex_WithClientes_ShowsClienteDetails` | Nombre y NIF visibles |
| `ClientesIndex_WithNoClientes_ShowsEmptyState` | Estado vacío cuando no hay clientes |
| `ClientesIndex_WhenApiReturnsNull_ShowsErrorMessage` | Error cuando la API falla |
| `ClientesIndex_NuevoClienteButton_NavigatesToNewClienteRoute` | Navega a `/clientes/nuevo` |
| `ClientesIndex_EditarButton_NavigatesToEditRoute` | Navega a `/clientes/editar/{id}` |
| `ClientesIndex_Search_CallsApiWithSearchParam` | Búsqueda envía parámetro `search` |
| `ClientesIndex_InactiveCliente_ShowsInactiveBadge` | Badge "Inactivo" para clientes inactivos |
| `ClientesIndex_Footer_ShowsCorrectClienteCount` | Footer muestra número correcto |

### FacturaDetalleTests.cs (12 tests)
| Test | Descripción |
|------|-------------|
| `FacturasIndex_LoadsFacturas_RendersTable` | Lista de facturas renderiza |
| `FacturaDetalle_WhenVerifactuEnviado_ShowsEnviadoBadge` | Badge "Enviada" visible |
| `FacturaDetalle_WhenVerifactuNoEnviado_ShowsPendienteBadge` | Sin huella = no enviada |
| `FacturaDetalle_DisplaysTotalsCorrectly` | Base, IVA y Total correctos |
| `FacturaDetalle_EstadoPagada_ShowsPagadaBadge` | Badge "Pagada" visible |
| `FacturaDetalle_ShowsNumeroAndClienteData` | Número y cliente visibles |
| `FacturaDetalle_TipoVerifactu_IsF1_ShowsCorrectType` | Tipo F1 se muestra |
| `FacturaDetalle_WithHuella_ShowsHuellaInfo` | Hash VERIFACTU visible |
| `FacturaDetalle_WithLineas_RendersLineasTable` | Líneas de factura renderizadas |
| `FacturaResponseDto_HasAllRequiredVerifactuFields` | DTO tiene todos los campos |
| `FacturaDto_Total_EqualsBaseImponiblePlusTotalIVA` | (Theory) Cálculo de totales |
| `FacturaDetalle_VerifactuFechaEnvio_OnlyShownWhenEnviada` | Fecha envío solo si enviada |

---

## ⚠️ Notas importantes

### Componentes testables en FacturaDetalleTests
Los `TestableFacturasComponent` y `TestableFacturaDetalleComponent` al final del archivo
son wrappers de test aislados. Una vez tengas los componentes reales `FacturasIndex.razor`
y `FacturaEditor.razor` completos del **Bloque 24**, reemplaza los `RenderComponent<T>()` por:

```csharp
// Antes (wrapper de test):
var cut = RenderComponent<TestableFacturaDetalleComponent>(...);

// Después (componente real):
var cut = RenderComponent<FacturaEditor>(parameters => parameters
    .Add(p => p.Id, 1));
```

### Propiedad IsLoading en ClientesIndex
El test `ClientesIndex_Initially_ShowsLoadingSpinner` accede a `cut.Instance.IsLoading`.
Asegúrate de que la propiedad `isLoading` en `ClientesIndex.razor` sea accesible:
```csharp
// En ClientesIndex.razor @code section
public bool IsLoading => isLoading; // Exponer la propiedad privada para tests
```

---

## 🔍 Troubleshooting frecuente

**Error: "No service for type IApiService"**
→ Añade el mock en el constructor del test: `Services.AddScoped<IApiService>(_ => _mock.Object)`

**Error: "Component requires authorization"**  
→ Llama a `AuthenticateUser()` en el constructor del test (heredado de `BlazorTestBase`)

**Error: "JSRuntime not available"**  
→ bUnit incluye un mock de JSRuntime automáticamente. Si el componente llama JS, usa:
```csharp
JSInterop.SetupVoid("...");  // Para void
JSInterop.Setup<T>("...");   // Para funciones con retorno
```

**Error con NavigationManager**  
→ bUnit provee `FakeNavigationManager`. Recupéralo con:
```csharp
var navManager = Services.GetRequiredService<NavigationManager>() as FakeNavigationManager;
```
