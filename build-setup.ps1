param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "TemizlikMasaUygulamasi.csproj"
$publishDir = Join-Path $root "publish\win-x64"
$issFile = Join-Path $root "installer\TemizlikMasaUygulamasi.iss"

Write-Host "[1/3] Self-contained publish aliniyor..."
dotnet publish $project -c $Configuration -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:PublishTrimmed=false -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish basarisiz oldu."
}

$possibleIscc = @(
    (
        @(
            (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
            "C:\Users\vedat\AppData\Local\Programs\Inno Setup 6\ISCC.exe",
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe"
        ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique
    )
)

if (-not $possibleIscc) {
    throw "ISCC bulunamadi. Inno Setup 6 kurulu olmali."
}

$iscc = $possibleIscc[0]
Write-Host "[2/3] Setup derleyicisi bulundu: $iscc"

Write-Host "[3/3] Setup dosyasi olusturuluyor..."
& $iscc $issFile
if ($LASTEXITCODE -ne 0) {
    throw "ISCC setup derleme adimi basarisiz oldu."
}

Write-Host "Tamamlandi. Setup ciktilari artifacts\\setup klasorunde."
