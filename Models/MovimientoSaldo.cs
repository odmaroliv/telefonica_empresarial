using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class MovimientoSaldo
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        [Required]
        public decimal Monto { get; set; }

        [Required]
        public string Concepto { get; set; } = string.Empty;

        [Required]
        public DateTime Fecha { get; set; } = DateTime.UtcNow;

        [Required]
        public string TipoMovimiento { get; set; } = string.Empty; // "Recarga", "Consumo", "Reembolso"

        public string? ReferenciaExterna { get; set; } // ID de transacción externa (Stripe, etc.)

        public int? NumeroTelefonicoId { get; set; }

        [ForeignKey("NumeroTelefonicoId")]
        public virtual NumeroTelefonico? NumeroTelefonico { get; set; }
    }
}
