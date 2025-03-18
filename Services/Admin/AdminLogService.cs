using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;
using TelefonicaEmpresaria.Models;

namespace TelefonicaEmpresarial.Services
{
    public interface IAdminLogService
    {
        Task LogActionAsync(string action, string targetType, string targetId, string details = null);
        Task<List<AdminLog>> GetRecentLogsAsync(int count = 50);
        Task<List<AdminLog>> GetLogsByAdminAsync(string adminId, int count = 50);
        Task<List<AdminLog>> GetLogsByTargetAsync(string targetType, string targetId, int count = 50);
        Task<List<AdminLog>> SearchLogsAsync(string searchTerm, DateTime? startDate = null, DateTime? endDate = null, int count = 50);
    }

    public class AdminLogService : IAdminLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminLogService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
        }

        public async Task LogActionAsync(string action, string targetType, string targetId, string details = null)
        {
            // Obtener el ID del usuario actual
            var userId = _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                // Si no hay usuario autenticado, no registrar la acción
                return;
            }

            // Verificar si el usuario tiene rol de Admin
            var isAdmin = await _userManager.IsInRoleAsync(await _userManager.FindByIdAsync(userId), "Admin");
            if (!isAdmin)
            {
                // Si el usuario no es administrador, no registrar la acción
                return;
            }

            // Crear el registro
            var log = new AdminLog
            {
                AdminId = userId,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString()
            };

            // Guardar en la base de datos
            _context.AdminLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task<List<AdminLog>> GetRecentLogsAsync(int count = 50)
        {
            return await _context.AdminLogs
                .Include(l => l.Admin)
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<AdminLog>> GetLogsByAdminAsync(string adminId, int count = 50)
        {
            return await _context.AdminLogs
                .Include(l => l.Admin)
                .Where(l => l.AdminId == adminId)
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<AdminLog>> GetLogsByTargetAsync(string targetType, string targetId, int count = 50)
        {
            return await _context.AdminLogs
                .Include(l => l.Admin)
                .Where(l => l.TargetType == targetType && l.TargetId == targetId)
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        public async Task<List<AdminLog>> SearchLogsAsync(string searchTerm, DateTime? startDate = null, DateTime? endDate = null, int count = 50)
        {
            var query = _context.AdminLogs
                .Include(l => l.Admin)
                .AsQueryable();

            // Aplicar filtros
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                query = query.Where(l =>
                    l.Action.ToLower().Contains(searchTerm) ||
                    l.TargetType.ToLower().Contains(searchTerm) ||
                    l.TargetId.ToLower().Contains(searchTerm) ||
                    (l.Details != null && l.Details.ToLower().Contains(searchTerm)) ||
                    (l.Admin.Email != null && l.Admin.Email.ToLower().Contains(searchTerm))
                );
            }

            if (startDate.HasValue)
            {
                query = query.Where(l => l.Timestamp >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(l => l.Timestamp <= endDate.Value);
            }

            return await query
                .OrderByDescending(l => l.Timestamp)
                .Take(count)
                .ToListAsync();
        }
    }
}