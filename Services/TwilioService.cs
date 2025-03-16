using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Rest.Api.V2010.Account.AvailablePhoneNumberCountry;
using Twilio.Types;

namespace TelefonicaEmpresarial.Services
{
    public interface ITwilioService
    {
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10);
        Task<TwilioNumeroComprado?> ComprarNumero(string numero);
        Task<bool> ConfigurarRedireccion(string twilioSid, string numeroDestino);
        Task<bool> ActivarSMS(string twilioSid);
        Task<bool> DesactivarSMS(string twilioSid);
        Task<bool> LiberarNumero(string twilioSid);
        Task<decimal> ObtenerCostoNumero(string numero);
        Task<decimal> ObtenerCostoSMS();
        Task<List<PaisDisponible>> ObtenerPaisesDisponibles();

    }

    public class TwilioService : ITwilioService
    {
        private readonly IConfiguration _configuration;
        private readonly string _accountSid;
        private readonly string _authToken;
        private readonly string _applicationSid;
        private readonly ILogger<TwilioService> _logger;
        private readonly List<PaisDisponible> _paisesDisponibles;
        private readonly Dictionary<string, string> _bundlesPorPais;

        public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _accountSid = _configuration["Twilio:AccountSid"] ?? throw new ArgumentNullException("Twilio:AccountSid");
            _authToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken");
            _applicationSid = _configuration["Twilio:ApplicationSid"] ?? "";

            // Inicializar Twilio con credenciales
            TwilioClient.Init(_accountSid, _authToken);

            // Cargar configuración de bundles por país
            _bundlesPorPais = new Dictionary<string, string>();
            var bundlesSection = _configuration.GetSection("Twilio:Bundles");
            if (bundlesSection.Exists())
            {
                foreach (var bundleConfig in bundlesSection.GetChildren())
                {
                    var codigoPais = bundleConfig.Key;
                    var bundleId = bundleConfig.Value;
                    if (!string.IsNullOrEmpty(bundleId))
                    {
                        _bundlesPorPais[codigoPais] = bundleId;
                        _logger.LogInformation($"Bundle configurado para {codigoPais}: {bundleId}");
                    }
                }
            }
            else
            {
                _logger.LogWarning("No se encontró configuración de bundles en appsettings.json");
            }


            // Lista de países disponibles con formato correcto
            _paisesDisponibles = new List<PaisDisponible>
            {
                new PaisDisponible { Codigo = "US", Nombre = "Estados Unidos", Prefijo = "+1" },
                new PaisDisponible { Codigo = "MX", Nombre = "México", Prefijo = "+52" },
                new PaisDisponible { Codigo = "CA", Nombre = "Canadá", Prefijo = "+1" },
                new PaisDisponible { Codigo = "GB", Nombre = "Reino Unido", Prefijo = "+44" },
                new PaisDisponible { Codigo = "ES", Nombre = "España", Prefijo = "+34" },
                new PaisDisponible { Codigo = "CO", Nombre = "Colombia", Prefijo = "+57" },
                new PaisDisponible { Codigo = "AR", Nombre = "Argentina", Prefijo = "+54" },
                new PaisDisponible { Codigo = "CL", Nombre = "Chile", Prefijo = "+56" },
                new PaisDisponible { Codigo = "PE", Nombre = "Perú", Prefijo = "+51" },
                new PaisDisponible { Codigo = "BR", Nombre = "Brasil", Prefijo = "+55" }
            };
        }

        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10)
        {
            // Configurar retry policy para resiliencia
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Buscando números disponibles para el país {pais}. Intento {currentRetry + 1}/{maxRetries}");

                    // Intento para país especificado
                    var availableNumbers = await LocalResource.ReadAsync(
                        pathCountryCode: pais,
                        limit: limite
                    );

                    var numeros = availableNumbers.Select(n => new TwilioNumeroDisponible
                    {
                        Number = n.PhoneNumber.ToString(),
                        Country = pais,
                        Type = "local",
                        MonthlyRentalRate = GetPrecioBasePorPais(pais),
                        Voice = true,
                        SMS = true
                    }).ToList();

                    if (numeros.Any())
                    {
                        _logger.LogInformation($"Encontrados {numeros.Count} números para {pais}");
                        return numeros;
                    }

                    throw new InvalidOperationException($"No se encontraron números disponibles para el país {pais}");
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio para país {pais}: {apiEx.Message}, Status: {apiEx.Status}");

                    // Si el país no es compatible o no hay números, intentar con US como último recurso
                    if (pais != "US" && currentRetry == maxRetries - 1)
                    {
                        _logger.LogWarning($"Intentando con US como alternativa al país {pais}");
                        try
                        {
                            var numbersUS = await LocalResource.ReadAsync(
                                pathCountryCode: "US",
                                limit: limite
                            );

                            var numerosUS = numbersUS.Select(n => new TwilioNumeroDisponible
                            {
                                Number = n.PhoneNumber.ToString(),
                                Country = "US",
                                Type = "local",
                                MonthlyRentalRate = GetPrecioBasePorPais("US"),
                                Voice = true,
                                SMS = true
                            }).ToList();

                            if (numerosUS.Any())
                            {
                                _logger.LogInformation($"Se encontraron {numerosUS.Count} números alternativos de US");
                                return numerosUS;
                            }
                        }
                        catch (Exception usEx)
                        {
                            _logger.LogError($"Error también al buscar números de US: {usEx.Message}");
                        }
                    }

                    // Incrementar el contador de reintentos
                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        // Espera exponencial entre reintentos (0.5s, 1s, 2s, etc.)
                        int delayMs = (int)Math.Pow(2, currentRetry) * 500;
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        // Si todos los reintentos fallan, registrar y lanzar excepción
                        throw new InvalidOperationException($"No se pudieron obtener números después de {maxRetries} intentos: {apiEx.Message}", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general buscando números: {ex.Message}");

                    // Incrementar contador de reintentos
                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry); // Espera incremental
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al buscar números después de {maxRetries} intentos", ex);
                    }
                }
            }

            // Si llegamos aquí después de todos los reintentos, lanzar excepción
            throw new InvalidOperationException($"No se pudieron obtener números disponibles después de {maxRetries} intentos");
        }

        public async Task<TwilioNumeroComprado?> ComprarNumero(string numero)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            // Determinar el país basado en el número
            string codigoPais = ExtractCountryCode(numero);

            // Obtener el bundle ID para el país correspondiente
            string? bundleId = null;
            if (_bundlesPorPais.TryGetValue(codigoPais, out var configuredBundleId))
            {
                bundleId = configuredBundleId;
                _logger.LogInformation($"Usando bundle {bundleId} para país {codigoPais}");
            }
            else
            {
                _logger.LogWarning($"No hay bundle configurado para {codigoPais}. Comprando número sin bundle regulatorio.");
            }

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Intentando comprar número {numero}" +
                        (bundleId != null ? $" con bundle {bundleId}" : " sin bundle") +
                        $". Intento {currentRetry + 1}/{maxRetries}");

                    // Crear el objeto para las opciones de compra
                    var options = new CreateIncomingPhoneNumberOptions();

                    // Configurar el número
                    options.PhoneNumber = new PhoneNumber(numero);

                    string addressSid = await ObtenerDireccionEmergencia();

                    if (string.IsNullOrEmpty(addressSid))
                    {
                        throw new InvalidOperationException("No se pudo obtener una dirección de emergencia para el número.");
                    }
                    options.AddressSid = addressSid;

                    // Añadir bundle si está configurado
                    if (!string.IsNullOrEmpty(bundleId))
                    {
                        options.BundleSid = bundleId;
                    }

                    // Comprar el número
                    var incomingNumber = await IncomingPhoneNumberResource.CreateAsync(options);

                    if (incomingNumber != null)
                    {
                        _logger.LogInformation($"Número comprado correctamente: {incomingNumber.PhoneNumber}, SID: {incomingNumber.Sid}");

                        return new TwilioNumeroComprado
                        {
                            Numero = incomingNumber.PhoneNumber.ToString(),
                            Sid = incomingNumber.Sid,
                            Status = "Activo",
                            BundleId = bundleId // Incluir el bundleId utilizado

                        };
                    }
                    else
                    {
                        _logger.LogWarning("La respuesta de compra de número fue nula");
                        throw new InvalidOperationException("No se pudo comprar el número: respuesta nula de Twilio");
                    }
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al comprar número: {apiEx.Message}, Status: {apiEx.Status}");

                    // Si el error es por fondos insuficientes o restricciones de la cuenta, no reintentamos
                    if (apiEx.Status == 400 || apiEx.Message.Contains("fund") || apiEx.Message.Contains("permission"))
                    {
                        throw new InvalidOperationException($"No se puede comprar el número: {apiEx.Message}", apiEx);
                    }

                    // Reintentar para errores temporales
                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo comprar el número después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al comprar número: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al comprar el número después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo comprar el número después de {maxRetries} intentos");
        }

        public async Task<bool> ConfigurarRedireccion(string twilioSid, string numeroDestino)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Configurando redirección para {twilioSid} hacia {numeroDestino}");

                    // Construir la URL para la redirección de llamadas
                    // Esta URL apunta al controlador que acabamos de crear
                    var appDomain = _configuration["AppUrl"] ?? "https://tudominio.com";
                    // Asegúrate que numeroDestino está URL encoded para pasarlo como parámetro
                    var encodedNumeroDestino = Uri.EscapeDataString(numeroDestino);
                    var voiceUrl = $"{appDomain}/api/twilio/redirect?RedirectTo={encodedNumeroDestino}";

                    // Actualizar el número en Twilio
                    var updateOptions = new UpdateIncomingPhoneNumberOptions(twilioSid)
                    {
                        VoiceUrl = new Uri(voiceUrl),
                        VoiceMethod = Twilio.Http.HttpMethod.Post,
                        SmsUrl = new Uri($"{appDomain}/api/webhooks/twilio/sms"),
                        SmsMethod = Twilio.Http.HttpMethod.Post,
                        StatusCallback = new Uri($"{appDomain}/api/webhooks/twilio/llamada"),
                        StatusCallbackMethod = Twilio.Http.HttpMethod.Post
                    };

                    var updatedNumber = await IncomingPhoneNumberResource.UpdateAsync(updateOptions);

                    if (updatedNumber != null)
                    {
                        _logger.LogInformation($"Redirección configurada correctamente para {twilioSid} a {numeroDestino}");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"Respuesta nula al configurar redirección para {twilioSid}");
                        throw new InvalidOperationException("No se pudo configurar la redirección: respuesta nula");
                    }
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al configurar redirección: {apiEx.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo configurar la redirección después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al configurar redirección: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al configurar la redirección después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo configurar la redirección después de {maxRetries} intentos");
        }

        public async Task<bool> ActivarSMS(string twilioSid)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Activando SMS para {twilioSid}");

                    var updatedNumber = await IncomingPhoneNumberResource.UpdateAsync(
                        pathSid: twilioSid,
                        smsUrl: new Uri($"{_configuration["AppUrl"]}/api/webhooks/twilio/sms"),
                        smsMethod: Twilio.Http.HttpMethod.Post
                    );

                    if (updatedNumber != null)
                    {
                        _logger.LogInformation($"SMS activado correctamente para {twilioSid}");
                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException("No se pudo activar SMS: respuesta nula");
                    }
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al activar SMS: {apiEx.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo activar SMS después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al activar SMS: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al activar SMS después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo activar SMS después de {maxRetries} intentos");
        }

        public async Task<bool> DesactivarSMS(string twilioSid)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Desactivando SMS para {twilioSid}");

                    var updatedNumber = await IncomingPhoneNumberResource.UpdateAsync(
                        pathSid: twilioSid,
                        smsUrl: null
                    );

                    if (updatedNumber != null)
                    {
                        _logger.LogInformation($"SMS desactivado correctamente para {twilioSid}");
                        return true;
                    }
                    else
                    {
                        throw new InvalidOperationException("No se pudo desactivar SMS: respuesta nula");
                    }
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al desactivar SMS: {apiEx.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo desactivar SMS después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al desactivar SMS: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al desactivar SMS después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo desactivar SMS después de {maxRetries} intentos");
        }

        public async Task<bool> LiberarNumero(string twilioSid)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Liberando número {twilioSid}");

                    await IncomingPhoneNumberResource.DeleteAsync(twilioSid);

                    _logger.LogInformation($"Número {twilioSid} liberado correctamente");
                    return true;
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al liberar número: {apiEx.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo liberar el número después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al liberar número: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al liberar el número después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo liberar el número después de {maxRetries} intentos");
        }

        public async Task<decimal> ObtenerCostoNumero(string numero)
        {
            // Implementación real para obtener precios desde Twilio
            try
            {
                // Extraer país del número
                string codigoPais = ExtractCountryCode(numero);

                // En un entorno real, se consultaría la API de precios de Twilio
                // Como esa API no está disponible directamente, usamos un proxy de precios basados en país
                return GetPrecioBasePorPais(codigoPais);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener costo del número: {ex.Message}");
                // Valor por defecto conservador
                return 12.0m;
            }
        }

        public async Task<decimal> ObtenerCostoSMS()
        {
            try
            {
                // En un entorno real, consultaríamos la API de precios de Twilio
                // Como no está disponible directamente, usamos un valor estándar
                return 0.07m;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al obtener costo de SMS: {ex.Message}");
                return 0.07m;
            }
        }

        public async Task<List<PaisDisponible>> ObtenerPaisesDisponibles()
        {
            await Task.Delay(10); // Para mantener la firma async
            return _paisesDisponibles;
        }

        private string ExtractCountryCode(string phoneNumber)
        {
            // Implementación simple para extraer el código de país
            if (string.IsNullOrEmpty(phoneNumber))
                return "US"; // Default

            if (phoneNumber.StartsWith("+1"))
                return "US";
            else if (phoneNumber.StartsWith("+52"))
                return "MX";
            else if (phoneNumber.StartsWith("+44"))
                return "GB";
            else if (phoneNumber.StartsWith("+34"))
                return "ES";
            else if (phoneNumber.StartsWith("+57"))
                return "CO";
            else if (phoneNumber.StartsWith("+54"))
                return "AR";
            else if (phoneNumber.StartsWith("+56"))
                return "CL";
            else if (phoneNumber.StartsWith("+51"))
                return "PE";
            else if (phoneNumber.StartsWith("+55"))
                return "BR";
            else
                return "US"; // Default as fallback
        }

        private decimal GetPrecioBasePorPais(string codigoPais)
        {
            // Precios base por país (en producción, estos vendrían de una tabla o servicio)
            var precios = new Dictionary<string, decimal>
            {
                {"US", 10.0m},
                {"MX", 12.0m},
                {"CA", 10.0m},
                {"GB", 15.0m},
                {"ES", 14.0m},
                {"CO", 13.0m},
                {"AR", 12.0m},
                {"CL", 12.0m},
                {"PE", 11.0m},
                {"BR", 13.0m}
            };

            return precios.ContainsKey(codigoPais) ? precios[codigoPais] : 12.0m;
        }

        private async Task<string> ObtenerDireccionEmergencia()
        {
            try
            {
                // Primero verificar si ya tenemos direcciones
                var addresses = await AddressResource.ReadAsync();
                if (addresses.Any())
                {
                    // Usar la primera dirección existente
                    return addresses.First().Sid;
                }
                var addressConfig = _configuration.GetSection("Twilio:Address");
                // Si no hay direcciones, crear una nueva
                var address = await AddressResource.CreateAsync(
            friendlyName: addressConfig["FriendlyName"] ?? "Dirección Configurada",
            customerName: addressConfig["CustomerName"] ?? "Número Empresarial",
            street: addressConfig["Street"] ?? "Dirección no especificada",
            city: addressConfig["City"] ?? "Ciudad no especificada",
            region: addressConfig["Region"] ?? "Región no especificada",
            postalCode: addressConfig["PostalCode"] ?? "00000",
            isoCountry: addressConfig["IsoCountry"] ?? "MX",
            emergencyEnabled: bool.Parse(addressConfig["EmergencyEnabled"] ?? "true")
        );

                return address.Sid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener dirección de emergencia");
                return string.Empty;
            }
        }
    }

    // Clases para mapear respuestas y datos
    public class TwilioNumeroDisponible
    {
        public string? Number { get; set; }
        public string? Country { get; set; }
        public string? Type { get; set; }
        public decimal MonthlyRentalRate { get; set; }
        public bool Voice { get; set; }
        public bool SMS { get; set; }
    }

    public class TwilioNumeroComprado
    {
        public string? Numero { get; set; }
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? BundleId { get; set; }
    }

    public class PaisDisponible
    {
        public string Codigo { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Prefijo { get; set; } = string.Empty;
    }
}