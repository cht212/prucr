using CRM.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace CRM.Data.Data
{
    public class CrmDbContext : DbContext
    {
        public CrmDbContext(DbContextOptions<CrmDbContext> options) : base(options)
        {
        }

        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Conversacion> Conversaciones { get; set; }
        public DbSet<Mensaje> Mensajes { get; set; }
        public DbSet<CrmUsuario> Usuarios { get; set; }
        public DbSet<Etiqueta> Etiquetas { get; set; }
        public DbSet<ClienteEtiqueta> ClienteEtiquetas { get; set; }
        public DbSet<Oportunidad> Oportunidades { get; set; }
        public DbSet<Tarea> Tareas { get; set; }
        public DbSet<NotaInterna> NotasInternas { get; set; }
        public DbSet<ActividadLog> ActividadLogs { get; set; }
        public DbSet<Campana> Campanas { get; set; }
        public DbSet<CampanaCliente> CampanasClientes { get; set; }
        public DbSet<ReglaAutomatica> ReglasAutomaticas { get; set; }
        public DbSet<KpiDashboard> KpisDashboard { get; set; }
        public DbSet<ReporteExportacion> ReportesExportacion { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }
        public DbSet<UsuarioPermiso> UsuarioPermisos { get; set; }
        public DbSet<CrmRol> Roles { get; set; }
        public DbSet<CrmRolPermiso> RolPermisos { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<LoginAttempt>(entity =>
            {
                entity.ToTable("crm_login_attempt");
                entity.HasKey(x => x.Key);
                entity.Property(x => x.Key).HasColumnName("c_key").HasMaxLength(64);
                entity.Property(x => x.Failures).HasColumnName("n_failures");
                entity.Property(x => x.BlockedUntilUtc).HasColumnName("d_blocked_until_utc");
                entity.Property(x => x.UpdatedUtc).HasColumnName("d_updated_utc");
            });

            // Mapeo de nombres de tablas exactos, compatible con MySQL 8.
            modelBuilder.Entity<Cliente>().ToTable("crm_cliente");
            modelBuilder.Entity<Cliente>().HasKey(c => c.nCliente);
            modelBuilder.Entity<Cliente>().Property(c => c.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<Cliente>().Property(c => c.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<Cliente>().Property(c => c.cTelefono).HasColumnName("c_telefono");
            modelBuilder.Entity<Cliente>().Property(c => c.cEmail).HasColumnName("c_email");
            modelBuilder.Entity<Cliente>().Property(c => c.cDocumento).HasColumnName("c_documento");
            modelBuilder.Entity<Cliente>().Property(c => c.cFotoPerfilUrl).HasColumnName("c_foto_perfil_url");
            modelBuilder.Entity<Cliente>().Property(c => c.cCanalOrigen).HasColumnName("c_canal_origen").HasMaxLength(32);
            modelBuilder.Entity<Cliente>().Property(c => c.dFechaRegistro).HasColumnName("d_fecha_registro");
            modelBuilder.Entity<Cliente>().Property(c => c.cEstado).HasColumnName("c_estado");
            modelBuilder.Entity<Cliente>().HasIndex(c => c.dFechaRegistro);
            modelBuilder.Entity<Cliente>().HasIndex(c => new { c.cCanalOrigen, c.dFechaRegistro });

            modelBuilder.Entity<Conversacion>().ToTable("crm_conversacion");
            modelBuilder.Entity<Conversacion>().HasKey(c => c.nConversacion);
            modelBuilder.Entity<Conversacion>().Property(c => c.nConversacion).HasColumnName("n_conversacion");
            modelBuilder.Entity<Conversacion>().Property(c => c.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<Conversacion>().Property(c => c.nUsuarioAsignado).HasColumnName("n_usuario_asignado");
            modelBuilder.Entity<Conversacion>().Property(c => c.cEstado).HasColumnName("c_estado").HasMaxLength(32);
            modelBuilder.Entity<Conversacion>().Property(c => c.cCanal).HasColumnName("c_canal").HasMaxLength(32);
            modelBuilder.Entity<Conversacion>().Property(c => c.cExternalThreadId).HasColumnName("c_external_thread_id");
            modelBuilder.Entity<Conversacion>().Property(c => c.cPhoneNumberId).HasColumnName("c_phone_number_id").HasMaxLength(100);
            modelBuilder.Entity<Conversacion>().Property(c => c.cBotEstado).HasColumnName("c_bot_estado");
            modelBuilder.Entity<Conversacion>().Property(c => c.dFechaInicio).HasColumnName("d_fecha_inicio");
            modelBuilder.Entity<Conversacion>().Property(c => c.dUltimoMensaje).HasColumnName("d_ultimo_mensaje");
            modelBuilder.Entity<Conversacion>().Property(c => c.dUltimoMensajeCliente).HasColumnName("d_ultimo_mensaje_cliente");
            modelBuilder.Entity<Conversacion>().Property(c => c.dBotPausadoDesde).HasColumnName("d_bot_pausado_desde");
            modelBuilder.Entity<Conversacion>().Property(c => c.nBotPausadoPor).HasColumnName("n_bot_pausado_por");
            modelBuilder.Entity<Conversacion>()
                .HasIndex(c => new { c.nUsuarioAsignado, c.cEstado, c.dUltimoMensaje });
            modelBuilder.Entity<Conversacion>()
                .HasIndex(c => new { c.cCanal, c.cEstado, c.dUltimoMensaje });
            modelBuilder.Entity<Conversacion>().HasIndex(c => c.dFechaInicio);

            modelBuilder.Entity<Conversacion>()
                .HasOne(c => c.Cliente)
                .WithMany(cl => cl.Conversaciones)
                .HasForeignKey(c => c.nCliente);

            modelBuilder.Entity<Conversacion>()
                .HasOne(c => c.UsuarioAsignado)
                .WithMany(u => u.ConversacionesAsignadas)
                .HasForeignKey(c => c.nUsuarioAsignado);

            modelBuilder.Entity<Mensaje>().ToTable("crm_mensaje");
            modelBuilder.Entity<Mensaje>().HasKey(m => m.nMensaje);
            modelBuilder.Entity<Mensaje>().Property(m => m.nMensaje).HasColumnName("n_mensaje");
            modelBuilder.Entity<Mensaje>().Property(m => m.nConversacion).HasColumnName("n_conversacion");
            modelBuilder.Entity<Mensaje>().Property(m => m.cWhatsappId).HasColumnName("c_whatsapp_id");
            modelBuilder.Entity<Mensaje>().Property(m => m.cCanal).HasColumnName("c_canal").HasMaxLength(32);
            modelBuilder.Entity<Mensaje>().Property(m => m.cExternalId).HasColumnName("c_external_id").HasMaxLength(450);
            modelBuilder.Entity<Mensaje>().Property(m => m.cReplyToExternalId).HasColumnName("c_reply_to_external_id").HasMaxLength(450);
            modelBuilder.Entity<Mensaje>().Property(m => m.cClientRequestId).HasColumnName("c_client_request_id").HasMaxLength(80);
            modelBuilder.Entity<Mensaje>().HasIndex(m => new { m.nConversacion, m.cClientRequestId }).IsUnique();
            modelBuilder.Entity<Mensaje>().Property(m => m.cDireccion).HasColumnName("c_direccion");
            modelBuilder.Entity<Mensaje>().Property(m => m.cTipo).HasColumnName("c_tipo").HasMaxLength(50);
            modelBuilder.Entity<Mensaje>().Property(m => m.cMensaje).HasColumnName("c_mensaje");
            modelBuilder.Entity<Mensaje>().Property(m => m.cEstado).HasColumnName("c_estado");
            modelBuilder.Entity<Mensaje>().Property(m => m.dFecha).HasColumnName("d_fecha");

            modelBuilder.Entity<Mensaje>()
                .HasOne(m => m.Conversacion)
                .WithMany(c => c.Mensajes)
                .HasForeignKey(m => m.nConversacion);

            modelBuilder.Entity<Mensaje>()
                .HasIndex(m => new { m.cCanal, m.cDireccion, m.cExternalId })
                .IsUnique();
            modelBuilder.Entity<Mensaje>()
                .HasIndex(m => new { m.cTipo, m.cCanal, m.dFecha });
            modelBuilder.Entity<Mensaje>()
                .HasIndex(m => new { m.nConversacion, m.dFecha });
            modelBuilder.Entity<Mensaje>()
                .HasIndex(m => new { m.nConversacion, m.cTipo, m.cReplyToExternalId, m.dFecha });
            modelBuilder.Entity<Mensaje>().HasIndex(m => m.dFecha);

            modelBuilder.Entity<CrmUsuario>().ToTable("crm_usuario");
            modelBuilder.Entity<CrmUsuario>().HasKey(u => u.nUsuario);
            modelBuilder.Entity<CrmUsuario>().Property(u => u.nUsuario).HasColumnName("n_usuario");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.cUsuario).HasColumnName("c_usuario");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.cEstado).HasColumnName("c_estado");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.cPasswordHash).HasColumnName("c_password_hash");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.cRol).HasColumnName("c_rol");
            modelBuilder.Entity<CrmUsuario>().Property(u => u.nRol).HasColumnName("n_rol");

            modelBuilder.Entity<CrmRol>().ToTable("crm_rol");
            modelBuilder.Entity<CrmRol>().HasKey(item => item.nRol);
            modelBuilder.Entity<CrmRol>().Property(item => item.nRol).HasColumnName("n_rol");
            modelBuilder.Entity<CrmRol>().Property(item => item.cNombre).HasColumnName("c_nombre").HasMaxLength(80);
            modelBuilder.Entity<CrmRol>().Property(item => item.cDescripcion).HasColumnName("c_descripcion").HasMaxLength(250);
            modelBuilder.Entity<CrmRol>().Property(item => item.cRolBase).HasColumnName("c_rol_base").HasMaxLength(30);
            modelBuilder.Entity<CrmRol>().Property(item => item.cEstado).HasColumnName("c_estado");
            modelBuilder.Entity<CrmRol>().Property(item => item.dFechaCreacion).HasColumnName("d_fecha_creacion");
            modelBuilder.Entity<CrmRol>().Property(item => item.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<CrmRol>().HasIndex(item => item.cNombre).IsUnique();
            modelBuilder.Entity<CrmRol>()
                .HasOne(item => item.CreadoPor)
                .WithMany()
                .HasForeignKey(item => item.nCreadoPor)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<CrmUsuario>()
                .HasOne(item => item.RolPersonalizado)
                .WithMany(item => item.Usuarios)
                .HasForeignKey(item => item.nRol)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<CrmRolPermiso>().ToTable("crm_rol_permiso");
            modelBuilder.Entity<CrmRolPermiso>().HasKey(item => new { item.nRol, item.cPermiso });
            modelBuilder.Entity<CrmRolPermiso>().Property(item => item.nRol).HasColumnName("n_rol");
            modelBuilder.Entity<CrmRolPermiso>().Property(item => item.cPermiso).HasColumnName("c_permiso").HasMaxLength(100);
            modelBuilder.Entity<CrmRolPermiso>()
                .HasOne(item => item.Rol)
                .WithMany(item => item.Permisos)
                .HasForeignKey(item => item.nRol)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UsuarioPermiso>().ToTable("crm_usuario_permiso");
            modelBuilder.Entity<UsuarioPermiso>().HasKey(item => new { item.nUsuario, item.cPermiso });
            modelBuilder.Entity<UsuarioPermiso>().Property(item => item.nUsuario).HasColumnName("n_usuario");
            modelBuilder.Entity<UsuarioPermiso>().Property(item => item.cPermiso).HasColumnName("c_permiso").HasMaxLength(100);
            modelBuilder.Entity<UsuarioPermiso>().Property(item => item.dFechaAsignacion).HasColumnName("d_fecha_asignacion");
            modelBuilder.Entity<UsuarioPermiso>().Property(item => item.nOtorgadoPor).HasColumnName("n_otorgado_por");
            modelBuilder.Entity<UsuarioPermiso>()
                .HasOne(item => item.Usuario)
                .WithMany()
                .HasForeignKey(item => item.nUsuario)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UsuarioPermiso>()
                .HasOne(item => item.OtorgadoPor)
                .WithMany()
                .HasForeignKey(item => item.nOtorgadoPor)
                .OnDelete(DeleteBehavior.SetNull);

            // =====================================================
            // ETIQUETAS (segmentación de clientes)
            // =====================================================

            modelBuilder.Entity<Etiqueta>().ToTable("crm_etiqueta");
            modelBuilder.Entity<Etiqueta>().HasKey(e => e.nEtiqueta);
            modelBuilder.Entity<Etiqueta>().Property(e => e.nEtiqueta).HasColumnName("n_etiqueta");
            modelBuilder.Entity<Etiqueta>().Property(e => e.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<Etiqueta>().Property(e => e.cColor).HasColumnName("c_color");
            modelBuilder.Entity<Etiqueta>().HasIndex(e => e.cNombre).IsUnique();

            modelBuilder.Entity<ClienteEtiqueta>().ToTable("crm_cliente_etiqueta");
            modelBuilder.Entity<ClienteEtiqueta>().HasKey(ce => new { ce.nCliente, ce.nEtiqueta });
            modelBuilder.Entity<ClienteEtiqueta>().Property(ce => ce.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<ClienteEtiqueta>().Property(ce => ce.nEtiqueta).HasColumnName("n_etiqueta");
            modelBuilder.Entity<ClienteEtiqueta>().Property(ce => ce.dFechaAsignacion).HasColumnName("d_fecha_asignacion");
            modelBuilder.Entity<ClienteEtiqueta>()
                .HasOne(ce => ce.Cliente)
                .WithMany()
                .HasForeignKey(ce => ce.nCliente);
            modelBuilder.Entity<ClienteEtiqueta>()
                .HasOne(ce => ce.Etiqueta)
                .WithMany(e => e.Clientes)
                .HasForeignKey(ce => ce.nEtiqueta);

            // =====================================================
            // OPORTUNIDADES (embudo de ventas real)
            // =====================================================

            modelBuilder.Entity<Oportunidad>().ToTable("crm_oportunidad");
            modelBuilder.Entity<Oportunidad>().HasKey(o => o.nOportunidad);
            modelBuilder.Entity<Oportunidad>().Property(o => o.nOportunidad).HasColumnName("n_oportunidad");
            modelBuilder.Entity<Oportunidad>().Property(o => o.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<Oportunidad>().Property(o => o.nConversacion).HasColumnName("n_conversacion");
            modelBuilder.Entity<Oportunidad>().Property(o => o.nUsuarioAsignado).HasColumnName("n_usuario_asignado");
            modelBuilder.Entity<Oportunidad>().Property(o => o.cTitulo).HasColumnName("c_titulo");
            modelBuilder.Entity<Oportunidad>().Property(o => o.nMonto).HasColumnName("n_monto").HasColumnType("decimal(12,2)");
            modelBuilder.Entity<Oportunidad>().Property(o => o.cMoneda).HasColumnName("c_moneda");
            modelBuilder.Entity<Oportunidad>().Property(o => o.cEtapa).HasColumnName("c_etapa").HasMaxLength(32);
            modelBuilder.Entity<Oportunidad>().Property(o => o.nProbabilidad).HasColumnName("n_probabilidad");
            modelBuilder.Entity<Oportunidad>().Property(o => o.dFechaCierreEstimada).HasColumnName("d_fecha_cierre_estimada");
            modelBuilder.Entity<Oportunidad>().Property(o => o.dFechaCierreReal).HasColumnName("d_fecha_cierre_real");
            modelBuilder.Entity<Oportunidad>().Property(o => o.cMotivoPerdida).HasColumnName("c_motivo_perdida");
            modelBuilder.Entity<Oportunidad>().Property(o => o.dFechaCreacion).HasColumnName("d_fecha_creacion");
            modelBuilder.Entity<Oportunidad>().Property(o => o.dFechaActualizacion).HasColumnName("d_fecha_actualizacion");
            modelBuilder.Entity<Oportunidad>().Property(o => o.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<Oportunidad>()
                .HasIndex(o => new { o.nUsuarioAsignado, o.cEtapa, o.dFechaCierreEstimada });
            modelBuilder.Entity<Oportunidad>().HasIndex(o => o.dFechaCreacion);

            modelBuilder.Entity<Oportunidad>()
                .HasOne(o => o.Cliente)
                .WithMany()
                .HasForeignKey(o => o.nCliente)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Oportunidad>()
                .HasOne(o => o.Conversacion)
                .WithMany()
                .HasForeignKey(o => o.nConversacion)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Oportunidad>()
                .HasOne(o => o.UsuarioAsignado)
                .WithMany()
                .HasForeignKey(o => o.nUsuarioAsignado)
                .OnDelete(DeleteBehavior.SetNull);

            // =====================================================
            // TAREAS (recordatorios / seguimientos)
            // =====================================================

            modelBuilder.Entity<Tarea>().ToTable("crm_tarea");
            modelBuilder.Entity<Tarea>().HasKey(t => t.nTarea);
            modelBuilder.Entity<Tarea>().Property(t => t.nTarea).HasColumnName("n_tarea");
            modelBuilder.Entity<Tarea>().Property(t => t.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<Tarea>().Property(t => t.nConversacion).HasColumnName("n_conversacion");
            modelBuilder.Entity<Tarea>().Property(t => t.nOportunidad).HasColumnName("n_oportunidad");
            modelBuilder.Entity<Tarea>().Property(t => t.cTitulo).HasColumnName("c_titulo");
            modelBuilder.Entity<Tarea>().Property(t => t.cDescripcion).HasColumnName("c_descripcion");
            modelBuilder.Entity<Tarea>().Property(t => t.dFechaVencimiento).HasColumnName("d_fecha_vencimiento");
            modelBuilder.Entity<Tarea>().Property(t => t.cEstado).HasColumnName("c_estado").HasMaxLength(32);
            modelBuilder.Entity<Tarea>().Property(t => t.nAsignadoA).HasColumnName("n_asignado_a");
            modelBuilder.Entity<Tarea>().Property(t => t.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<Tarea>().Property(t => t.dFechaCreacion).HasColumnName("d_fecha_creacion");
            modelBuilder.Entity<Tarea>().Property(t => t.dFechaCompletada).HasColumnName("d_fecha_completada");
            modelBuilder.Entity<Tarea>()
                .HasIndex(t => new { t.nAsignadoA, t.cEstado, t.dFechaVencimiento });
            modelBuilder.Entity<Tarea>().HasIndex(t => t.dFechaCreacion);

            modelBuilder.Entity<Tarea>()
                .HasOne(t => t.Cliente)
                .WithMany()
                .HasForeignKey(t => t.nCliente)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Tarea>()
                .HasOne(t => t.Conversacion)
                .WithMany()
                .HasForeignKey(t => t.nConversacion)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Tarea>()
                .HasOne(t => t.Oportunidad)
                .WithMany()
                .HasForeignKey(t => t.nOportunidad)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Tarea>()
                .HasOne(t => t.AsignadoA)
                .WithMany()
                .HasForeignKey(t => t.nAsignadoA)
                .OnDelete(DeleteBehavior.SetNull);

            // =====================================================
            // NOTAS INTERNAS
            // =====================================================

            modelBuilder.Entity<NotaInterna>().ToTable("crm_nota_interna");
            modelBuilder.Entity<NotaInterna>().HasKey(n => n.nNota);
            modelBuilder.Entity<NotaInterna>().Property(n => n.nNota).HasColumnName("n_nota");
            modelBuilder.Entity<NotaInterna>().Property(n => n.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<NotaInterna>().Property(n => n.nConversacion).HasColumnName("n_conversacion");
            modelBuilder.Entity<NotaInterna>().Property(n => n.cTexto).HasColumnName("c_texto");
            modelBuilder.Entity<NotaInterna>().Property(n => n.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<NotaInterna>().Property(n => n.dFecha).HasColumnName("d_fecha");

            modelBuilder.Entity<NotaInterna>()
                .HasOne(n => n.Cliente)
                .WithMany()
                .HasForeignKey(n => n.nCliente)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<NotaInterna>()
                .HasOne(n => n.Conversacion)
                .WithMany()
                .HasForeignKey(n => n.nConversacion)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<NotaInterna>()
                .HasOne(n => n.CreadoPor)
                .WithMany()
                .HasForeignKey(n => n.nCreadoPor)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // AUDITORÍA (activity log)
            // =====================================================

            modelBuilder.Entity<ActividadLog>().ToTable("crm_actividad_log");
            modelBuilder.Entity<ActividadLog>().HasKey(a => a.nActividad);
            modelBuilder.Entity<ActividadLog>().Property(a => a.nActividad).HasColumnName("n_actividad");
            modelBuilder.Entity<ActividadLog>().Property(a => a.cEntidad).HasColumnName("c_entidad");
            modelBuilder.Entity<ActividadLog>().Property(a => a.nEntidadId).HasColumnName("n_entidad_id");
            modelBuilder.Entity<ActividadLog>().Property(a => a.cAccion).HasColumnName("c_accion");
            modelBuilder.Entity<ActividadLog>().Property(a => a.cValorAnterior).HasColumnName("c_valor_anterior");
            modelBuilder.Entity<ActividadLog>().Property(a => a.cValorNuevo).HasColumnName("c_valor_nuevo");
            modelBuilder.Entity<ActividadLog>().Property(a => a.nUsuario).HasColumnName("n_usuario");
            modelBuilder.Entity<ActividadLog>().Property(a => a.dFecha).HasColumnName("d_fecha");
            modelBuilder.Entity<ActividadLog>().HasIndex(a => new { a.cEntidad, a.nEntidadId });
            modelBuilder.Entity<ActividadLog>().HasIndex(a => a.dFecha);
            modelBuilder.Entity<ActividadLog>().HasIndex(a => new { a.nUsuario, a.dFecha });

            modelBuilder.Entity<ActividadLog>()
                .HasOne(a => a.Usuario)
                .WithMany()
                .HasForeignKey(a => a.nUsuario)
                .OnDelete(DeleteBehavior.SetNull);

            // =====================================================
            // CAMPAÑAS (marketing / prospección)
            // =====================================================

            modelBuilder.Entity<Campana>().ToTable("crm_campana");
            modelBuilder.Entity<Campana>().HasKey(c => c.nCampana);
            modelBuilder.Entity<Campana>().Property(c => c.nCampana).HasColumnName("n_campana");
            modelBuilder.Entity<Campana>().Property(c => c.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<Campana>().Property(c => c.cDescripcion).HasColumnName("c_descripcion");
            modelBuilder.Entity<Campana>().Property(c => c.cTipo).HasColumnName("c_tipo");
            modelBuilder.Entity<Campana>().Property(c => c.cEstado).HasColumnName("c_estado");
            modelBuilder.Entity<Campana>().Property(c => c.dFechaInicio).HasColumnName("d_fecha_inicio");
            modelBuilder.Entity<Campana>().Property(c => c.dFechaFin).HasColumnName("d_fecha_fin");
            modelBuilder.Entity<Campana>().Property(c => c.nAsignadoA).HasColumnName("n_asignado_a");
            modelBuilder.Entity<Campana>().Property(c => c.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<Campana>().Property(c => c.dFechaCreacion).HasColumnName("d_fecha_creacion");

            modelBuilder.Entity<Campana>()
                .HasOne(c => c.AsignadoA)
                .WithMany()
                .HasForeignKey(c => c.nAsignadoA)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Campana>()
                .HasOne(c => c.CreadoPor)
                .WithMany()
                .HasForeignKey(c => c.nCreadoPor)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CampanaCliente>().ToTable("crm_campana_cliente");
            modelBuilder.Entity<CampanaCliente>().HasKey(cc => new { cc.nCampana, cc.nCliente });
            modelBuilder.Entity<CampanaCliente>().Property(cc => cc.nCampana).HasColumnName("n_campana");
            modelBuilder.Entity<CampanaCliente>().Property(cc => cc.nCliente).HasColumnName("n_cliente");
            modelBuilder.Entity<CampanaCliente>().Property(cc => cc.dFechaAsignacion).HasColumnName("d_fecha_asignacion");
            modelBuilder.Entity<CampanaCliente>().Property(cc => cc.cEstado).HasColumnName("c_estado");

            modelBuilder.Entity<CampanaCliente>()
                .HasOne(cc => cc.Campana)
                .WithMany()
                .HasForeignKey(cc => cc.nCampana)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CampanaCliente>()
                .HasOne(cc => cc.Cliente)
                .WithMany()
                .HasForeignKey(cc => cc.nCliente)
                .OnDelete(DeleteBehavior.Cascade);

            // =====================================================
            // REGLAS AUTOMÁTICAS (automatización comercial)
            // =====================================================

            modelBuilder.Entity<ReglaAutomatica>().ToTable("crm_regla_automatica");
            modelBuilder.Entity<ReglaAutomatica>().HasKey(r => r.nRegla);
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.nRegla).HasColumnName("n_regla");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cEntidad).HasColumnName("c_entidad");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cEvento).HasColumnName("c_evento");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cCondicion).HasColumnName("c_condicion");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cAccion).HasColumnName("c_accion");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.cValorAccion).HasColumnName("c_valor_accion");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.nAsignadoA).HasColumnName("n_asignado_a");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.nCreadoPor).HasColumnName("n_creado_por");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.bActiva).HasColumnName("b_activa");
            modelBuilder.Entity<ReglaAutomatica>().Property(r => r.dFechaCreacion).HasColumnName("d_fecha_creacion");

            modelBuilder.Entity<ReglaAutomatica>()
                .HasOne(r => r.AsignadoA)
                .WithMany()
                .HasForeignKey(r => r.nAsignadoA)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReglaAutomatica>()
                .HasOne(r => r.CreadoPor)
                .WithMany()
                .HasForeignKey(r => r.nCreadoPor)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // KPI DASHBOARD EJECUTIVO
            // =====================================================

            modelBuilder.Entity<KpiDashboard>().ToTable("crm_kpi_dashboard");
            modelBuilder.Entity<KpiDashboard>().HasKey(k => k.nKpi);
            modelBuilder.Entity<KpiDashboard>().Property(k => k.nKpi).HasColumnName("n_kpi");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.cTipo).HasColumnName("c_tipo");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.cValor).HasColumnName("c_valor");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.cMeta).HasColumnName("c_meta");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.cPeriodo).HasColumnName("c_periodo");
            modelBuilder.Entity<KpiDashboard>().Property(k => k.dFechaCreacion).HasColumnName("d_fecha_creacion");

            // =====================================================
            // HISTORIAL DE EXPORTACIONES
            // =====================================================

            modelBuilder.Entity<ReporteExportacion>().ToTable("crm_reporte_exportacion");
            modelBuilder.Entity<ReporteExportacion>().HasKey(r => r.nReporte);
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.nReporte).HasColumnName("n_reporte");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.cNombre).HasColumnName("c_nombre");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.cTipo).HasColumnName("c_tipo");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.cEntidad).HasColumnName("c_entidad");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.cRuta).HasColumnName("c_ruta");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.dFechaCreacion).HasColumnName("d_fecha_creacion");
            modelBuilder.Entity<ReporteExportacion>().Property(r => r.nCreadoPor).HasColumnName("n_creado_por");

            modelBuilder.Entity<ReporteExportacion>()
                .HasOne(r => r.CreadoPor)
                .WithMany()
                .HasForeignKey(r => r.nCreadoPor)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
