# Normalize C# file headers under Source/
# Adds single-line header "// RimWorld 1.6 / C# 7.3" at the top of each .cs file
# Removes duplicate or variant header lines in the first 10 lines

$root = Join-Path $PSScriptRoot "..\Source"
$canonical = '// RimWorld 1.6 / C# 7.3'

Get-ChildItem -Path $root -Recurse -Filter *.cs | ForEach-Object {
    $path = $_.FullName
    $lines = Get-Content -Path $path -ErrorAction SilentlyContinue
    if (-not $lines) { return }

    # Remove any existing header variants within first 12 lines
    $firstN = [Math]::Min(12, $lines.Length)
    $head = $lines[0..($firstN-1)]

    # Detect any header-like lines (RimWorld/Rimworld or duplicate canonical)
    $newHead = @()
    $foundCanonical = $false
    foreach ($l in $head) {
        if ($l -match "RimWorld 1.6 / C# 7.3" -or $l -match "Rimworld 1.6 / C# 7.3") {
            if (-not $foundCanonical) { $foundCanonical = $true; continue } # skip first occurrence (we'll add canonical later)
            else { continue } # drop duplicates
        }
        else { $newHead += $l }
    }

    # If canonical missing, insert it; ensure it's the very first line
    if (-not $foundCanonical) {
        $newLines = @($canonical) + $lines
    } else {
        # We removed existing header occurrences from the firstN block; ensure canonical is first
        # Remove any leading empty lines
        $remaining = $lines | Where-Object { $_ -ne $null }
        # Place canonical and then the rest, but avoid duplicating if already at top
        $rest = $lines
        # Ensure canonical as first line
        if ($lines[0] -match "RimWorld 1.6 / C# 7.3") {
            $newLines = $lines
        } else {
            # Remove any header-like lines from top block
            $trimmed = $lines
            for ($i=0; $i -lt $firstN; $i++) {
                if ($i -ge $trimmed.Length) { break }
                if ($trimmed[$i] -match "RimWorld 1.6 / C# 7.3" -or $trimmed[$i] -match "Rimworld 1.6 / C# 7.3") {
                    $start = $i + 1
                    if ($start -ge $trimmed.Length) { $trimmed = @() } else { $trimmed = $trimmed[$start..($trimmed.Length-1)] }
                    break
                }
            }
            $newLines = @($canonical) + $trimmed
        }
    }

    # Write back only if changed
    $out = $newLines -join "`n"
    $orig = $lines -join "`n"
    if ($out -ne $orig) {
        Write-Host "Updating header in: $path"
        Set-Content -LiteralPath $path -Value $out -Encoding UTF8
    }
}

Write-Host "Header normalization complete."