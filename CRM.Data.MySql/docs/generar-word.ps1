[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Convert-InlineMarkdown {
    param([string]$Text)

    $result = $Text
    $result = [regex]::Replace($result, '!\[([^\]]*)\]\([^\)]+\)', '$1')
    $result = [regex]::Replace($result, '\[([^\]]+)\]\([^\)]+\)', '$1')
    $result = $result.Replace('**', '').Replace('__', '').Replace('`', '')
    return [System.Security.SecurityElement]::Escape($result)
}

function New-WordParagraph {
    param(
        [string]$Text,
        [string]$Style
    )

    $escaped = Convert-InlineMarkdown $Text
    return '<w:p><w:pPr><w:pStyle w:val="' + $Style +
        '"/></w:pPr><w:r><w:t xml:space="preserve">' + $escaped +
        '</w:t></w:r></w:p>'
}

function Update-WordDocument {
    param(
        [string]$MarkdownPath,
        [string]$WordPath
    )

    $paragraphs = [System.Text.StringBuilder]::new()
    $inCodeBlock = $false

    foreach ($rawLine in [System.IO.File]::ReadAllLines($MarkdownPath)) {
        $line = $rawLine.TrimEnd()

        if ($line -match '^```') {
            $inCodeBlock = -not $inCodeBlock
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            [void]$paragraphs.Append('<w:p/>')
            continue
        }

        if ($inCodeBlock) {
            [void]$paragraphs.Append((New-WordParagraph $line 'Normal'))
        }
        elseif ($line -match '^# (.+)$') {
            [void]$paragraphs.Append((New-WordParagraph $Matches[1] 'Title'))
        }
        elseif ($line -match '^## (.+)$') {
            [void]$paragraphs.Append((New-WordParagraph $Matches[1] 'Heading2'))
        }
        elseif ($line -match '^### (.+)$') {
            [void]$paragraphs.Append((New-WordParagraph $Matches[1] 'Heading3'))
        }
        elseif ($line -match '^[-*] (.+)$') {
            [void]$paragraphs.Append((New-WordParagraph $Matches[1] 'ListBullet'))
        }
        elseif ($line -match '^\d+\. (.+)$') {
            [void]$paragraphs.Append((New-WordParagraph $Matches[1] 'ListNumber'))
        }
        else {
            [void]$paragraphs.Append((New-WordParagraph $line 'Normal'))
        }
    }

    $xml = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
        '<w:body>' + $paragraphs.ToString() +
        '<w:sectPr><w:pgSz w:w="12240" w:h="15840"/>' +
        '<w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>' +
        '</w:sectPr></w:body></w:document>'

    $archive = [System.IO.Compression.ZipFile]::Open(
        $WordPath,
        [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $oldEntry = $archive.GetEntry('word/document.xml')
        if ($null -ne $oldEntry) {
            $oldEntry.Delete()
        }

        $entry = $archive.CreateEntry(
            'word/document.xml',
            [System.IO.Compression.CompressionLevel]::Optimal)
        $stream = $entry.Open()
        $writer = [System.IO.StreamWriter]::new(
            $stream,
            [System.Text.UTF8Encoding]::new($false))
        try {
            $writer.Write($xml)
        }
        finally {
            $writer.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

$documents = @(
    @('DOCUMENTACION_COMPLETA_CRM_HPD.md', 'DOCUMENTACION_COMPLETA_CRM_HPD.docx'),
    @('MANUAL_USO_CRM_HPD.md', 'MANUAL_USO_CRM_HPD.docx')
)

foreach ($document in $documents) {
    Update-WordDocument `
        (Join-Path $PSScriptRoot $document[0]) `
        (Join-Path $PSScriptRoot $document[1])
    Write-Host "Actualizado: $($document[1])"
}
