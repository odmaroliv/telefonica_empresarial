using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    /// <summary>
    /// Modelo para representar una llamada saliente
    /// </summary>
    public class LlamadaSaliente
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// ID del usuario que realiza la llamada
        /// </summary>
        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        /// <summary>
        /// ID del número telefónico desde el que se realiza la llamada
        /// </summary>
        [Required]
        public int NumeroTelefonicoId { get; set; }

        [ForeignKey("NumeroTelefonicoId")]
        public virtual NumeroTelefonico? NumeroTelefonico { get; set; }

        /// <summary>
        /// Número de destino de la llamada (formato E.164)
        /// </summary>
        [Required]
        public string NumeroDestino { get; set; } = string.Empty;

        /// <summary>
        /// Fecha y hora de inicio de la llamada
        /// </summary>
        [Required]
        public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Fecha y hora de finalización de la llamada (nulo si aún está en curso)
        /// </summary>
        public DateTime? FechaFin { get; set; }

        /// <summary>
        /// Duración de la llamada en segundos
        /// </summary>
        public int? Duracion { get; set; }

        /// <summary>
        /// Estado actual de la llamada
        /// </summary>
        [Required]
        public string Estado { get; set; } = "iniciando"; // iniciando, en-curso, completada, fallida, cancelada

        /// <summary>
        /// ID de la llamada en Twilio (CallSid)
        /// </summary>
        public string? TwilioCallSid { get; set; }

        /// <summary>
        /// Costo de la llamada (se actualiza al finalizar)
        /// </summary>
        public decimal? Costo { get; set; }

        /// <summary>
        /// Notas o detalles adicionales (error, etc.)
        /// </summary>
        public string? Detalles { get; set; }

        /// <summary>
        /// Fecha de procesamiento del consumo
        /// </summary>
        public DateTime? FechaProcesamientoConsumo { get; set; }

        /// <summary>
        /// Indica si el consumo de la llamada ya fue procesado
        /// </summary>
        public bool ConsumoRegistrado { get; set; } = false;
        public DateTime? UltimoHeartbeat { get; set; }
    }

}