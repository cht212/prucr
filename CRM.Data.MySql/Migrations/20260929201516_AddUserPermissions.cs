using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "crm_usuario_permiso",
                columns: table => new
                {
                    n_usuario = table.Column<int>(type: "int", nullable: false),
                    c_permiso = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    d_fecha_asignacion = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    n_otorgado_por = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_crm_usuario_permiso", x => new { x.n_usuario, x.c_permiso });
                    table.ForeignKey(
                        name: "FK_crm_usuario_permiso_crm_usuario_n_otorgado_por",
                        column: x => x.n_otorgado_por,
                        principalTable: "crm_usuario",
                        principalColumn: "n_usuario",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_crm_usuario_permiso_crm_usuario_n_usuario",
                        column: x => x.n_usuario,
                        principalTable: "crm_usuario",
                        principalColumn: "n_usuario",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_crm_usuario_permiso_n_otorgado_por",
                table: "crm_usuario_permiso",
                column: "n_otorgado_por");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crm_usuario_permiso");
        }
    }
}
