# Ensure second line in each .cs file is the canonical path comment
# Usage: .\ensure-second-line.ps1 [-Path <dir>] [-WhatIf]
param(
    [string]$Path = "f:\Source\SurvivalTools\Source",
    [switch]$WhatIf
)

function ToForwardSlash([string]$p) { return $p -replace '\\', '/' }

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
# If run from anywhere, allow explicit Path param; default to Source folder
$base = Resolve-Path $Path
$files = Get-ChildItem -Path $base -Recurse -Filter "*.cs" -File | Where-Object { -not $_.PSIsContainer }
$changed = @()
foreach ($f in $files) {
    try {
        $rel = $f.FullName.Substring((Resolve-Path "f:\Source\SurvivalTools").Path.Length + 1)
    } catch {
        # fallback: compute relative to provided path
        $rel = $f.FullName.Substring($base.Path.Length + 1)
    }
    $relForward = ToForwardSlash($rel)
    $desired = "// $relForward"

    $content = Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue
    if ($null -eq $content) { continue }

    if ($content.Length -lt 1) {
        # create header and insert second line
        if ($WhatIf) { Write-Output "Would update: $relForward (file empty)"; continue }
        $new = @()
        $new += "// RimWorld 1.6 / C# 7.3"
        $new += $desired
        $new += ""  # spacer
        Set-Content -LiteralPath $f.FullName -Value $new -Encoding UTF8
        $changed += $relForward
        continue
    }

    # Ensure first line is canonical header; preserve it if present otherwise insert
    $first = $content[0].TrimEnd()
    if ($first -ne "// RimWorld 1.6 / C# 7.3") {
        # insert canonical header if not present
        if ($WhatIf) { Write-Output "Would insert canonical header into: $relForward"; }
        else {
            $content = @("// RimWorld 1.6 / C# 7.3") + $content
        }
    }

    # Re-read in case we modified
    $content = Get-Content -LiteralPath $f.FullName -ErrorAction SilentlyContinue
    if ($content.Length -lt 2) {
        # add second line
        if ($WhatIf) { Write-Output "Would insert second line into: $relForward"; continue }
        $new = $content + ,$desired
        Set-Content -LiteralPath $f.FullName -Value $new -Encoding UTF8
        $changed += $relForward
        continue
    }

    $second = $content[1].TrimEnd()
    if ($second -ne $desired) {
        if ($WhatIf) { Write-Output "Would replace/insert second line in: $relForward -> $desired"; continue }

        # If the existing second line looks like a using/namespace/class/attribute or comment,
        # insert the desired path comment at line 2 and preserve the rest. Otherwise replace it.
        $pattern = '^(using\s|namespace\s|\[|public\s|internal\s|class\s|record\s|interface\s|struct\s|//|///)'
        if ($second -match $pattern) {
            # safe slice: build new content as [firstLine] + [desired] + [rest]
            if ($content.Length -gt 1) { $rest = $content[1..($content.Length - 1)] } else { $rest = @() }
            $new = @($content[0]) + @($desired) + $rest
            Set-Content -LiteralPath $f.FullName -Value $new -Encoding UTF8
        }
        else {
            $content[1] = $desired
            Set-Content -LiteralPath $f.FullName -Value $content -Encoding UTF8
        }

        $changed += $relForward
    }
}

if ($changed.Count -eq 0) { Write-Output "No files changed." } else { Write-Output "Updated files:`n$($changed -join "`n")" }
