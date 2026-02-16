<#
.SYNOPSIS
    Build script for Oahu on Windows.

.DESCRIPTION
    Publishes the cross-platform Avalonia application
    (Connect.app.avalonia.core) as a self-contained deployment for Windows
    and optionally creates an Inno Setup installer.

.PARAMETER Configuration
    Build configuration (Release or Debug). Default: Release.

.PARAMETER Runtime
    Target runtime identifier. Default: win-x64.
    Common values: win-x64, win-arm64.

.PARAMETER OutputDir
    Directory for build artifacts. Default: ./artifacts.

.PARAMETER SelfContained
    Publish as a self-contained application (bundles .NET runtime).
    Default: $true.

.PARAMETER SingleFile
    Publish as a single-file executable. Default: $false.

.PARAMETER CreateInstaller
    Run Inno Setup to create an installer after publishing. Requires
    Inno Setup 6 to be installed (iscc.exe on PATH or in the default
    install location).

.PARAMETER SigningCertThumbprint
    Thumbprint of a code-signing certificate in the Windows certificate
    store. If provided, the published executable and installer will be
    signed with signtool.

.PARAMETER TimestampServer
    RFC 3161 timestamp server URL used during code signing.
    Default: http://timestamp.digicert.com.

.EXAMPLE
    .\build-windows.ps1
    # Publishes the Avalonia app as a Release build for win-x64 to ./artifacts.

.EXAMPLE
    .\build-windows.ps1 -Configuration Debug -Runtime win-arm64
    # Debug build targeting ARM64.

.EXAMPLE
    .\build-windows.ps1 -CreateInstaller
    # Publishes and creates an Inno Setup installer.

.EXAMPLE
    .\build-windows.ps1 -CreateInstaller -SigningCertThumbprint "AABBCCDD..."
    # Publishes, signs the exe, creates and signs the installer.
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [string]$Runtime = "win-x64",

    [string]$OutputDir = "./artifacts",

    [bool]$SelfContained = $true,

    [switch]$SingleFile,

    [switch]$CreateInstaller,

    [string]$SigningCertThumbprint,

    [string]$TimestampServer = "http://timestamp.digicert.com"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$RepoRoot  = (Resolve-Path "$ScriptDir/..").Path
$SrcDir    = Join-Path $RepoRoot "src"

$Project    = Join-Path $SrcDir "Connect.app.avalonia.core/Connect.app.avalonia.core.csproj"
$ProjectDir = "Connect.app.avalonia.core"
$AppLabel   = "Avalonia"

$PublishDir = Join-Path $OutputDir "publish"

if (-not (Test-Path $Project)) {
    Write-Error "Project file not found: $Project"
    exit 1
}

# ---------------------------------------------------------------------------
# Version
# ---------------------------------------------------------------------------
Write-Host "=== Oahu Windows Build ($AppLabel) ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Runtime:       $Runtime"
Write-Host "Output:        $OutputDir"

# Restore dotnet local tools (Nerdbank.GitVersioning)
dotnet tool restore | Out-Null

$AppVersion = $null
try {
    $AppVersion = dotnet nbgv get-version -v SimpleVersion --project (Join-Path $SrcDir $ProjectDir) 2>$null
    if ($LASTEXITCODE -ne 0) { $AppVersion = $null }
} catch {
    $AppVersion = $null
}

if (-not $AppVersion) {
    # Fallback: read base version from version.json
    $VersionJson = Get-Content (Join-Path $RepoRoot "version.json") -Raw | ConvertFrom-Json
    $AppVersion  = "$($VersionJson.version).0"
    Write-Host "  (using fallback version from version.json)"
}

Write-Host "Version:       $AppVersion"
Write-Host ""

# ---------------------------------------------------------------------------
# Clean
# ---------------------------------------------------------------------------
if (Test-Path $OutputDir) {
    Write-Host "==> Cleaning previous output..."
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# ---------------------------------------------------------------------------
# Publish
# ---------------------------------------------------------------------------
Write-Host "==> Publishing..." -ForegroundColor Yellow

$dotnetArgs = @(
    "publish", $Project,
    "--configuration", $Configuration,
    "--runtime", $Runtime,
    "--self-contained", ($SelfContained ? "true" : "false"),
    "-p:PublishSingleFile=$($SingleFile.IsPresent)",
    "-p:PublishTrimmed=false",
    "--output", $PublishDir
)

& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

Write-Host "  Published to: $PublishDir" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Code signing (published binaries)
# ---------------------------------------------------------------------------
if ($SigningCertThumbprint) {
    Write-Host "==> Signing published executable..." -ForegroundColor Yellow

    $exePath = Join-Path $PublishDir "Oahu.exe"
    if (-not (Test-Path $exePath)) {
        Write-Error "Executable not found at $exePath"
        exit 1
    }

    # Locate signtool
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty Source
    if (-not $signtool) {
        # Try the Windows SDK default locations
        $sdkPaths = @(
            "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe",
            "${env:ProgramFiles}\Windows Kits\10\bin\*\x64\signtool.exe"
        )
        foreach ($pattern in $sdkPaths) {
            $found = Get-Item $pattern -ErrorAction SilentlyContinue |
                     Sort-Object FullName -Descending |
                     Select-Object -First 1
            if ($found) { $signtool = $found.FullName; break }
        }
    }

    if (-not $signtool) {
        Write-Error "signtool.exe not found. Install the Windows SDK or add signtool to PATH."
        exit 1
    }

    & $signtool sign /sha1 $SigningCertThumbprint /fd sha256 /tr $TimestampServer /td sha256 $exePath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Code signing failed."
        exit $LASTEXITCODE
    }
    Write-Host "  Signed: $exePath" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# Inno Setup installer
# ---------------------------------------------------------------------------
if ($CreateInstaller) {
    Write-Host "==> Creating installer with Inno Setup..." -ForegroundColor Yellow

    # Locate iscc.exe
    $iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty Source
    if (-not $iscc) {
        $defaultPaths = @(
            "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
        )
        foreach ($p in $defaultPaths) {
            if (Test-Path $p) { $iscc = $p; break }
        }
    }

    if (-not $iscc) {
        Write-Warning "Inno Setup (iscc.exe) not found. Skipping installer creation."
        Write-Warning "Install Inno Setup 6 from https://jrsoftware.org/isdl.php"
    } else {
        $issFile = Join-Path $SrcDir "InnoSetup/Oahu setup.iss"
        if (-not (Test-Path $issFile)) {
            Write-Error "Inno Setup script not found: $issFile"
            exit 1
        }

        # Pass version and source dir as defines so the .iss can be reused
        # without manual edits.
        $installerOutput = Join-Path (Resolve-Path $OutputDir).Path "installer"
        New-Item -ItemType Directory -Path $installerOutput -Force | Out-Null

        # Extract architecture from runtime (e.g. win-x64 -> x64, win-arm64 -> arm64)
        $Arch = $Runtime -replace '^win-', ''

        & $iscc $issFile `
            /DMyAppVersion="$AppVersion" `
            /DMySourceDir="$(Resolve-Path $PublishDir)" `
            /DMyArchitecture="$Arch" `
            /O"$installerOutput"

        if ($LASTEXITCODE -ne 0) {
            Write-Error "Inno Setup failed with exit code $LASTEXITCODE."
            exit $LASTEXITCODE
        }

        $installerExe = Get-ChildItem $installerOutput -Filter "*.exe" |
                        Select-Object -First 1
        if ($installerExe) {
            Write-Host "  Installer: $($installerExe.FullName)" -ForegroundColor Green

            # Sign the installer
            if ($SigningCertThumbprint -and $signtool) {
                Write-Host "  Signing installer..."
                & $signtool sign /sha1 $SigningCertThumbprint /fd sha256 /tr $TimestampServer /td sha256 $installerExe.FullName
                if ($LASTEXITCODE -ne 0) {
                    Write-Warning "Installer signing failed."
                }
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=== Build complete ===" -ForegroundColor Cyan
Write-Host "Artifacts:"
Write-Host "  Published output: $PublishDir"
if ($CreateInstaller -and (Test-Path (Join-Path $OutputDir "installer"))) {
    $installerFile = Get-ChildItem (Join-Path $OutputDir "installer") -Filter "*.exe" |
                     Select-Object -First 1
    if ($installerFile) {
        Write-Host "  Installer:        $($installerFile.FullName)"
    }
}
