<#
  Manual performance check before a release (DT-227): the NFR-01 test — one company with 500,000 trips, the Created Trips list (first
  page, filters, totals, search) answers in under 1 second. It is an explicit test, so the normal `dotnet test` run and CI leave it out;
  this script runs only the explicit tests of the API test project, in Release, on its own database (DirectServiceMaster_PerfTests,
  recreated on every run), so the 500,000 trips never reach the database of the normal API tests.

  Run on a quiet machine (close Visual Studio, browsers and dev servers): the result is a timing.
    powershell -ExecutionPolicy Bypass -File tools/perf/run-performance-check.ps1
    powershell -ExecutionPolicy Bypass -File tools/perf/run-performance-check.ps1 -ConnectionString "Server=...;Database=DirectServiceMaster_PerfTests;..."
  Exit code 0 = passed. See docs/step9/Setup_and_Run.md §8.
#>
param(
    [string]$ConnectionString = "Server=localhost;Database=DirectServiceMaster_PerfTests;Integrated Security=true;TrustServerCertificate=true;Encrypt=true",
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $root
try {
    # Frees the memory held by idle compiler servers (SQL Server needs it for the 500,000-row seeding).
    dotnet build-server shutdown | Out-Null
    if (-not $NoBuild) {
        dotnet build tests/DirectService.Transportation.Api.Tests -c Release
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $env:DST_API_TEST_DB = $ConnectionString
    dotnet test --project tests/DirectService.Transportation.Api.Tests -c Release --no-build --explicit only --output Detailed
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:DST_API_TEST_DB -ErrorAction SilentlyContinue
    Pop-Location
}
