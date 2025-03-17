using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using Quartz.Impl.Matchers;

public class QuartzJobsHealthCheck : IHealthCheck
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly ILogger<QuartzJobsHealthCheck> _logger;

    public QuartzJobsHealthCheck(ISchedulerFactory schedulerFactory, ILogger<QuartzJobsHealthCheck> logger)
    {
        _schedulerFactory = schedulerFactory;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Iniciando verificación de health check para Quartz");

            var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

            _logger.LogInformation("Scheduler obtenido: {SchedulerName}, Estado: {IsStarted}",
                scheduler.SchedulerName, scheduler.IsStarted);

            if (!scheduler.IsStarted)
                return HealthCheckResult.Unhealthy("El scheduler de Quartz no está iniciado");

            // Verificar jobs específicos
            var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
            _logger.LogInformation("Se encontraron {Count} jobs", jobKeys.Count);

            var data = new Dictionary<string, object>();

            foreach (var jobKey in jobKeys)
            {
                _logger.LogInformation("Procesando job {JobName}", jobKey.Name);

                try
                {
                    var jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken);
                    var triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken);

                    _logger.LogInformation("Job {JobName} tiene {TriggerCount} triggers",
                        jobKey.Name, triggers.Count);

                    foreach (var trigger in triggers)
                    {
                        try
                        {
                            var nextFireTime = trigger.GetNextFireTimeUtc();
                            var previousFireTime = trigger.GetPreviousFireTimeUtc();

                            string nextFireStr = nextFireTime.HasValue
                                ? nextFireTime.Value.LocalDateTime.ToString()
                                : "No programado";

                            string prevFireStr = previousFireTime.HasValue
                                ? previousFireTime.Value.LocalDateTime.ToString()
                                : "Nunca ejecutado";

                            data.Add($"{jobKey.Name}-{trigger.Key.Name}:NextRun", nextFireStr);
                            data.Add($"{jobKey.Name}-{trigger.Key.Name}:LastRun", prevFireStr);

                            _logger.LogInformation("Trigger {TriggerName} para job {JobName}: Próxima ejecución: {NextFire}, Última ejecución: {PrevFire}",
                                trigger.Key.Name, jobKey.Name, nextFireStr, prevFireStr);
                        }
                        catch (Exception triggerEx)
                        {
                            _logger.LogError(triggerEx, "Error procesando trigger {TriggerName} para job {JobName}",
                                trigger.Key.Name, jobKey.Name);
                            data.Add($"{jobKey.Name}-{trigger.Key.Name}:Error", triggerEx.Message);
                        }
                    }
                }
                catch (Exception jobEx)
                {
                    _logger.LogError(jobEx, "Error procesando job {JobName}", jobKey.Name);
                    data.Add($"{jobKey.Name}:Error", jobEx.Message);
                }
            }

            _logger.LogInformation("Health check de Quartz completado con éxito");
            return HealthCheckResult.Healthy("Quartz jobs funcionando correctamente", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar Quartz jobs");
            return HealthCheckResult.Unhealthy($"Error al verificar Quartz jobs: {ex.Message}", ex);
        }
    }
}