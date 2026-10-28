#!/bin/sh
set -eu
run_id=${VERIFY_RUN_ID:-$(date -u +%Y%m%dT%H%M%SZ)-$$}
case "$run_id" in *[!A-Za-z0-9_-]*|'') exit 2;; esac
export VERIFY_RUN_ID="$run_id"
root="evidence/runs/$run_id"
mkdir -p "$root"
step() {
    key=$1; shift
    printf '%s\n' "$@" > "$root/$key.argv.txt"
    start=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    set +e
    "$@" > "$root/$key.stdout.log" 2> "$root/$key.stderr.log"
    result=$?
    set -e
    end=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    printf '{"run_id":"%s","started_at":"%s","ended_at":"%s","exit_code":%s}\n' "$run_id" "$start" "$end" "$result" > "$root/$key.meta.json"
    cat "$root/$key.stdout.log" "$root/$key.stderr.log"
    test "$result" -eq 0
}
step restore dotnet restore tests/ApprovalFlow.Tests --locked-mode
step format-source dotnet format src/ApprovalFlow/ApprovalFlow.csproj --verify-no-changes --no-restore --verbosity diagnostic
step format-tests dotnet format tests/ApprovalFlow.Tests/ApprovalFlow.Tests.csproj --verify-no-changes --no-restore --verbosity diagnostic
step integration dotnet test tests/ApprovalFlow.Tests -c Release --no-restore --results-directory "$root" --logger "trx;LogFileName=integration-$run_id.trx"
step dependencies dotnet list tests/ApprovalFlow.Tests package --include-transitive --format json
step dependency-audit dotnet list tests/ApprovalFlow.Tests package --vulnerable --include-transitive --format json
cp "$root/dependencies.stdout.log" "$root/dependencies.json"
cp "$root/dependency-audit.stdout.log" "$root/dependency-audit.json"
