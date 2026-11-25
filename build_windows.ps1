param(
    [string]$Dotnet = "",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ProjectDir

if (-not $Dotnet) {
    $LocalDotnet = Join-Path $ProjectDir ".dotnet-sdk\dotnet.exe"
    if (Test-Path -LiteralPath $LocalDotnet) { $Dotnet = $LocalDotnet }
    elseif (Get-Command dotnet -ErrorAction SilentlyContinue) { $Dotnet = "dotnet" }
}
if (-not $Dotnet) { throw "The .NET 8 SDK was not found. Install it or pass -Dotnet with the SDK path." }

if (-not $InnoCompiler) {
    $LocalInno = Join-Path $ProjectDir ".build-tools\Inno\ISCC.exe"
    $SystemInno = Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"
    if (Test-Path -LiteralPath $LocalInno) { $InnoCompiler = $LocalInno }
    elseif (Test-Path -LiteralPath $SystemInno) { $InnoCompiler = $SystemInno }
}
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw "Inno Setup 6 was not found. Install it or pass -InnoCompiler." }

& $Dotnet test "Empiro.IpScanner.sln" --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw "The test suite failed." }

$PublishDir = Join-Path $ProjectDir "dist\publish"
& $Dotnet publish "src\Empiro.IpScanner.Wpf\Empiro.IpScanner.Wpf.csproj" --configuration Release --runtime win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    --output $PublishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "The WPF application could not be published." }

$PublishedExe = Join-Path $PublishDir "Empiro-IP-Scanner.exe"
if (-not (Test-Path -LiteralPath $PublishedExe)) { throw "The published Empiro executable was not found." }
Copy-Item -LiteralPath $PublishedExe -Destination (Join-Path $ProjectDir "dist\Empiro-IP-Scanner.exe") -Force
& $InnoCompiler "EmpiroIPScanner.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed to create the installer." }

Write-Host "Self-contained app: $ProjectDir\dist\Empiro-IP-Scanner.exe"
$VersionLine = Select-String -Path "EmpiroIPScanner.iss" -Pattern '^#define AppVersion "([^"]+)"' | Select-Object -First 1
$AppVersion = $VersionLine.Matches[0].Groups[1].Value
Write-Host "Installer: $ProjectDir\dist\Empiro-IP-Scanner-Setup-$AppVersion-x64.exe"
