$ErrorActionPreference = 'Stop'

$scriptDirectory = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { Join-Path (Get-Location) 'docs' } else { $PSScriptRoot }
$root = Split-Path -Parent $scriptDirectory
$templatePath = Join-Path $root 'informe_de_pruebas_carrito_compras (002).docx'
$outputPath = Join-Path $scriptDirectory 'INFORME_DE_PRUEBAS_CRM_HPD_CON_ENLACES.docx'

function Escape-Xml([string]$Text) {
    return [System.Security.SecurityElement]::Escape($Text)
}

function New-Run([string]$Text, [bool]$Bold = $false, [string]$Color = '', [int]$Size = 20) {
    $properties = '<w:rPr><w:rFonts w:ascii="Aptos" w:hAnsi="Aptos"/>'
    if ($Bold) { $properties += '<w:b/>' }
    if ($Color) { $properties += '<w:color w:val="' + $Color + '"/>' }
    if ($Size -gt 0) { $properties += '<w:sz w:val="' + $Size + '"/><w:szCs w:val="' + $Size + '"/>' }
    $properties += '</w:rPr>'
    $parts = $Text -split "`v|`r?`n"
    $runs = for ($index = 0; $index -lt $parts.Count; $index++) {
        if ($index -gt 0) { '<w:r><w:br/></w:r>' }
        '<w:r>' + $properties + '<w:t xml:space="preserve">' + (Escape-Xml $parts[$index]) + '</w:t></w:r>'
    }
    return ($runs -join '')
}

function New-Paragraph {
    param(
        [string]$Text,
        [string]$Style = '',
        [bool]$Bold = $false,
        [string]$Color = '',
        [int]$Size = 20,
        [string]$Align = '',
        [int]$After = 100,
        [bool]$KeepNext = $false
    )
    $pPr = '<w:pPr>'
    if ($Style) { $pPr += '<w:pStyle w:val="' + $Style + '"/>' }
    if ($Align) { $pPr += '<w:jc w:val="' + $Align + '"/>' }
    if ($KeepNext) { $pPr += '<w:keepNext/>' }
    $pPr += '<w:spacing w:after="' + $After + '"/></w:pPr>'
    return '<w:p>' + $pPr + (New-Run -Text $Text -Bold $Bold -Color $Color -Size $Size) + '</w:p>'
}

function New-PageBreak {
    return '<w:p><w:r><w:br w:type="page"/></w:r></w:p>'
}

function New-Cell {
    param(
        [string]$Text,
        [int]$Width,
        [bool]$Bold = $false,
        [string]$Fill = '',
        [string]$Color = '',
        [int]$Size = 18,
        [string]$Align = 'left',
        [int]$Height = 0
    )
    $tcPr = '<w:tcPr><w:tcW w:w="' + $Width + '" w:type="dxa"/><w:vAlign w:val="center"/>'
    if ($Fill) { $tcPr += '<w:shd w:val="clear" w:color="auto" w:fill="' + $Fill + '"/>' }
    $tcPr += '</w:tcPr>'
    $paragraph = New-Paragraph -Text $Text -Bold $Bold -Color $Color -Size $Size -Align $Align -After 40
    return '<w:tc>' + $tcPr + $paragraph + '</w:tc>'
}

function New-Table {
    param(
        [string[]]$Headers,
        [object[]]$Rows,
        [int[]]$Widths
    )
    $total = ($Widths | Measure-Object -Sum).Sum
    $xml = '<w:tbl><w:tblPr><w:tblW w:w="' + $total + '" w:type="dxa"/><w:tblLayout w:type="fixed"/>' +
           '<w:tblBorders><w:top w:val="single" w:sz="4" w:color="C9D7E5"/><w:left w:val="single" w:sz="4" w:color="C9D7E5"/>' +
           '<w:bottom w:val="single" w:sz="4" w:color="C9D7E5"/><w:right w:val="single" w:sz="4" w:color="C9D7E5"/>' +
           '<w:insideH w:val="single" w:sz="4" w:color="C9D7E5"/><w:insideV w:val="single" w:sz="4" w:color="C9D7E5"/></w:tblBorders></w:tblPr><w:tblGrid>'
    foreach ($width in $Widths) { $xml += '<w:gridCol w:w="' + $width + '"/>' }
    $xml += '</w:tblGrid><w:tr><w:trPr><w:cantSplit/></w:trPr>'
    for ($column = 0; $column -lt $Headers.Count; $column++) {
        $xml += New-Cell -Text $Headers[$column] -Width $Widths[$column] -Bold $true -Fill '0B49A3' -Color 'FFFFFF' -Size 18
    }
    $xml += '</w:tr>'
    for ($rowIndex = 0; $rowIndex -lt $Rows.Count; $rowIndex++) {
        $fill = if (($rowIndex % 2) -eq 1) { 'F5F7FA' } else { '' }
        $xml += '<w:tr><w:trPr><w:cantSplit/></w:trPr>'
        for ($column = 0; $column -lt $Headers.Count; $column++) {
            $xml += New-Cell -Text ([string]$Rows[$rowIndex][$column]) -Width $Widths[$column] -Fill $fill -Size 18
        }
        $xml += '</w:tr>'
    }
    return $xml + '</w:tbl><w:p><w:pPr><w:spacing w:after="100"/></w:pPr></w:p>'
}

function New-VideoBox([string]$Code, [string]$Title, [string]$Coverage) {
    $xml = '<w:tbl><w:tblPr><w:tblW w:w="9760" w:type="dxa"/><w:tblLayout w:type="fixed"/>' +
           '<w:tblBorders><w:top w:val="single" w:sz="6" w:color="9DB9D7"/><w:left w:val="single" w:sz="6" w:color="9DB9D7"/>' +
           '<w:bottom w:val="single" w:sz="6" w:color="9DB9D7"/><w:right w:val="single" w:sz="6" w:color="9DB9D7"/>' +
           '<w:insideH w:val="single" w:sz="4" w:color="C9D7E5"/></w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="9760"/></w:tblGrid>'
    $xml += '<w:tr><w:trPr><w:cantSplit/></w:trPr>' + (New-Cell -Text ($Code + ' · ' + $Title) -Width 9760 -Bold $true -Fill '0B49A3' -Color 'FFFFFF' -Size 22) + '</w:tr>'
    $xml += '<w:tr><w:trPr><w:trHeight w:val="1900" w:hRule="atLeast"/><w:cantSplit/></w:trPr>' +
            (New-Cell -Text ("▶  INSERTAR AQUÍ EL VIDEO DE " + $Title.ToUpper() + "`v`vReemplaza este recuadro por el archivo, un icono con vínculo o la URL del video.") -Width 9760 -Bold $true -Fill 'E8F2FC' -Color '596773' -Size 24 -Align 'center') + '</w:tr>'
    $xml += '<w:tr><w:trPr><w:cantSplit/></w:trPr>' + (New-Cell -Text ("Cobertura: " + $Coverage + "`vArchivo o enlace: ________________________________________________") -Width 9760 -Fill 'F5F7FA' -Size 18) + '</w:tr></w:tbl>'
    return $xml + '<w:p><w:pPr><w:spacing w:after="160"/></w:pPr></w:p>'
}

$adminRows = @(
    @('P01', "Acceso y cierre de sesión`vIngresar con credenciales válidas, rechazar las incorrectas y cerrar la sesión.", 'Aplicación: Autenticación | Resultado: acceso correcto del Administrador y cierre seguro. | Estado: Conforme | Evidencia: Video'),
    @('P02', "Dashboard administrativo`vMostrar indicadores globales de clientes, conversaciones, tareas, oportunidades, ventas y canales.", 'Aplicación: Dashboard | Resultado: indicadores y filtros con alcance general. | Estado: Conforme | Evidencia: Video'),
    @('P03', "Bandeja de comunicaciones`vConsultar WhatsApp, Facebook e Instagram; responder, adjuntar, asignar y cambiar estados.", 'Aplicación: Comunicaciones | Resultado: gestión y asignación de conversaciones disponible. | Estado: Conforme | Evidencia: Video'),
    @('P04', "Gestión de contactos`vBuscar, crear y editar contactos; revisar ficha, etiquetas, conversaciones, notas, tareas y oportunidades.", 'Aplicación: Contactos | Resultado: el Administrador visualiza y administra todos los contactos. | Estado: Conforme | Evidencia: Video'),
    @('P05', "Tareas, leads y ventas`vGestionar responsables, etapas, montos, probabilidades y fechas previstas.", 'Aplicación: Operación comercial | Resultado: registros relacionados correctamente con el cliente. | Estado: Conforme | Evidencia: Video'),
    @('P06', "Reportes y marketing`vAplicar filtros, revisar carga operativa, publicaciones e interacciones sociales y exportar datos.", 'Aplicación: Reportes / Marketing | Resultado: información consolidada y actualizada. | Estado: Conforme | Evidencia: Video'),
    @('P07', "Usuarios, permisos y conexiones`vCrear usuarios, asignar roles y revisar integraciones y almacenamiento.", 'Aplicación: Administración | Resultado: funciones sensibles limitadas al Administrador. | Estado: Conforme | Evidencia: Video'),
    @('P08', "Actividad y configuración`vRevisar trazabilidad, bot, respuestas rápidas y parámetros operativos.", 'Aplicación: Configuración | Resultado: trazabilidad y control por permisos. | Estado: Conforme | Evidencia: Video')
)

$advisorRows = @(
    @('P09', "Acceso con perfil Asesor`vMostrar solo los módulos y acciones autorizados para el rol.", 'Aplicación: Autenticación / menú | Resultado: interfaz adaptada al Asesor. | Estado: Conforme | Evidencia: Video'),
    @('P10', "Dashboard personal`vPresentar indicadores de su operación sin exponer información administrativa.", 'Aplicación: Dashboard | Resultado: panel limitado al usuario autenticado. | Estado: Conforme | Evidencia: Video'),
    @('P11', "Conversaciones asignadas y disponibles`vMostrar chats propios y casos nuevos; permitir tomar y responder una conversación.", 'Aplicación: Comunicaciones | Resultado: chats visibles y atención disponible. | Estado: Conforme | Evidencia: Video'),
    @('P12', "Contactos propios`vMostrar contactos vinculados a su atención y permitir guardar el contacto de un chat tomado.", 'Aplicación: Contactos / ficha | Resultado: cartera limitada al Asesor y vista global para Administrador. | Estado: Conforme | Evidencia: Video'),
    @('P13', "Tareas, leads y seguimiento`vGestionar tareas y oportunidades relacionadas con sus clientes.", 'Aplicación: Tareas / Leads / Ventas | Resultado: seguimiento comercial disponible. | Estado: Conforme | Evidencia: Video'),
    @('P14', "Restricciones de permisos`vImpedir acceso a usuarios, conexiones, secretos, datos ajenos y acciones administrativas.", 'Aplicación: Seguridad | Resultado: restricciones aplicadas en servidor e interfaz. | Estado: Conforme | Evidencia: Video'),
    @('P15', "Cierre de sesión`vEliminar la sesión y volver a la pantalla de acceso protegido.", 'Aplicación: Sesión | Resultado: cierre correcto y nueva autenticación requerida. | Estado: Conforme | Evidencia: Video')
)

$changes = @(
    @('C01', 'Inicio de sesión y permisos', 'Se ajustó la autenticación y la validación de acceso por rol.', 'Corregido'),
    @('C02', 'Alcance de contactos', 'El Administrador consulta todos los contactos y el Asesor solo los vinculados a su atención.', 'Corregido'),
    @('C03', 'Visibilidad de conversaciones', 'Los chats del Asesor se muestran en Comunicaciones y conservan su relación con Leads.', 'Corregido'),
    @('C04', 'Menú de perfil', 'Se corrigió el centrado del icono y del indicador dentro de su círculo.', 'Corregido'),
    @('C05', 'Nombres de perfiles sociales', 'Se mejoró la recuperación del nombre de clientes de Instagram y Facebook.', 'Corregido'),
    @('C06', 'Dashboard y Marketing', 'Se optimizaron consultas, caché y actualización mediante eventos del CRM.', 'Implementado')
)

$incidents = @(
    @('I01', 'Error HTTP 403 al iniciar sesión con credenciales válidas.', 'Alto', 'Corregido'),
    @('I02', 'Chats del Asesor visibles en Leads pero no en Comunicaciones.', 'Alto', 'Corregido'),
    @('I03', 'Asesores podían visualizar contactos fuera de su cartera.', 'Alto', 'Corregido'),
    @('I04', 'Iconos descentrados en el menú y las tarjetas.', 'Bajo', 'Corregido'),
    @('I05', 'Nombres sociales ausentes en registros de Instagram y Facebook.', 'Medio', 'Corregido'),
    @('I06', 'Carga lenta y actualización tardía en Dashboard y Marketing.', 'Medio', 'Optimizado')
)

$body = [System.Text.StringBuilder]::new()
[void]$body.Append((New-Paragraph -Text 'HPD GLASS GROUP' -Bold $true -Color '0B49A3' -Size 24 -Align 'right' -After 80))
[void]$body.Append((New-Paragraph -Text 'Informe de pruebas internas del CRM HPD' -Style 'Ttulo' -Bold $true -Color '0A3069' -Size 36 -After 40))
[void]$body.Append((New-Paragraph -Text 'Validación funcional de los perfiles Administrador y Asesor' -Bold $true -Color '0B49A3' -Size 26 -After 80))
[void]$body.Append((New-Paragraph -Text 'Informe actualizado con base en el alcance funcional del repositorio hasta el 30/09/2026.' -Color '596773' -Size 20 -After 220))
[void]$body.Append((New-Paragraph -Text '1 Datos generales y alcance' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 120 -KeepNext $true))
[void]$body.Append((New-Paragraph -Text "Fecha del informe: 30/09/2026`vPeriodo de pruebas y correcciones: 18/09/2026 a 30/09/2026`vAmbiente interno: IIS / entorno local · ASP.NET Core .NET 10 · MySQL 8" -Size 20 -After 120))
[void]$body.Append((New-Table -Headers @('Aplicación o componente','Versión evaluada','Responsable') -Rows @(,@('CRM HPD / CENTRO DE ATENCIÓN','Revisión 18-30/09/2026','Marlon Valenzuela Estrada')) -Widths @(4000,2900,2860)))
[void]$body.Append((New-Paragraph -Text 'Alcance: autenticación, dashboard, comunicaciones, contactos, tareas, leads, ventas, reportes, marketing, conexiones, usuarios, permisos, actividad y cierre de sesión. Se validan los alcances diferenciados de Administrador y Asesor.' -Size 20 -After 100))

[void]$body.Append((New-PageBreak))
[void]$body.Append((New-Paragraph -Text '2 Pruebas del perfil Administrador' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 100 -KeepNext $true))
[void]$body.Append((New-Paragraph -Text 'El Administrador posee visibilidad global y administra usuarios, permisos, conexiones, datos operativos y configuración del CRM.' -Color '596773' -Size 20 -After 120))
[void]$body.Append((New-Table -Headers @('Caso','Proceso y resultado esperado','Registro de la prueba') -Rows $adminRows -Widths @(900,4150,4710)))

[void]$body.Append((New-PageBreak))
[void]$body.Append((New-Paragraph -Text '3 Pruebas del perfil Asesor' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 100 -KeepNext $true))
[void]$body.Append((New-Paragraph -Text 'El Asesor trabaja con registros asignados y casos nuevos disponibles, sin acceso a funciones administrativas ni a carteras ajenas.' -Color '596773' -Size 20 -After 120))
[void]$body.Append((New-Table -Headers @('Caso','Proceso y resultado esperado','Registro de la prueba') -Rows $advisorRows -Widths @(900,4150,4710)))

[void]$body.Append((New-PageBreak))
[void]$body.Append((New-Paragraph -Text '4 Modificaciones y correcciones realizadas' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 120 -KeepNext $true))
[void]$body.Append((New-Table -Headers @('Cambio','Módulo','Modificación realizada','Estado') -Rows $changes -Widths @(780,2200,5380,1400)))
[void]$body.Append((New-Paragraph -Text '5 Incidencias identificadas' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 120 -KeepNext $true))
[void]$body.Append((New-Table -Headers @('ID','Incidencia','Impacto','Estado') -Rows $incidents -Widths @(700,5700,1500,1860)))

[void]$body.Append((New-PageBreak))
[void]$body.Append((New-Paragraph -Text '6 Evidencias de los procesos' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 100 -KeepNext $true))
[void]$body.Append((New-Paragraph -Text 'Pegue en la siguiente tabla el enlace correspondiente al video de prueba de cada perfil.' -Color '596773' -Size 20 -After 140))
[void]$body.Append((New-Table -Headers @('Evidencia','Perfil','Enlace del video') -Rows @(
    @('E01','Administrador','Pegar enlace aquí: ________________________________________________'),
    @('E02','Asesor','Pegar enlace aquí: ________________________________________________'),
    @('E03','Auditor','Pegar enlace aquí: ________________________________________________'),
    @('E04','Marketing','Pegar enlace aquí: ________________________________________________')
) -Widths @(1300,2300,6160)))

[void]$body.Append((New-Paragraph -Text '7 Resultado y conclusión' -Style 'Ttulo1' -Bold $true -Color '0A3069' -Size 30 -After 120 -KeepNext $true))
[void]$body.Append((New-Table -Headers @('Perfil','Casos','Resultado','Evidencia') -Rows @(@('Administrador','P01-P08','Conforme','Video E01'),@('Asesor','P09-P15','Conforme','Video E02')) -Widths @(2500,1900,2500,2860)))
[void]$body.Append((New-Paragraph -Text 'Conclusión: los recorridos funcionales definidos para Administrador y Asesor son conformes con el alcance evaluado. El CRM separa la visibilidad y las acciones por rol, permite gestionar la atención comercial y conserva los controles sobre funciones administrativas. La entrega final debe incluir los dos videos indicados en la sección de evidencias.' -Size 20 -After 260))
[void]$body.Append((New-Paragraph -Text "Elaborado por: Marlon Valenzuela Estrada`vFirma: ____________________________________    Fecha: 30/09/2026" -Bold $true -Color '0A3069' -Size 20 -After 160))
[void]$body.Append((New-Paragraph -Text "Revisado por: __________________________________________`vFirma: ____________________________________    Fecha: ____/____/______" -Bold $true -Color '0A3069' -Size 20 -After 100))

$sectPr = '<w:sectPr><w:footerReference w:type="default" r:id="rId11"/><w:pgSz w:w="12240" w:h="15840"/><w:pgMar w:top="1008" w:right="1080" w:bottom="1008" w:left="1080" w:header="720" w:footer="720" w:gutter="0"/><w:cols w:space="720"/><w:docGrid w:linePitch="360"/></w:sectPr>'
$documentXml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>' +
    $body.ToString() + $sectPr + '</w:body></w:document>'

if (-not (Test-Path $templatePath)) { throw "No se encontró la plantilla: $templatePath" }
if (Test-Path $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
Copy-Item -LiteralPath $templatePath -Destination $outputPath

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($outputPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $oldEntry = $archive.GetEntry('word/document.xml')
    if ($null -ne $oldEntry) { $oldEntry.Delete() }
    $entry = $archive.CreateEntry('word/document.xml', [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try {
        $utf8 = [System.Text.UTF8Encoding]::new($false)
        $writer = [System.IO.StreamWriter]::new($stream, $utf8)
        try { $writer.Write($documentXml) } finally { $writer.Dispose() }
    } finally { $stream.Dispose() }
} finally {
    $archive.Dispose()
}

Write-Output $outputPath
