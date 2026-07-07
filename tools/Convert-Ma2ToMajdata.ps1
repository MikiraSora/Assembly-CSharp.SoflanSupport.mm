param(
    [string]$InputPath = 'F:\SDEZ_165\Package\Sinmai_Data\StreamingAssets\A000\music\music003999\003999_02.ma2.bak_20260703023239',
    [string]$OutputPath = '',
    [string]$MusicXmlPath = '',
    [int]$InoteIndex = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$InvariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

function Format-InvariantDouble {
    param([double]$Value)
    return $Value.ToString('G17', $InvariantCulture)
}

function Format-InvariantFloat {
    param([double]$Value)
    return $Value.ToString('G9', $InvariantCulture)
}

function Get-TotalGrid {
    param(
        [int64]$Bar,
        [int64]$Grid,
        [int64]$Resolution
    )
    return $Bar * $Resolution + $Grid
}

function Split-Ma2Line {
    param([string]$Line)
    return $Line -split '\s+'
}

function Get-MusicMetadata {
    param(
        [string]$XmlPath,
        [string]$ChartFileName
    )

    $metadata = [ordered]@{
        Title = ''
        Artist = ''
        Level = ''
        Designer = ''
        MusicId = ''
        MusicBpm = ''
    }

    if ([string]::IsNullOrWhiteSpace($XmlPath) -or -not [System.IO.File]::Exists($XmlPath)) {
        return $metadata
    }

    [xml]$musicXml = [System.IO.File]::ReadAllText($XmlPath, [System.Text.Encoding]::UTF8)
    $root = $musicXml.MusicData
    if ($null -eq $root) {
        return $metadata
    }

    $metadata.Title = [string]$root.name.str
    $metadata.Artist = [string]$root.artistName.str
    $metadata.MusicId = [string]$root.name.id
    $metadata.MusicBpm = [string]$root.bpm

    $sourceChartName = $ChartFileName
    if ($sourceChartName -match '^(?<name>.+?\.ma2)(?:\.bak_.+)?$') {
        $sourceChartName = $Matches['name']
    }

    foreach ($noteData in @($root.notesData.Notes)) {
        $path = [string]$noteData.file.path
        if (-not $path.Equals($sourceChartName, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $level = [string]$noteData.level
        $levelDecimal = [string]$noteData.levelDecimal
        if (-not [string]::IsNullOrWhiteSpace($levelDecimal) -and $levelDecimal -ne '0') {
            $metadata.Level = "$level.$levelDecimal"
        }
        else {
            $metadata.Level = $level
        }

        $metadata.Designer = [string]$noteData.notesDesigner.str
        break
    }

    return $metadata
}

function Add-Event {
    param(
        [hashtable]$Events,
        [int64]$Grid
    )

    if (-not $Events.ContainsKey($Grid)) {
        $Events[$Grid] = [ordered]@{
            Bpms = New-Object 'System.Collections.Generic.List[string]'
            Speeds = @{}
            Notes = @{}
        }
    }

    return $Events[$Grid]
}

function Add-SpeedEvent {
    param(
        [hashtable]$Events,
        [int64]$Grid,
        [int]$Group,
        [string]$Speed
    )

    $event = Add-Event -Events $Events -Grid $Grid
    $event.Speeds[$Group] = $Speed
}

function Add-NoteEvent {
    param(
        [hashtable]$Events,
        [int64]$Grid,
        [int]$Group,
        [string]$NoteText
    )

    $event = Add-Event -Events $Events -Grid $Grid
    if (-not $event.Notes.ContainsKey($Group)) {
        $event.Notes[$Group] = New-Object 'System.Collections.Generic.List[string]'
    }

    $event.Notes[$Group].Add($NoteText)
}

if (-not [System.IO.File]::Exists($InputPath)) {
    throw "MA2 file not found: $InputPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path -Path ([System.IO.Path]::GetDirectoryName($InputPath)) -ChildPath ([System.IO.Path]::GetFileName($InputPath) + '.majdata.txt')
}

if ([string]::IsNullOrWhiteSpace($MusicXmlPath)) {
    $candidateXml = Join-Path -Path ([System.IO.Path]::GetDirectoryName($InputPath)) -ChildPath 'Music.xml'
    if ([System.IO.File]::Exists($candidateXml)) {
        $MusicXmlPath = $candidateXml
    }
}

if ($InoteIndex -le 0) {
    $leaf = [System.IO.Path]::GetFileName($InputPath)
    if ($leaf -match '_(?<diff>\d{2})\.ma2') {
        $InoteIndex = [int]$Matches['diff'] + 1
    }
    else {
        $InoteIndex = 1
    }
}

$lines = [System.IO.File]::ReadAllLines($InputPath, [System.Text.Encoding]::UTF8)
$resolution = 384L
$events = @{}
$sflByGroup = @{}
$stats = [ordered]@{
    Bpm = 0
    Sfl = 0
    SflReset = 0
    Notes = 0
    UnsupportedNotes = New-Object 'System.Collections.Generic.List[string]'
    MaxNoteGrid = 0L
    MaxSflEndGrid = 0L
    MinBpm = $null
    MaxBpm = $null
    FirstBpm = $null
}

foreach ($rawLine in $lines) {
    $line = $rawLine.Trim()
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = Split-Ma2Line -Line $line
    if ($parts.Count -eq 0) {
        continue
    }

    switch ($parts[0]) {
        'RESOLUTION' {
            $resolution = [int64]$parts[1]
            break
        }
        'BPM' {
            if ($parts.Count -lt 4) {
                throw "Invalid BPM line: $line"
            }

            $grid = Get-TotalGrid -Bar ([int64]$parts[1]) -Grid ([int64]$parts[2]) -Resolution $resolution
            $bpmText = Format-InvariantFloat ([double]::Parse($parts[3], $InvariantCulture))
            $event = Add-Event -Events $events -Grid $grid
            $event.Bpms.Add($bpmText)

            $bpmValue = [double]::Parse($parts[3], $InvariantCulture)
            if ($null -eq $stats.FirstBpm) {
                $stats.FirstBpm = $bpmText
            }
            if ($null -eq $stats.MinBpm -or $bpmValue -lt $stats.MinBpm) {
                $stats.MinBpm = $bpmValue
            }
            if ($null -eq $stats.MaxBpm -or $bpmValue -gt $stats.MaxBpm) {
                $stats.MaxBpm = $bpmValue
            }
            $stats.Bpm++
            break
        }
        'SFL' {
            if ($parts.Count -lt 6) {
                throw "Invalid SFL line: $line"
            }

            $startGrid = Get-TotalGrid -Bar ([int64]$parts[1]) -Grid ([int64]$parts[2]) -Resolution $resolution
            $duration = [int64]$parts[3]
            $speedText = Format-InvariantFloat ([double]::Parse($parts[4], $InvariantCulture))
            $group = [int]$parts[5]
            $endGrid = $startGrid + $duration

            if (-not $sflByGroup.ContainsKey($group)) {
                $sflByGroup[$group] = New-Object 'System.Collections.Generic.List[object]'
            }

            $sflByGroup[$group].Add([pscustomobject]@{
                StartGrid = $startGrid
                EndGrid = $endGrid
                Duration = $duration
                Group = $group
                Speed = $speedText
            })

            Add-SpeedEvent -Events $events -Grid $startGrid -Group $group -Speed $speedText
            if ($endGrid -gt $stats.MaxSflEndGrid) {
                $stats.MaxSflEndGrid = $endGrid
            }
            $stats.Sfl++
            break
        }
        { $_ -in @('NMTAP','NMSTR','EXTAP','EXSTR','BRTAP','BRSTR','BXTAP','BXSTR','NMTTP') } {
            if ($parts.Count -lt 4) {
                throw "Invalid note line: $line"
            }

            $grid = Get-TotalGrid -Bar ([int64]$parts[1]) -Grid ([int64]$parts[2]) -Resolution $resolution
            $position = [int]$parts[3] + 1
            $group = 0
            if ($parts.Count -ge 5 -and $parts[4] -match '^#(?<group>\d+)$') {
                $group = [int]$Matches['group']
            }

            $suffix = ''
            $noteType = $parts[0]
            if ($noteType -match 'STR') { $suffix += [char]36 + [char]36 }
            if ($noteType -match '^BX') { $suffix += 'bx' }
            elseif ($noteType -match '^EX') { $suffix += 'x' }
            elseif ($noteType -match '^BR') { $suffix += 'b' }
            $noteText = [string]$position + $suffix

            Add-NoteEvent -Events $events -Grid $grid -Group $group -NoteText $noteText
            if ($grid -gt $stats.MaxNoteGrid) {
                $stats.MaxNoteGrid = $grid
            }
            $stats.Notes++
            break
        }
        { $_ -match '^(?:EX|BR|BX|NM|CN|SI|SL|SC|SU|SS|SV|SX|SF)' } {
            $stats.UnsupportedNotes.Add($line)
            break
        }
    }
}

if ($stats.UnsupportedNotes.Count -gt 0) {
    $preview = ($stats.UnsupportedNotes | Select-Object -First 10) -join [Environment]::NewLine
    throw "This converter currently supports the target file's NMTAP notes only. Unsupported note lines:`n$preview"
}

foreach ($group in @($sflByGroup.Keys | Sort-Object)) {
    $records = @($sflByGroup[$group] | Sort-Object StartGrid, EndGrid)
    for ($i = 0; $i -lt $records.Count; $i++) {
        $record = $records[$i]
        if ($record.Duration -le 0) {
            continue
        }

        $nextRecord = $null
        if ($i + 1 -lt $records.Count) {
            $nextRecord = $records[$i + 1]
        }

        $hasImmediateNext = $false
        if ($null -ne $nextRecord -and [int64]$nextRecord.StartGrid -le [int64]$record.EndGrid) {
            $hasImmediateNext = $true
        }

        if (-not $hasImmediateNext -and [double]::Parse($record.Speed, $InvariantCulture) -ne 1.0) {
            Add-SpeedEvent -Events $events -Grid ([int64]$record.EndGrid) -Group ([int]$record.Group) -Speed '1'
            $stats.SflReset++
        }
    }
}

if ($events.Count -eq 0) {
    throw 'No convertible BPM/SFL/note events were found in MA2.'
}

$metadata = Get-MusicMetadata -XmlPath $MusicXmlPath -ChartFileName ([System.IO.Path]::GetFileName($InputPath))
if ([string]::IsNullOrWhiteSpace($metadata.Title)) {
    $metadata.Title = [System.IO.Path]::GetFileNameWithoutExtension($InputPath)
}
if ([string]::IsNullOrWhiteSpace($metadata.Artist)) {
    $metadata.Artist = ''
}
if ([string]::IsNullOrWhiteSpace($metadata.Level)) {
    $metadata.Level = ''
}
if ([string]::IsNullOrWhiteSpace($metadata.Designer)) {
    $metadata.Designer = ''
}
if ([string]::IsNullOrWhiteSpace($metadata.MusicId)) {
    if (([System.IO.Path]::GetFileName($InputPath)) -match '^(?<id>\d{6})_') {
        $metadata.MusicId = [string]([int]$Matches['id'])
    }
}

$gridKeys = @($events.Keys | Sort-Object {[int64]$_})
$chartLines = New-Object 'System.Collections.Generic.List[string]'

for ($i = 0; $i -lt $gridKeys.Count; $i++) {
    $grid = [int64]$gridKeys[$i]
    $event = $events[$grid]
    $segment = New-Object System.Text.StringBuilder

    foreach ($bpm in $event.Bpms) {
        [void]($segment.Append('(').Append($bpm).Append(')'))
    }

    foreach ($group in @($event.Speeds.Keys | Sort-Object {[int]$_})) {
        [void]($segment.Append('<HS').Append([string]$group).Append('*').Append([string]$event.Speeds[$group]).Append('>'))
    }

    foreach ($group in @($event.Notes.Keys | Sort-Object {[int]$_})) {
        $notes = ($event.Notes[$group] -join '/')
        if ([int]$group -eq 0) {
            [void]($segment.Append($notes))
        }
        else {
            [void]($segment.Append('<HS').Append([string]$group).Append('>(').Append($notes).Append(')'))
        }
    }

    if ($i + 1 -lt $gridKeys.Count) {
        $nextGrid = [int64]$gridKeys[$i + 1]
        $deltaGrid = $nextGrid - $grid
        if ($deltaGrid -le 0) {
            throw "Invalid event grid order: $grid -> $nextGrid"
        }

        $beats = [double]$resolution / [double]$deltaGrid
        [void]($segment.Append('{').Append((Format-InvariantDouble $beats)).Append('},'))
    }
    else {
        [void]($segment.Append('{4},'))
    }

    $chartLines.Add($segment.ToString())
}

$text = New-Object System.Text.StringBuilder
[void]($text.Append('&title=').AppendLine($metadata.Title))
[void]($text.Append('&artist=').AppendLine($metadata.Artist))
[void]($text.Append('&first=0').AppendLine())
if (-not [string]::IsNullOrWhiteSpace($metadata.Designer)) {
    [void]($text.Append('&des_').Append($InoteIndex).Append('=').AppendLine($metadata.Designer))
    [void]($text.Append('&des=').AppendLine($metadata.Designer))
}
if (-not [string]::IsNullOrWhiteSpace($metadata.Level)) {
    [void]($text.Append('&lv_').Append($InoteIndex).Append('=').AppendLine($metadata.Level))
}
if (-not [string]::IsNullOrWhiteSpace($metadata.MusicId)) {
    [void]($text.Append('&id=').AppendLine($metadata.MusicId))
}
if ($null -ne $stats.FirstBpm) {
    [void]($text.Append('&wholebpm=').AppendLine([string]$stats.FirstBpm))
}
[void]($text.Append('&source_ma2=').AppendLine($InputPath))
[void]($text.Append('&inote_').Append($InoteIndex).Append('=').AppendLine())
foreach ($chartLine in $chartLines) {
    [void]($text.AppendLine($chartLine))
}

$outDir = [System.IO.Path]::GetDirectoryName($OutputPath)
if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not [System.IO.Directory]::Exists($outDir)) {
    [System.IO.Directory]::CreateDirectory($outDir) | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutputPath, $text.ToString(), $utf8NoBom)

$summary = [ordered]@{
    InputPath = $InputPath
    OutputPath = $OutputPath
    MusicXmlPath = $MusicXmlPath
    Resolution = $resolution
    InoteIndex = $InoteIndex
    Title = $metadata.Title
    Artist = $metadata.Artist
    Level = $metadata.Level
    Designer = $metadata.Designer
    FirstBpm = $stats.FirstBpm
    MinBpm = if ($null -eq $stats.MinBpm) { $null } else { Format-InvariantFloat $stats.MinBpm }
    MaxBpm = if ($null -eq $stats.MaxBpm) { $null } else { Format-InvariantFloat $stats.MaxBpm }
    BpmRecords = $stats.Bpm
    SflRecords = $stats.Sfl
    SflResetEventsAdded = $stats.SflReset
    TapNotes = $stats.Notes
    MajdataEventLines = $chartLines.Count
    MaxNoteGrid = $stats.MaxNoteGrid
    MaxSflEndGrid = $stats.MaxSflEndGrid
}

$summaryPath = $OutputPath + '.summary.json'
$summaryJson = $summary | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($summaryPath, $summaryJson + [Environment]::NewLine, $utf8NoBom)

Write-Host "Wrote: $OutputPath"
Write-Host "Summary: $summaryPath"
Write-Host ("BPM={0}, SFL={1}, SFL resets={2}, NMTAP={3}, event lines={4}" -f $stats.Bpm, $stats.Sfl, $stats.SflReset, $stats.Notes, $chartLines.Count)
