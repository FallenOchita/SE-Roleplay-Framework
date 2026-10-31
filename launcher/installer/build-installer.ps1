$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'SE.Roleplay.Framework.Launcher\SE.Roleplay.Framework.Launcher.csproj'
$publish = Join-Path $root 'publish'
$artifacts = Join-Path $root 'artifacts'
$iss = Join-Path $PSScriptRoot 'SE1-RP-Launcher.iss'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET 8 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/8.0.' }
$isccCandidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is required. Install it from https://jrsoftware.org/isdl.php.' }
New-Item -ItemType Directory -Force -Path $publish, $artifacts | Out-Null
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
Write-Host "Installer created in: $artifacts"
