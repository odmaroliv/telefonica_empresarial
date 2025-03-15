using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class Transaccion
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        public int? NumeroTelefonicoId { get; set; }

        [ForeignKey("NumeroTelefonicoId")]
        public virtual NumeroTelefonico? NumeroTelefonico { get; set; }

        [Required]
        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Required]
        public decimal Monto { get; set; }

        [Required]
        public string Concepto { get; set; } = string.Empty;

        [Required]
        public string StripePaymentId { get; set; } = string.Empty;

        public string? Status { get; set; } = "Procesando";

        public string? DetalleError { get; set; }
    }

}
