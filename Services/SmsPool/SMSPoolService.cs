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
        Task<SMSPoolNumero> ObtenerNumeroPorId(int numeroId);
        Task<bool> ResolverNumerosPendientes(string userId);
        Task<bool> SincronizarComprasActivas(string userId);
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

        public async Task<SMSPoolNumero> ObtenerNumeroPorId(int numeroId)
        {
            try
            {
                // Obtener el número por su ID, incluyendo el servicio relacionado
                var numero = await _context.SMSPoolNumeros
                    .Include(n => n.Servicio)
                    .FirstOrDefaultAsync(n => n.Id == numeroId);

                if (numero == null)
                {
                    _logger.LogWarning($"Número con ID {numeroId} no encontrado");
                    return null;
                }

                // Si el número está en estado pendiente, intentar sincronizar
                if (numero.Estado == "Pendiente")
                {
                    await SincronizarComprasActivas(numero.UserId);

                    // Recargar el número después de la sincronización
                    numero = await _context.SMSPoolNumeros
                        .Include(n => n.Servicio)
                        .FirstOrDefaultAsync(n => n.Id == numeroId);
                }

                return numero;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al obtener número por ID: {numeroId}");
                return null;
            }
        }


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

                // Verificar si la respuesta es HTML en lugar de JSON
                if (responseString.TrimStart().StartsWith("<"))
                {
                    _logger.LogWarning($"Respuesta en formato HTML detectada para {endpoint}: {responseString.Substring(0, Math.Min(200, responseString.Length))}");

                    // Si es una compra, registrar que la respuesta fue en formato HTML y continuar
                    if (endpoint == "purchase/sms" && response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("La operación de compra probablemente fue exitosa pero la respuesta es HTML. Se marcará para conciliación.");

                        // Devolver un objeto que indique que se debe verificar mediante conciliación
                        if (typeof(T) == typeof(object) || typeof(T).Name == "JToken" || typeof(T).Name == "JObject")
                        {
                            var conciliacionObj = new
                            {
                                success = 0,
                                message = "La compra podría haber sido exitosa pero se requiere conciliación para confirmar",
                                requiere_conciliacion = true,
                                parametros = parameters
                            };

                            return (T)(object)JObject.FromObject(conciliacionObj);
                        }
                    }

                    // Para otros endpoints, lanzar excepción específica que puede ser manejada
                    throw new ApplicationException($"La API devolvió HTML en lugar de JSON para el endpoint {endpoint}");
                }

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
                    if (typeof(T) == typeof(JArray))
                    {
                        return (T)(object)JArray.Parse(responseString);
                    }
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

        public async Task<bool> SincronizarComprasActivas(string userId)
        {
            try
            {
                // Consultar compras activas en SMSPool
                var response = await SendPostRequest<dynamic>("request/active", new Dictionary<string, string>());

                if (response == null)
                {
                    return false;
                }

                // Procesar cada compra activa
                foreach (var compra in response)
                {
                    string orderId = compra.id?.ToString();
                    string numero = compra.number?.ToString();
                    string serviceId = compra.service?.ToString();

                    // Verificar si ya existe en nuestra base de datos
                    var numeroExistente = await _context.SMSPoolNumeros
                        .FirstOrDefaultAsync(n => n.OrderId == orderId);

                    if (numeroExistente == null)
                    {
                        // Buscar el servicio por su ServiceId
                        var servicio = await _context.SMSPoolServicios
                            .FirstOrDefaultAsync(s => s.ServiceId == serviceId);

                        if (servicio != null)
                        {
                            // Crear el registro en nuestra base de datos
                            var nuevoNumero = new SMSPoolNumero
                            {
                                UserId = userId,
                                ServicioId = servicio.Id,
                                Numero = numero,
                                OrderId = orderId,
                                Pais = compra.country?.ToString() ?? "US",
                                FechaCompra = DateTime.UtcNow,
                                FechaExpiracion = DateTime.UtcNow.AddMinutes(20),
                                Estado = "Activo",
                                CostoPagado = servicio.PrecioVenta,
                                SMSRecibido = false
                            };

                            _context.SMSPoolNumeros.Add(nuevoNumero);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al sincronizar compras activas con SMSPool");
                return false;
            }
        }
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

                var response = await SendPostRequest<JArray>("request/suggested_countries", parameters);
                var paises = new List<KeyValuePair<string, string>>();

                if (response != null)
                {
                    try
                    {
                        // Procesar el array de países
                        foreach (JObject countryObj in response)
                        {
                            string shortName = countryObj["short_name"]?.ToString();
                            string fullName = countryObj["name"]?.ToString();

                            if (!string.IsNullOrEmpty(shortName) && !string.IsNullOrEmpty(fullName))
                            {
                                paises.Add(new KeyValuePair<string, string>(shortName, fullName));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al procesar la lista de países: " + ex.Message);
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

        // Método auxiliar para obtener nombres de país desde códigos
        private string GetCountryName(string countryCode)
        {
            var countryNames = new Dictionary<string, string>
    {
        { "US", "Estados Unidos" },
        { "GB", "Reino Unido" },
        { "CA", "Canadá" },
        { "ES", "España" },
        { "MX", "México" },

    };

            return countryNames.ContainsKey(countryCode) ? countryNames[countryCode] : countryCode;
        }

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

                try
                {
                    // Intentar comprar número
                    var parameters = new Dictionary<string, string>
            {
                { "country", pais },
                { "service", servicio.ServiceId }
            };

                    var response = await SendPostRequest<dynamic>("purchase/sms", parameters);

                    // Procesar respuesta exitosa
                    if (response != null && response.success != null && response.success.ToString() == "1")
                    {
                        // Buscar el ID de orden en diferentes campos posibles
                        string orderId = null;

                        // Intentar extraer el ID de diferentes campos en la respuesta
                        if (response.orderid != null)
                        {
                            orderId = response.orderid.ToString();
                        }
                        else if (response.order_id != null)
                        {
                            orderId = response.order_id.ToString();
                        }
                        else if (response.id != null)
                        {
                            orderId = response.id.ToString();
                        }

                        // Si no se encontró ningún ID, generar uno temporal
                        if (string.IsNullOrEmpty(orderId))
                        {
                            _logger.LogWarning("No se pudo extraer OrderId de la respuesta de la API. Generando ID temporal.");
                            orderId = "TEMP-" + Guid.NewGuid().ToString();
                        }

                        string numero = response.number?.ToString() ?? "Pendiente";

                        // Descontar saldo y guardar en base de datos
                        bool saldoDescontado = await _saldoService.DescontarSaldo(
                            userId,
                            servicio.PrecioVenta,
                            $"Compra de número temporal para {servicio.Nombre}"
                        );

                        if (!saldoDescontado)
                        {
                            await CancelarNumeroEnSMSPool(orderId);
                            return (null, "Error al descontar saldo");
                        }

                        var nuevoNumero = new SMSPoolNumero
                        {
                            UserId = userId,
                            ServicioId = servicioId,
                            Numero = numero,
                            OrderId = orderId,
                            CodigoRecibido = string.Empty, // Valor predeterminado
                            Pais = pais,
                            FechaCompra = DateTime.UtcNow,
                            FechaExpiracion = DateTime.UtcNow.AddMinutes(20),
                            Estado = "Activo",
                            CostoPagado = servicio.PrecioVenta,
                            SMSRecibido = false,
                            VerificacionExitosa = false,
                            FechaUltimaComprobacion = DateTime.UtcNow
                        };

                        _context.SMSPoolNumeros.Add(nuevoNumero);
                        await _context.SaveChangesAsync();

                        return (nuevoNumero, string.Empty);
                    }
                    else
                    {
                        string errorMsg = "Error en la respuesta de SMSPool";
                        if (response != null && response.message != null)
                        {
                            errorMsg = response.message.ToString();
                        }

                        if (response != null && response.success != null && response.success.ToString() == "0")
                        {
                            if (response.message != null && response.message.ToString() == "Failed to connect to database.")
                            {
                                errorMsg = "Error de conexión a la base de datos del proveedor. Intente más tarde.";
                                //_logger.LogWarning("SMSPool reportó error de conexión a su base de datos: {Message}", response.message);
                            }
                            else if (response.message != null)
                            {
                                errorMsg = response.message.ToString();
                            }
                            return (null, errorMsg);
                        }

                        // Aquí solo intentamos sincronizar si hay posibilidad de que la compra haya sido exitosa
                        // a pesar del error de la API (casos ambiguos)
                        bool posibleCompraExitosa = errorMsg.Contains("timeout") ||
                                                    errorMsg.Contains("connection") ||
                                                    errorMsg.Contains("network");

                        if (posibleCompraExitosa)
                        {
                            // Si la API falló pero podría haber completado la compra, intentar sincronizar
                            await SincronizarComprasActivas(userId);

                            // Verificar si la sincronización encontró el número que acabamos de intentar comprar
                            var numeroSincronizado = await _context.SMSPoolNumeros
                                .Where(n => n.UserId == userId && n.ServicioId == servicioId)
                                .OrderByDescending(n => n.FechaCompra)
                                .FirstOrDefaultAsync();

                            if (numeroSincronizado != null && (DateTime.UtcNow - numeroSincronizado.FechaCompra).TotalMinutes < 5)
                            {
                                // La compra probablemente tuvo éxito a pesar del error de API
                                return (numeroSincronizado, string.Empty);
                            }
                        }

                        return (null, errorMsg);
                    }
                }
                // Uso en el método ComprarNumeroTemporal en la sección de manejo de excepciones
                catch (Exception ex)
                {
                    // Verificar si es un error conocido donde NO DEBEMOS crear número pendiente
                    string mensajeUsuario;
                    if (EsErrorConocido(ex.Message, out mensajeUsuario))
                    {
                        _logger.LogWarning(ex, "Error conocido que no requiere crear número pendiente: {Message}", ex.Message);
                        return (null, mensajeUsuario);
                    }

                    // Solo para errores de CONEXIÓN (no errores de negocio)
                    // creamos número pendiente e intentamos sincronizar
                    if (EsErrorDeConexion(ex))
                    {
                        _logger.LogWarning(ex, "Error de conexión con SMSPool, intentando sincronizar compras");

                        // Guardar un registro de compra "pendiente" 
                        var numeroPendiente = new SMSPoolNumero
                        {
                            UserId = userId,
                            ServicioId = servicioId,
                            Numero = "Pendiente", // Placeholder hasta sincronizar
                            OrderId = "PENDING-" + Guid.NewGuid().ToString(), // Asegurar que no sea null
                            CodigoRecibido = string.Empty, // Asegurar que no sea null
                            Pais = pais,
                            FechaCompra = DateTime.UtcNow,
                            FechaExpiracion = DateTime.UtcNow.AddMinutes(20),
                            Estado = "Pendiente", // Estado especial
                            CostoPagado = servicio.PrecioVenta,
                            SMSRecibido = false,
                            VerificacionExitosa = false,
                            FechaUltimaComprobacion = DateTime.UtcNow
                        };

                        _context.SMSPoolNumeros.Add(numeroPendiente);
                        await _context.SaveChangesAsync();

                        // Intentar sincronizar
                        bool sincronizado = await SincronizarComprasActivas(userId);

                        if (sincronizado)
                        {
                            // Verificar si se encontró una compra real que reemplaza la pendiente
                            var numeroReal = await _context.SMSPoolNumeros
                                .Where(n => n.UserId == userId && n.ServicioId == servicioId && n.Estado == "Activo")
                                .OrderByDescending(n => n.FechaCompra)
                                .FirstOrDefaultAsync();

                            if (numeroReal != null && numeroReal.Id != numeroPendiente.Id)
                            {
                                // Eliminar el registro pendiente
                                _context.SMSPoolNumeros.Remove(numeroPendiente);
                                await _context.SaveChangesAsync();

                                return (numeroReal, string.Empty);
                            }
                        }

                        // No se pudo sincronizar, mantener como pendiente
                        return (numeroPendiente, "La compra está pendiente de confirmación. Intente verificar más tarde.");
                    }
                    else
                    {
                        // Para cualquier otro tipo de error, simplemente informamos al usuario
                        // sin crear números pendientes
                        _logger.LogError(ex, "Error al comprar número temporal (sin crear pendiente)");
                        return (null, $"Error al solicitar número: {ex.Message}");
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al comprar número temporal");
                return (null, $"Error al comprar número: {ex.Message}");
            }
        }

        // Función auxiliar para verificar patrones de error específicos
        private bool EsErrorConocido(string mensajeError, out string mensajeUsuario)
        {
            // Normalizar mensaje (quitar HTML, normalizar espacios, pasar a minúsculas)
            string mensajeNormalizado = NormalizarMensajeError(mensajeError).ToLower();

            // Lista de patrones específicos con sus mensajes correspondientes
            var patronesErroresConocidos = new Dictionary<string, string>
    {
        { "no numbers available at the moment", "No hay números disponibles en este momento. Por favor, intente más tarde." },
        { "pool.*no numbers available", "No hay números disponibles en este momento. Por favor, intente más tarde." },
        { "service not found", "El servicio solicitado no está disponible." },
        { "country not found", "El país seleccionado no está disponible." },
        { "invalid service", "El servicio solicitado no es válido." },
        { "max count of same numbers", "Ha alcanzado el límite máximo de números permitidos para este servicio." }
    };

            foreach (var patron in patronesErroresConocidos)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(mensajeNormalizado, patron.Key))
                {
                    mensajeUsuario = patron.Value;
                    return true;
                }
            }

            mensajeUsuario = "Ha ocurrido un error al solicitar el número.";
            return false;
        }

        // Función para quitar etiquetas HTML y normalizar espacios
        private string NormalizarMensajeError(string mensaje)
        {
            // Si el mensaje es null, devolver cadena vacía
            if (string.IsNullOrEmpty(mensaje))
                return string.Empty;

            // Quitar etiquetas HTML
            string sinHTML = System.Text.RegularExpressions.Regex.Replace(mensaje, "<.*?>", " ");

            // Normalizar espacios (múltiples espacios a uno solo)
            string espaciosNormalizados = System.Text.RegularExpressions.Regex.Replace(sinHTML, @"\s+", " ");

            return espaciosNormalizados.Trim();
        }

        // Función para determinar si es un error de conexión
        private bool EsErrorDeConexion(Exception ex)
        {
            // Lista de tipos de excepción que indican problemas de conexión
            if (ex is System.Net.WebException ||
                ex is System.Net.Http.HttpRequestException ||
                ex is System.IO.IOException ||
                ex is System.Threading.Tasks.TaskCanceledException)
            {
                return true;
            }

            // Verificar patrones en el mensaje de error
            string mensajeError = ex.Message.ToLower();
            string[] patronesErrorConexion = new[] {
        "timeout", "timed out", "connection", "connectivity",
        "network", "socket", "host", "refused", "reset", "unreachable"
    };

            return patronesErrorConexion.Any(patron => mensajeError.Contains(patron));
        }
        public async Task<bool> ResolverNumerosPendientes(string userId)
        {
            try
            {
                // Buscar números pendientes del usuario
                var numerosPendientes = await _context.SMSPoolNumeros
                    .Where(n => n.UserId == userId && n.Estado == "Pendiente")
                    .ToListAsync();

                if (!numerosPendientes.Any())
                {
                    return true; // No hay pendientes
                }

                // Intentar sincronizar con SMSPool
                await SincronizarComprasActivas(userId);

                // Volver a buscar los números pendientes
                bool todosResueltos = true;
                foreach (var numeroPendiente in numerosPendientes)
                {
                    // Verificar si hay un número real correspondiente
                    var numeroReal = await _context.SMSPoolNumeros
                        .Where(n => n.UserId == userId &&
                               n.ServicioId == numeroPendiente.ServicioId &&
                               n.Estado == "Activo" &&
                               n.Id != numeroPendiente.Id)
                        .OrderByDescending(n => n.FechaCompra)
                        .FirstOrDefaultAsync();

                    if (numeroReal != null)
                    {
                        // Se encontró un número real, eliminar el pendiente
                        _context.SMSPoolNumeros.Remove(numeroPendiente);
                    }
                    else
                    {
                        // Verificar si el número pendiente está vencido (más de 30 minutos)
                        if ((DateTime.UtcNow - numeroPendiente.FechaCompra).TotalMinutes > 30)
                        {
                            // Marcar como fallido
                            numeroPendiente.Estado = "Fallido";
                            _context.SMSPoolNumeros.Update(numeroPendiente);
                        }
                        else
                        {
                            todosResueltos = false; // Aún hay pendientes
                        }
                    }
                }

                await _context.SaveChangesAsync();
                return todosResueltos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver números pendientes");
                return false;
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