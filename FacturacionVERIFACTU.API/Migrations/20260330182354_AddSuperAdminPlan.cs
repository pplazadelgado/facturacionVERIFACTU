using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FacturacionVERIFACTU.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSuperAdminPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_fin_plan",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "fecha_incicio_plan",
                table: "Tenants",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "notas_admin",
                table: "Tenants",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan",
                table: "Tenants",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "precio_mensual",
                table: "Tenants",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "recibos_servicio",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<int>(type: "integer", nullable: false),
                    numero_recibo = table.Column<int>(type: "integer", nullable: false),
                    Concepto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    periodo_dsde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    periodo_hasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    importe_base = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    porcentaje_iva = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    importe_iva = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    fecha_emision = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    creado_por_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recibos_servicio", x => x.id);
                    table.ForeignKey(
                        name: "FK_recibos_servicio_Tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "Tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recibos_servicio_numero_recibo",
                table: "recibos_servicio",
                column: "numero_recibo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recibos_servicio_tenant_id",
                table: "recibos_servicio",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recibos_servicio");

            migrationBuilder.DropColumn(
                name: "fecha_fin_plan",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "fecha_incicio_plan",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "notas_admin",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "plan",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "precio_mensual",
                table: "Tenants");
        }
    }
}
