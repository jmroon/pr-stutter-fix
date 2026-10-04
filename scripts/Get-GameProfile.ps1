function Get-PrGameProfile {
    param([ValidateSet('FFVI','FFIV')][string]$Game = 'FFVI', [string]$GameDirectory)
    $roman = if ($Game -eq 'FFIV') { 'IV' } else { 'VI' }
    if (!$GameDirectory) { $GameDirectory = "D:/SteamLibrary/steamapps/common/FINAL FANTASY $roman PR" }
    $suffix = if ($Game -eq 'FFIV') { 'FFIV/' } else { '' }
    $assembly = if ($Game -eq 'FFIV') { 'bb2f4c9db44c8ee9b065696aeafb77561a1709492130f045433e6d215abc42ed' } else { '0029a22ed933aa3b6ea3b1290060502267f514d61ba6e6619308d844557f2ffd' }
    $metadata = if ($Game -eq 'FFIV') { '37400ca079eddb18cda06450c02c7bd8eb2c057175502e75e2e2c8bc09b31f41' } else { 'f50d9d1ff84f8033b8acbdc6845ab3a0f2793dd253c36a4a7d212303980844dd' }
    [pscustomobject]@{Game=$Game; Directory=$GameDirectory; Process="FINAL FANTASY $roman"; DataDirectory="FINAL FANTASY ${roman}_Data"; Output="bin/${suffix}Release/net6.0"; AssemblyHash=$assembly; MetadataHash=$metadata}
}
function Assert-PrGameBuild {
    param($Profile)
    $inputs = @{ 'GameAssembly.dll'=$Profile.AssemblyHash; "$($Profile.DataDirectory)/il2cpp_data/Metadata/global-metadata.dat"=$Profile.MetadataHash }
    foreach ($relative in $inputs.Keys) {
        $path = Join-Path $Profile.Directory $relative
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path).Hash -ne $inputs[$relative]) { throw "Unsupported $($Profile.Game) input: $path" }
    }
}
