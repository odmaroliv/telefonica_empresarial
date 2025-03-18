using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace TelefonicaEmpresaria.Services.BackgroundJobs
{
    public class TransaccionesMonitorHealthCheck : IHealthCheck
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<TransaccionesMonitorHealthCheck> _logger;

        public TransaccionesMonitorHealthCheck(
            ISchedulerFactory schedulerFactory,
            ILogger<TransaccionesMonitorHealthCheck> logger)
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

                // Obtener información del job
                var jobKey = new JobKey("TransaccionesMonitorJob");
                var jobExists = await scheduler.CheckExists(jobKey, cancellationToken);

                if (!jobExists)
                {
                    return HealthCheckResult.Unhealthy("El job de monitoreo de transacciones no está registrado en el scheduler");
                }

                // Obtener información de los triggers asociados
                var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);
                if (triggers.Count == 0)
                {
                    return HealthCheckResult.Degraded("El job de monitoreo de transacciones no tiene triggers asociados");
                }

                // Verificar si algún trigger está activo
                bool anyActiveTrigger = false;
                foreach (var trigger in triggers)
                {
                    var triggerState = await scheduler.GetTriggerState(trigger.Key, cancellationToken);
                    if (triggerState == TriggerState.Normal || triggerState == TriggerState.Blocked)
                    {
                        anyActiveTrigger = true;
                        break;
                    }
                }

                if (!anyActiveTrigger)
                {
                    return HealthCheckResult.Degraded("No hay triggers activos para el job de monitoreo de transacciones");
                }

                // Si llegamos aquí, es que el job está configurado correctamente
                var jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken);
                var jobData = new Dictionary<string, object>
                {
                    { "JobType", jobDetail.JobType.Name },
                    { "IsActive", scheduler.IsStarted && !scheduler.InStandbyMode },
                    { "TriggerCount", triggers.Count }
                };

                // Todo parece estar bien
                return HealthCheckResult.Healthy("Job de monitoreo de transacciones configurado correctamente", jobData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar el estado del job de monitoreo de transacciones");
                return HealthCheckResult.Unhealthy("Error al verificar el estado del job de monitoreo de transacciones", ex);
            }
        }
    }
}