using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class AddFechaTansaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FechaCompletado",
                table: "Transacciones",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FechaCompletado",
                table: "Transacciones");
        }
    }
}
