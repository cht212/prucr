using CRM.Data.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Data.Migrations;

[DbContext(typeof(CrmDbContext))]
[Migration("20260930230000_AddMessageReplyContext")]
public partial class AddMessageReplyContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "c_reply_to_external_id",
            table: "crm_mensaje",
            type: "varchar(450)",
            maxLength: 450,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "c_reply_to_external_id",
            table: "crm_mensaje");
    }
}
