using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPeriodoContratadoYDescuento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DescuentoAplicado",
                table: "NumerosTelefonicos",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PeriodoContratado",
                table: "NumerosTelefonicos",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescuentoAplicado",
                table: "NumerosTelefonicos");

            migrationBuilder.DropColumn(
                name: "PeriodoContratado",
                table: "NumerosTelefonicos");
        }
    }
}
