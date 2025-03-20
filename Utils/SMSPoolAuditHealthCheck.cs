using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;

namespace TelefonicaEmpresarial.Services.HealthChecks
{
    public class SMSPoolAuditHealthCheck : IHealthCheck
    {
        private readonly ISchedulerFactory _schedulerFactory;
        private readonly ILogger<SMSPoolAuditHealthCheck> _logger;

        public SMSPoolAuditHealthCheck(
            ISchedulerFactory schedulerFactory,
            ILogger<SMSPoolAuditHealthCheck> logger)
        {
            _schedulerFactory = schedulerFactory;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

                // Verificar si el job está programado
                var jobKey = new JobKey("SMSPoolAuditJob");
                bool exists = await scheduler.CheckExists(jobKey, cancellationToken);

                if (!exists)
                {
                    return HealthCheckResult.Unhealthy("El job de auditoría de SMSPool no está registrado");
                }

                // Verificar si hay disparadores para el job
                var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);

                if (!triggers.Any())
                {
                    return HealthCheckResult.Degraded("El job de auditoría de SMSPool no tiene disparadores configurados");
                }

                // Verificar cuándo fue la última ejecución
                var jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken);
                if (jobDetail.JobDataMap.ContainsKey("LastExecutionTime"))
                {
                    var lastExecutionTime = jobDetail.JobDataMap.GetString("LastExecutionTime");
                    if (DateTime.TryParse(lastExecutionTime, out var lastExecution))
                    {
                        if (DateTime.UtcNow - lastExecution > TimeSpan.FromMinutes(10))
                        {
                            return HealthCheckResult.Degraded($"El job de auditoría de SMSPool no se ha ejecutado desde {lastExecution}");
                        }
                    }
                }

                return HealthCheckResult.Healthy("El job de auditoría de SMSPool está funcionando correctamente");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar el estado del job de auditoría de SMSPool");
                return HealthCheckResult.Unhealthy("Error al verificar el estado del job de auditoría de SMSPool", ex);
            }
        }
    }
}