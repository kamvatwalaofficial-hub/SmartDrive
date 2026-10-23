#!/usr/bin/env bash
# Runs an xUnit v3 test project and turns each failure into a GitHub annotation, so failures are
# readable from the checks API without downloading logs.
set -uo pipefail
project="$1"
dotnet run --project "$project" -c Release --no-build -- -noColor 2>&1 | tee test.log
status=${PIPESTATUS[0]}
awk '
  /\[FAIL\]/ { if (buf != "") print buf; buf = $0; n = 0; next }
  buf != "" && n < 14 { gsub(/^[ \t]+/, ""); buf = buf " %0A" $0; n++ }
  END { if (buf != "") print buf }
' test.log | head -40 | while IFS= read -r line; do
  echo "::error title=Test failure::${line}"
done
exit $status
