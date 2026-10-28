import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    data = json.load(f)
if data.get("problems") or not data.get("projects"):
    raise SystemExit("Dependency audit did not complete")
vulnerable = [p for project in data.get("projects", []) for framework in project.get("frameworks", []) for key in ("topLevelPackages", "transitivePackages") for p in framework.get(key, []) if p.get("vulnerabilities")]
if vulnerable:
    raise SystemExit("Vulnerable dependencies: " + ", ".join(p["id"] for p in vulnerable))
print("Dependency audit: no known vulnerable packages reported")
