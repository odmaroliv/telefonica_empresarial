using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class AgregarLlamadasSalientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlamadasSalientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    NumeroTelefonicoId = table.Column<int>(type: "integer", nullable: false),
                    NumeroDestino = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FechaInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaFin = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Duracion = table.Column<int>(type: "integer", nullable: true),
                    Estado = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TwilioCallSid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Costo = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Detalles = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    FechaProcesamientoConsumo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumoRegistrado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlamadasSalientes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LlamadasSalientes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LlamadasSalientes_NumerosTelefonicos_NumeroTelefonicoId",
                        column: x => x.NumeroTelefonicoId,
                        principalTable: "NumerosTelefonicos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia aplicado sobre el costo del proveedor (3.0 = 300%)", "3.0" });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia para el servicio de SMS (3.5 = 350%)", "3.5" });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia por minuto de llamadas (4.0 = 400%)", "4.0" });

            migrationBuilder.InsertData(
                table: "ConfiguracionesSistema",
                columns: new[] { "Id", "Clave", "Descripcion", "Valor" },
                values: new object[,]
                {
                    { 5, "CostoMinimoNumero", "Costo mínimo mensual para números telefónicos (MXN)", "100.0" },
                    { 6, "CostoMinimoSMS", "Costo mínimo mensual para servicio SMS (MXN)", "25.0" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlamadasSalientes_NumeroTelefonicoId",
                table: "LlamadasSalientes",
                column: "NumeroTelefonicoId");

            migrationBuilder.CreateIndex(
                name: "IX_LlamadasSalientes_UserId",
                table: "LlamadasSalientes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlamadasSalientes");

            migrationBuilder.DeleteData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia aplicado sobre el costo del proveedor (1.5 = 150%)", "1.5" });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia para el servicio de SMS (2.0 = 200%)", "2.0" });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia por minuto de llamadas (2.5 = 250%)", "2.5" });
        }
    }
}
