# Organización de estilos

Las hojas se cargan desde `wwwroot/index.html` en este orden:

1. `00-foundation.css`: variables, normalización, estructura y navegación general.
2. `10-inbox.css`: bandeja, chat, mensajes, adjuntos y ficha del contacto.
3. `20-crm-modules.css`: módulos operativos como contactos, tareas, ventas y reportes.
4. `30-theme-dashboard.css`: modo oscuro, correcciones compartidas y dashboard.
5. `40-marketing.css`: panel, métricas, publicaciones y comentarios de Marketing.
6. `50-responsive-overrides.css`: estructura responsive, navegación e interfaz móvil.
7. `60-hpd-reference-theme.css`: sistema visual canónico trasladado desde
   `sacar el diseño/frontend`.

El frontend de referencia es la única fuente de verdad para tipografía, color,
espaciado, estados, tablas, tarjetas y formularios. Las hojas de cada módulo deben
conservar únicamente su estructura y comportamiento particular; no deben crear un
lenguaje visual alternativo.

La portada del dashboard reproduce la composición de
`sacar el diseño/frontend/src/pages/index.vue`: tarjeta de bienvenida y una
tarjeta de resumen con cuatro indicadores, avatares tonales y separadores.
Los indicadores muestran datos del CRM y conservan sus accesos a bandeja,
tareas y ventas. Los filtros y paneles operativos aparecen debajo del resumen.
Los tamaños, radios y colores se toman de los estilos de Vuetify y del tema
HPD de esa carpeta.

`login.css` pertenece exclusivamente a `wwwroot/login.html` y se carga desde esa
página; no forma parte de la cascada de la aplicación principal.

## Reglas de consistencia

- Usa las variables y componentes de `60-hpd-reference-theme.css`; evita crear
  valores visuales nuevos dentro de un módulo.
- `50-responsive-overrides.css` se reserva para disposición responsive, no para
  cambiar colores, pesos tipográficos ni apariencias de componentes.
- Breakpoints compartidos: `1100px` para tableta, `720px` para móvil y `520px`
  para pantallas compactas.
- No declares dos valores distintos de una misma propiedad para el mismo selector
  dentro del mismo contexto. Si existe una variante, usa una clase o un breakpoint.
- Los estados interactivos deben utilizar `:focus-visible` y los colores semánticos
  `--primary`, `--success`, `--warning` y `--error`.
- Los iconos de la aplicación principal y del acceso se renderizan desde
  `js/remix-icons.js`, generado a partir de `@iconify-json/ri@1.2.6`, la misma
  familia usada por `sacar el diseño/frontend`. No se debe volver a cargar
  Lucide ni mezclar otra familia de iconos.
- Los módulos sólo definen estructura y variantes semánticas. La apariencia de
  tarjetas, tablas, botones, formularios, estados y navegación pertenece a
  `60-hpd-reference-theme.css`.

## Revisión de la auditoría de listas (2026-10-01)

Se retiraron los selectores sin referencias en el HTML y los módulos actuales,
así como declaraciones sobrescritas por el mismo selector dentro del mismo
contexto. Las variantes de tema, los breakpoints y los estados interactivos
siguen siendo parte de la cascada: una diferencia entre hojas no demuestra,
por sí sola, un error.

Los comentarios públicos se gestionan desde Marketing. Se retiraron la antigua
bandeja sin ruta y `renderDashboardLegacy`; el dashboard operativo sigue usando
sus funciones compartidas.

`tag-pill` tiene estilos en el tema canónico. `stage-select` y
`sales-client-search` comparten los estilos generales de los controles y definen
sólo su tamaño en la hoja del módulo. La leyenda de seguidores consume los
estilos de Marketing. `social-brand-slot` es un marcador que se reemplaza por
el logo de la red, no un componente visual independiente. Las clases
decorativas redundantes `marketing-chart-panel` y `administrator` se retiraron.
