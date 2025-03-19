using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    /// <inheritdoc />
    public partial class TablasSMSPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SMSPoolConfiguraciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Clave = table.Column<string>(type: "text", nullable: false),
                    Valor = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMSPoolConfiguraciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SMSPoolServicios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceId = table.Column<string>(type: "text", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    IconoUrl = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false),
                    CostoBase = table.Column<decimal>(type: "numeric", nullable: false),
                    PrecioVenta = table.Column<decimal>(type: "numeric", nullable: false),
                    TiempoEstimadoMinutos = table.Column<int>(type: "integer", nullable: false),
                    PaisesDisponibles = table.Column<string>(type: "text", nullable: false),
                    TasaExito = table.Column<decimal>(type: "numeric", nullable: false),
                    UltimaActualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMSPoolServicios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SMSPoolNumeros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ServicioId = table.Column<int>(type: "integer", nullable: false),
                    Numero = table.Column<string>(type: "text", nullable: false),
                    OrderId = table.Column<string>(type: "text", nullable: false),
                    Pais = table.Column<string>(type: "text", nullable: false),
                    FechaCompra = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Estado = table.Column<string>(type: "text", nullable: false),
                    CostoPagado = table.Column<decimal>(type: "numeric", nullable: false),
                    SMSRecibido = table.Column<bool>(type: "boolean", nullable: false),
                    FechaUltimaComprobacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CodigoRecibido = table.Column<string>(type: "text", nullable: false),
                    VerificacionExitosa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMSPoolNumeros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SMSPoolNumeros_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SMSPoolNumeros_SMSPoolServicios_ServicioId",
                        column: x => x.ServicioId,
                        principalTable: "SMSPoolServicios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SMSPoolVerificaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumeroId = table.Column<int>(type: "integer", nullable: false),
                    FechaRecepcion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MensajeCompleto = table.Column<string>(type: "text", nullable: false),
                    CodigoExtraido = table.Column<string>(type: "text", nullable: false),
                    Remitente = table.Column<string>(type: "text", nullable: false),
                    Utilizado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SMSPoolVerificaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SMSPoolVerificaciones_SMSPoolNumeros_NumeroId",
                        column: x => x.NumeroId,
                        principalTable: "SMSPoolNumeros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolConfiguraciones_Clave",
                table: "SMSPoolConfiguraciones",
                column: "Clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolNumeros_OrderId",
                table: "SMSPoolNumeros",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolNumeros_ServicioId",
                table: "SMSPoolNumeros",
                column: "ServicioId");

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolNumeros_UserId",
                table: "SMSPoolNumeros",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolServicios_ServiceId",
                table: "SMSPoolServicios",
                column: "ServiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SMSPoolVerificaciones_NumeroId",
                table: "SMSPoolVerificaciones",
                column: "NumeroId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SMSPoolConfiguraciones");

            migrationBuilder.DropTable(
                name: "SMSPoolVerificaciones");

            migrationBuilder.DropTable(
                name: "SMSPoolNumeros");

            migrationBuilder.DropTable(
                name: "SMSPoolServicios");
        }
    }
}
