using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarConfiguracionLlamadasTrue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
               name: "LlamadasSalientes",
               table: "NumerosTelefonicos",
               type: "boolean",
               nullable: false,
               defaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "LlamadasEntrantes",
                table: "NumerosTelefonicos",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "LlamadasSalientes",
                table: "NumerosTelefonicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<bool>(
                name: "LlamadasEntrantes",
                table: "NumerosTelefonicos",
                type: "boolean",
                nullable: false,
                defaultValue: false);

        }
    }
}
