using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageClientRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "c_client_request_id",
                table: "crm_mensaje",
                type: "varchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_crm_mensaje_n_conversacion_c_client_request_id",
                table: "crm_mensaje",
                columns: new[] { "n_conversacion", "c_client_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_crm_mensaje_n_conversacion_c_client_request_id",
                table: "crm_mensaje");

            migrationBuilder.DropColumn(
                name: "c_client_request_id",
                table: "crm_mensaje");
        }
    }
}
