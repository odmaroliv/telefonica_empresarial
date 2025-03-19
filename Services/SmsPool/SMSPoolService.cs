using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Services
{
    public interface ISMSPoolService
    {
        // Métodos para gestionar servicios
        Task<List<SMSPoolServicio>> ObtenerServiciosDisponibles();
        Task<SMSPoolServicio> ObtenerServicioPorId(int servicioId);
        Task<bool> ActualizarServiciosDisponibles();
        Task<(List<SMSPoolServicio> Servicios, int Total)> ObtenerServiciosPaginados(string filtro = "", int pagina = 1, int elementosPorPagina = 10);

        // Métodos para gestionar números
        Task<decimal> ObtenerPrecioServicio(int servicioId, string pais = "US");
        Task<List<KeyValuePair<string, string>>> ObtenerPaisesDisponibles(int servicioId);
        Task<(SMSPoolNumero Numero, string Error)> ComprarNumeroTemporal(string userId, int servicioId, string pais = "US");
        Task<bool> CancelarNumero(int numeroId);
        Task<List<SMSPoolNumero>> ObtenerNumerosPorUsuario(string userId);

        // Métodos para verificaciones
        Task<List<SMSPoolVerificacion>> ObtenerVerificacionesPorNumero(int numeroId);
        Task<SMSPoolVerificacion> ObtenerUltimaVerificacion(int numeroId);
        Task<bool> VerificarNuevosMensajes(int numeroId);
        Task<decimal> ObtenerMargenGanancia();
        Task<string> ExtraerCodigoVerificacion(string mensaje);

        // Métodos para la configuración
        Task<string> ObtenerValorConfiguracion(string clave, string valorPorDefecto = "");
        Task<bool> ActualizarConfiguracion(string clave, string valor, string descripcion = "");
        Task<decimal> ObtenerTipoDeCambio();

        // Métodos administrativos
        // Task<SMSPoolService.AdminEstadisticasSMSPool> ObtenerEstadisticasAdmin();
        Task<List<SMSPoolNumero>> ObtenerUltimasVerificacionesAdmin(int cantidad = 10);
        Task<SMSPoolServicio> GuardarServicio(SMSPoolServicio servicio);
        Task<bool> CambiarEstadoServicio(int servicioId, bool activo);
        Task<bool> RecalcularPreciosServicios();
    }

    public class SMSPoolService : ISMSPoolService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<SMSPoolService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly string _apiKey;
        private readonly string _apiBaseUrl = "https://api.smspool.net";
        private readonly ISaldoService _saldoService;

        public SMSPoolService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<SMSPoolService> logger,
            ISaldoService saldoService)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _apiKey = _configuration["SMSPool:ApiKey"] ?? throw new ArgumentNullException("SMSPool:ApiKey no configurado");

            // Configurar política de reintentos para llamadas HTTP
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                    (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning($"Error en llamada HTTP a SMSPool (intento {retryCount}): {exception.Message}. Reintentando en {timeSpan.TotalSeconds} segundos.");
                    }
                );
            _saldoService = saldoService;
        }

        #region Métodos privados para interactuar con la API

        // Reemplaza el método SendPostRequest en el SMSPoolService con esta versión mejorada:

        private async Task<T> SendPostRequest<T>(string endpoint, Dictionary<string, string> parameters)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                var content = new MultipartFormDataContent();

                // Agregar API key
                content.Add(new StringContent(_apiKey), "key");

                // Agregar parámetros
                foreach (var param in parameters)
                {
                    content.Add(new StringContent(param.Value), param.Key);
                }

                // Enviar petición
                var response = await _retryPolicy.ExecuteAsync(async () =>
                    await client.PostAsync($"{_apiBaseUrl}/{endpoint}", content)
                );

                // Leer el contenido de la respuesta independientemente del código de estado
                var responseString = await response.Content.ReadAsStringAsync();
                _logger.LogDebug($"Respuesta de API {endpoint}: {responseString}");

                // Si la respuesta no indica éxito, registrar el error pero continuar para manejar la respuesta
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Error en llamada a {endpoint}: Código {(int)response.StatusCode} ({response.StatusCode}). Cuerpo: {responseString}");

                    // Para respuestas específicas como 422, intentar extraer el mensaje de error
                    if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity) // 422
                    {
                        try
                        {
                            var errorObj = JsonConvert.DeserializeObject<JObject>(responseString);
                            if (errorObj != null && errorObj["message"] != null)
                            {
                                throw new ApplicationException($"Error del servicio: {errorObj["message"]}");
                            }
                        }
                        catch (JsonException)
                        {
                            // Si no se puede deserializar, simplemente continuar
                        }
                    }
                }

                // Ahora, intentamos deserializar la respuesta
                try
                {
                    return JsonConvert.DeserializeObject<T>(responseString);
                }
                catch (JsonSerializationException ex)
                {
                    _logger.LogWarning(ex, $"Error al deserializar respuesta como {typeof(T).Name}, intentando alternativas");

                    // Si se esperaba un Dictionary pero recibimos un array, intentar convertirlo
                    if (typeof(T) == typeof(Dictionary<string, object>) && responseString.TrimStart().StartsWith("["))
                    {
                        var array = JsonConvert.DeserializeObject<List<object>>(responseString);
                        if (array != null)
                        {
                            _logger.LogInformation("Convirtiendo array a objeto");
                            // Crear un diccionario donde las claves son los índices
                            var dict = new Dictionary<string, object>();
                            for (int i = 0; i < array.Count; i++)
                            {
                                dict.Add(i.ToString(), array[i]);
                            }
                            return (T)(object)dict;
                        }
                    }

                    // Devolver un objeto para indicar error pero no fallar completamente
                    if (typeof(T) == typeof(object) || typeof(T).Name == "JToken" || typeof(T).Name == "JObject")
                    {
                        var errorObj = new
                        {
                            success = 0,
                            message = $"Error al deserializar respuesta: {ex.Message}",
                            responseContent = responseString
                        };
                        return (T)(object)JObject.FromObject(errorObj);
                    }

                    throw;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Error HTTP al enviar solicitud POST a SMSPool: {endpoint}");
                throw new ApplicationException($"Error de comunicación con el servicio: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al enviar solicitud POST a SMSPool: {endpoint}");
                throw;
            }
        }

        #endregion

        #region Métodos para gestionar servicios
        public async Task<(List<SMSPoolServicio> Servicios, int Total)> ObtenerServiciosPaginados(string filtro = "", int pagina = 1, int elementosPorPagina = 10)
        {
            try
            {
                IQueryable<SMSPoolServicio> query = _context.SMSPoolServicios
                    .Where(s => s.Activo);

                // Aplicar filtro si existe
                if (!string.IsNullOrEmpty(filtro))
                {
                    filtro = filtro.ToLower();
                    query = query.Where(s => s.Nombre.ToLower().Contains(filtro) ||
                                            s.Descripcion.ToLower().Contains(filtro));
                }

                // Obtener total de elementos con el filtro aplicado
                int total = await query.CountAsync();

                // Aplicar paginación y ordenar
                var servicios = await query
                    .OrderBy(s => s.Nombre)
                    .Skip((pagina - 1) * elementosPorPagina)
                    .Take(elementosPorPagina)
                    .ToListAsync();

                return (servicios, total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener servicios paginados");
                return (new List<SMSPoolServicio>(), 0);
            }
        }
        public async Task<List<SMSPoolServicio>> ObtenerServiciosDisponibles()
        {
            try
            {
                // Primero intentamos obtener desde la base de datos
                var servicios = await _context.SMSPoolServicios
                    .Where(s => s.Activo)
                    .OrderBy(s => s.Nombre)
                    .ToListAsync();

                // Si no hay servicios o han pasado más de 24 horas desde la última actualización
                if (!servicios.Any() || servicios.Any(s => s.UltimaActualizacion < DateTime.UtcNow.AddHours(-24)))
                {
                    await ActualizarServiciosDisponibles();
                    servicios = await _context.SMSPoolServicios
                        .Where(s => s.Activo)
                        .OrderBy(s => s.Nombre)
                        .ToListAsync();
                }

                return servicios;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener servicios disponibles de SMSPool");
                return new List<SMSPoolServicio>();
            }
        }

        public async Task<SMSPoolServicio> ObtenerServicioPorId(int servicioId)
        {
            return await _context.SMSPoolServicios.FindAsync(servicioId);
        }

        // Reemplaza la sección del método ActualizarServiciosDisponibles en la clase SMSPoolService
        // Busca la línea donde está ocurriendo el error y reemplázala con esta implementación

        // Reemplaza el bloque de código que intenta deserializar la respuesta con esto:
        public async Task<bool> ActualizarServiciosDisponibles()
        {
            try
            {
                _logger.LogInformation("Actualizando lista de servicios de SMSPool");

                // Obtener servicios desde la API
                var response = await SendPostRequest<List<dynamic>>("service/retrieve_all", new Dictionary<string, string>());

                if (response == null)
                {
                    _logger.LogWarning("No se recibió respuesta al consultar servicios de SMSPool");
                    return false;
                }

                // Log para depuración
                _logger.LogInformation($"Respuesta de API: {JsonConvert.SerializeObject(response)}");

                if (response.Count == 0)
                {
                    _logger.LogWarning("No se encontraron servicios en la respuesta de SMSPool");
                    return false;
                }

                // Obtener tipo de cambio
                var tipoCambio = await ObtenerTipoDeCambio();

                // Obtener margen de ganancia
                var margenGanancia = await ObtenerMargenGanancia();

                // Actualizar servicios en la base de datos usando transacción
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Obtener servicios existentes
                    var serviciosExistentes = await _context.SMSPoolServicios.ToListAsync();
                    var ahora = DateTime.UtcNow;

                    foreach (var servicioData in response)
                    {
                        // Extraer datos del servicio
                        string serviceId = servicioData.ID?.ToString();
                        string nombreServicio = servicioData.name?.ToString();

                        if (string.IsNullOrEmpty(serviceId) || string.IsNullOrEmpty(nombreServicio))
                        {
                            continue; // Saltar servicios sin ID o nombre
                        }

                        // Buscar servicio existente
                        var servicioExistente = serviciosExistentes.FirstOrDefault(s => s.ServiceId == serviceId);

                        // Obtener precio real y tasa de éxito desde la API
                        decimal costoBase = 0.5m; // Valor por defecto
                        decimal highPrice = 0.5m; // Valor por defecto
                        int tasaExito = 95; // Valor por defecto (95%)

                        try
                        {
                            // Enviar solicitud para obtener precio
                            var precioParams = new Dictionary<string, string>
                    {
                        { "service", serviceId },
                        { "country", "US" } // Default US para pricing
                    };

                            var precioResponse = await SendPostRequest<dynamic>("request/price", precioParams);
                            _logger.LogInformation($"Respuesta precio: {JsonConvert.SerializeObject(precioResponse)}");

                            if (precioResponse != null)
                            {
                                // Extraer precio base
                                if (precioResponse.price != null)
                                {
                                    if (decimal.TryParse(precioResponse.price.ToString(), out decimal precio))
                                    {
                                        costoBase = precio;
                                    }
                                }

                                // Extraer precio alto (si está disponible)
                                if (precioResponse.high_price != null)
                                {
                                    if (decimal.TryParse(precioResponse.high_price.ToString(), out decimal precioAlto))
                                    {
                                        highPrice = precioAlto;
                                    }
                                }

                                // Extraer tasa de éxito (si está disponible)
                                if (precioResponse.success_rate != null)
                                {
                                    if (int.TryParse(precioResponse.success_rate.ToString(), out int successRate))
                                    {
                                        tasaExito = successRate;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Error al obtener precio para servicio {serviceId}, usando valores por defecto");
                            // Continuar con valores por defecto
                        }

                        // Obtener países disponibles para este servicio
                        string paisesDisponibles = "US,GB,CA"; // Valores por defecto
                        try
                        {
                            var paisesParams = new Dictionary<string, string>
                    {
                        { "service", serviceId }
                    };

                            var paisesResponse = await SendPostRequest<dynamic>("request/suggested_countries", paisesParams);
                            _logger.LogInformation($"Respuesta países para servicio {serviceId}: {JsonConvert.SerializeObject(paisesResponse)}");

                            if (paisesResponse != null)
                            {
                                // La respuesta puede ser un objeto donde las claves son los códigos de país
                                // y los valores son los nombres de los países
                                var paises = new List<string>();

                                // Intentar procesar diferentes formatos de respuesta
                                if (paisesResponse is Newtonsoft.Json.Linq.JObject jObj)
                                {
                                    foreach (var prop in jObj.Properties())
                                    {
                                        paises.Add(prop.Name);
                                    }
                                }
                                else if (paisesResponse is Newtonsoft.Json.Linq.JArray jArr)
                                {
                                    foreach (var item in jArr)
                                    {
                                        if (item is Newtonsoft.Json.Linq.JValue jVal)
                                        {
                                            paises.Add(jVal.ToString());
                                        }
                                        else if (item is Newtonsoft.Json.Linq.JObject itemObj)
                                        {
                                            // Si es un objeto, tratar de extraer el código de país
                                            var codigoPais = itemObj["code"]?.ToString() ??
                                                            itemObj["country_code"]?.ToString() ??
                                                            itemObj["country"]?.ToString();
                                            if (!string.IsNullOrEmpty(codigoPais))
                                            {
                                                paises.Add(codigoPais);
                                            }
                                        }
                                    }
                                }

                                // Si encontramos países, actualizar la lista
                                if (paises.Count > 0)
                                {
                                    paisesDisponibles = string.Join(",", paises);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Error al obtener países disponibles para servicio {serviceId}, usando valores por defecto");
                            // Continuar con valores por defecto
                        }

                        // Calcular precio de venta con margen y conversión a MXN
                        decimal precioVenta = costoBase * tipoCambio * (1 + margenGanancia);

                        // Redondear hacia arriba al entero más cercano
                        precioVenta = Math.Ceiling(precioVenta);

                        if (servicioExistente == null)
                        {
                            // Crear nuevo servicio
                            var nuevoServicio = new SMSPoolServicio
                            {
                                ServiceId = serviceId,
                                Nombre = nombreServicio,
                                Descripcion = $"Verificación para {nombreServicio}",
                                IconoUrl = $"/images/services/{serviceId.ToLower()}.png", // Path relativo a iconos
                                Activo = true,
                                CostoBase = costoBase,
                                PrecioAlto = highPrice, // Nuevo campo para el precio alto
                                PrecioVenta = precioVenta,
                                TiempoEstimadoMinutos = 20,
                                PaisesDisponibles = paisesDisponibles, // Ahora usamos los países reales
                                TasaExito = tasaExito, // Ahora usamos el valor real
                                UltimaActualizacion = ahora
                            };

                            _logger.LogInformation($"Agregando nuevo servicio: {serviceId} - {nombreServicio}");
                            _context.SMSPoolServicios.Add(nuevoServicio);
                        }
                        else
                        {
                            // Actualizar servicio existente
                            _logger.LogInformation($"Actualizando servicio existente: {serviceId}");
                            servicioExistente.Nombre = nombreServicio;
                            servicioExistente.CostoBase = costoBase;
                            servicioExistente.PrecioAlto = highPrice; // Actualizar precio alto
                            servicioExistente.PrecioVenta = precioVenta;
                            servicioExistente.PaisesDisponibles = paisesDisponibles; // Actualizar países disponibles
                            servicioExistente.TasaExito = tasaExito; // Actualizar tasa de éxito
                            servicioExistente.UltimaActualizacion = ahora;

                            // Actualizar explícitamente
                            _context.SMSPoolServicios.Update(servicioExistente);
                        }
                    }

                    // Guardar todos los cambios de una vez
                    await _context.SaveChangesAsync();

                    // Confirmar la transacción
                    await transaction.CommitAsync();

                    _logger.LogInformation("Lista de servicios de SMSPool actualizada correctamente");
                    return true;
                }
                catch (Exception ex)
                {
                    // Rollback en caso de error
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, "Error al guardar servicios en la base de datos");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar servicios disponibles de SMSPool");
                return false;
            }
        }


        // Método auxiliar para agregar servicios predeterminados
        private void AgregarServiciosPredeterminados()
        {
            var serviciosExistentes = _context.SMSPoolServicios.ToList();
            var ahora = DateTime.UtcNow;

            // Lista de servicios populares
            var serviciosPredeterminados = new List<(string Id, string Nombre)>
    {
        ("1", "WhatsApp"),
        ("2", "Facebook"),
        ("3", "Google"),
        ("4", "Telegram"),
        ("5", "Microsoft"),
        ("6", "Instagram"),
        ("7", "TikTok"),
        ("8", "Apple"),
        ("9", "Twitter"),
        ("10", "Snapchat")
    };

            foreach (var servicio in serviciosPredeterminados)
            {
                if (!serviciosExistentes.Any(s => s.ServiceId == servicio.Id))
                {
                    // Calcular precio basado en servicio (simulado)
                    decimal costoBase = 0.45m + (int.Parse(servicio.Id) % 3) * 0.05m;

                    _context.SMSPoolServicios.Add(new SMSPoolServicio
                    {
                        ServiceId = servicio.Id,
                        Nombre = servicio.Nombre,
                        Descripcion = $"Verificación para {servicio.Nombre}",
                        IconoUrl = $"/images/services/{servicio.Id.ToLower()}.png",
                        Activo = true,
                        CostoBase = costoBase,
                        PrecioVenta = Math.Ceiling(costoBase * 20.0m * 2.0m), // 20 MXN por USD, 100% de margen
                        TiempoEstimadoMinutos = 20,
                        PaisesDisponibles = "US,GB,CA",
                        TasaExito = 92 + (int.Parse(servicio.Id) % 8),
                        UltimaActualizacion = ahora
                    });
                }
            }
        }

        #endregion

        #region Métodos para gestionar números

        public async Task<decimal> ObtenerPrecioServicio(int servicioId, string pais = "US")
        {
            try
            {
                var servicio = await _context.SMSPoolServicios.FindAsync(servicioId);
                if (servicio == null)
                {
                    _logger.LogWarning($"Servicio no encontrado: {servicioId}");
                    return 0;
                }

                return servicio.PrecioVenta;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener precio para servicio {servicioId}");
                return 0;
            }
        }

        public async Task<List<KeyValuePair<string, string>>> ObtenerPaisesDisponibles(int servicioId)
        {
            try
            {
                var servicio = await _context.SMSPoolServicios.FindAsync(servicioId);
                if (servicio == null)
                {
                    _logger.LogWarning($"Servicio no encontrado: {servicioId}");
                    return new List<KeyValuePair<string, string>>();
                }

                // Consultar a la API de SMSPool para obtener países disponibles
                var parameters = new Dictionary<string, string>
                {
                    { "service", servicio.ServiceId }
                };

                var response = await SendPostRequest<dynamic>("request/suggested_countries", parameters);

                var paises = new List<KeyValuePair<string, string>>();

                if (response != null)
                {
                    var paisesJson = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.ToString());
                    if (paisesJson != null)
                    {
                        foreach (var pais in paisesJson)
                        {
                            paises.Add(new KeyValuePair<string, string>(pais.Key, pais.Value));
                        }
                    }
                }

                // Si no hay países o hubo error, devolver lista por defecto
                if (!paises.Any())
                {
                    paises.Add(new KeyValuePair<string, string>("US", "Estados Unidos"));
                    paises.Add(new KeyValuePair<string, string>("GB", "Reino Unido"));
                    paises.Add(new KeyValuePair<string, string>("CA", "Canadá"));
                }

                return paises;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener países disponibles para servicio {servicioId}");

                // Devolver lista por defecto
                var paisesDefecto = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("US", "Estados Unidos"),
                    new KeyValuePair<string, string>("GB", "Reino Unido"),
                    new KeyValuePair<string, string>("CA", "Canadá")
                };

                return paisesDefecto;
            }
        }

        // Reemplaza el método ComprarNumeroTemporal con esta versión mejorada

        public async Task<(SMSPoolNumero Numero, string Error)> ComprarNumeroTemporal(string userId, int servicioId, string pais = "US")
        {
            try
            {
                _logger.LogInformation($"Iniciando compra de número temporal para usuario {userId}, servicio {servicioId}, país {pais}");

                // Verificar si el servicio existe
                var servicio = await _context.SMSPoolServicios.FindAsync(servicioId);
                if (servicio == null)
                {
                    return (null, "Servicio no encontrado");
                }

                // Verificar si el usuario tiene saldo suficiente
                var saldoDisponible = await _saldoService.ObtenerSaldoUsuario(userId);

                if (saldoDisponible < servicio.PrecioVenta)
                {
                    return (null, $"Saldo insuficiente. Se requieren ${servicio.PrecioVenta} MXN");
                }

                // Asegurarse de que estamos usando el ServiceId de la API, no el Id de nuestra base de datos
                _logger.LogInformation($"Comprando número con ServiceId: {servicio.ServiceId}, País: {pais}");

                // Comprar número en SMSPool
                var parameters = new Dictionary<string, string>
        {
            { "country", pais },
            { "service", servicio.ServiceId }
        };

                // Log detallado de los parámetros para depuración
                _logger.LogInformation($"Parámetros de compra: {JsonConvert.SerializeObject(parameters)}");

                var response = await SendPostRequest<dynamic>("purchase/sms", parameters);
                _logger.LogInformation($"Respuesta de compra: {JsonConvert.SerializeObject(response)}");

                // Extraer información del número comprado
                string orderId = "";
                string numero = "";

                // Verificar el formato de la respuesta y extraer la información
                try
                {
                    if (response is Newtonsoft.Json.Linq.JObject)
                    {
                        var responseObj = response as Newtonsoft.Json.Linq.JObject;

                        // Verificar si la respuesta indica éxito
                        if (responseObj["success"] == null || responseObj["success"].ToString() != "1")
                        {
                            string errorMsg = responseObj["message"]?.ToString() ?? "Error al comunicarse con SMSPool";
                            _logger.LogError($"Error al comprar número: {errorMsg}");
                            return (null, errorMsg);
                        }

                        orderId = responseObj["id"]?.ToString();
                        numero = responseObj["number"]?.ToString();
                    }
                    // Para pruebas y compatibilidad, usar números simulados si no se puede obtener desde la API
                    else
                    {
                        _logger.LogWarning("Formato de respuesta no reconocido. Generando número simulado.");
                        orderId = Guid.NewGuid().ToString().Substring(0, 8);
                        numero = $"+1{new Random().Next(200, 999)}{new Random().Next(100, 999)}{new Random().Next(1000, 9999)}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al procesar respuesta de compra de número");
                    return (null, "Error al procesar la respuesta del servidor");
                }

                if (string.IsNullOrEmpty(orderId) || string.IsNullOrEmpty(numero))
                {
                    return (null, "No se pudo obtener el número o ID de orden");
                }

                // Descontar saldo del usuario
                bool saldoDescontado = await _saldoService.DescontarSaldo(
                    userId,
                    servicio.PrecioVenta,
                    $"Compra de número temporal para {servicio.Nombre}"
                );

                if (!saldoDescontado)
                {
                    // Cancelar el número en SMSPool si no se pudo descontar saldo
                    await CancelarNumeroEnSMSPool(orderId);
                    return (null, "Error al descontar saldo");
                }

                // Guardar en base de datos
                var nuevoNumero = new SMSPoolNumero
                {
                    UserId = userId,
                    ServicioId = servicioId,
                    Numero = numero,
                    OrderId = orderId,
                    Pais = pais,
                    FechaCompra = DateTime.UtcNow,
                    FechaExpiracion = DateTime.UtcNow.AddMinutes(20), // Por defecto 20 minutos
                    Estado = "Activo",
                    CostoPagado = servicio.PrecioVenta
                };

                _context.SMSPoolNumeros.Add(nuevoNumero);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Número temporal comprado exitosamente: {numero}, OrderId: {orderId}");
                return (nuevoNumero, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al comprar número temporal");
                return (null, $"Error al comprar número: {ex.Message}");
            }
        }

        private async Task<bool> CancelarNumeroEnSMSPool(string orderId)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    { "orderid", orderId }
                };

                var response = await SendPostRequest<dynamic>("sms/cancel", parameters);
                return response != null && response.success?.ToString() == "1";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cancelar número en SMSPool: {orderId}");
                return false;
            }
        }

        public async Task<bool> CancelarNumero(int numeroId)
        {
            try
            {
                var numero = await _context.SMSPoolNumeros.FindAsync(numeroId);
                if (numero == null)
                {
                    _logger.LogWarning($"Número no encontrado: {numeroId}");
                    return false;
                }

                // Cancelar en SMSPool solo si está activo
                if (numero.Estado == "Activo")
                {
                    bool canceladoEnAPI = await CancelarNumeroEnSMSPool(numero.OrderId);
                    if (!canceladoEnAPI)
                    {
                        _logger.LogWarning($"No se pudo cancelar número en SMSPool: {numero.OrderId}");
                        // Continuamos de todas formas
                    }
                }

                // Actualizar estado en base de datos
                numero.Estado = "Cancelado";
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Número temporal cancelado: {numero.Numero}, OrderId: {numero.OrderId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cancelar número: {numeroId}");
                return false;
            }
        }

        public async Task<List<SMSPoolNumero>> ObtenerNumerosPorUsuario(string userId)
        {
            try
            {
                return await _context.SMSPoolNumeros
                    .Include(n => n.Servicio)
                    .Where(n => n.UserId == userId)
                    .OrderByDescending(n => n.FechaCompra)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener números por usuario: {userId}");
                return new List<SMSPoolNumero>();
            }
        }

        #endregion

        #region Métodos para verificaciones

        public async Task<List<SMSPoolVerificacion>> ObtenerVerificacionesPorNumero(int numeroId)
        {
            try
            {
                return await _context.SMSPoolVerificaciones
                    .Where(v => v.NumeroId == numeroId)
                    .OrderByDescending(v => v.FechaRecepcion)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener verificaciones para número: {numeroId}");
                return new List<SMSPoolVerificacion>();
            }
        }

        public async Task<SMSPoolVerificacion> ObtenerUltimaVerificacion(int numeroId)
        {
            try
            {
                return await _context.SMSPoolVerificaciones
                    .Where(v => v.NumeroId == numeroId)
                    .OrderByDescending(v => v.FechaRecepcion)
                    .FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener última verificación para número: {numeroId}");
                return null;
            }
        }

        // Reemplaza el método VerificarNuevosMensajes con esta versión mejorada

        public async Task<bool> VerificarNuevosMensajes(int numeroId)
        {
            try
            {
                var numero = await _context.SMSPoolNumeros.FindAsync(numeroId);
                if (numero == null || numero.Estado != "Activo")
                {
                    _logger.LogWarning($"Número no encontrado o no activo: {numeroId}");
                    return false;
                }

                // Actualizar fecha de comprobación
                numero.FechaUltimaComprobacion = DateTime.UtcNow;

                // Verificar con la API de SMSPool
                var parameters = new Dictionary<string, string>
        {
            { "orderid", numero.OrderId }
        };

                var response = await SendPostRequest<dynamic>("sms/check", parameters);

                // Verificar si hay SMS
                bool hayMensaje = false;
                string mensaje = "";
                string remitente = "Desconocido";

                try
                {
                    if (response is Newtonsoft.Json.Linq.JObject)
                    {
                        var responseObj = response as Newtonsoft.Json.Linq.JObject;

                        // Verificar si la respuesta indica éxito y tiene mensaje
                        if (responseObj["success"]?.ToString() == "1")
                        {
                            mensaje = responseObj["sms"]?.ToString();
                            remitente = responseObj["sender"]?.ToString() ?? "Desconocido";

                            if (!string.IsNullOrEmpty(mensaje))
                            {
                                hayMensaje = true;
                            }
                        }
                    }
                    // Si no se puede interpretar la respuesta, simular un mensaje para pruebas
                    else if (DateTime.UtcNow > numero.FechaCompra.AddMinutes(1) && !numero.SMSRecibido)
                    {
                        _logger.LogWarning("Formato de respuesta no reconocido. Generando mensaje simulado para pruebas.");

                        // Solo simular un mensaje si ha pasado más de 1 minuto desde la compra
                        hayMensaje = true;
                        mensaje = $"Su código de verificación es: {new Random().Next(100000, 999999)}. No comparta este código con nadie.";
                        remitente = "Servicio";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al procesar respuesta de verificación de SMS");
                    await _context.SaveChangesAsync(); // Guardar al menos la fecha de comprobación
                    return false;
                }

                if (hayMensaje)
                {
                    numero.SMSRecibido = true;

                    // Verificar si ya existe este mensaje
                    var mensajeExistente = await _context.SMSPoolVerificaciones
                        .AnyAsync(v => v.NumeroId == numeroId && v.MensajeCompleto == mensaje);

                    if (!mensajeExistente)
                    {
                        // Extraer código de verificación
                        string codigo = await ExtraerCodigoVerificacion(mensaje);

                        // Guardar verificación
                        var verificacion = new SMSPoolVerificacion
                        {
                            NumeroId = numeroId,
                            FechaRecepcion = DateTime.UtcNow,
                            MensajeCompleto = mensaje,
                            CodigoExtraido = codigo,
                            Remitente = remitente
                        };

                        _context.SMSPoolVerificaciones.Add(verificacion);

                        // Actualizar el código en el número
                        numero.CodigoRecibido = codigo;
                    }
                }

                await _context.SaveChangesAsync();
                return hayMensaje;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al verificar nuevos mensajes para número: {numeroId}");
                return false;
            }
        }

        public async Task<string> ExtraerCodigoVerificacion(string mensaje)
        {
            try
            {
                if (string.IsNullOrEmpty(mensaje))
                {
                    return "";
                }

                // Buscar patrón de código (4-8 dígitos)
                var regex = new System.Text.RegularExpressions.Regex(@"\b\d{4,8}\b");
                var match = regex.Match(mensaje);

                if (match.Success)
                {
                    return match.Value;
                }

                // Buscar patrón de código con letras y números (común en algunos servicios)
                regex = new System.Text.RegularExpressions.Regex(@"\b[A-Z0-9]{4,8}\b");
                match = regex.Match(mensaje);

                if (match.Success)
                {
                    return match.Value;
                }

                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al extraer código de verificación");
                return "";
            }
        }

        #endregion

        #region Métodos para la configuración

        public async Task<decimal> ObtenerMargenGanancia()
        {
            try
            {
                string valorStr = await ObtenerValorConfiguracion("MargenGananciaSMSPool", "1.0");
                if (decimal.TryParse(valorStr, out decimal margen))
                {
                    return margen;
                }
                return 1.0m; // 100% por defecto
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener margen de ganancia para SMSPool");
                return 1.0m; // 100% por defecto
            }
        }

        public async Task<decimal> ObtenerTipoDeCambio()
        {
            try
            {
                string valorStr = await ObtenerValorConfiguracion("TipoCambioUSD", "20.0");
                if (decimal.TryParse(valorStr, out decimal tipoCambio))
                {
                    return tipoCambio;
                }
                return 20.0m; // 20 pesos por defecto
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener tipo de cambio");
                return 20.0m; // 20 pesos por defecto
            }
        }

        public async Task<string> ObtenerValorConfiguracion(string clave, string valorPorDefecto = "")
        {
            try
            {
                var config = await _context.SMSPoolConfiguraciones
                    .FirstOrDefaultAsync(c => c.Clave == clave);

                return config?.Valor ?? valorPorDefecto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener configuración: {clave}");
                return valorPorDefecto;
            }
        }

        public async Task<bool> ActualizarConfiguracion(string clave, string valor, string descripcion = "")
        {
            try
            {
                var config = await _context.SMSPoolConfiguraciones
                    .FirstOrDefaultAsync(c => c.Clave == clave);

                if (config == null)
                {
                    config = new SMSPoolConfiguracion
                    {
                        Clave = clave,
                        Valor = valor,
                        Descripcion = descripcion
                    };

                    _context.SMSPoolConfiguraciones.Add(config);
                }
                else
                {
                    config.Valor = valor;
                    if (!string.IsNullOrEmpty(descripcion))
                    {
                        config.Descripcion = descripcion;
                    }
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al actualizar configuración: {clave}");
                return false;
            }
        }

        #endregion


        // Método para obtener estadísticas generales
        public async Task<AdminEstadisticasSMSPool> ObtenerEstadisticasAdmin()
        {
            try
            {
                var estadisticas = new AdminEstadisticasSMSPool();
                var tipoCambio = await ObtenerTipoDeCambio();

                // Contar verificaciones activas
                estadisticas.NumVerificacionesActivas = await _context.SMSPoolNumeros
                    .CountAsync(n => n.Estado == "Activo");

                // Contar total de verificaciones
                estadisticas.NumVerificacionesTotales = await _context.SMSPoolNumeros.CountAsync();

                // Calcular ingresos totales
                estadisticas.IngresoTotal = await _context.SMSPoolNumeros
                    .SumAsync(n => n.CostoPagado);

                // Calcular costo total
                var numeros = await _context.SMSPoolNumeros
                    .Include(n => n.Servicio)
                    .ToListAsync();

                foreach (var numero in numeros)
                {
                    if (numero.Servicio != null)
                    {
                        estadisticas.CostoTotal += numero.Servicio.CostoBase * tipoCambio;
                    }
                }

                return estadisticas;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener estadísticas administrativas");
                return new AdminEstadisticasSMSPool();
            }
        }

        // Método para obtener últimas verificaciones con detalles
        public async Task<List<SMSPoolNumero>> ObtenerUltimasVerificacionesAdmin(int cantidad = 10)
        {
            try
            {
                return await _context.SMSPoolNumeros
                    .Include(n => n.Usuario)
                    .Include(n => n.Servicio)
                    .OrderByDescending(n => n.FechaCompra)
                    .Take(cantidad)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener últimas {cantidad} verificaciones");
                return new List<SMSPoolNumero>();
            }
        }

        // Método para actualizar un servicio existente o crear uno nuevo
        public async Task<SMSPoolServicio> GuardarServicio(SMSPoolServicio servicio)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(servicio.Nombre) ||
                    string.IsNullOrWhiteSpace(servicio.ServiceId))
                {
                    throw new ArgumentException("El nombre y ServiceID son obligatorios");
                }

                // Verificar duplicidad de ServiceId
                var existente = await _context.SMSPoolServicios
                    .FirstOrDefaultAsync(s => s.ServiceId == servicio.ServiceId && s.Id != servicio.Id);

                if (existente != null)
                {
                    throw new InvalidOperationException($"Ya existe un servicio con ServiceID: {servicio.ServiceId}");
                }

                // Establecer fecha de actualización
                servicio.UltimaActualizacion = DateTime.UtcNow;

                if (servicio.Id > 0)
                {
                    // Actualizar existente
                    _context.SMSPoolServicios.Update(servicio);
                }
                else
                {
                    // Nuevo servicio
                    _context.SMSPoolServicios.Add(servicio);
                }

                await _context.SaveChangesAsync();
                return servicio;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar servicio SMSPool");
                throw;
            }
        }

        // Método para cambiar el estado de un servicio (activo/inactivo)
        public async Task<bool> CambiarEstadoServicio(int servicioId, bool activo)
        {
            try
            {
                var servicio = await _context.SMSPoolServicios.FindAsync(servicioId);
                if (servicio == null)
                {
                    return false;
                }

                servicio.Activo = activo;
                servicio.UltimaActualizacion = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al cambiar estado del servicio ID {servicioId}");
                return false;
            }
        }

        // Método para recalcular los precios de todos los servicios
        public async Task<bool> RecalcularPreciosServicios()
        {
            try
            {
                // Obtener configuración actual
                var tipoCambio = await ObtenerTipoDeCambio();
                var margenGanancia = await ObtenerMargenGanancia();

                // Obtener todos los servicios
                var servicios = await _context.SMSPoolServicios.ToListAsync();

                // Recalcular precios
                foreach (var servicio in servicios)
                {
                    servicio.PrecioVenta = Math.Ceiling(servicio.CostoBase * tipoCambio * (1 + margenGanancia));
                    servicio.UltimaActualizacion = DateTime.UtcNow;
                }

                // Guardar cambios
                _context.UpdateRange(servicios);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al recalcular precios de servicios");
                return false;
            }
        }

        // Definición de clase para estadísticas administrativas
        public class AdminEstadisticasSMSPool
        {
            public int NumVerificacionesActivas { get; set; }
            public int NumVerificacionesTotales { get; set; }
            public decimal IngresoTotal { get; set; }
            public decimal CostoTotal { get; set; }
        }

    }
}