param(
    [string]$Version = "16.0",
    [string]$Source,
    [string]$Output
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($Output)) {
    $Output = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\Fort.ind UWP\Assets\Emoji\unicode-emoji.txt"
}

$groupSlugs = @{
    "Smileys & Emotion" = "smileys-emotion"
    "People & Body"     = "people-body"
    "Animals & Nature"  = "animals-nature"
    "Food & Drink"      = "food-drink"
    "Travel & Places"   = "travel-places"
    "Activities"        = "activities"
    "Objects"           = "objects"
    "Symbols"           = "symbols"
    "Flags"             = "flags"
}

$toneModifiers = 0x1F3FB, 0x1F3FC, 0x1F3FD, 0x1F3FE, 0x1F3FF
$variationSelector = [string][char]0xFE0F

if ([string]::IsNullOrEmpty($Source)) {
    $url = "https://unicode.org/Public/emoji/$Version/emoji-test.txt"
    Write-Host "Downloading $url"
    $text = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
}
else {
    $text = [System.IO.File]::ReadAllText($Source, [System.Text.Encoding]::UTF8)
}

$entries = New-Object System.Collections.Generic.List[object]
$byKey = @{}
$group = $null

foreach ($raw in $text -split "`n") {
    $line = $raw.TrimEnd("`r")

    if ($line.StartsWith("# group: ")) {
        $name = $line.Substring(9).Trim()
        $group = $groupSlugs[$name]
        continue
    }

    if ($null -eq $group -or $line.StartsWith("#") -or $line.Trim().Length -eq 0) { continue }

    $match = [regex]::Match($line, '^([0-9A-F ]+?)\s*;\s*fully-qualified\s*#\s*\S+\s+E(\d+\.\d+)\s+(.+)$')
    if (-not $match.Success) { continue }

    $points = @($match.Groups[1].Value.Trim() -split '\s+' | ForEach-Object { [Convert]::ToInt32($_, 16) })
    $emoji = -join ($points | ForEach-Object { [char]::ConvertFromUtf32($_) })
    $emojiVersion = $match.Groups[2].Value
    $title = $match.Groups[3].Value.Trim()

    $tones = @($points | Where-Object { $toneModifiers -contains $_ })
    if ($tones.Count -gt 0) {
        $distinct = @($tones | Select-Object -Unique)
        if ($distinct.Count -ne 1) { continue }

        $basePoints = @($points | Where-Object { ($toneModifiers -notcontains $_) -and $_ -ne 0xFE0F })
        $key = -join ($basePoints | ForEach-Object { [char]::ConvertFromUtf32($_) })
        $base = $byKey[$key]
        if ($null -eq $base) { continue }

        $index = [array]::IndexOf($toneModifiers, $distinct[0])
        if ($null -eq $base.Tones[$index]) {
            $base.Tones[$index] = $emoji
            if ([version]$emojiVersion -gt [version]$base.ToneVersion) { $base.ToneVersion = $emojiVersion }
        }
        continue
    }

    $entry = [pscustomobject]@{
        Group       = $group
        Emoji       = $emoji
        Version     = $emojiVersion
        Name        = $title
        Tones       = New-Object 'string[]' 5
        ToneVersion = "0.0"
    }
    $entries.Add($entry)

    $key = $emoji.Replace($variationSelector, "")
    if (-not $byKey.ContainsKey($key)) { $byKey[$key] = $entry }
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.Append("unicode-emoji`t$Version`r`n")

foreach ($entry in $entries) {
    $toneText = ""
    $toneVersion = ""
    if (@($entry.Tones | Where-Object { $null -eq $_ }).Count -eq 0) {
        $toneText = $entry.Tones -join " "
        $toneVersion = $entry.ToneVersion
    }

    [void]$builder.Append("$($entry.Group)`t$($entry.Emoji)`t$($entry.Version)`t$($entry.Name)`t$toneText`t$toneVersion`r`n")
}

$folder = Split-Path -Parent $Output
if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }

[System.IO.File]::WriteAllText($Output, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $($entries.Count) emoji to $Output"
