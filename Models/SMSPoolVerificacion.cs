using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class SMSPoolVerificacion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int NumeroId { get; set; }

    [ForeignKey("NumeroId")]
    public virtual SMSPoolNumero Numero { get; set; }

    [Required]
    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    [Required]
    public string MensajeCompleto { get; set; } // Mensaje SMS completo

    public string CodigoExtraido { get; set; } // Código extraído automáticamente

    public string Remitente { get; set; } // Remitente del mensaje

    public bool Utilizado { get; set; } = false; // Si el código ha sido utilizado
}
