using System.ComponentModel.DataAnnotations;

public class SMSPoolConfiguracion
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Clave { get; set; }

    [Required]
    public string Valor { get; set; }

    public string Descripcion { get; set; }
}