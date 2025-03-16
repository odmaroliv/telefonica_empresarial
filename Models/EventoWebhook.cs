using System.ComponentModel.DataAnnotations;

namespace TelefonicaEmpresaria.Models
{
    public class EventoWebhook
    {
        [Key]
        public string EventoId { get; set; } = string.Empty;

        [Required]
        public DateTime FechaRecibido { get; set; } = DateTime.UtcNow;

        public DateTime? FechaUltimoIntento { get; set; }

        public DateTime? FechaCompletado { get; set; }

        [Required]
        public bool Completado { get; set; } = false;

        [Required]
        public int NumeroIntentos { get; set; } = 1;

        public string? Detalles { get; set; }
    }
}
