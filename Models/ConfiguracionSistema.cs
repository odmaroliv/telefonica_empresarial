using System.ComponentModel.DataAnnotations;

namespace TelefonicaEmpresaria.Models
{
    public class ConfiguracionSistema
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Clave { get; set; } = string.Empty;

        [Required]
        public string Valor { get; set; } = string.Empty;

        public string? Descripcion { get; set; }
    }
}
