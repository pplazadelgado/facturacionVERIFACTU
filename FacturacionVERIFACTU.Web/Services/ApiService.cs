using Microsoft.AspNetCore.Identity.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FacturacionVERIFACTU.Web.Services
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;
        private readonly IAuthService _authService;
        private readonly ILogger<ApiService> _logger;

        public ApiService(
            HttpClient httpCient,
            IAuthService authService,
            ILogger<ApiService> logger)
        {
            _httpClient = httpCient;
            _authService = authService;
            _logger = logger;
        }

        private async Task AddAuthHeaderAsync()
        {
            var token = await _authService.GetTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
                    _logger.LogWarning("GET {Endpoint} falló: {Status} - {Error}", endpoint, response.StatusCode,errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<T>();
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
                var response = await _httpClient.PostAsJsonAsync(endpoint, data);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("POST {Endpoint} falló: {Status} - {Error}", endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<TResponse>();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error en POST {Endpoint}", endpoint);
                return default;
            }
        }

        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                await AddAuthHeaderAsync();
                var response = await _httpClient.PutAsJsonAsync(endpoint, data);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("PUT {Endpoint} falló: {Status} - {Error}", endpoint, response.StatusCode,errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }

                return await response.Content.ReadFromJsonAsync<TResponse>();
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
                    Content = JsonContent.Create(data)
                };
                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = await ExtractErrorMessageAsync(response);
                    _logger.LogWarning("PATC {Endpoint} fallo: {Status} - {Error}", endpoint, response.StatusCode, errorMessage);
                    throw new HttpRequestException(errorMessage ?? $"Error {response.StatusCode}");
                }
                return await response.Content.ReadFromJsonAsync<TResponse>();
            }
            catch (HttpRequestException)
            {
                throw;
            }
            catch(Exception ex)
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
            {
                return "Se produjo un error al procesar la solicitud.";
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    {
                        return message.GetString();
                    }

                    if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                    {
                        return detail.GetString();
                    }

                    if (root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                    {
                        return title.GetString();
                    }
                }
            }
            catch (JsonException)
            {
            }

            return content;
        }
    }
}
