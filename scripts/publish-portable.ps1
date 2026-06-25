param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "src\\PrinterScanner.App\\PrinterScanner.App.csproj"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw "O SDK do .NET 8 nao foi encontrado no PATH. Instale o SDK e execute este script novamente."
}

$PublishDir = Join-Path $ProjectRoot "publish\\$Runtime-portable"

& $dotnet.Source publish $ProjectFile `
    -c Release `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:PublishReadyToRun=false `
    /p:PublishTrimmed=false `
    /p:PublishDir="$PublishDir\\"

Write-Host ""
Write-Host "Publicacao concluida em: $PublishDir"
