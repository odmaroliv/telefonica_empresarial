using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.Wrap;

namespace TelefonicaEmpresarial.Infrastructure.Resilience
{
    public static class PoliciasReintentos
    {
        // Configuración de políticas globales que se pueden utilizar en toda la aplicación

        /// <summary>
        /// Política estándar para reintentos en operaciones de base de datos
        /// </summary>
        public static AsyncRetryPolicy ObtenerPoliticaDB()
        {
            return Polly.Policy
                .Handle<DbUpdateException>()
                .Or<DbUpdateConcurrencyException>()
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial (2, 4, 8 segundos)
                    (exception, timeSpan, retryCount, context) =>
                    {
                        // Log a realizar en cada reintento
                        var logger = context.GetLogger();
                        logger?.LogWarning($"Reintento {retryCount} después de {timeSpan.TotalSeconds}s por error en BD: {exception.Message}");
                    }
                );
        }

        /// <summary>
        /// Política para operaciones de API externa (Twilio, Stripe, etc.)
        /// </summary>
        public static AsyncRetryPolicy ObtenerPoliticaAPI()
        {
            return Polly.Policy
                .Handle<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    3, // Número de reintentos
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Espera exponencial
                    (exception, timeSpan, retryCount, context) =>
                    {
                        var logger = context.GetLogger();
                        logger?.LogWarning($"Reintento {retryCount} después de {timeSpan.TotalSeconds}s por error en API externa: {exception.Message}");
                    }
                );
        }

        /// <summary>
        /// Política avanzada que combina tiempo límite y reintentos para operaciones críticas
        /// </summary>
        public static AsyncPolicyWrap ObtenerPoliticaAvanzada()
        {
            // Política de tiempo límite - cancelar después de 30 segundos
            var timeoutPolicy = Polly.Policy
                .TimeoutAsync(30, TimeoutStrategy.Pessimistic);

            // Política de reintentos
            var retryPolicy = Polly.Policy
                .Handle<Exception>(ex =>
                    !(ex is ArgumentException) && // No reintentar errores de argumento
                    !(ex is UnauthorizedAccessException)) // No reintentar errores de autorización
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount, context) =>
                    {
                        var logger = context.GetLogger();
                        logger?.LogWarning($"Operación avanzada: reintento {retryCount} después de {timeSpan.TotalSeconds}s: {exception.Message}");
                    }
                );

            // Combinar políticas - el tiempo límite envuelve al reintento
            return Polly.Policy.WrapAsync(retryPolicy, timeoutPolicy);
        }
    }

    /// <summary>
    /// Extensiones para el contexto de Polly
    /// </summary>
    public static class PolicyContextExtensions
    {
        /// <summary>
        /// Extensión para obtener el logger del contexto
        /// </summary>
        public static ILogger GetLogger(this Context context)
        {
            if (context.TryGetValue("logger", out object logger))
            {
                return logger as ILogger;
            }
            return null;
        }
    }
}