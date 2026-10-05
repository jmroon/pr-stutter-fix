function Install-ComparisonOutput {
    param([hashtable]$Output, $Entry)
    # Index the dictionary explicitly: '.Remove' resolves to Hashtable.Remove
    # when the key is missing, making an ordinary copy look like a removal.
    $removeFile = $Output['RemoveFile'] -eq $true
    if ($removeFile) {
        if (Test-Path -LiteralPath $Output.Path) { Remove-Item -LiteralPath $Output.Path }
        if (Test-Path -LiteralPath $Output.Path) { throw 'Obsolete diagnostic DLL still present.' }
        $Entry.InstalledHash = 'ABSENT'
        return
    }
    if ($Output.Source) { Copy-Item -LiteralPath $Output.Source -Destination $Output.Path -Force }
    else { [IO.File]::WriteAllText($Output.Path, $Output.Text, [Text.UTF8Encoding]::new($false)) }
    $Entry.InstalledHash = (Get-FileHash -LiteralPath $Output.Path).Hash
    if ($Output.Source -and $Entry.InstalledHash -ne (Get-FileHash -LiteralPath $Output.Source).Hash) { throw 'Installed DLL hash mismatch.' }
    if (!$Output.Source -and [IO.File]::ReadAllText($Output.Path) -ne $Output.Text) { throw 'Installed configuration mismatch.' }
}
