#!/usr/bin/env bash
# Manual performance check before a release (DT-227) — the same as run-performance-check.ps1, for Linux / macOS / Git Bash.
# Runs only the explicit tests of the API test project (the NFR-01 500,000-trip, 1-second test), in Release, on its own database
# (DirectServiceMaster_PerfTests, recreated on every run). The normal `dotnet test` run and CI leave it out.
#   tools/perf/run-performance-check.sh                     # local SQL Server, Windows authentication
#   DST_API_TEST_DB="Server=…;Database=DirectServiceMaster_PerfTests;User Id=…;Password=…;TrustServerCertificate=true" tools/perf/run-performance-check.sh
# Exit code 0 = passed. See docs/step9/Setup_and_Run.md §8.
set -euo pipefail
cd "$(dirname "$0")/../.."
export DST_API_TEST_DB="${DST_API_TEST_DB:-Server=localhost;Database=DirectServiceMaster_PerfTests;Integrated Security=true;TrustServerCertificate=true;Encrypt=true}"
dotnet build-server shutdown > /dev/null
[ "${NO_BUILD:-}" = "1" ] || dotnet build tests/DirectService.Transportation.Api.Tests -c Release
dotnet test --project tests/DirectService.Transportation.Api.Tests -c Release --no-build --explicit only --output Detailed
