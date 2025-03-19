using System.Security.Claims;
namespace TelefonicaEmpresaria.Utils
{
    public interface ICurrentUserService
    {
        string? UserId { get; }
        bool IsAdmin { get; }
    }


    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public string UserId
        {
            get
            {
                if (IsWebhookRequest)
                    return null; // No se usa en filtros para webhooks

                return _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            }
        }

        public bool IsAdmin
        {
            get
            {
                if (IsWebhookRequest || IsBackgroundJob)
                    return true;


                return _httpContextAccessor.HttpContext?.User?.IsInRole("Admin") ?? false;
            }
        }

        private bool IsWebhookRequest
        {
            get
            {
                var path = _httpContextAccessor.HttpContext?.Request.Path.Value ?? "";
                return path.Contains("/api/webhooks/") ||
                       path.Contains("/api/twilio/") ||
                       path.Contains("api/webhooks/twilio") ||
                       path.Contains("api/llamadas");

            }
        }
        private bool IsBackgroundJob
        {
            get
            {
                return _httpContextAccessor.HttpContext == null;
            }
        }
    }

}
