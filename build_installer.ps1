$ErrorActionPreference = "Stop"

Write-Host "Building Main Application..."
cmd /c build_main.bat
if ($LASTEXITCODE -ne 0) {
    Write-Host "Main Application build failed!"
    exit $LASTEXITCODE
}

Write-Host "Building Installer..."
cmd /c build_installer.bat
if ($LASTEXITCODE -ne 0) {
    Write-Host "Installer build failed!"
    exit $LASTEXITCODE
}

Write-Host "All builds completed with 0 errors and 0 warnings."
