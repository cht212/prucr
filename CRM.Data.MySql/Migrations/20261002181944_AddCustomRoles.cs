using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace CRM.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "n_rol",
                table: "crm_usuario",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "crm_rol",
                columns: table => new
                {
                    n_rol = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    c_nombre = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    c_descripcion = table.Column<string>(type: "varchar(250)", maxLength: 250, nullable: false),
                    c_rol_base = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    c_estado = table.Column<string>(type: "varchar(1)", nullable: false),
                    d_fecha_creacion = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    n_creado_por = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_rol", x => x.n_rol);
                    table.ForeignKey(
                        name: "FK_crm_rol_crm_usuario_n_creado_por",
                        column: x => x.n_creado_por,
                        principalTable: "crm_usuario",
                        principalColumn: "n_usuario",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "crm_rol_permiso",
                columns: table => new
                {
                    n_rol = table.Column<int>(type: "int", nullable: false),
                    c_permiso = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_rol_permiso", x => new { x.n_rol, x.c_permiso });
                    table.ForeignKey(
                        name: "FK_crm_rol_permiso_crm_rol_n_rol",
                        column: x => x.n_rol,
                        principalTable: "crm_rol",
                        principalColumn: "n_rol",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_crm_usuario_n_rol",
                table: "crm_usuario",
                column: "n_rol");

            migrationBuilder.CreateIndex(
                name: "IX_crm_rol_c_nombre",
                table: "crm_rol",
                column: "c_nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_rol_n_creado_por",
                table: "crm_rol",
                column: "n_creado_por");

            migrationBuilder.AddForeignKey(
                name: "FK_crm_usuario_crm_rol_n_rol",
                table: "crm_usuario",
                column: "n_rol",
                principalTable: "crm_rol",
                principalColumn: "n_rol",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_crm_usuario_crm_rol_n_rol",
                table: "crm_usuario");

            migrationBuilder.DropTable(
                name: "crm_rol_permiso");

            migrationBuilder.DropTable(
                name: "crm_rol");

            migrationBuilder.DropIndex(
                name: "IX_crm_usuario_n_rol",
                table: "crm_usuario");

            migrationBuilder.DropColumn(
                name: "n_rol",
                table: "crm_usuario");
        }
    }
}
