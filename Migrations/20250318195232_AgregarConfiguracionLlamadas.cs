using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarConfiguracionLlamadas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LlamadasEntrantes",
                table: "NumerosTelefonicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LlamadasSalientes",
                table: "NumerosTelefonicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LlamadasEntrantes",
                table: "NumerosTelefonicos");

            migrationBuilder.DropColumn(
                name: "LlamadasSalientes",
                table: "NumerosTelefonicos");
        }
    }
}
