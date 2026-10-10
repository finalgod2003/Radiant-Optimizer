param([switch]$SkipWebAssets)
$ErrorActionPreference = 'Stop'
$clientRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$clientDotnet = Join-Path $clientRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $clientDotnet)) { $clientDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$clientProject = Join-Path $PSScriptRoot 'Radiant.Desktop/Radiant.Desktop.csproj'
$clientOutput = Join-Path $clientRoot 'artifacts/windows-preview/RadiantOptimizer-win-x64'
$clientArtifacts = Join-Path $clientRoot 'artifacts/windows-preview'
$clientProperties = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Directory.Build.props') -Raw)
$clientVersion = [string]$clientProperties.Project.PropertyGroup.Version
if ($clientVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Desktop release version must use major.minor.patch.' }
New-Item -ItemType Directory -Path $clientOutput -Force | Out-Null
& $clientDotnet run --project (Join-Path $PSScriptRoot 'Radiant.Tests/Radiant.Tests.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Core diagnostic checks failed.' }
& $clientDotnet publish $clientProject --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output $clientOutput
if ($LASTEXITCODE -ne 0) { throw 'Windows client publish failed.' }

$clientExe = Join-Path $clientOutput 'RadiantOptimizer.exe'
$clientVerify = Join-Path $clientArtifacts 'hardware-verification.json'
$clientProcess = Start-Process -FilePath $clientExe -ArgumentList @('--verify', ('"' + $clientVerify + '"')) -WindowStyle Hidden -PassThru
if (-not $clientProcess.WaitForExit(45000)) { $clientProcess.Kill(); throw 'Published client verification timed out.' }
if ($clientProcess.ExitCode -ne 0) { throw 'Published client verification failed; inspect artifacts/windows-preview.' }

if (-not $SkipWebAssets) {
    $clientImages = Join-Path $clientRoot 'public/design-preview'
    # Render each view in a separate session with its own hardware snapshot.
    foreach ($clientTheme in @('light', 'dark')) {
        foreach ($clientView in @('overview', 'checks', 'hardware')) {
            $clientProcess = Start-Process -FilePath $clientExe -ArgumentList @('--render-preview', ('"' + $clientImages + '"'), $clientView, $clientTheme) -WindowStyle Hidden -PassThru
            if (-not $clientProcess.WaitForExit(45000)) { $clientProcess.Kill(); throw 'Client view rendering timed out.' }
            if ($clientProcess.ExitCode -ne 0) { throw 'Client view rendering failed; inspect public/design-preview/render-error.txt.' }
        }
    }
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $clientOutput 'README.md') -Force
$clientArchiveName = 'RadiantOptimizer-' + $clientVersion + '-win-x64.zip'
$clientArchive = Join-Path $clientArtifacts $clientArchiveName
Compress-Archive -Path (Join-Path $clientOutput '*') -DestinationPath $clientArchive -Force
$clientHash = (Get-FileHash -LiteralPath $clientArchive -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $clientArtifacts 'SHA256SUMS.txt'), ($clientHash + '  ' + $clientArchiveName + [Environment]::NewLine))
if (-not $SkipWebAssets) {
    $clientDownloads = Join-Path $clientRoot 'public/downloads/preview'
    New-Item -ItemType Directory -Path $clientDownloads -Force | Out-Null
    Copy-Item -LiteralPath $clientArchive -Destination $clientDownloads -Force
    Copy-Item -LiteralPath (Join-Path $clientArtifacts 'SHA256SUMS.txt') -Destination $clientDownloads -Force
}
Write-Output ('Executable: ' + $clientExe)
Write-Output ('Archive: ' + $clientArchive)
Write-Output ('SHA-256: ' + $clientHash)
