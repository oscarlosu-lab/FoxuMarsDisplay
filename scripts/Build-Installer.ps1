$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$appProj = Join-Path $root "src\FoxuMarsDisplay\FoxuMarsDisplay.csproj"
$launcherProj = Join-Path $root "src\FoxuMarsDisplay.Launcher\FoxuMarsDisplay.Launcher.csproj"
$setupProj = Join-Path $root "src\FoxuMarsDisplay.Setup\FoxuMarsDisplay.Setup.csproj"

$stage = Join-Path $env:TEMP ("FoxuMarsStage_" + [guid]::NewGuid().ToString("N"))
$payloadZip = Join-Path $root "src\FoxuMarsDisplay.Setup\payload.zip"
$distDir = Join-Path $root "dist"

try {
    $appOut = Join-Path $stage "app"
    $launcherOut = Join-Path $stage "launcher"

    Write-Host "[1/4] Building FoxuMarsDisplay + Launcher (Release)..." -ForegroundColor Cyan
    dotnet build $appProj -c Release -o $appOut | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "FoxuMarsDisplay build failed" }
    dotnet build $launcherProj -c Release -o $launcherOut | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "FoxuMarsDisplay.Launcher build failed" }

    Write-Host "[2/4] Staging payload..." -ForegroundColor Cyan
    $payloadDir = Join-Path $stage "payload"
    New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

    Copy-Item "$appOut\*" $payloadDir -Recurse -Force
    Copy-Item "$launcherOut\FoxuMarsDisplay.Launcher.*" $payloadDir -Force

    $stageNative = Join-Path $payloadDir "native\x64"
    $vendorNative = Join-Path $root "vendor\native\x64"
    if (Test-Path $vendorNative) {
        New-Item -ItemType Directory -Path $stageNative -Force | Out-Null
        Copy-Item "$vendorNative\*.dll" $stageNative -Force
    }
    if (!(Test-Path (Join-Path $stageNative "MSDISPLAYSDKWRRAPER.dll"))) {
        throw "MSDISPLAYSDKWRRAPER.dll missing from $stageNative"
    }

    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force }
    Compress-Archive -Path "$payloadDir\*" -DestinationPath $payloadZip -CompressionLevel Optimal

    Write-Host "[3/4] Publishing single-file FoxuMarsDisplay_Setup.exe..." -ForegroundColor Cyan
    $setupOut = Join-Path $stage "setup_pub"
    dotnet publish $setupProj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o $setupOut | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "FoxuMarsDisplay.Setup publish failed" }

    Write-Host "[4/4] Copying installer to dist\FoxuMarsDisplay_Setup.exe..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    $finalExe = Join-Path $distDir "FoxuMarsDisplay_Setup.exe"
    Copy-Item (Join-Path $setupOut "FoxuMarsDisplay_Setup.exe") $finalExe -Force

    $lenMb = [math]::Round((Get-Item $finalExe).Length / 1MB, 2)
    Write-Host "OK: $finalExe ($lenMb MB)" -ForegroundColor Green
}
finally {
    if (Test-Path $payloadZip) { Remove-Item $payloadZip -Force -ErrorAction SilentlyContinue }
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
}
