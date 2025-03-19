using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class TablasSMSPoolPrecioAlto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PrecioAlto",
                table: "SMSPoolServicios",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrecioAlto",
                table: "SMSPoolServicios");
        }
    }
}
