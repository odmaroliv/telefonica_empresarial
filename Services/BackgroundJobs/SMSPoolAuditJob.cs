using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Timeout;
using Quartz;
using System.Collections.Concurrent;
using System.Text.Json;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class SMSPoolAuditJob : IJob
    {
        private readonly ILogger<SMSPoolAuditJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ISMSPoolService _smsPoolService;
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int MAX_RETRIES = 3;
        private const int TIMEOUT_SECONDS = 30; // Timeout para cada operación
        private const int MAX_MINUTES_ACTIVE = 21; // Solo verificar números activos en los últimos 21 minutos

        public SMSPoolAuditJob(
            ILogger<SMSPoolAuditJob> logger,
            IServiceProvider serviceProvider,
            ISMSPoolService smsPoolService,
            ApplicationDbContext dbContext,
            UserManager<ApplicationUser> userManager)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _smsPoolService = smsPoolService;
            _dbContext = dbContext;
            _userManager = userManager;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de auditoría de números SMSPool");

            try
            {
                // Crear un token de cancelación para limitar el tiempo total de ejecución
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // Limitar la ejecución total a 2 minutos

                // Obtener un admin para registrar las acciones
                var admin = await _userManager.FindByNameAsync("admin");
                if (admin == null)
                {
                    // Intentar obtener cualquier usuario con rol de Admin
                    var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
                    admin = adminUsers.FirstOrDefault();

                    if (admin == null)
                    {
                        _logger.LogWarning("No se encontró usuario admin para registrar acciones. Se continuará sin registro de auditoría.");
                    }
                }

                // Combinar políticas de timeout y reintento
                var timeoutPolicy = Policy.TimeoutAsync(TIMEOUT_SECONDS);

                var retryPolicy = Policy
                    .Handle<Exception>(ex =>
                        !(ex is OperationCanceledException) &&
                        !(ex is TimeoutRejectedException))
                    .WaitAndRetryAsync(
                        MAX_RETRIES,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (exception, timeSpan, retryCount, context) =>
                        {
                            _logger.LogWarning($"Intento {retryCount} de auditoría SMSPool fallido: {exception.Message}. Reintentando en {timeSpan.TotalSeconds} segundos.");
                        });

                var policy = Policy.WrapAsync(timeoutPolicy, retryPolicy);

                await policy.ExecuteAsync(async (ct) =>
                {
                    await VerificarNumerosActivos(admin, ct);
                }, cts.Token);

                // Registrar la última ejecución exitosa en el JobDetail
                context.JobDetail.JobDataMap["LastExecutionTime"] = DateTime.UtcNow.ToString("o");

                _logger.LogInformation("Job de auditoría de números SMSPool completado exitosamente");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Job de auditoría de números SMSPool cancelado por timeout.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de auditoría de números SMSPool después de varios intentos");
            }
        }

        private async Task VerificarNumerosActivos(ApplicationUser admin, CancellationToken cancellationToken)
        {
            try
            {
                // Calcular la fecha límite (hace 21 minutos) para filtrar números activos recientes
                var fechaLimite = DateTime.UtcNow.AddMinutes(-MAX_MINUTES_ACTIVE);

                // 1. Obtener todos los números activos en SMSPool que están dentro del período de 21 minutos
                var numerosActivosEnSMSPool = await ObtenerNumerosActivosEnSMSPool(fechaLimite, cancellationToken);
                _logger.LogInformation($"Se encontraron {numerosActivosEnSMSPool.Count} números activos recientes en SMSPool (últimos {MAX_MINUTES_ACTIVE} minutos)");

                if (numerosActivosEnSMSPool.Count == 0)
                {
                    _logger.LogInformation("No hay números activos recientes en SMSPool para verificar");
                    return;
                }

                // 2. Extraer IDs de órdenes de los números activos en SMSPool
                var orderIdsEnSMSPool = numerosActivosEnSMSPool
                    .Where(n => !string.IsNullOrEmpty(n.OrderId))
                    .Select(n => n.OrderId)
                    .ToList();

                // 3. Obtener los números correspondientes en la base de datos
                var numerosEnBD = await _dbContext.SMSPoolNumeros
                    .Where(n => orderIdsEnSMSPool.Contains(n.OrderId))
                    .ToListAsync(cancellationToken);

                _logger.LogInformation($"Se encontraron {numerosEnBD.Count} números en la base de datos con OrderIds correspondientes");

                // 4. Identificar números en SMSPool que no están en la base de datos
                var ordersIdsEnBD = numerosEnBD.Select(n => n.OrderId).ToList();
                var numerosParaCancelar = numerosActivosEnSMSPool
                    .Where(n => !string.IsNullOrEmpty(n.OrderId) && !ordersIdsEnBD.Contains(n.OrderId))
                    .ToList();

                _logger.LogInformation($"Se encontraron {numerosParaCancelar.Count} números en SMSPool no registrados en la base de datos");

                // 5. Cancelar los números que no están en la base de datos
                if (numerosParaCancelar.Any())
                {
                    await CancelarNumeros(numerosParaCancelar, admin, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("No hay números recientes que requieran cancelación");
                }

                // 6. Verificar números pendientes y actualizarlos si es necesario
                await ResolverNumerosPendientes(fechaLimite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Verificación de números activos cancelada por timeout.");
                throw; // Relanzar para que se maneje adecuadamente
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar números activos");
                throw; // Relanzar para que la política de reintentos pueda actuar
            }
        }

        private async Task<List<SMSPoolNumero>> ObtenerNumerosActivosEnSMSPool(DateTime fechaLimite, CancellationToken cancellationToken)
        {
            try
            {
                // Crear una lista para almacenar todos los números activos
                var numerosActivos = new List<SMSPoolNumero>();

                // Obtener los usuarios distintos que tienen números en la plataforma
                // Solo nos interesan los números creados recientemente (dentro del límite de 21 minutos)
                var usuariosIds = await _dbContext.SMSPoolNumeros
                    .Where(n => n.FechaCompra >= fechaLimite && n.Estado == "Activo")
                    .Select(n => n.UserId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                if (!usuariosIds.Any())
                {
                    _logger.LogInformation("No hay usuarios con números activos recientes en la base de datos");
                    return numerosActivos;
                }

                // Iniciar un semáforo para limitar las llamadas concurrentes a la API
                using var semaphore = new SemaphoreSlim(3); // Reducido a 3 llamadas concurrentes para ser más conservadores

                // Lista para almacenar las excepciones que puedan ocurrir en las tareas
                var exceptions = new ConcurrentBag<Exception>();

                // Crear una lista de tareas para obtener números activos por usuario
                var tareas = new List<Task>();

                foreach (var userId in usuariosIds)
                {
                    // Verificar si ya se canceló la operación
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Obtención de números activos cancelada por timeout.");
                        break;
                    }

                    tareas.Add(Task.Run(async () =>
                    {
                        try
                        {
                            // Verificar nuevamente si se canceló la operación
                            if (cancellationToken.IsCancellationRequested)
                                return;

                            await semaphore.WaitAsync(cancellationToken);

                            try
                            {
                                // Aplicar timeout a la llamada a la API
                                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                                // Sincronizar compras activas del usuario en SMSPool
                                await _smsPoolService.SincronizarComprasActivas(userId);

                                // Obtener números activos del usuario desde la BD (solo los recientes)
                                var numerosUsuario = await _dbContext.SMSPoolNumeros
                                    .Where(n => n.UserId == userId && n.Estado == "Activo" && n.FechaCompra >= fechaLimite)
                                    .ToListAsync(linkedCts.Token);

                                // Agregar los números a la lista general
                                lock (numerosActivos)
                                {
                                    numerosActivos.AddRange(numerosUsuario);
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // No es necesario hacer nada, la tarea fue cancelada
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                            _logger.LogError(ex, $"Error al obtener números activos para usuario {userId}");
                        }
                    }, cancellationToken));
                }

                // Esperar a que todas las tareas se completen o sean canceladas
                try
                {
                    await Task.WhenAll(tareas);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Algunas tareas de obtención de números fueron canceladas");
                }

                // Si hay excepciones, lanzar una excepción agregada para que se reintente
                if (!exceptions.IsEmpty)
                {
                    _logger.LogWarning($"Se produjeron {exceptions.Count} errores al obtener números activos");

                    // Si todas las tareas fallaron, relanzar para que el policy lo reintente
                    if (exceptions.Count >= usuariosIds.Count)
                    {
                        throw new AggregateException("Todas las tareas de obtención de números fallaron", exceptions);
                    }
                }

                return numerosActivos;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Obtención de números activos cancelada por timeout");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener números activos desde SMSPool");
                throw;
            }
        }

        private async Task CancelarNumeros(List<SMSPoolNumero> numeros, ApplicationUser admin, CancellationToken cancellationToken)
        {
            if (!numeros.Any())
                return;

            _logger.LogInformation($"Comenzando cancelación de {numeros.Count} números no registrados");

            // Iniciar un semáforo para limitar las llamadas concurrentes a la API
            using var semaphore = new SemaphoreSlim(2); // Reducido a 2 cancelaciones concurrentes

            // Lista para almacenar las excepciones que puedan ocurrir en las tareas
            var exceptions = new ConcurrentBag<Exception>();

            // Crear una lista de tareas para cancelar números
            var tareas = new List<Task>();

            foreach (var numero in numeros)
            {
                // Verificar si ya se canceló la operación
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Cancelación de números interrumpida por timeout.");
                    break;
                }

                tareas.Add(Task.Run(async () =>
                {
                    try
                    {
                        // Verificar nuevamente si se canceló la operación
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        await semaphore.WaitAsync(cancellationToken);

                        try
                        {
                            // Aplicar timeout a la cancelación
                            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                            // Cancelar el número en SMSPool
                            bool resultadoCancelacion = await CancelarNumeroEnSMSPool(numero.OrderId, linkedCts.Token);

                            if (resultadoCancelacion)
                            {
                                _logger.LogInformation($"Número con OrderId {numero.OrderId} cancelado exitosamente");

                                // Registrar la cancelación en el log de auditoría si hay un admin disponible
                                if (admin != null)
                                {
                                    await RegistrarCancelacionEnLog(numero, admin);
                                }
                            }
                            else
                            {
                                _logger.LogWarning($"No se pudo cancelar el número con OrderId {numero.OrderId}");
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // No es necesario hacer nada, la tarea fue cancelada
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                        _logger.LogError(ex, $"Error no manejado al cancelar número con OrderId {numero.OrderId}");
                    }
                }, cancellationToken));
            }

            // Esperar a que todas las tareas se completen o sean canceladas
            try
            {
                await Task.WhenAll(tareas);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Algunas tareas de cancelación fueron canceladas");
            }

            // Registrar si hubo errores
            if (!exceptions.IsEmpty)
            {
                _logger.LogWarning($"Se produjeron {exceptions.Count} errores al cancelar números");
            }
        }

        private async Task<bool> CancelarNumeroEnSMSPool(string orderId, CancellationToken cancellationToken)
        {
            try
            {
                // Crear un scope para obtener una nueva instancia del servicio
                using var scope = _serviceProvider.CreateScope();
                var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

                // Obtener la API key y la URL base
                var apiKey = configuration["SMSPool:ApiKey"] ?? throw new ArgumentNullException("SMSPool:ApiKey no configurado");
                var apiBaseUrl = "https://api.smspool.net";

                // Crear el cliente HTTP
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8); // Timeout más corto para el cliente HTTP

                var content = new MultipartFormDataContent();

                // Agregar API key y OrderId
                content.Add(new StringContent(apiKey), "key");
                content.Add(new StringContent(orderId), "orderid");

                // Enviar petición de cancelación
                var response = await client.PostAsync($"{apiBaseUrl}/sms/cancel", content, cancellationToken);

                // Leer la respuesta
                var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug($"Respuesta de API de cancelación para OrderId {orderId}: {responseString}");

                // Verificar si la cancelación fue exitosa
                if (response.IsSuccessStatusCode && responseString.Contains("\"success\":1"))
                {
                    return true;
                }

                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, $"Timeout al enviar solicitud de cancelación para OrderId {orderId}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, $"Error HTTP al enviar solicitud de cancelación para OrderId {orderId}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al enviar solicitud de cancelación para OrderId {orderId}");
                return false;
            }
        }

        private async Task RegistrarCancelacionEnLog(SMSPoolNumero numero, ApplicationUser admin)
        {
            try
            {
                // Crear un registro de auditoría utilizando tu modelo existente
                var log = new AdminLog
                {
                    AdminId = admin.Id,
                    Admin = admin, // Asignar la relación directamente
                    Action = "CancelarNumeroSMSPool",
                    TargetType = "SMSPoolNumero",
                    TargetId = numero.OrderId,
                    Details = JsonSerializer.Serialize(new
                    {
                        Numero = numero.Numero,
                        OrderId = numero.OrderId,
                        ServicioId = numero.ServicioId,
                        UserId = numero.UserId,
                        Pais = numero.Pais,
                        FechaCompra = numero.FechaCompra,
                        FechaCancelacion = DateTime.UtcNow,
                        Motivo = "Número existe en SMSPool pero no en la base de datos"
                    }),
                    Timestamp = DateTime.UtcNow,
                    IpAddress = "Job Automático"
                };

                // Agregar y guardar el log
                _dbContext.AdminLogs.Add(log);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al registrar cancelación en log para OrderId {numero.OrderId}");
            }
        }

        private async Task ResolverNumerosPendientes(DateTime fechaLimite, CancellationToken cancellationToken)
        {
            try
            {
                // Obtener números pendientes recientes (dentro del límite de tiempo)
                var numerosPendientes = await _dbContext.SMSPoolNumeros
                    .Where(n => n.Estado == "Pendiente" && n.FechaCompra >= fechaLimite)
                    .ToListAsync(cancellationToken);

                if (!numerosPendientes.Any())
                {
                    _logger.LogInformation("No hay números pendientes recientes que requieran resolución");
                    return;
                }

                _logger.LogInformation($"Encontrados {numerosPendientes.Count} números pendientes recientes para resolver");

                // Agrupar por usuario para optimizar llamadas a SMSPool
                var usuariosConPendientes = numerosPendientes
                    .GroupBy(n => n.UserId)
                    .Select(g => g.Key)
                    .ToList();

                foreach (var userId in usuariosConPendientes)
                {
                    // Verificar si ya se canceló la operación
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogWarning("Resolución de números pendientes interrumpida por timeout.");
                        break;
                    }

                    try
                    {
                        // Aplicar timeout a la resolución
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

                        // Intentar resolver números pendientes para este usuario
                        await _smsPoolService.ResolverNumerosPendientes(userId);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning($"Resolución de números pendientes para usuario {userId} cancelada por timeout");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al resolver números pendientes para usuario {userId}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Resolución de números pendientes cancelada por timeout");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al resolver números pendientes");
            }
        }
    }
}