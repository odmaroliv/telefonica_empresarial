using Quartz;
using TelefonicaEmpresaria.Services.BackgroundJobs;
using TelefonicaEmpresaria.Services.TelefonicaEmpresarial.Services;
using TelefonicaEmpresarial.Infrastructure.Resilience;

namespace TelefonicaEmpresarial.Services.BackgroundJobs
{
    [DisallowConcurrentExecution]
    public class TransaccionesMonitorJob : IJob
    {
        private readonly ITransaccionMonitorService _transaccionMonitorService;
        private readonly IStripeService _stripeService;
        private readonly ISaldoService _saldoService;
        private readonly ILogger<TransaccionesMonitorJob> _logger;

        public TransaccionesMonitorJob(
            ITransaccionMonitorService transaccionMonitorService,
            IStripeService stripeService,
            ISaldoService saldoService,
            ILogger<TransaccionesMonitorJob> logger)
        {
            _transaccionMonitorService = transaccionMonitorService;
            _stripeService = stripeService;
            _saldoService = saldoService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Iniciando job de monitoreo de transacciones");

            try
            {
                // Usar una política de reintentos avanzada para esta operación crítica
                var policyContext = new Polly.Context
                {
                    ["logger"] = _logger
                };

                await PoliciasReintentos.ObtenerPoliticaAvanzada().ExecuteAsync(
                    async (ctx) => await VerificarTransaccionesPendientes(),
                    policyContext
                );

                _logger.LogInformation("Job de monitoreo de transacciones completado exitosamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al ejecutar job de monitoreo de transacciones");
            }
        }

        private async Task VerificarTransaccionesPendientes()
        {
            _logger.LogInformation("Verificando transacciones pendientes...");

            // Obtener transacciones pendientes de las últimas 24 horas
            var transaccionesPendientes = await _transaccionMonitorService.ObtenerTransaccionesPendientes(24);

            _logger.LogInformation($"Se encontraron {transaccionesPendientes.Count} transacciones pendientes");

            foreach (var transaccion in transaccionesPendientes)
            {
                try
                {
                    // Solo verificar transacciones de recarga de saldo iniciadas hace más de 1 hora
                    if (transaccion.TipoOperacion == "RecargaSaldo" &&
                        transaccion.Estado == "Iniciada" &&
                        transaccion.FechaCreacion < DateTime.UtcNow.AddHours(-1))
                    {
                        // Usar la política de API para verificaciones en Stripe
                        var policyContext = new Polly.Context
                        {
                            ["logger"] = _logger
                        };

                        await PoliciasReintentos.ObtenerPoliticaAPI().ExecuteAsync(
                                        async ctx =>
                                        {
                                            // Si necesitas el logger:
                                            var logger = ctx.GetLogger();
                                            // Llamar tu método
                                            await VerificarTransaccion(transaccion);
                                        },
                                        policyContext
                                    );

                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error al verificar transacción pendiente {transaccion.ReferenciaExterna}");
                }
            }
        }

        private async Task VerificarTransaccion(TransaccionAuditoria transaccion)
        {
            // Verificar si el movimiento ya fue procesado
            bool yaExiste = await _saldoService.ExisteTransaccion(transaccion.ReferenciaExterna);

            if (yaExiste)
            {
                await _transaccionMonitorService.ActualizarEstadoTransaccion(
                    transaccion.ReferenciaExterna,
                    "Completada",
                    "Verificado por job de monitoreo");
                return;
            }

            // Verificar estado en Stripe
            var sesion = await _stripeService.ObtenerDetallesSesion(transaccion.ReferenciaExterna);

            if (sesion == null)
            {
                await _transaccionMonitorService.ActualizarEstadoTransaccion(
                    transaccion.ReferenciaExterna,
                    "Fallida",
                    "Sesión no encontrada en Stripe");
                return;
            }

            if (sesion.PaymentStatus == "paid" && !yaExiste)
            {
                // La transacción está pagada pero no procesada - potencial problema con webhook
                _logger.LogWarning($"Transacción {transaccion.ReferenciaExterna} pagada pero no procesada");

                await _transaccionMonitorService.ActualizarEstadoTransaccion(
                    transaccion.ReferenciaExterna,
                    "RequiereRevisión",
                    "Pago confirmado pero no procesado");
            }
            else if (sesion.PaymentStatus != "paid")
            {
                await _transaccionMonitorService.ActualizarEstadoTransaccion(
                    transaccion.ReferenciaExterna,
                    "EnEspera",
                    $"Estado de pago: {sesion.PaymentStatus}");
            }
        }
    }
}