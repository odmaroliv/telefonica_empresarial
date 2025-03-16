using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{

    public class DocumentacionUsuario
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty;

        [ForeignKey("UserId")]
        public virtual ApplicationUser? Usuario { get; set; }

        [Required]
        public string CodigoPais { get; set; } = "MX";

        // Campos para documentación
        public string? IdentificacionUrl { get; set; }
        public DateTime? FechaSubidaIdentificacion { get; set; }

        public string? ComprobanteDomicilioUrl { get; set; }
        public DateTime? FechaSubidaComprobanteDomicilio { get; set; }

        public string? DocumentoFiscalUrl { get; set; } // RFC, TIN, etc.
        public DateTime? FechaSubidaDocumentoFiscal { get; set; }

        public string? FormularioRegulatorioUrl { get; set; }
        public DateTime? FechaSubidaFormularioRegulatorio { get; set; }

        [Required]
        public string EstadoVerificacion { get; set; } = "Pendiente"; // Pendiente, Aprobado, Rechazado

        public string? MotivoRechazo { get; set; }

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime? FechaVerificacion { get; set; }
    }
}
