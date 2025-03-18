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
        public string? UserId { get; }
        public bool IsAdmin { get; }

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            var user = httpContextAccessor.HttpContext?.User;
            if (user != null && user.Identity?.IsAuthenticated == true)
            {
                UserId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                // Verificamos si el usuario tiene el rol "Admin"
                IsAdmin = user.IsInRole("Admin");
            }
        }
    }

}
