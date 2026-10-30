param(
    [switch]$Test,
    [switch]$LiveTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$frameworkName = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
$framework = Join-Path $env:WINDIR "Microsoft.NET\$frameworkName\v4.0.30319"
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x C# compiler was not found. Enable .NET Framework in Windows Features.'
}

$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.Net.Http.dll',
    'System.Web.Extensions.dll'
) | ForEach-Object { "/reference:$(Join-Path $framework $_)" }

$appPath = Join-Path $PSScriptRoot 'GoldWidget.exe'
$clientSource = Join-Path $PSScriptRoot 'QuoteClient.cs'
$appSource = Join-Path $PSScriptRoot 'GoldWidget.cs'
$holdingsSource = Join-Path $PSScriptRoot 'Holdings.cs'
& $compiler /nologo /target:winexe /optimize+ /warnaserror+ "/out:$appPath" @references $clientSource $holdingsSource $appSource
if ($LASTEXITCODE -ne 0) { throw 'Widget compilation failed.' }
Write-Output "Built: $appPath"

if ($Test -or $LiveTest) {
    $testPath = Join-Path $PSScriptRoot 'tests\QuoteTests.exe'
    $testSource = Join-Path $PSScriptRoot 'tests\QuoteTests.cs'
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ "/out:$testPath" @references $clientSource $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if ($LiveTest) {
        & $testPath --live
    } else {
        & $testPath
    }
    if ($LASTEXITCODE -ne 0) { throw 'Quote tests failed.' }

    $widgetTestPath = Join-Path $PSScriptRoot 'tests\WidgetTests.exe'
    $widgetTestSource = Join-Path $PSScriptRoot 'tests\WidgetTests.cs'
    & $compiler /nologo /target:exe /main:WidgetTests /optimize+ /warnaserror+ "/out:$widgetTestPath" @references $clientSource $holdingsSource $appSource $widgetTestSource
    if ($LASTEXITCODE -ne 0) { throw 'Widget test compilation failed.' }
    & $widgetTestPath
    if ($LASTEXITCODE -ne 0) { throw 'Widget presentation tests failed.' }

    $holdingsTestPath = Join-Path $PSScriptRoot 'tests\HoldingsTests.exe'
    $holdingsTestSource = Join-Path $PSScriptRoot 'tests\HoldingsTests.cs'
    & $compiler /nologo /target:exe /optimize+ /warnaserror+ "/out:$holdingsTestPath" @references $holdingsSource $holdingsTestSource
    if ($LASTEXITCODE -ne 0) { throw 'Holdings test compilation failed.' }
    & $holdingsTestPath
    if ($LASTEXITCODE -ne 0) { throw 'Holdings persistence tests failed.' }
}
