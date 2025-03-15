using Microsoft.AspNetCore.Identity;

namespace TelefonicaEmpresaria.Models
{
    // Extensión del usuario de identidad
    public class ApplicationUser : IdentityUser
    {
        public string? Nombre { get; set; }
        public string? Apellidos { get; set; }
        public string? Direccion { get; set; }
        public string? Ciudad { get; set; }
        public string? CodigoPostal { get; set; }
        public string? Pais { get; set; }
        public string? RFC { get; set; }
        public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
        public string? StripeCustomerId { get; set; }
        public virtual ICollection<NumeroTelefonico>? NumerosTelefonicos { get; set; }
        public virtual ICollection<Transaccion>? Transacciones { get; set; }
    }
}
