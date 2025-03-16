using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class NombreDeLaMigracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentacionUsuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CodigoPais = table.Column<string>(type: "text", nullable: false),
                    IdentificacionUrl = table.Column<string>(type: "text", nullable: true),
                    FechaSubidaIdentificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ComprobanteDomicilioUrl = table.Column<string>(type: "text", nullable: true),
                    FechaSubidaComprobanteDomicilio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DocumentoFiscalUrl = table.Column<string>(type: "text", nullable: true),
                    FechaSubidaDocumentoFiscal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FormularioRegulatorioUrl = table.Column<string>(type: "text", nullable: true),
                    FechaSubidaFormularioRegulatorio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EstadoVerificacion = table.Column<string>(type: "text", nullable: false),
                    MotivoRechazo = table.Column<string>(type: "text", nullable: true),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaVerificacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentacionUsuarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentacionUsuarios_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventosWebhook",
                columns: table => new
                {
                    EventoId = table.Column<string>(type: "text", nullable: false),
                    FechaRecibido = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaUltimoIntento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FechaCompletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Completado = table.Column<bool>(type: "boolean", nullable: false),
                    NumeroIntentos = table.Column<int>(type: "integer", nullable: false),
                    Detalles = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosWebhook", x => x.EventoId);
                });

            migrationBuilder.CreateTable(
                name: "RequisitosRegulatorios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CodigoPais = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    RequiereIdentificacion = table.Column<bool>(type: "boolean", nullable: false),
                    RequiereComprobanteDomicilio = table.Column<bool>(type: "boolean", nullable: false),
                    RequiereDocumentoFiscal = table.Column<bool>(type: "boolean", nullable: false),
                    RequiereFormularioRegulatorio = table.Column<bool>(type: "boolean", nullable: false),
                    DocumentacionRequerida = table.Column<string>(type: "text", nullable: false),
                    InstruccionesVerificacion = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    MaximoNumerosPermitidos = table.Column<int>(type: "integer", nullable: true),
                    RequiereVerificacionPreviaCompra = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequisitosRegulatorios", x => x.Id);
                });

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

            migrationBuilder.InsertData(
                table: "ConfiguracionesSistema",
                columns: new[] { "Id", "Clave", "Descripcion", "Valor" },
                values: new object[] { 4, "MargenGananciaLlamadas", "Margen de ganancia por minuto de llamadas (2.5 = 250%)", "2.5" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentacionUsuarios_UserId_CodigoPais",
                table: "DocumentacionUsuarios",
                columns: new[] { "UserId", "CodigoPais" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequisitosRegulatorios_CodigoPais",
                table: "RequisitosRegulatorios",
                column: "CodigoPais",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentacionUsuarios");

            migrationBuilder.DropTable(
                name: "EventosWebhook");

            migrationBuilder.DropTable(
                name: "RequisitosRegulatorios");

            migrationBuilder.DeleteData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia aplicado sobre el costo del proveedor (0.8 = 80%)", "0.8" });

            migrationBuilder.UpdateData(
                table: "ConfiguracionesSistema",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "Descripcion", "Valor" },
                values: new object[] { "Margen de ganancia para el servicio de SMS (0.85 = 85%)", "0.85" });
        }
    }
}
