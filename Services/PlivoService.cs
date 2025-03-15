namespace TelefonicaEmpresaria.Services
{
    using Microsoft.Extensions.Configuration;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;

    namespace TelefonicaEmpresarial.Services
    {
        public interface IPlivoService
        {
            Task<List<PlivoNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "mx", int limite = 10);
            Task<PlivoNumeroComprado?> ComprarNumero(string numero);
            Task<bool> ConfigurarRedireccion(string plivoUuid, string numeroDestino);
            Task<bool> ActivarSMS(string plivoUuid);
            Task<bool> DesactivarSMS(string plivoUuid);
            Task<bool> LiberarNumero(string plivoUuid);
            Task<decimal> ObtenerCostoNumero(string numero);
            Task<decimal> ObtenerCostoSMS();
        }

        public class PlivoService : IPlivoService
        {
            private readonly HttpClient _httpClient;
            private readonly IConfiguration _configuration;
            private readonly string _authId;
            private readonly string _authToken;
            private readonly string _appId;

            public PlivoService(HttpClient httpClient, IConfiguration configuration)
            {
                _httpClient = httpClient;
                _configuration = configuration;
                _authId = _configuration["Plivo:AuthId"] ?? throw new ArgumentNullException("Plivo:AuthId");
                _authToken = _configuration["Plivo:AuthToken"] ?? throw new ArgumentNullException("Plivo:AuthToken");
                _appId = _configuration["Plivo:AppId"] ?? throw new ArgumentNullException("Plivo:AppId");

                // Configurar HttpClient
                _httpClient.BaseAddress = new Uri("https://api.plivo.com/v1/Account/");

                var authBytes = Encoding.ASCII.GetBytes($"{_authId}:{_authToken}");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(authBytes));
            }

            public async Task<List<PlivoNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "mx", int limite = 10)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_authId}/PhoneNumber/?country_iso={pais}&limit={limite}&services=voice,sms");
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PlivoRespuestaNumerosDisponibles>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return result?.Objects ?? new List<PlivoNumeroDisponible>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al obtener números disponibles: {ex.Message}");
                    return new List<PlivoNumeroDisponible>();
                }
            }

            public async Task<PlivoNumeroComprado?> ComprarNumero(string numero)
            {
                try
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(new { numbers = numero }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_authId}/PhoneNumber/", content);
                    response.EnsureSuccessStatusCode();

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PlivoRespuestaCompraNumero>(responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result?.Numbers != null && result.Numbers.ContainsKey(numero))
                    {
                        return new PlivoNumeroComprado
                        {
                            Numero = numero,
                            Uuid = result.Numbers[numero],
                            Status = "Activo"
                        };
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al comprar número: {ex.Message}");
                    return null;
                }
            }

            public async Task<bool> ConfigurarRedireccion(string plivoUuid, string numeroDestino)
            {
                try
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            app_id = _appId,
                            number = numeroDestino
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_authId}/Number/{plivoUuid}/", content);
                    response.EnsureSuccessStatusCode();

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al configurar redirección: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> ActivarSMS(string plivoUuid)
            {
                try
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            app_id = _appId,
                            sms_enabled = true
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_authId}/Number/{plivoUuid}/", content);
                    response.EnsureSuccessStatusCode();

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al activar SMS: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> DesactivarSMS(string plivoUuid)
            {
                try
                {
                    var content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            app_id = _appId,
                            sms_enabled = false
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _httpClient.PostAsync($"{_authId}/Number/{plivoUuid}/", content);
                    response.EnsureSuccessStatusCode();

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al desactivar SMS: {ex.Message}");
                    return false;
                }
            }

            public async Task<bool> LiberarNumero(string plivoUuid)
            {
                try
                {
                    var response = await _httpClient.DeleteAsync($"{_authId}/Number/{plivoUuid}/");
                    response.EnsureSuccessStatusCode();

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al liberar número: {ex.Message}");
                    return false;
                }
            }

            public async Task<decimal> ObtenerCostoNumero(string numero)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_authId}/PhoneNumber/{numero}/");
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PlivoDetalleNumero>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Si no podemos obtener el costo, establecemos un valor predeterminado
                    return result?.MonthlyRentalRate ?? 5.0m;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al obtener costo del número: {ex.Message}");
                    // Valor predeterminado si hay un error
                    return 5.0m;
                }
            }

            public async Task<decimal> ObtenerCostoSMS()
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_authId}/Pricing/?country_iso=mx");
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PlivoPreciosSMS>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    // Si no podemos obtener el costo, establecemos un valor predeterminado
                    return result?.MessageRate ?? 0.05m;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al obtener costo de SMS: {ex.Message}");
                    // Valor predeterminado si hay un error
                    return 0.05m;
                }
            }
        }

        // Clases para mapear respuestas de Plivo
        public class PlivoNumeroDisponible
        {
            public string? Number { get; set; }
            public string? Country { get; set; }
            public string? Type { get; set; }
            public decimal MonthlyRentalRate { get; set; }
            public bool Voice { get; set; }
            public bool SMS { get; set; }
        }

        public class PlivoRespuestaNumerosDisponibles
        {
            public List<PlivoNumeroDisponible>? Objects { get; set; }
        }

        public class PlivoRespuestaCompraNumero
        {
            public Dictionary<string, string>? Numbers { get; set; }
            public string? Status { get; set; }
        }

        public class PlivoNumeroComprado
        {
            public string? Numero { get; set; }
            public string? Uuid { get; set; }
            public string? Status { get; set; }
        }

        public class PlivoDetalleNumero
        {
            public decimal MonthlyRentalRate { get; set; }
        }

        public class PlivoPreciosSMS
        {
            public decimal MessageRate { get; set; }
        }
    }
}
