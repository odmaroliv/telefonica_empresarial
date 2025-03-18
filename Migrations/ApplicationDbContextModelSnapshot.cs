﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using TelefonicaEmpresaria.Data.TelefonicaEmpresarial.Data;

#nullable disable

namespace TelefonicaEmpresaria.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    partial class ApplicationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.14")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRole", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Name")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedName")
                        .IsUnique()
                        .HasDatabaseName("RoleNameIndex");

                    b.ToTable("AspNetRoles", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<string>("RoleId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetRoleClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("ClaimType")
                        .HasColumnType("text");

                    b.Property<string>("ClaimValue")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserClaims", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.Property<string>("LoginProvider")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("ProviderKey")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("ProviderDisplayName")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("LoginProvider", "ProviderKey");

                    b.HasIndex("UserId");

                    b.ToTable("AspNetUserLogins", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<string>", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<string>("RoleId")
                        .HasColumnType("text");

                    b.HasKey("UserId", "RoleId");

                    b.HasIndex("RoleId");

                    b.ToTable("AspNetUserRoles", (string)null);
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.Property<string>("UserId")
                        .HasColumnType("text");

                    b.Property<string>("LoginProvider")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("Name")
                        .HasMaxLength(128)
                        .HasColumnType("character varying(128)");

                    b.Property<string>("Value")
                        .HasColumnType("text");

                    b.HasKey("UserId", "LoginProvider", "Name");

                    b.ToTable("AspNetUserTokens", (string)null);
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.AdminLog", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Action")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("AdminId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Details")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("IpAddress")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("TargetId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("TargetType")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("Timestamp")
                        .HasColumnType("timestamp with time zone");

                    b.HasKey("Id");

                    b.HasIndex("AdminId");

                    b.ToTable("AdminLogs");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.ApplicationUser", b =>
                {
                    b.Property<string>("Id")
                        .HasColumnType("text");

                    b.Property<int>("AccessFailedCount")
                        .HasColumnType("integer");

                    b.Property<string>("Apellidos")
                        .HasColumnType("text");

                    b.Property<string>("Ciudad")
                        .HasColumnType("text");

                    b.Property<string>("CodigoPostal")
                        .HasColumnType("text");

                    b.Property<string>("ConcurrencyStamp")
                        .IsConcurrencyToken()
                        .HasColumnType("text");

                    b.Property<string>("Direccion")
                        .HasColumnType("text");

                    b.Property<string>("Email")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<bool>("EmailConfirmed")
                        .HasColumnType("boolean");

                    b.Property<DateTime>("FechaRegistro")
                        .HasColumnType("timestamp with time zone");

                    b.Property<bool>("LockoutEnabled")
                        .HasColumnType("boolean");

                    b.Property<DateTimeOffset?>("LockoutEnd")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Nombre")
                        .HasColumnType("text");

                    b.Property<string>("NormalizedEmail")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("NormalizedUserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.Property<string>("Pais")
                        .HasColumnType("text");

                    b.Property<string>("PasswordHash")
                        .HasColumnType("text");

                    b.Property<string>("PhoneNumber")
                        .HasColumnType("text");

                    b.Property<bool>("PhoneNumberConfirmed")
                        .HasColumnType("boolean");

                    b.Property<string>("RFC")
                        .HasColumnType("text");

                    b.Property<string>("SecurityStamp")
                        .HasColumnType("text");

                    b.Property<string>("StripeCustomerId")
                        .HasColumnType("text");

                    b.Property<bool>("TwoFactorEnabled")
                        .HasColumnType("boolean");

                    b.Property<string>("UserName")
                        .HasMaxLength(256)
                        .HasColumnType("character varying(256)");

                    b.HasKey("Id");

                    b.HasIndex("NormalizedEmail")
                        .HasDatabaseName("EmailIndex");

                    b.HasIndex("NormalizedUserName")
                        .IsUnique()
                        .HasDatabaseName("UserNameIndex");

                    b.ToTable("AspNetUsers", (string)null);
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.ConfiguracionSistema", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Clave")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Descripcion")
                        .HasColumnType("text");

                    b.Property<string>("Valor")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("ConfiguracionesSistema");

                    b.HasData(
                        new
                        {
                            Id = 1,
                            Clave = "MargenGanancia",
                            Descripcion = "Margen de ganancia aplicado sobre el costo del proveedor (3.0 = 300%)",
                            Valor = "3.0"
                        },
                        new
                        {
                            Id = 2,
                            Clave = "MargenGananciaSMS",
                            Descripcion = "Margen de ganancia para el servicio de SMS (3.5 = 350%)",
                            Valor = "3.5"
                        },
                        new
                        {
                            Id = 3,
                            Clave = "IVA",
                            Descripcion = "Impuesto al Valor Agregado",
                            Valor = "0.16"
                        },
                        new
                        {
                            Id = 4,
                            Clave = "MargenGananciaLlamadas",
                            Descripcion = "Margen de ganancia por minuto de llamadas (4.0 = 400%)",
                            Valor = "4.0"
                        },
                        new
                        {
                            Id = 5,
                            Clave = "CostoMinimoNumero",
                            Descripcion = "Costo mínimo mensual para números telefónicos (MXN)",
                            Valor = "100.0"
                        },
                        new
                        {
                            Id = 6,
                            Clave = "CostoMinimoSMS",
                            Descripcion = "Costo mínimo mensual para servicio SMS (MXN)",
                            Valor = "25.0"
                        });
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.DocumentacionUsuario", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("CodigoPais")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("ComprobanteDomicilioUrl")
                        .HasColumnType("text");

                    b.Property<string>("DocumentoFiscalUrl")
                        .HasColumnType("text");

                    b.Property<string>("EstadoVerificacion")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("FechaCreacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaSubidaComprobanteDomicilio")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaSubidaDocumentoFiscal")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaSubidaFormularioRegulatorio")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaSubidaIdentificacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaVerificacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("FormularioRegulatorioUrl")
                        .HasColumnType("text");

                    b.Property<string>("IdentificacionUrl")
                        .HasColumnType("text");

                    b.Property<string>("MotivoRechazo")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId", "CodigoPais")
                        .IsUnique();

                    b.ToTable("DocumentacionUsuarios");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.EventoWebhook", b =>
                {
                    b.Property<string>("EventoId")
                        .HasColumnType("text");

                    b.Property<bool>("Completado")
                        .HasColumnType("boolean");

                    b.Property<string>("Detalles")
                        .HasColumnType("text");

                    b.Property<DateTime?>("FechaCompletado")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime>("FechaRecibido")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaUltimoIntento")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("NumeroIntentos")
                        .HasColumnType("integer");

                    b.HasKey("EventoId");

                    b.ToTable("EventosWebhook");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LlamadaSaliente", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<bool>("ConsumoRegistrado")
                        .HasColumnType("boolean");

                    b.Property<decimal?>("Costo")
                        .HasColumnType("decimal(10, 2)");

                    b.Property<string>("Detalles")
                        .HasMaxLength(1000)
                        .HasColumnType("character varying(1000)");

                    b.Property<int?>("Duracion")
                        .HasColumnType("integer");

                    b.Property<string>("Estado")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<DateTime?>("FechaFin")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime>("FechaInicio")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaProcesamientoConsumo")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("NumeroDestino")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<int>("NumeroTelefonicoId")
                        .HasColumnType("integer");

                    b.Property<string>("TwilioCallSid")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)");

                    b.Property<DateTime?>("UltimoHeartbeat")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("NumeroTelefonicoId");

                    b.HasIndex("UserId");

                    b.ToTable("LlamadasSalientes", (string)null);
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LogLlamada", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<int>("Duracion")
                        .HasColumnType("integer");

                    b.Property<string>("Estado")
                        .HasColumnType("text");

                    b.Property<DateTime>("FechaHora")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("IdLlamadaPlivo")
                        .HasColumnType("text");

                    b.Property<string>("NumeroOrigen")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("NumeroTelefonicoId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("NumeroTelefonicoId");

                    b.ToTable("LogsLlamadas");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LogSMS", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<DateTime>("FechaHora")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("IdMensajePlivo")
                        .HasColumnType("text");

                    b.Property<string>("Mensaje")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("NumeroOrigen")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int>("NumeroTelefonicoId")
                        .HasColumnType("integer");

                    b.HasKey("Id");

                    b.HasIndex("NumeroTelefonicoId");

                    b.ToTable("LogsSMS");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.MovimientoSaldo", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Concepto")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime>("Fecha")
                        .HasColumnType("timestamp with time zone");

                    b.Property<decimal>("Monto")
                        .HasColumnType("numeric");

                    b.Property<int?>("NumeroTelefonicoId")
                        .HasColumnType("integer");

                    b.Property<string>("ReferenciaExterna")
                        .HasColumnType("text");

                    b.Property<string>("TipoMovimiento")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("NumeroTelefonicoId");

                    b.HasIndex("UserId");

                    b.ToTable("MovimientosSaldo");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.NumeroTelefonico", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<bool>("Activo")
                        .HasColumnType("boolean");

                    b.Property<decimal>("CostoMensual")
                        .HasColumnType("numeric");

                    b.Property<decimal?>("CostoSMS")
                        .HasColumnType("numeric");

                    b.Property<DateTime>("FechaCompra")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime>("FechaExpiracion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Numero")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("NumeroRedireccion")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("PlivoUuid")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("SMSHabilitado")
                        .HasColumnType("boolean");

                    b.Property<string>("StripeSubscriptionId")
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("NumerosTelefonicos");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.RequisitosRegulatorios", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<bool>("Activo")
                        .HasColumnType("boolean");

                    b.Property<string>("CodigoPais")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("DocumentacionRequerida")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("InstruccionesVerificacion")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<int?>("MaximoNumerosPermitidos")
                        .HasColumnType("integer");

                    b.Property<string>("Nombre")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<bool>("RequiereComprobanteDomicilio")
                        .HasColumnType("boolean");

                    b.Property<bool>("RequiereDocumentoFiscal")
                        .HasColumnType("boolean");

                    b.Property<bool>("RequiereFormularioRegulatorio")
                        .HasColumnType("boolean");

                    b.Property<bool>("RequiereIdentificacion")
                        .HasColumnType("boolean");

                    b.Property<bool>("RequiereVerificacionPreviaCompra")
                        .HasColumnType("boolean");

                    b.HasKey("Id");

                    b.HasIndex("CodigoPais")
                        .IsUnique();

                    b.ToTable("RequisitosRegulatorios");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.SaldoCuenta", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<decimal>("Saldo")
                        .HasColumnType("numeric");

                    b.Property<DateTime>("UltimaActualizacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("SaldosCuenta");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.Transaccion", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("Concepto")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("DetalleError")
                        .HasColumnType("text");

                    b.Property<DateTime>("Fecha")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime?>("FechaCompletado")
                        .HasColumnType("timestamp with time zone");

                    b.Property<decimal>("Monto")
                        .HasColumnType("numeric");

                    b.Property<int?>("NumeroTelefonicoId")
                        .HasColumnType("integer");

                    b.Property<string>("Status")
                        .HasColumnType("text");

                    b.Property<string>("StripePaymentId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.HasIndex("NumeroTelefonicoId");

                    b.HasIndex("UserId");

                    b.ToTable("Transacciones");
                });

            modelBuilder.Entity("TransaccionAuditoria", b =>
                {
                    b.Property<int>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("integer");

                    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));

                    b.Property<string>("DatosRequest")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("DetalleError")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("Estado")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<DateTime?>("FechaActualizacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<DateTime>("FechaCreacion")
                        .HasColumnType("timestamp with time zone");

                    b.Property<decimal>("Monto")
                        .HasColumnType("numeric");

                    b.Property<string>("ReferenciaExterna")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("TipoOperacion")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("UserId")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("TransaccionesAuditoria");
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityRoleClaim<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserClaim<string>", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserLogin<string>", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserRole<string>", b =>
                {
                    b.HasOne("Microsoft.AspNetCore.Identity.IdentityRole", null)
                        .WithMany()
                        .HasForeignKey("RoleId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("Microsoft.AspNetCore.Identity.IdentityUserToken<string>", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", null)
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.AdminLog", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Admin")
                        .WithMany()
                        .HasForeignKey("AdminId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Admin");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.DocumentacionUsuario", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LlamadaSaliente", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.NumeroTelefonico", "NumeroTelefonico")
                        .WithMany()
                        .HasForeignKey("NumeroTelefonicoId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("NumeroTelefonico");

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LogLlamada", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.NumeroTelefonico", "NumeroTelefonico")
                        .WithMany("LogsLlamadas")
                        .HasForeignKey("NumeroTelefonicoId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("NumeroTelefonico");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.LogSMS", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.NumeroTelefonico", "NumeroTelefonico")
                        .WithMany("LogsSMS")
                        .HasForeignKey("NumeroTelefonicoId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("NumeroTelefonico");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.MovimientoSaldo", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.NumeroTelefonico", "NumeroTelefonico")
                        .WithMany()
                        .HasForeignKey("NumeroTelefonicoId");

                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("NumeroTelefonico");

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.NumeroTelefonico", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany("NumerosTelefonicos")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.SaldoCuenta", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.Transaccion", b =>
                {
                    b.HasOne("TelefonicaEmpresaria.Models.NumeroTelefonico", "NumeroTelefonico")
                        .WithMany()
                        .HasForeignKey("NumeroTelefonicoId");

                    b.HasOne("TelefonicaEmpresaria.Models.ApplicationUser", "Usuario")
                        .WithMany("Transacciones")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("NumeroTelefonico");

                    b.Navigation("Usuario");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.ApplicationUser", b =>
                {
                    b.Navigation("NumerosTelefonicos");

                    b.Navigation("Transacciones");
                });

            modelBuilder.Entity("TelefonicaEmpresaria.Models.NumeroTelefonico", b =>
                {
                    b.Navigation("LogsLlamadas");

                    b.Navigation("LogsSMS");
                });
#pragma warning restore 612, 618
        }
    }
}
