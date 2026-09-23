param([switch]$Publish, [switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$taskDotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
& $taskDotnet restore BlueSpectrum.csproj -r win-x64 -p:SelfContained=true --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
& $taskDotnet build BlueSpectrum.csproj -c Release -r win-x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if ($Test) {
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    & $taskDotnet 'bin\Release\net10.0-windows\win-x64\BlueSpectrum.dll' --self-test 'artifacts\self-test.json'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. See artifacts\self-test.json.' }
}
if ($Publish) {
    & $taskDotnet publish BlueSpectrum.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination publish
    Copy-Item -LiteralPath licenses,docs -Destination publish -Recurse -Force
}
