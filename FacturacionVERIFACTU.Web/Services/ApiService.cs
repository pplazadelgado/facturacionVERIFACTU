using Microsoft.AspNetCore.Identity.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FacturacionVERIFACTU.Web.Services
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly ILogger<ApiService> _logger;

        // ← Opciones compartidas para toda la clase
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public ApiService(
            HttpClient httpClient,
            IAuthService authService,
            ILogger<ApiService> logger)
        {
            _httpClient = httpClient;
            _authService = authService;
            _logger = logger;
        }

        private async Task AddAuthHeaderAsync()
        {
            var token = await _authService.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        public async Task<T?> GetAsync<T>(string endpoint)
        {
            try
            {
                await AddAuthHeaderAsync();
                var response = await _httpClient.GetAsync(endpoint);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("GET {Endpoint} falló: {Status} - {Error}",
                        endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en GET {Endpoint}", endpoint);
                return default;
            }
        }

        public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                await AddAuthHeaderAsync();
                // ← Usar JsonContent.Create con opciones en lugar de PostAsJsonAsync
                var content = JsonContent.Create(data, options: _jsonOptions);
                var response = await _httpClient.PostAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("POST {Endpoint} falló: {Status} - {Error}",
                        endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en POST {Endpoint}", endpoint);
                return default;
            }
        }

        public async Task<ApiResult> PostAsyncDetailed<TRequest>(string endpoint, TRequest data)
        {
            try
            {
                await AddAuthHeaderAsync();
                var content = JsonContent.Create(data, options: _jsonOptions);
                var response = await _httpClient.PostAsync(endpoint, content);

                var errorMessage = response.IsSuccessStatusCode
                    ? null
                    : await ExtractErrorMessageAsync(response);

                return new ApiResult(response.IsSuccessStatusCode, response.StatusCode, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en POST {Endpoint}", endpoint);
                return new ApiResult(false, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                await AddAuthHeaderAsync();
                var content = JsonContent.Create(data, options: _jsonOptions);
                var response = await _httpClient.PutAsync(endpoint, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("PUT {Endpoint} falló: {Status} - {Error}",
                        endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en PUT {Endpoint}", endpoint);
                return default;
            }
        }

        public async Task<TResponse?> PatchAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                await AddAuthHeaderAsync();
                var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
                {
                    Content = JsonContent.Create(data, options: _jsonOptions)
                };
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("PATCH {Endpoint} falló: {Status} - {Error}",
                        endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en PATCH {Endpoint}", endpoint);
                return default;
            }
        }

        public async Task<bool> DeleteAsync(string endpoint)
        {
            var result = await DeleteAsyncDetailed(endpoint);
            return result.Success;
        }

        public async Task<ApiResult> DeleteAsyncDetailed(string endpoint)
        {
            try
            {
                await AddAuthHeaderAsync();
                var response = await _httpClient.DeleteAsync(endpoint);

                var errorMessage = response.IsSuccessStatusCode
                    ? null
                    : await ExtractErrorMessageAsync(response);

                return new ApiResult(response.IsSuccessStatusCode, response.StatusCode, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en DELETE {Endpoint}", endpoint);
                return new ApiResult(false, HttpStatusCode.InternalServerError, ex.Message);
            }
        }

        private static async Task<string?> ExtractErrorMessageAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
                return "Se produjo un error al procesar la solicitud.";

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    // Errores de validación de ASP.NET (errors.campo[])
                    if (root.TryGetProperty("errors", out var errors) &&
                        errors.ValueKind == JsonValueKind.Object)
                    {
                        var mensajes = new List<string>();
                        foreach (var prop in errors.EnumerateObject())
                        {
                            foreach (var msg in prop.Value.EnumerateArray())
                            {
                                mensajes.Add(msg.GetString() ?? string.Empty);
                            }
                        }
                        if (mensajes.Any())
                            return string.Join(" | ", mensajes);
                    }

                    if (root.TryGetProperty("mensaje", out var mensaje) &&
                        mensaje.ValueKind == JsonValueKind.String)
                        return mensaje.GetString();

                    if (root.TryGetProperty("message", out var message) &&
                        message.ValueKind == JsonValueKind.String)
                        return message.GetString();

                    if (root.TryGetProperty("title", out var title) &&
                        title.ValueKind == JsonValueKind.String)
                        return title.GetString();
                }
            }
            catch (JsonException) { }

            return content;
        }
    }
}