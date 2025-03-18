using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class NumeroTelefonico
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Numero { get; set; } = string.Empty;

        [Required]
        public string PlivoUuid { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        [Required]
        public string NumeroRedireccion { get; set; } = string.Empty;

        public bool SMSHabilitado { get; set; } = false;

        [Required]
        public DateTime FechaCompra { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime FechaExpiracion { get; set; }

        public bool Activo { get; set; } = true;

        public string? StripeSubscriptionId { get; set; }

        [Required]
        public decimal CostoMensual { get; set; }

        public decimal? CostoSMS { get; set; }


        public int PeriodoContratado { get; set; } = 1;


        public decimal? DescuentoAplicado { get; set; }

        public virtual ICollection<LogLlamada>? LogsLlamadas { get; set; }
        public virtual ICollection<LogSMS>? LogsSMS { get; set; }
    }
}
