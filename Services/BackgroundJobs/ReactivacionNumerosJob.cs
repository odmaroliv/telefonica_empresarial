using Microsoft.EntityFrameworkCore;
using Polly;
using Quartz;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    /// <summary>
    /// Job para reactivar números que fueron desactivados por falta de saldo
    /// pero cuyos usuarios han recargado saldo suficiente para cubrir la renovación
    /// </summary>
    [DisallowConcurrentExecution]
    public class ReactivacionNumerosJob : IJob
    {
        private readonly ILogger<ReactivacionNumerosJob> _logger;
        private readonly IServiceProvider _serviceProvider;
        private const int TAMANO_LOTE = 30; // Procesar en lotes de 30 números
        private const int DIAS_REACTIVACION = 7; // Permitir reactivar números desactivados hasta 7 días después

        public ReactivacionNumerosJob(
            ILogger<ReactivacionNumerosJob> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de reactivación de números");

            // Crear scope para acceder a servicios scoped
            using var scope = _serviceProvider.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var telefonicaService = scope.ServiceProvider.GetRequiredService<ITelefonicaService>();
            var saldoService = scope.ServiceProvider.GetRequiredService<ISaldoService>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            try
            {
                await ReactivarNumerosDesactivados(dbContext, saldoService, telefonicaService, notificationService);
                _logger.LogInformation("Job de reactivación de números completado exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de reactivación de números");
            }
        }

        private async Task ReactivarNumerosDesactivados(
            ApplicationDbContext dbContext,
            ISaldoService saldoService,
            ITelefonicaService telefonicaService,
            INotificationService notificationService)
        {
            try
            {
                var fechaHoy = DateTime.UtcNow;
                var fechaLimiteReactivacion = fechaHoy.AddDays(-DIAS_REACTIVACION);

                // Buscar números desactivados recientemente (en los últimos DIAS_REACTIVACION días)
                // que no hayan sido liberados (aún mantienen su PlivoUuid)
                var numerosDesactivados = await dbContext.NumerosTelefonicos
                    .Where(n => !n.Activo &&
                           n.FechaExpiracion > fechaLimiteReactivacion &&
                           n.PlivoUuid != null &&
                           n.PlivoUuid != "pendiente" &&
                           n.PlivoUuid != "liberado")
                    .Include(n => n.Usuario)
                    .OrderByDescending(n => n.FechaExpiracion) // Priorizar los desactivados más recientemente
                    .Take(TAMANO_LOTE)
                    .ToListAsync();

                _logger.LogInformation($"Se encontraron {numerosDesactivados.Count} números candidatos para reactivación");

                foreach (var numero in numerosDesactivados)
                {
                    try
                    {
                        // Calcular el costo total de reactivación
                        decimal costoTotal = numero.CostoMensual;
                        if (numero.SMSHabilitado && numero.CostoSMS.HasValue)
                        {
                            costoTotal += numero.CostoSMS.Value;
                        }

                        // Verificar si el usuario tiene saldo suficiente
                        bool saldoSuficiente = await saldoService.VerificarSaldoSuficiente(numero.UserId, costoTotal);

                        if (saldoSuficiente)
                        {
                            await ReactivarNumero(numero, costoTotal, saldoService, telefonicaService, dbContext);

                            // Notificar al usuario que su número ha sido reactivado
                            //await notificationService.EnviarNotificacion(
                            //    numero.UserId,
                            //    "Número reactivado automáticamente",
                            //    $"Tu número {numero.Numero} ha sido reactivado automáticamente al detectar que has recargado saldo suficiente.",
                            //    "success");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error al reactivar número {numero.Id}: {ex.Message}");
                    }
                }

                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al reactivar números desactivados");
                throw;
            }
        }

        private async Task ReactivarNumero(
            NumeroTelefonico numero,
            decimal costoTotal,
            ISaldoService saldoService,
            ITelefonicaService telefonicaService,
            ApplicationDbContext dbContext)
        {
            try
            {
                _logger.LogInformation($"Reactivando número {numero.Id} ({numero.Numero})");

                // Utilizar una política de reintentos para operaciones críticas
                var retryPolicy = Policy
                    .Handle<DbUpdateException>()
                    .Or<DbUpdateConcurrencyException>()
                    .WaitAndRetryAsync(
                        3, // Número de reintentos
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                        (exception, timeSpan, retryCount, context) =>
                        {
                            _logger.LogWarning($"Error en BD al reactivar número (intento {retryCount}): {exception.Message}");
                        }
                    );

                await retryPolicy.ExecuteAsync(async () =>
                {
                    // Verificar si el número aún existe en Twilio
                    // En ITelefonicaService no tenemos un método directo para verificar,
                    // así que asumimos que si podemos actualizar, el número sigue activo

                    // Descontar saldo
                    string concepto = $"Reactivación de número {numero.Numero}";
                    bool saldoDescontado = await saldoService.DescontarSaldo(
                        numero.UserId,
                        costoTotal,
                        concepto,
                        numero.Id);

                    if (saldoDescontado)
                    {
                        // Reconfigurar redirección si es necesario
                        if (!string.IsNullOrEmpty(numero.NumeroRedireccion))
                        {
                            // Usar ActualizarRedireccion en lugar de ConfigurarRedireccion
                            await telefonicaService.ActualizarRedireccion(numero.Id, numero.NumeroRedireccion);
                        }

                        // Reactivar SMS si estaba habilitado
                        if (numero.SMSHabilitado)
                        {
                            // Usar HabilitarSMS en lugar de ActivarSMS
                            await telefonicaService.HabilitarSMS(numero.Id);
                        }

                        // Actualizar estado y fecha de expiración
                        numero.Activo = true;
                        numero.FechaExpiracion = DateTime.UtcNow.AddMonths(1);

                        // Registrar transacción exitosa
                        dbContext.Transacciones.Add(new Transaccion
                        {
                            UserId = numero.UserId,
                            NumeroTelefonicoId = numero.Id,
                            Fecha = DateTime.UtcNow,
                            Monto = costoTotal,
                            Concepto = concepto,
                            StripePaymentId = "reactivacion_automatica",
                            Status = "Completado"
                        });

                        await dbContext.SaveChangesAsync();

                        _logger.LogInformation($"Número {numero.Id} reactivado exitosamente hasta {numero.FechaExpiracion}");
                    }
                    else
                    {
                        _logger.LogWarning($"No se pudo descontar saldo para reactivación de número {numero.Id}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error al reactivar número {numero.Id}");
                throw;
            }
        }
    }
}