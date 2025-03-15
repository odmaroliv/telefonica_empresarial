using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class LogLlamada
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int NumeroTelefonicoId { get; set; }

        [ForeignKey("NumeroTelefonicoId")]
        public virtual NumeroTelefonico? NumeroTelefonico { get; set; }

        [Required]
        public string NumeroOrigen { get; set; } = string.Empty;

        [Required]
        public DateTime FechaHora { get; set; } = DateTime.UtcNow;

        [Required]
        public int Duracion { get; set; } = 0;

        public string? Estado { get; set; } = "Completada";

        public string? IdLlamadaPlivo { get; set; }
    }

}
