using System.ComponentModel.DataAnnotations;

namespace TelefonicaEmpresaria.Models
{
    public class RequisitosRegulatorios
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string CodigoPais { get; set; } = "MX"; // MX, US, etc.

        [Required]
        public string Nombre { get; set; } = string.Empty; // Nombre del país

        [Required]
        public bool RequiereIdentificacion { get; set; } = false;

        [Required]
        public bool RequiereComprobanteDomicilio { get; set; } = false;

        [Required]
        public bool RequiereDocumentoFiscal { get; set; } = false; // RFC, TIN, etc.

        [Required]
        public bool RequiereFormularioRegulatorio { get; set; } = false;

        [Required]
        public string DocumentacionRequerida { get; set; } = string.Empty; // Descripción de los documentos necesarios

        [Required]
        public string InstruccionesVerificacion { get; set; } = string.Empty; // Instrucciones para el usuario

        [Required]
        public bool Activo { get; set; } = true;

        // Restricciones adicionales
        public int? MaximoNumerosPermitidos { get; set; }

        public bool RequiereVerificacionPreviaCompra { get; set; } = false;
    }


}