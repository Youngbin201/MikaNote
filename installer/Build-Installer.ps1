param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "MikaNote.App\MikaNote.App.csproj"
$publishDir = Join-Path $PSScriptRoot "publish"
$redistDir = Join-Path $PSScriptRoot "redist"
$issFile = Join-Path $PSScriptRoot "MikaNote.iss"
$releaseOutput = Join-Path $repoRoot "MikaNote.App\bin\$Configuration\net8.0-windows"
$desktopRuntimeVersion = "8.0.25"
$desktopRuntimeFileName = "windowsdesktop-runtime-$desktopRuntimeVersion-win-x64.exe"
$desktopRuntimeUrl = "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$desktopRuntimeVersion/$desktopRuntimeFileName"
$desktopRuntimePath = Join-Path $redistDir $desktopRuntimeFileName

Write-Host "Preparing MikaNote installer files..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (-not (Test-Path $redistDir)) {
    New-Item -ItemType Directory -Path $redistDir | Out-Null
}

if (-not (Test-Path $desktopRuntimePath)) {
    Write-Host "Downloading .NET Desktop Runtime $desktopRuntimeVersion..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $desktopRuntimeUrl -OutFile $desktopRuntimePath
}

if ($SelfContained) {
    Write-Host "Publishing self-contained build ($Configuration / $Runtime)..." -ForegroundColor Cyan
    dotnet publish $appProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        --no-restore `
        /p:PublishSingleFile=false `
        /p:DebugType=None `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-Host "Building framework-dependent release ($Configuration)..." -ForegroundColor Cyan
    dotnet build $appProject -c $Configuration

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE"
    }

    New-Item -ItemType Directory -Path $publishDir | Out-Null
    Copy-Item (Join-Path $releaseOutput "*") $publishDir -Recurse -Force
}

$iscc = $null
$candidateLocal = Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"
$candidateProgramFilesX86 = if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe" } else { $null }
$candidateProgramFiles = if ($env:ProgramFiles) { Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe" } else { $null }

if ($candidateLocal -and (Test-Path $candidateLocal)) {
    $iscc = $candidateLocal
}
elseif ($candidateProgramFilesX86 -and (Test-Path $candidateProgramFilesX86)) {
    $iscc = $candidateProgramFilesX86
}
elseif ($candidateProgramFiles -and (Test-Path $candidateProgramFiles)) {
    $iscc = $candidateProgramFiles
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "Installer files are ready." -ForegroundColor Green
    Write-Host "Inno Setup was not found. To build the installer, open:" -ForegroundColor Yellow
    Write-Host "  $issFile"
    exit 0
}
Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
& "$iscc" "$issFile"

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Installer build complete." -ForegroundColor Green
