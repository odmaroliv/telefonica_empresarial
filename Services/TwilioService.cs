using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Twilio;
using Twilio.Exceptions;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Rest.Api.V2010.Account.AvailablePhoneNumberCountry;
using Twilio.Types;

namespace TelefonicaEmpresarial.Services
{
    public interface ITwilioService
    {
        /// <summary>
        /// Obtiene el precio real de un número específico desde la API de Twilio
        /// </summary>
        /// <param name="numeroTelefono">Número en formato E.164, ej: +14155552671</param>
        /// <returns>Precio mensual del número en USD</returns>
        Task<decimal> ObtenerPrecioNumero(string numeroTelefono);

        /// <summary>
        /// Verifica si un número excede el precio máximo permitido
        /// </summary>
        /// <param name="numeroTelefono">Número en formato E.164, ej: +14155552671</param>
        /// <param name="precioMaximoUSD">Precio máximo permitido en USD</param>
        /// <returns>True si el precio es aceptable, False si excede el máximo</returns>
        Task<bool> VerificarPrecioAceptable(string numeroTelefono, decimal precioMaximoUSD = 3.0m);
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10, string ciudad = "");
        Task<List<TwilioNumeroDisponible>> ObtenerNumerosPorCodigoArea(string pais, string codigoArea, int limite = 10);

        Task<TwilioNumeroComprado?> ComprarNumero(string numero);
        Task<bool> ConfigurarRedireccion(string twilioSid, string numeroDestino);
        Task<bool> ActivarSMS(string twilioSid);
        Task<bool> DesactivarSMS(string twilioSid);
        Task<bool> LiberarNumero(string twilioSid);
        Task<decimal> ObtenerCostoNumero(string numero);
        Task<decimal> ObtenerCostoSMS();
        Task<List<PaisDisponible>> ObtenerPaisesDisponibles();
        Task<bool> VerificarNumeroActivo(string sid);
        Task<bool> ConfigurarURLRechazo(string twilioSid, string rejectUrl);

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
        private readonly Dictionary<string, decimal> _preciosCache = new();
        private readonly HttpClient _httpClient;

        public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _accountSid = _configuration["Twilio:AccountSid"] ?? throw new ArgumentNullException("Twilio:AccountSid");
            _authToken = _configuration["Twilio:AuthToken"] ?? throw new ArgumentNullException("Twilio:AuthToken");
            _applicationSid = _configuration["Twilio:ApplicationSid"] ?? "";
            _httpClient = httpClient;
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

            // Configurar el HttpClient [basic
            var authString = $"{_accountSid}:{_authToken}";
            var base64Auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(authString));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64Auth);


            _httpClient.BaseAddress = new Uri("https://pricing.twilio.com/v1/");
        }

        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosDisponibles(string pais = "MX", int limite = 10, string ciudad = "")
        {
            // Configurar retry policy para resiliencia
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Buscando números disponibles para país {pais}" +
                        (!string.IsNullOrEmpty(ciudad) ? $" y localidad {ciudad}" : "") +
                        $" con límite {limite}");

                    // Si se especificó una ciudad, usamos la API de búsqueda por localidad
                    var availableNumbers = string.IsNullOrEmpty(ciudad)
                        ? await LocalResource.ReadAsync(
                            pathCountryCode: pais,
                            limit: limite)
                        : await LocalResource.ReadAsync(
                            pathCountryCode: pais,
                            inLocality: ciudad,    // Usar el parámetro ciudad
                            limit: limite);

                    var numeros = availableNumbers.Select(n => new TwilioNumeroDisponible
                    {
                        Number = n.PhoneNumber.ToString(),
                        Country = pais,
                        Type = "local",
                        MonthlyRentalRate = GetPrecioBasePorPais(pais),
                        Voice = true,
                        SMS = true,
                        // Añadir información de localidad si está disponible
                        Locality = n.Locality ?? ciudad // Usar la localidad de Twilio o la ciudad proporcionada
                    }).ToList();

                    if (numeros.Any())
                    {
                        _logger.LogInformation($"Encontrados {numeros.Count} números para {pais}" +
                            (!string.IsNullOrEmpty(ciudad) ? $" y localidad {ciudad}" : ""));
                        return numeros;
                    }

                    // Si no se encontraron números con la ciudad especificada
                    if (!string.IsNullOrEmpty(ciudad))
                    {
                        _logger.LogWarning($"No se encontraron números para {ciudad} en {pais}, intentando sin filtro de ciudad");

                        // Intentar nuevamente sin especificar ciudad
                        availableNumbers = await LocalResource.ReadAsync(
                            pathCountryCode: pais,
                            limit: limite);

                        var numerosAlternativos = availableNumbers.Select(n => new TwilioNumeroDisponible
                        {
                            Number = n.PhoneNumber.ToString(),
                            Country = pais,
                            Type = "local",
                            MonthlyRentalRate = GetPrecioBasePorPais(pais),
                            Voice = true,
                            SMS = true,
                            Locality = n.Locality
                        }).ToList();

                        if (numerosAlternativos.Any())
                        {
                            _logger.LogInformation($"Se encontraron {numerosAlternativos.Count} números alternativos para {pais}");
                            return numerosAlternativos;
                        }
                    }

                    throw new InvalidOperationException($"No se encontraron números disponibles para el país {pais}" +
                        (!string.IsNullOrEmpty(ciudad) ? $" y localidad {ciudad}" : ""));
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
                                SMS = true,
                                Locality = n.Locality
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
            try
            {
                _logger.LogInformation($"Obteniendo costo real para el número {numero}");

                // Llamar al servicio de pricing de Twilio para obtener el precio real
                decimal precioRealUSD = await ObtenerPrecioNumero(numero);

                // Convertir USD a MXN si es necesario
                decimal tipoCambioUSDMXN = await ObtenerTipoCambioActual();
                decimal precioRealMXN = precioRealUSD * tipoCambioUSDMXN;

                _logger.LogInformation($"Precio real obtenido para {numero}: ${precioRealUSD} USD (${precioRealMXN} MXN)");

                return precioRealMXN;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener costo real del número: {ex.Message}");

                // Extraer país del número y devolver un precio estimado como fallback
                string codigoPais = ExtractCountryCode(numero);
                decimal precioBase = GetPrecioBasePorPais(codigoPais);

                _logger.LogWarning($"Usando precio base para {numero}: ${precioBase} MXN");
                return precioBase;
            }
        }

        // Método para obtener el tipo de cambio actual (puedes implementarlo según tu fuente de datos)
        private async Task<decimal> ObtenerTipoCambioActual()
        {
            try
            {
                //Implementar la config adelante
                //var tipoCambio = await _context.ConfiguracionesSistema
                //    .Where(c => c.Clave == "TipoCambioUSDMXN")
                //    .Select(c => decimal.Parse(c.Valor))
                //    .FirstOrDefaultAsync();
                var tipoCambio = 20.0m;
                return tipoCambio > 0 ? tipoCambio : 20.0m; // Valor predeterminado si no hay configuración
            }
            catch
            {
                return 20.0m;
            }
        }


        /// <summary>
        /// [Deprecado] Método para determinar si un número tiene un patrón fácil de recordar 
        /// </summary>
        /// <param name="numero"></param>
        /// <returns></returns>
        private bool EsNumeroFacilRecordar(string numero)
        {
            // Quitar el código de país y caracteres no numéricos
            string digitosSolos = new string(numero.Where(char.IsDigit).ToArray());

            // Eliminar el código de país (asumiendo 1-3 dígitos para el código)
            if (digitosSolos.Length > 3)
            {
                digitosSolos = digitosSolos.Substring(Math.Min(3, digitosSolos.Length - 7));
            }

            // Verificar patrones fáciles de recordar
            // 1. Secuencias (123456, 654321)
            bool esSecuencia = true;
            for (int i = 1; i < digitosSolos.Length; i++)
            {
                if (digitosSolos[i] != digitosSolos[i - 1] + 1 && digitosSolos[i] != digitosSolos[i - 1] - 1)
                {
                    esSecuencia = false;
                    break;
                }
            }

            // 2. Dígitos repetidos (111111, 222222)
            bool esRepetido = true;
            for (int i = 1; i < digitosSolos.Length; i++)
            {
                if (digitosSolos[i] != digitosSolos[0])
                {
                    esRepetido = false;
                    break;
                }
            }

            // 3. Patrones alternantes (121212, 343434)
            bool esAlternante = true;
            if (digitosSolos.Length >= 4) // Necesitamos al menos 4 dígitos para un patrón alternante
            {
                for (int i = 2; i < digitosSolos.Length; i++)
                {
                    if (digitosSolos[i] != digitosSolos[i - 2])
                    {
                        esAlternante = false;
                        break;
                    }
                }
            }
            else
            {
                esAlternante = false;
            }

            // 4. Terminación con dígitos repetidos (xxxx0000)
            bool terminaRepetido = digitosSolos.Length >= 8 &&
                                  digitosSolos.Substring(digitosSolos.Length - 4).All(c => c == digitosSolos[digitosSolos.Length - 4]);

            return esSecuencia || esRepetido || esAlternante || terminaRepetido;
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

        public async Task<bool> VerificarNumeroActivo(string sid)
        {
            try
            {
                _logger.LogInformation($"Verificando si el número con SID {sid} sigue activo en Twilio");

                if (string.IsNullOrEmpty(sid) || sid == "pendiente" || sid == "liberado")
                {
                    return false;
                }

                // Usar el método correcto de la API de Twilio
                var phoneNumber = await IncomingPhoneNumberResource.FetchAsync(sid);

                // Si obtenemos el número sin excepciones, está activo
                return phoneNumber != null;
            }
            catch (Twilio.Exceptions.ApiException ex)
            {
                _logger.LogWarning($"Número con SID {sid} no encontrado en Twilio: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar número en Twilio: {ex.Message}");
                return false;
            }
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

        public async Task<bool> ConfigurarURLRechazo(string twilioSid, string rejectUrl)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Configurando URL de rechazo para {twilioSid} a {rejectUrl}");

                    // Actualizar el número en Twilio
                    var updateOptions = new UpdateIncomingPhoneNumberOptions(twilioSid)
                    {
                        VoiceUrl = new Uri(rejectUrl),
                        VoiceMethod = Twilio.Http.HttpMethod.Post
                    };

                    var updatedNumber = await IncomingPhoneNumberResource.UpdateAsync(updateOptions);

                    if (updatedNumber != null)
                    {
                        _logger.LogInformation($"URL de rechazo configurada correctamente para {twilioSid}");
                        return true;
                    }
                    else
                    {
                        _logger.LogWarning($"Respuesta nula al configurar URL de rechazo para {twilioSid}");
                        throw new InvalidOperationException("No se pudo configurar la URL de rechazo: respuesta nula");
                    }
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio al configurar URL de rechazo: {apiEx.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"No se pudo configurar la URL de rechazo después de {maxRetries} intentos", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general al configurar URL de rechazo: {ex.Message}");

                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al configurar la URL de rechazo después de {maxRetries} intentos", ex);
                    }
                }
            }

            throw new InvalidOperationException($"No se pudo configurar la URL de rechazo después de {maxRetries} intentos");
        }
        public async Task<List<TwilioNumeroDisponible>> ObtenerNumerosPorCodigoArea(string pais, string codigoArea, int limite = 10)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    _logger.LogInformation($"Buscando números disponibles para país {pais} y código de área {codigoArea}");

                    // Convertir el código de área a int para usarlo con Twilio
                    if (!int.TryParse(codigoArea, out int codigoAreaInt))
                    {
                        throw new ArgumentException($"El código de área '{codigoArea}' no es un número válido.");
                    }

                    // Usar el parámetro areaCode de Twilio (como int)
                    var availableNumbers = await LocalResource.ReadAsync(
                        pathCountryCode: pais,
                        areaCode: codigoAreaInt,
                        limit: limite);

                    var numeros = availableNumbers.Select(n => new TwilioNumeroDisponible
                    {
                        Number = n.PhoneNumber.ToString(),
                        Country = pais,
                        Type = "local",
                        MonthlyRentalRate = GetPrecioBasePorPais(pais),
                        Voice = true,
                        SMS = true,
                        Locality = n.Locality ?? ExtractAreaCodeInfo(n.PhoneNumber.ToString(), codigoArea)
                    }).ToList();

                    if (numeros.Any())
                    {
                        _logger.LogInformation($"Encontrados {numeros.Count} números para código de área {codigoArea}");
                        return numeros;
                    }

                    throw new InvalidOperationException($"No se encontraron números disponibles para el código de área {codigoArea}");
                }
                catch (ApiException apiEx)
                {
                    _logger.LogError($"Error de API Twilio para código de área {codigoArea}: {apiEx.Message}, Status: {apiEx.Status}");

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
                        throw new InvalidOperationException($"No se pudieron obtener números con código de área {codigoArea} después de {maxRetries} intentos: {apiEx.Message}", apiEx);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error general buscando números para código de área {codigoArea}: {ex.Message}");

                    // Incrementar contador de reintentos
                    currentRetry++;

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(1000 * currentRetry); // Espera incremental
                    }
                    else
                    {
                        throw new InvalidOperationException($"Error al buscar números para código de área {codigoArea} después de {maxRetries} intentos", ex);
                    }
                }
            }

            // Si llegamos aquí después de todos los reintentos, lanzar excepción
            throw new InvalidOperationException($"No se pudieron obtener números disponibles después de {maxRetries} intentos");
        }

        // Método auxiliar para extraer información del código de área si Twilio no proporciona localidad
        private string ExtractAreaCodeInfo(string phoneNumber, string areaCode)
        {
            // Mapa de códigos de área a localidades para EE.UU.
            var areaCodeMap = new Dictionary<string, string>
    {
        // California
        {"213", "Los Ángeles, CA"},
        {"310", "Los Ángeles (Oeste), CA"},
        {"323", "Los Ángeles (Centro), CA"},
        {"415", "San Francisco, CA"},
        {"510", "Oakland/Berkeley, CA"},
        {"530", "Sacramento (Norte), CA"},
        {"559", "Fresno, CA"},
        {"562", "Long Beach, CA"},
        {"619", "San Diego, CA"},
        {"626", "Pasadena, CA"},
        {"650", "San Mateo, CA"},
        {"661", "Bakersfield, CA"},
        {"707", "Santa Rosa, CA"},
        {"714", "Anaheim, CA"},
        {"760", "Palm Springs, CA"},
        {"805", "Santa Barbara, CA"},
        {"818", "Burbank/Glendale, CA"},
        {"831", "Monterey, CA"},
        {"858", "San Diego (Norte), CA"},
        {"909", "San Bernardino, CA"},
        {"916", "Sacramento, CA"},
        {"925", "Concord, CA"},
        {"949", "Irvine, CA"},
        {"951", "Riverside, CA"},
        
        // Nueva York
        {"212", "Nueva York (Manhattan), NY"},
        {"315", "Syracuse, NY"},
        {"516", "Long Island (Nassau), NY"},
        {"518", "Albany, NY"},
        {"585", "Rochester, NY"},
        {"607", "Binghamton, NY"},
        {"631", "Long Island (Suffolk), NY"},
        {"646", "Nueva York (Manhattan), NY"},
        {"716", "Buffalo, NY"},
        {"718", "Nueva York (Outer Boroughs), NY"},
        {"845", "Poughkeepsie, NY"},
        {"914", "Westchester, NY"},
        {"917", "Nueva York (Móvil), NY"},
        
        // Otras ciudades grandes
        {"202", "Washington, DC"},
        {"215", "Filadelfia, PA"},
        {"267", "Filadelfia, PA"},
        {"312", "Chicago (Centro), IL"},
        {"404", "Atlanta, GA"},
        {"469", "Dallas, TX"},
        {"512", "Austin, TX"},
        {"615", "Nashville, TN"},
        {"702", "Las Vegas, NV"},
        {"713", "Houston, TX"},
        {"773", "Chicago (Afueras), IL"},
        {"786", "Miami, FL"},
        {"305", "Miami, FL"},
        {"303", "Denver, CO"},
        {"314", "St. Louis, MO"},
        {"702", "Las Vegas, NV"},
        {"713", "Houston, TX"},
        {"702", "Las Vegas, NV"},
        
        // México
        {"55", "Ciudad de México, MX"},
        {"33", "Guadalajara, MX"},
        {"81", "Monterrey, MX"},
        {"664", "Tijuana, MX"},
        {"998", "Cancún, MX"},
        {"444", "San Luis Potosí, MX"},
        {"222", "Puebla, MX"},
        {"999", "Mérida, MX"},
        {"477", "León, MX"},
        {"667", "Culiacán, MX"},
        {"614", "Chihuahua, MX"},
        {"871", "Torreón, MX"},
        {"229", "Veracruz, MX"},
        {"662", "Hermosillo, MX"}
    };

            // Extraer el código de área del número si no se proporcionó uno
            if (string.IsNullOrEmpty(areaCode) && phoneNumber.StartsWith("+"))
            {
                // Intentar extraer el código de área
                if (phoneNumber.StartsWith("+1") && phoneNumber.Length >= 12)
                {
                    // Para números de EE.UU., el código de área son los siguientes 3 dígitos
                    areaCode = phoneNumber.Substring(2, 3);
                }
                else if (phoneNumber.StartsWith("+52") && phoneNumber.Length >= 12)
                {
                    // Para números de México, el código de área varía (2-3 dígitos)
                    // Para simplificar, tomamos hasta 3 dígitos después del +52
                    areaCode = phoneNumber.Length >= 15 ? phoneNumber.Substring(3, 3) : phoneNumber.Substring(3, 2);
                }
            }

            // Buscar en el mapa de códigos
            if (!string.IsNullOrEmpty(areaCode) && areaCodeMap.TryGetValue(areaCode, out var locality))
            {
                return locality;
            }

            // Si no se encuentra, devolver el código de área como información
            return $"Código de área: {areaCode}";
        }
        public async Task<decimal> ObtenerPrecioNumero(string numeroTelefono)
        {
            try
            {
                // Si tenemos el precio en caché, lo devolvemos directamente
                if (_preciosCache.TryGetValue(numeroTelefono, out decimal precioCache))
                {
                    _logger.LogInformation($"Precio para {numeroTelefono} obtenido de caché: ${precioCache} USD");
                    return precioCache;
                }

                // Extraer el código de país del número para hacer la consulta a la API de precios
                string codigoPais = ExtractCountryCode(numeroTelefono);

                // Determinar el tipo de número (local, toll-free, mobile, etc.)
                string tipoNumero = DeterminarTipoNumero(numeroTelefono);

                // Hacer la solicitud a la API de precios de Twilio
                var response = await _httpClient.GetAsync($"PhoneNumbers/Countries/{codigoPais}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Error al obtener precios para país {codigoPais}: {response.StatusCode}");
                    return GetPrecioEstimadoPorPais(codigoPais);
                }

                // Parsear la respuesta JSON
                var content = await response.Content.ReadAsStringAsync();
                var pricingInfo = JsonSerializer.Deserialize<TwilioPricingResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (pricingInfo?.PhoneNumberPrices == null || !pricingInfo.PhoneNumberPrices.Any())
                {
                    _logger.LogWarning($"No se encontraron precios para país {codigoPais}");
                    return GetPrecioEstimadoPorPais(codigoPais);
                }

                // Buscar el precio para el tipo de número específico
                var precioPorTipo = pricingInfo.PhoneNumberPrices
                    .FirstOrDefault(p => p.NumberType.ToLower() == tipoNumero.ToLower());

                if (precioPorTipo == null)
                {
                    // Si no encontramos el tipo específico, usar el precio de 'local' como fallback
                    precioPorTipo = pricingInfo.PhoneNumberPrices
                        .FirstOrDefault(p => p.NumberType.ToLower() == "local");
                }

                if (precioPorTipo == null)
                {
                    _logger.LogWarning($"No se encontró precio para tipo {tipoNumero} en país {codigoPais}");
                    return GetPrecioEstimadoPorPais(codigoPais);
                }

                // Convertir el precio a decimal y guardarlo en caché
                if (decimal.TryParse(precioPorTipo.CurrentPrice, out decimal precio))
                {
                    _preciosCache[numeroTelefono] = precio;
                    _logger.LogInformation($"Precio obtenido para {numeroTelefono}: ${precio} USD");
                    return precio;
                }

                _logger.LogWarning($"No se pudo convertir el precio '{precioPorTipo.CurrentPrice}' a decimal");
                return GetPrecioEstimadoPorPais(codigoPais);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener precio para número {numeroTelefono}");
                // Extraer el país y devolver un precio estimado
                return GetPrecioEstimadoPorPais(ExtractCountryCode(numeroTelefono));
            }
        }

        public async Task<bool> VerificarPrecioAceptable(string numeroTelefono, decimal precioMaximoUSD = 3.0m)
        {
            try
            {
                decimal precioReal = await ObtenerPrecioNumero(numeroTelefono);

                bool esAceptable = precioReal <= precioMaximoUSD;

                if (!esAceptable)
                {
                    _logger.LogWarning($"Número {numeroTelefono} excede el precio máximo. Precio: ${precioReal} USD, Máximo: ${precioMaximoUSD} USD");
                }

                return esAceptable;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar precio aceptable para {numeroTelefono}");
                // En caso de error, asumimos que el precio es aceptable para no bloquear la compra
                return true;
            }
        }




        // Método auxiliar para determinar el tipo de número
        private string DeterminarTipoNumero(string phoneNumber)
        {
            // Esta es una simplificación, en realidad necesitarías lógica más compleja
            // para identificar correctamente el tipo de número basado en sus características

            // Para EE.UU., los números que empiezan con +1 800, +1 888, +1 877, etc. son toll-free
            if (phoneNumber.StartsWith("+1"))
            {
                string areaCode = phoneNumber.Substring(2, 3);
                if (areaCode == "800" || areaCode == "888" || areaCode == "877" ||
                    areaCode == "866" || areaCode == "855" || areaCode == "844")
                {
                    return "toll free";
                }
            }

            // Por defecto, consideramos que es local
            return "local";
        }

        // Método fallback para estimar precios cuando la API no responde
        private decimal GetPrecioEstimadoPorPais(string codigoPais)
        {
            // Estos valores son estimaciones y deberían actualizarse basado en experiencia real
            var precios = new Dictionary<string, decimal>
            {
                {"US", 1.00m},
                {"MX", 1.50m},
                {"CA", 1.00m},
                {"GB", 1.50m},
                {"ES", 1.20m},
                {"DE", 1.20m},
                {"FR", 1.20m},
                {"IT", 1.20m},
                {"AU", 1.50m},
                {"NZ", 1.50m},
                {"IN", 2.00m},
                {"BR", 2.00m}
            };

            return precios.ContainsKey(codigoPais) ? precios[codigoPais] : 2.00m;
        }
    }


    public class TwilioPricingResponse
    {
        public string Country { get; set; }
        public string IsoCountry { get; set; }
        public List<PhoneNumberPrice> PhoneNumberPrices { get; set; }
        public string PriceUnit { get; set; }
        public string Url { get; set; }
    }

    public class PhoneNumberPrice
    {
        public string NumberType { get; set; }
        public string BasePrice { get; set; }
        public string CurrentPrice { get; set; }
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
        public string? Locality { get; set; }
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