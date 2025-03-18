using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TelefonicaEmpresaria.Models
{
    public class AdminLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string AdminId { get; set; }

        [ForeignKey("AdminId")]
        public ApplicationUser Admin { get; set; }

        [Required]
        public string Action { get; set; }

        [Required]
        public string TargetType { get; set; }

        [Required]
        public string TargetId { get; set; }

        public string Details { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        public string IpAddress { get; set; }
    }
}