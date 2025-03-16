using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class SaldoCuenta
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        [Required]
        public decimal Saldo { get; set; } = 0;

        [Required]
        public DateTime UltimaActualizacion { get; set; } = DateTime.UtcNow;
    }
}
