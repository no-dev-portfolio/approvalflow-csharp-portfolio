"""Run the Compose verification with a unique evidence directory (Python 3)."""
import argparse, datetime, json, os, pathlib, subprocess, sys, uuid
ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument("--env-file", default=str(ROOT / ".env"))
parser.add_argument("--project-name", default="approvalflow-verification")
parser.add_argument("--nuget-cache", help="Optional existing Docker volume for faster package restore")
args = parser.parse_args()
run_id = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
folder = ROOT / "evidence" / "runs" / run_id
folder.mkdir(parents=True)
compose = ["docker", "compose", "--project-directory", str(ROOT), "--env-file", str(pathlib.Path(args.env_file).resolve()), "-p", args.project_name]
steps = []
def now(): return datetime.datetime.now(datetime.timezone.utc).isoformat()
def run(name, command):
    start = now()
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
    (folder / (name + ".stdout.log")).write_text(result.stdout, encoding="utf-8")
    (folder / (name + ".stderr.log")).write_text(result.stderr, encoding="utf-8")
    meta = dict(run_id=run_id, started_at=start, ended_at=now(), exit_code=result.returncode, command=[str(x).replace(str(ROOT), "$PROJECT").replace(str(pathlib.Path(args.env_file).resolve()), "$ENV_FILE") for x in command])
    (folder / (name + ".meta.json")).write_text(json.dumps(meta, indent=2), encoding="utf-8")
    steps.append(meta)
    if result.returncode: raise RuntimeError(name + " failed; see " + str(folder))
    return result.stdout
outcome = "failed"
try:
    run("start-db", compose + ["up", "-d", "db"])
    command = compose + ["--profile", "test", "run", "--rm", "-e", "VERIFY_RUN_ID=" + run_id]
    if args.nuget_cache: command += ["-v", args.nuget_cache + ":/root/.nuget/packages"]
    command += ["tests", "sh", "scripts/verify-dotnet.sh"]
    run("dotnet", command)
    run("audit-gate", [sys.executable, "scripts/check-audit.py", str(folder / "dependency-audit.json")])
    run("frontend", compose + ["--profile", "test", "run", "--rm", "frontend"])
    prefix = "ApprovalFlowTest_" + run_id + "_"
    sql = "SET NOCOUNT ON; SELECT SYSUTCDATETIME() AS observed_at_utc; SELECT COUNT(*) AS remaining_test_databases FROM sys.databases WHERE LEFT(name," + str(len(prefix)) + ") = N'" + prefix + "'; SELECT name FROM sys.databases WHERE LEFT(name," + str(len(prefix)) + ") = N'" + prefix + "'; IF EXISTS(SELECT 1 FROM sys.databases WHERE LEFT(name," + str(len(prefix)) + ") = N'" + prefix + "') THROW 51001, 'Test databases remain', 1;"
    run("database-cleanup", compose + ["exec", "-T", "db", "sh", "-c", 'exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -b -W -Q "$1"', "sh", sql])
    outcome = "passed"
except Exception as error:
    outcome = "failed"
    print(str(error), file=sys.stderr)
finally:
    (folder / "run.json").write_text(json.dumps(dict(run_id=run_id, outcome=outcome, steps=steps), indent=2), encoding="utf-8")
    print(str(folder))
if outcome != "passed": sys.exit(1)
