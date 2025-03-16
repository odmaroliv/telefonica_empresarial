using System.Net;
using System.Text.Json;

namespace TelefonicaEmpresarial.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "Error no manejado en la aplicación");

            var statusCode = HttpStatusCode.InternalServerError;
            string message = "Ocurrió un error inesperado. Por favor, intente nuevamente más tarde.";

            // Personalizar mensaje según tipo de excepción
            if (exception is UnauthorizedAccessException)
            {
                statusCode = HttpStatusCode.Unauthorized;
                message = "No tiene autorización para realizar esta acción.";
            }
            else if (exception is ArgumentException ||
                     exception is FormatException ||
                     exception is InvalidOperationException)
            {
                statusCode = HttpStatusCode.BadRequest;
                message = "La solicitud no puede ser procesada debido a datos incorrectos.";
            }
            else if (exception is KeyNotFoundException)
            {
                statusCode = HttpStatusCode.NotFound;
                message = "El recurso solicitado no existe.";
            }

            // Solo mostrar detalles técnicos en desarrollo
            var response = new
            {
                StatusCode = (int)statusCode,
                Message = message,
                DetailedError = _environment.IsDevelopment() ? exception.ToString() : null
            };

            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "application/json";

            var jsonResponse = JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(jsonResponse);
        }
    }

    // Extensión para facilitar la configuración del middleware
    public static class ExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ExceptionHandlingMiddleware>();
        }
    }
}