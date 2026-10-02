-- CRM MySQL: instalacion en una base NUEVA y VACIA, MySQL 8.
-- Generado desde las seis migraciones hasta AddMessageClientRequestId.
-- No contiene clientes, mensajes, usuarios ni credenciales locales.
-- Ejecutar UNA sola vez en la base seleccionada; no usar para actualizaciones.
-- MySQL confirma DDL implicitamente: ante un fallo, no repetir sobre tablas parciales.
SET NAMES utf8mb4;

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) NOT NULL,
    `ProductVersion` varchar(32) NOT NULL,
    PRIMARY KEY (`MigrationId`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

START TRANSACTION;
CREATE TABLE `crm_cliente` (
    `n_cliente` bigint NOT NULL AUTO_INCREMENT,
    `c_nombre` longtext NOT NULL,
    `c_telefono` longtext NOT NULL,
    `c_email` longtext NULL,
    `c_documento` longtext NULL,
    `c_foto_perfil_url` longtext NULL,
    `c_canal_origen` longtext NOT NULL,
    `d_fecha_registro` datetime(6) NOT NULL,
    `c_estado` varchar(1) NOT NULL,
    PRIMARY KEY (`n_cliente`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_etiqueta` (
    `n_etiqueta` int NOT NULL AUTO_INCREMENT,
    `c_nombre` varchar(255) NOT NULL,
    `c_color` longtext NOT NULL,
    PRIMARY KEY (`n_etiqueta`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_kpi_dashboard` (
    `n_kpi` bigint NOT NULL AUTO_INCREMENT,
    `c_nombre` longtext NOT NULL,
    `c_tipo` longtext NOT NULL,
    `c_valor` longtext NOT NULL,
    `c_meta` longtext NULL,
    `c_periodo` longtext NOT NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    PRIMARY KEY (`n_kpi`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_login_attempt` (
    `c_key` varchar(64) NOT NULL,
    `n_failures` int NOT NULL,
    `d_blocked_until_utc` datetime(6) NULL,
    `d_updated_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`c_key`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_usuario` (
    `n_usuario` int NOT NULL AUTO_INCREMENT,
    `c_usuario` longtext NOT NULL,
    `c_nombre` longtext NOT NULL,
    `c_estado` varchar(1) NOT NULL,
    `c_password_hash` longtext NULL,
    `c_rol` longtext NOT NULL,
    PRIMARY KEY (`n_usuario`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_cliente_etiqueta` (
    `n_cliente` bigint NOT NULL,
    `n_etiqueta` int NOT NULL,
    `d_fecha_asignacion` datetime(6) NOT NULL,
    PRIMARY KEY (`n_cliente`, `n_etiqueta`),
    CONSTRAINT `FK_crm_cliente_etiqueta_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE CASCADE,
    CONSTRAINT `FK_crm_cliente_etiqueta_crm_etiqueta_n_etiqueta` FOREIGN KEY (`n_etiqueta`) REFERENCES `crm_etiqueta` (`n_etiqueta`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_actividad_log` (
    `n_actividad` bigint NOT NULL AUTO_INCREMENT,
    `c_entidad` varchar(255) NOT NULL,
    `n_entidad_id` bigint NOT NULL,
    `c_accion` longtext NOT NULL,
    `c_valor_anterior` longtext NULL,
    `c_valor_nuevo` longtext NULL,
    `n_usuario` int NULL,
    `d_fecha` datetime(6) NOT NULL,
    PRIMARY KEY (`n_actividad`),
    CONSTRAINT `FK_crm_actividad_log_crm_usuario_n_usuario` FOREIGN KEY (`n_usuario`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_campana` (
    `n_campana` bigint NOT NULL AUTO_INCREMENT,
    `c_nombre` longtext NOT NULL,
    `c_descripcion` longtext NULL,
    `c_tipo` longtext NOT NULL,
    `c_estado` longtext NOT NULL,
    `d_fecha_inicio` datetime(6) NOT NULL,
    `d_fecha_fin` datetime(6) NULL,
    `n_asignado_a` int NULL,
    `n_creado_por` int NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    PRIMARY KEY (`n_campana`),
    CONSTRAINT `FK_crm_campana_crm_usuario_n_asignado_a` FOREIGN KEY (`n_asignado_a`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE RESTRICT,
    CONSTRAINT `FK_crm_campana_crm_usuario_n_creado_por` FOREIGN KEY (`n_creado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_conversacion` (
    `n_conversacion` bigint NOT NULL AUTO_INCREMENT,
    `n_cliente` bigint NOT NULL,
    `n_usuario_asignado` int NULL,
    `c_estado` longtext NOT NULL,
    `c_canal` longtext NOT NULL,
    `c_external_thread_id` longtext NULL,
    `c_phone_number_id` varchar(100) NULL,
    `c_bot_estado` longtext NOT NULL,
    `d_fecha_inicio` datetime(6) NOT NULL,
    `d_ultimo_mensaje` datetime(6) NULL,
    `d_ultimo_mensaje_cliente` datetime(6) NULL,
    `d_bot_pausado_desde` datetime(6) NULL,
    `n_bot_pausado_por` int NULL,
    PRIMARY KEY (`n_conversacion`),
    CONSTRAINT `FK_crm_conversacion_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE CASCADE,
    CONSTRAINT `FK_crm_conversacion_crm_usuario_n_usuario_asignado` FOREIGN KEY (`n_usuario_asignado`) REFERENCES `crm_usuario` (`n_usuario`)
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_regla_automatica` (
    `n_regla` bigint NOT NULL AUTO_INCREMENT,
    `c_nombre` longtext NOT NULL,
    `c_entidad` longtext NOT NULL,
    `c_evento` longtext NOT NULL,
    `c_condicion` longtext NOT NULL,
    `c_accion` longtext NOT NULL,
    `c_valor_accion` longtext NULL,
    `n_asignado_a` int NULL,
    `n_creado_por` int NULL,
    `b_activa` tinyint(1) NOT NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    PRIMARY KEY (`n_regla`),
    CONSTRAINT `FK_crm_regla_automatica_crm_usuario_n_asignado_a` FOREIGN KEY (`n_asignado_a`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE RESTRICT,
    CONSTRAINT `FK_crm_regla_automatica_crm_usuario_n_creado_por` FOREIGN KEY (`n_creado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_reporte_exportacion` (
    `n_reporte` bigint NOT NULL AUTO_INCREMENT,
    `c_nombre` longtext NOT NULL,
    `c_tipo` longtext NOT NULL,
    `c_entidad` longtext NOT NULL,
    `c_ruta` longtext NOT NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    `n_creado_por` int NULL,
    PRIMARY KEY (`n_reporte`),
    CONSTRAINT `FK_crm_reporte_exportacion_crm_usuario_n_creado_por` FOREIGN KEY (`n_creado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_campana_cliente` (
    `n_campana` bigint NOT NULL,
    `n_cliente` bigint NOT NULL,
    `d_fecha_asignacion` datetime(6) NOT NULL,
    `c_estado` longtext NOT NULL,
    PRIMARY KEY (`n_campana`, `n_cliente`),
    CONSTRAINT `FK_crm_campana_cliente_crm_campana_n_campana` FOREIGN KEY (`n_campana`) REFERENCES `crm_campana` (`n_campana`) ON DELETE CASCADE,
    CONSTRAINT `FK_crm_campana_cliente_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_mensaje` (
    `n_mensaje` bigint NOT NULL AUTO_INCREMENT,
    `n_conversacion` bigint NOT NULL,
    `c_whatsapp_id` longtext NULL,
    `c_canal` varchar(32) NOT NULL,
    `c_external_id` varchar(450) NULL,
    `c_direccion` varchar(1) NOT NULL,
    `c_tipo` longtext NULL,
    `c_mensaje` longtext NOT NULL,
    `c_estado` longtext NULL,
    `d_fecha` datetime(6) NOT NULL,
    PRIMARY KEY (`n_mensaje`),
    CONSTRAINT `FK_crm_mensaje_crm_conversacion_n_conversacion` FOREIGN KEY (`n_conversacion`) REFERENCES `crm_conversacion` (`n_conversacion`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_nota_interna` (
    `n_nota` bigint NOT NULL AUTO_INCREMENT,
    `n_cliente` bigint NOT NULL,
    `n_conversacion` bigint NULL,
    `c_texto` longtext NOT NULL,
    `n_creado_por` int NOT NULL,
    `d_fecha` datetime(6) NOT NULL,
    PRIMARY KEY (`n_nota`),
    CONSTRAINT `FK_crm_nota_interna_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE RESTRICT,
    CONSTRAINT `FK_crm_nota_interna_crm_conversacion_n_conversacion` FOREIGN KEY (`n_conversacion`) REFERENCES `crm_conversacion` (`n_conversacion`) ON DELETE SET NULL,
    CONSTRAINT `FK_crm_nota_interna_crm_usuario_n_creado_por` FOREIGN KEY (`n_creado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_oportunidad` (
    `n_oportunidad` bigint NOT NULL AUTO_INCREMENT,
    `n_cliente` bigint NOT NULL,
    `n_conversacion` bigint NULL,
    `n_usuario_asignado` int NULL,
    `c_titulo` longtext NOT NULL,
    `n_monto` decimal(12,2) NOT NULL,
    `c_moneda` longtext NOT NULL,
    `c_etapa` longtext NOT NULL,
    `n_probabilidad` int NOT NULL,
    `d_fecha_cierre_estimada` datetime(6) NULL,
    `d_fecha_cierre_real` datetime(6) NULL,
    `c_motivo_perdida` longtext NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    `d_fecha_actualizacion` datetime(6) NULL,
    `n_creado_por` int NULL,
    PRIMARY KEY (`n_oportunidad`),
    CONSTRAINT `FK_crm_oportunidad_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE RESTRICT,
    CONSTRAINT `FK_crm_oportunidad_crm_conversacion_n_conversacion` FOREIGN KEY (`n_conversacion`) REFERENCES `crm_conversacion` (`n_conversacion`) ON DELETE SET NULL,
    CONSTRAINT `FK_crm_oportunidad_crm_usuario_n_usuario_asignado` FOREIGN KEY (`n_usuario_asignado`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_tarea` (
    `n_tarea` bigint NOT NULL AUTO_INCREMENT,
    `n_cliente` bigint NULL,
    `n_conversacion` bigint NULL,
    `n_oportunidad` bigint NULL,
    `c_titulo` longtext NOT NULL,
    `c_descripcion` longtext NULL,
    `d_fecha_vencimiento` datetime(6) NOT NULL,
    `c_estado` longtext NOT NULL,
    `n_asignado_a` int NULL,
    `n_creado_por` int NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    `d_fecha_completada` datetime(6) NULL,
    PRIMARY KEY (`n_tarea`),
    CONSTRAINT `FK_crm_tarea_crm_cliente_n_cliente` FOREIGN KEY (`n_cliente`) REFERENCES `crm_cliente` (`n_cliente`) ON DELETE RESTRICT,
    CONSTRAINT `FK_crm_tarea_crm_conversacion_n_conversacion` FOREIGN KEY (`n_conversacion`) REFERENCES `crm_conversacion` (`n_conversacion`) ON DELETE SET NULL,
    CONSTRAINT `FK_crm_tarea_crm_oportunidad_n_oportunidad` FOREIGN KEY (`n_oportunidad`) REFERENCES `crm_oportunidad` (`n_oportunidad`) ON DELETE SET NULL,
    CONSTRAINT `FK_crm_tarea_crm_usuario_n_asignado_a` FOREIGN KEY (`n_asignado_a`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE INDEX `IX_crm_actividad_log_c_entidad_n_entidad_id` ON `crm_actividad_log` (`c_entidad`, `n_entidad_id`);

CREATE INDEX `IX_crm_actividad_log_n_usuario` ON `crm_actividad_log` (`n_usuario`);

CREATE INDEX `IX_crm_campana_n_asignado_a` ON `crm_campana` (`n_asignado_a`);

CREATE INDEX `IX_crm_campana_n_creado_por` ON `crm_campana` (`n_creado_por`);

CREATE INDEX `IX_crm_campana_cliente_n_cliente` ON `crm_campana_cliente` (`n_cliente`);

CREATE INDEX `IX_crm_cliente_etiqueta_n_etiqueta` ON `crm_cliente_etiqueta` (`n_etiqueta`);

CREATE INDEX `IX_crm_conversacion_n_cliente` ON `crm_conversacion` (`n_cliente`);

CREATE INDEX `IX_crm_conversacion_n_usuario_asignado` ON `crm_conversacion` (`n_usuario_asignado`);

CREATE UNIQUE INDEX `IX_crm_etiqueta_c_nombre` ON `crm_etiqueta` (`c_nombre`);

CREATE UNIQUE INDEX `IX_crm_mensaje_c_canal_c_direccion_c_external_id` ON `crm_mensaje` (`c_canal`, `c_direccion`, `c_external_id`);

CREATE INDEX `IX_crm_mensaje_n_conversacion` ON `crm_mensaje` (`n_conversacion`);

CREATE INDEX `IX_crm_nota_interna_n_cliente` ON `crm_nota_interna` (`n_cliente`);

CREATE INDEX `IX_crm_nota_interna_n_conversacion` ON `crm_nota_interna` (`n_conversacion`);

CREATE INDEX `IX_crm_nota_interna_n_creado_por` ON `crm_nota_interna` (`n_creado_por`);

CREATE INDEX `IX_crm_oportunidad_n_cliente` ON `crm_oportunidad` (`n_cliente`);

CREATE INDEX `IX_crm_oportunidad_n_conversacion` ON `crm_oportunidad` (`n_conversacion`);

CREATE INDEX `IX_crm_oportunidad_n_usuario_asignado` ON `crm_oportunidad` (`n_usuario_asignado`);

CREATE INDEX `IX_crm_regla_automatica_n_asignado_a` ON `crm_regla_automatica` (`n_asignado_a`);

CREATE INDEX `IX_crm_regla_automatica_n_creado_por` ON `crm_regla_automatica` (`n_creado_por`);

CREATE INDEX `IX_crm_reporte_exportacion_n_creado_por` ON `crm_reporte_exportacion` (`n_creado_por`);

CREATE INDEX `IX_crm_tarea_n_asignado_a` ON `crm_tarea` (`n_asignado_a`);

CREATE INDEX `IX_crm_tarea_n_cliente` ON `crm_tarea` (`n_cliente`);

CREATE INDEX `IX_crm_tarea_n_conversacion` ON `crm_tarea` (`n_conversacion`);

CREATE INDEX `IX_crm_tarea_n_oportunidad` ON `crm_tarea` (`n_oportunidad`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260929154259_InitialMySql', '10.0.12');

CREATE TABLE `crm_usuario_permiso` (
    `n_usuario` int NOT NULL,
    `c_permiso` varchar(100) NOT NULL,
    `d_fecha_asignacion` datetime(6) NOT NULL,
    `n_otorgado_por` int NULL,
    PRIMARY KEY (`n_usuario`, `c_permiso`),
    CONSTRAINT `FK_crm_usuario_permiso_crm_usuario_n_otorgado_por` FOREIGN KEY (`n_otorgado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL,
    CONSTRAINT `FK_crm_usuario_permiso_crm_usuario_n_usuario` FOREIGN KEY (`n_usuario`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE INDEX `IX_crm_usuario_permiso_n_otorgado_por` ON `crm_usuario_permiso` (`n_otorgado_por`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260929201516_AddUserPermissions', '10.0.12');

ALTER TABLE `crm_mensaje` ADD `c_reply_to_external_id` varchar(450) NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260930230000_AddMessageReplyContext', '10.0.12');

ALTER TABLE `crm_tarea` MODIFY `c_estado` varchar(32) NOT NULL;

ALTER TABLE `crm_oportunidad` MODIFY `c_etapa` varchar(32) NOT NULL;

ALTER TABLE `crm_mensaje` MODIFY `c_tipo` varchar(50) NULL;

ALTER TABLE `crm_conversacion` MODIFY `c_estado` varchar(32) NOT NULL;

ALTER TABLE `crm_conversacion` MODIFY `c_canal` varchar(32) NOT NULL;

ALTER TABLE `crm_cliente` MODIFY `c_canal_origen` varchar(32) NOT NULL;

CREATE INDEX `IX_crm_tarea_n_asignado_a_c_estado_d_fecha_vencimiento` ON `crm_tarea` (`n_asignado_a`, `c_estado`, `d_fecha_vencimiento`);

CREATE INDEX `IX_crm_tarea_d_fecha_creacion` ON `crm_tarea` (`d_fecha_creacion`);

CREATE INDEX `IX_crm_oportunidad_n_usuario_asignado_c_etapa_d_fecha_cierre_es~` ON `crm_oportunidad` (`n_usuario_asignado`, `c_etapa`, `d_fecha_cierre_estimada`);

CREATE INDEX `IX_crm_oportunidad_d_fecha_creacion` ON `crm_oportunidad` (`d_fecha_creacion`);

CREATE INDEX `IX_crm_mensaje_c_tipo_c_canal_d_fecha` ON `crm_mensaje` (`c_tipo`, `c_canal`, `d_fecha`);

CREATE INDEX `IX_crm_mensaje_n_conversacion_c_tipo_c_reply_to_external_id_d_f~` ON `crm_mensaje` (`n_conversacion`, `c_tipo`, `c_reply_to_external_id`, `d_fecha`);

CREATE INDEX `IX_crm_mensaje_n_conversacion_d_fecha` ON `crm_mensaje` (`n_conversacion`, `d_fecha`);

CREATE INDEX `IX_crm_mensaje_d_fecha` ON `crm_mensaje` (`d_fecha`);

CREATE INDEX `IX_crm_conversacion_c_canal_c_estado_d_ultimo_mensaje` ON `crm_conversacion` (`c_canal`, `c_estado`, `d_ultimo_mensaje`);

CREATE INDEX `IX_crm_conversacion_n_usuario_asignado_c_estado_d_ultimo_mensaje` ON `crm_conversacion` (`n_usuario_asignado`, `c_estado`, `d_ultimo_mensaje`);

CREATE INDEX `IX_crm_conversacion_d_fecha_inicio` ON `crm_conversacion` (`d_fecha_inicio`);

CREATE INDEX `IX_crm_cliente_c_canal_origen_d_fecha_registro` ON `crm_cliente` (`c_canal_origen`, `d_fecha_registro`);

CREATE INDEX `IX_crm_cliente_d_fecha_registro` ON `crm_cliente` (`d_fecha_registro`);

CREATE INDEX `IX_crm_actividad_log_d_fecha` ON `crm_actividad_log` (`d_fecha`);

CREATE INDEX `IX_crm_actividad_log_n_usuario_d_fecha` ON `crm_actividad_log` (`n_usuario`, `d_fecha`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261002142642_AddPerformanceIndexes', '10.0.12');

ALTER TABLE `crm_usuario` ADD `n_rol` int NULL;

CREATE TABLE `crm_rol` (
    `n_rol` int NOT NULL AUTO_INCREMENT,
    `c_nombre` varchar(80) NOT NULL,
    `c_descripcion` varchar(250) NOT NULL,
    `c_rol_base` varchar(30) NOT NULL,
    `c_estado` varchar(1) NOT NULL,
    `d_fecha_creacion` datetime(6) NOT NULL,
    `n_creado_por` int NULL,
    PRIMARY KEY (`n_rol`),
    CONSTRAINT `FK_crm_rol_crm_usuario_n_creado_por` FOREIGN KEY (`n_creado_por`) REFERENCES `crm_usuario` (`n_usuario`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE TABLE `crm_rol_permiso` (
    `n_rol` int NOT NULL,
    `c_permiso` varchar(100) NOT NULL,
    PRIMARY KEY (`n_rol`, `c_permiso`),
    CONSTRAINT `FK_crm_rol_permiso_crm_rol_n_rol` FOREIGN KEY (`n_rol`) REFERENCES `crm_rol` (`n_rol`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE INDEX `IX_crm_usuario_n_rol` ON `crm_usuario` (`n_rol`);

CREATE UNIQUE INDEX `IX_crm_rol_c_nombre` ON `crm_rol` (`c_nombre`);

CREATE INDEX `IX_crm_rol_n_creado_por` ON `crm_rol` (`n_creado_por`);

ALTER TABLE `crm_usuario` ADD CONSTRAINT `FK_crm_usuario_crm_rol_n_rol` FOREIGN KEY (`n_rol`) REFERENCES `crm_rol` (`n_rol`) ON DELETE SET NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261002181944_AddCustomRoles', '10.0.12');

ALTER TABLE `crm_mensaje` ADD `c_client_request_id` varchar(80) NULL;

CREATE UNIQUE INDEX `IX_crm_mensaje_n_conversacion_c_client_request_id` ON `crm_mensaje` (`n_conversacion`, `c_client_request_id`);

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20261002213909_AddMessageClientRequestId', '10.0.12');

COMMIT;

