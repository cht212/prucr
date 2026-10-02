using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CRM.Data.Migrations;

public partial class AddPerformanceIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Los índices simples también respaldan claves foráneas en MySQL.
        // Se conservan mientras se agregan los índices compuestos para evitar
        // que MySQL rechace la migración antes de crear sus reemplazos.

        migrationBuilder.AlterColumn<string>("c_estado", "crm_tarea", "varchar(32)", maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "longtext");
        migrationBuilder.AlterColumn<string>("c_etapa", "crm_oportunidad", "varchar(32)", maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "longtext");
        migrationBuilder.AlterColumn<string>("c_tipo", "crm_mensaje", "varchar(50)", maxLength: 50, nullable: true, oldClrType: typeof(string), oldType: "longtext", oldNullable: true);
        migrationBuilder.AlterColumn<string>("c_estado", "crm_conversacion", "varchar(32)", maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "longtext");
        migrationBuilder.AlterColumn<string>("c_canal", "crm_conversacion", "varchar(32)", maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "longtext");
        migrationBuilder.AlterColumn<string>("c_canal_origen", "crm_cliente", "varchar(32)", maxLength: 32, nullable: false, oldClrType: typeof(string), oldType: "longtext");

        migrationBuilder.CreateIndex("IX_crm_tarea_n_asignado_a_c_estado_d_fecha_vencimiento", "crm_tarea", new[] { "n_asignado_a", "c_estado", "d_fecha_vencimiento" });
        migrationBuilder.CreateIndex("IX_crm_tarea_d_fecha_creacion", "crm_tarea", "d_fecha_creacion");
        migrationBuilder.CreateIndex("IX_crm_oportunidad_n_usuario_asignado_c_etapa_d_fecha_cierre_es~", "crm_oportunidad", new[] { "n_usuario_asignado", "c_etapa", "d_fecha_cierre_estimada" });
        migrationBuilder.CreateIndex("IX_crm_oportunidad_d_fecha_creacion", "crm_oportunidad", "d_fecha_creacion");
        migrationBuilder.CreateIndex("IX_crm_mensaje_c_tipo_c_canal_d_fecha", "crm_mensaje", new[] { "c_tipo", "c_canal", "d_fecha" });
        migrationBuilder.CreateIndex("IX_crm_mensaje_n_conversacion_c_tipo_c_reply_to_external_id_d_f~", "crm_mensaje", new[] { "n_conversacion", "c_tipo", "c_reply_to_external_id", "d_fecha" });
        migrationBuilder.CreateIndex("IX_crm_mensaje_n_conversacion_d_fecha", "crm_mensaje", new[] { "n_conversacion", "d_fecha" });
        migrationBuilder.CreateIndex("IX_crm_mensaje_d_fecha", "crm_mensaje", "d_fecha");
        migrationBuilder.CreateIndex("IX_crm_conversacion_c_canal_c_estado_d_ultimo_mensaje", "crm_conversacion", new[] { "c_canal", "c_estado", "d_ultimo_mensaje" });
        migrationBuilder.CreateIndex("IX_crm_conversacion_n_usuario_asignado_c_estado_d_ultimo_mensaje", "crm_conversacion", new[] { "n_usuario_asignado", "c_estado", "d_ultimo_mensaje" });
        migrationBuilder.CreateIndex("IX_crm_conversacion_d_fecha_inicio", "crm_conversacion", "d_fecha_inicio");
        migrationBuilder.CreateIndex("IX_crm_cliente_c_canal_origen_d_fecha_registro", "crm_cliente", new[] { "c_canal_origen", "d_fecha_registro" });
        migrationBuilder.CreateIndex("IX_crm_cliente_d_fecha_registro", "crm_cliente", "d_fecha_registro");
        migrationBuilder.CreateIndex("IX_crm_actividad_log_d_fecha", "crm_actividad_log", "d_fecha");
        migrationBuilder.CreateIndex("IX_crm_actividad_log_n_usuario_d_fecha", "crm_actividad_log", new[] { "n_usuario", "d_fecha" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_crm_tarea_n_asignado_a_c_estado_d_fecha_vencimiento", "crm_tarea");
        migrationBuilder.DropIndex("IX_crm_tarea_d_fecha_creacion", "crm_tarea");
        migrationBuilder.DropIndex("IX_crm_oportunidad_n_usuario_asignado_c_etapa_d_fecha_cierre_es~", "crm_oportunidad");
        migrationBuilder.DropIndex("IX_crm_oportunidad_d_fecha_creacion", "crm_oportunidad");
        migrationBuilder.DropIndex("IX_crm_mensaje_c_tipo_c_canal_d_fecha", "crm_mensaje");
        migrationBuilder.DropIndex("IX_crm_mensaje_n_conversacion_c_tipo_c_reply_to_external_id_d_f~", "crm_mensaje");
        migrationBuilder.DropIndex("IX_crm_mensaje_n_conversacion_d_fecha", "crm_mensaje");
        migrationBuilder.DropIndex("IX_crm_mensaje_d_fecha", "crm_mensaje");
        migrationBuilder.DropIndex("IX_crm_conversacion_c_canal_c_estado_d_ultimo_mensaje", "crm_conversacion");
        migrationBuilder.DropIndex("IX_crm_conversacion_n_usuario_asignado_c_estado_d_ultimo_mensaje", "crm_conversacion");
        migrationBuilder.DropIndex("IX_crm_conversacion_d_fecha_inicio", "crm_conversacion");
        migrationBuilder.DropIndex("IX_crm_cliente_c_canal_origen_d_fecha_registro", "crm_cliente");
        migrationBuilder.DropIndex("IX_crm_cliente_d_fecha_registro", "crm_cliente");
        migrationBuilder.DropIndex("IX_crm_actividad_log_d_fecha", "crm_actividad_log");
        migrationBuilder.DropIndex("IX_crm_actividad_log_n_usuario_d_fecha", "crm_actividad_log");

        migrationBuilder.AlterColumn<string>("c_estado", "crm_tarea", "longtext", nullable: false, oldClrType: typeof(string), oldType: "varchar(32)", oldMaxLength: 32);
        migrationBuilder.AlterColumn<string>("c_etapa", "crm_oportunidad", "longtext", nullable: false, oldClrType: typeof(string), oldType: "varchar(32)", oldMaxLength: 32);
        migrationBuilder.AlterColumn<string>("c_tipo", "crm_mensaje", "longtext", nullable: true, oldClrType: typeof(string), oldType: "varchar(50)", oldMaxLength: 50, oldNullable: true);
        migrationBuilder.AlterColumn<string>("c_estado", "crm_conversacion", "longtext", nullable: false, oldClrType: typeof(string), oldType: "varchar(32)", oldMaxLength: 32);
        migrationBuilder.AlterColumn<string>("c_canal", "crm_conversacion", "longtext", nullable: false, oldClrType: typeof(string), oldType: "varchar(32)", oldMaxLength: 32);
        migrationBuilder.AlterColumn<string>("c_canal_origen", "crm_cliente", "longtext", nullable: false, oldClrType: typeof(string), oldType: "varchar(32)", oldMaxLength: 32);

    }
}
