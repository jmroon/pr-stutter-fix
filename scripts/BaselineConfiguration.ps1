function Disable-IniKey([string]$Path, [string]$Section) {
    $lines = [System.Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $Path) { $lines.AddRange([System.IO.File]::ReadAllLines($Path)) }
    $sectionStart = -1; $sectionEnd = $lines.Count; $key = -1
    for ($i=0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*\[([^]]+)\]\s*$') {
            if ($sectionStart -ge 0) { $sectionEnd=$i; break }
            if ($Matches[1] -eq $Section) { $sectionStart=$i }
        } elseif ($sectionStart -ge 0 -and $lines[$i] -match '^\s*Enabled\s*=') {
            if ($key -ge 0) { throw "Duplicate Enabled key in $Path" }
            $key=$i
        }
    }
    if ($key -ge 0) { $lines[$key]='Enabled = false' }
    elseif ($sectionStart -ge 0) { $lines.Insert($sectionEnd,'Enabled = false') }
    else { $lines.Add(''); $lines.Add("[$Section]"); $lines.Add('Enabled = false') }
    return ($lines -join "`r`n") + "`r`n"
}
