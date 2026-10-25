#!/usr/bin/env python3
"""Validate real CLI exports against JSON v1 and the vendored OASIS SARIF schema.

Requires Python 3.10+, jsonschema==4.26.0 and a Release build of the CLI.
Uses isolated package-free inputs; no repository fixtures or user files are changed.
"""

import json
import os
from pathlib import Path
import subprocess
import tempfile

from jsonschema import Draft4Validator, Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll"
SCHEMA = json.loads((ROOT / "docs/schema/report.v1.schema.json").read_text(encoding="utf-8"))
SARIF = json.loads((ROOT / "docs/schema/sarif-2.1.0.schema.json").read_text(encoding="utf-8"))
Draft202012Validator.check_schema(SCHEMA)
Draft4Validator.check_schema(SARIF)
JSON_VALIDATOR = Draft202012Validator(SCHEMA, format_checker=FormatChecker())
SARIF_VALIDATOR = Draft4Validator(SARIF, format_checker=FormatChecker())


def run(*arguments, expected=0):
    result = subprocess.run(
        ["dotnet", *map(str, arguments)], cwd=ROOT, capture_output=True,
        text=True, encoding="utf-8", timeout=120, check=False,
    )
    if result.returncode != expected:
        raise AssertionError(f"Expected exit {expected}, got {result.returncode}: {result.stdout}\n{result.stderr}")
    return result


def validate(text, format_name):
    report = json.loads(text)
    if format_name == "sarif":
        SARIF_VALIDATOR.validate(report)
        run_data = report["runs"][0]
        envelope = dict(run_data["properties"])
        envelope["rules"] = [dict(id=rule["id"], title=rule["shortDescription"]["text"],
                                  documentationUrl=rule["helpUri"])
                             for rule in run_data["tool"]["driver"]["rules"]]
        envelope["diagnostics"] = [result["properties"] for result in run_data["results"]
                                   if result.get("baselineState") != "absent"]
        envelope["resolved"] = [result["properties"] for result in run_data["results"]
                                if result.get("baselineState") == "absent"]
    else:
        envelope = report
    JSON_VALIDATOR.validate(envelope)
    return envelope


def main():
    with tempfile.TemporaryDirectory(prefix="archguard-report-schema-") as temporary:
        directory = Path(temporary)
        (directory / "global.json").write_bytes((ROOT / "global.json").read_bytes())
        (directory / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        for project in ("App", "Library"):
            folder = directory / project
            folder.mkdir()
            (folder / f"{project}.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '</PropertyGroup>' + ('<ItemGroup><ProjectReference Include="../Library/Library.csproj" />'
                '</ItemGroup>' if project == "App" else '') + '</Project>', encoding="utf-8")
        source = directory / ("App/A #é%.cs" if os.name == "nt" else "App/A #é%:Part.cs")
        source.write_text("public class Consumer : Library.Target { }", encoding="utf-8")
        (directory / "Library/Target.cs").write_text("namespace Library; public class Target { }", encoding="utf-8")
        input_path = directory / "Input.slnx"
        input_path.write_text('<Solution><Project Path="App/App.csproj" /><Project Path="Library/Library.csproj" /></Solution>', encoding="utf-8")
        policy = directory / "policy.yaml"
        policy_text = '''version: 1
modules:
  - id: App
    projects: ["App/*.csproj"]
  - id: Library
    projects: ["Library/*.csproj"]
layers:
  - id: All
    projects: ["**/*.csproj"]
rules:
  AG001: info
'''
        policy.write_text(policy_text, encoding="utf-8")
        run("restore", input_path, "--source", directory, "--disable-parallel", "--disable-build-servers")
        baseline = directory / "baseline.json"
        common = [input_path, "--policy", policy, "--trusted"]
        run(CLI, "baseline", "update", *common, "--baseline", baseline)
        for format_name in ("json", "sarif"):
            arguments = [CLI, "scan", *common, "--format", format_name]
            first = run(*arguments)
            envelope = validate(first.stdout, format_name)
            assert len(envelope["diagnostics"]) == 1
            assert envelope["diagnostics"][0]["severity"] == "info"
            assert run(*arguments).stdout == first.stdout
            accepted = validate(run(*arguments, "--baseline", baseline).stdout, format_name)
            assert accepted["diagnostics"][0]["baselineState"] == "existing"
            path = directory / "report.out"
            written = run(*arguments, "--output", path)
            assert not written.stdout
            assert path.read_bytes() == first.stdout.encode("utf-8")
            failed = run(*arguments, "--output", directory / "missing/report", expected=2)
            assert not failed.stdout and failed.stderr
            assert path.read_bytes() == first.stdout.encode("utf-8")

            # Gate-failing errors are still completed, successfully executed SARIF runs.
            policy.write_text(policy_text.replace("AG001: info", "AG001: error"), encoding="utf-8")
            failed_gate = validate(run(*arguments, expected=1).stdout, format_name)
            assert failed_gate["status"] == "completed" and failed_gate["gateOutcome"] == "failed"
            source.write_text("public class Consumer : Library.Target { } class Broken : Missing { }", encoding="utf-8")
            partial = validate(run(*arguments, "--baseline", baseline, expected=2).stdout, format_name)
            assert partial["status"] == "incomplete" and partial["gateOutcome"] == "notEvaluated"
            assert partial["diagnostics"] and partial["issues"] and not partial["resolved"]
            source.write_text("public class Consumer : Library.Target { }", encoding="utf-8")
            policy.write_text(policy_text.replace('  - id: App\n', '  - id: App\n    allowedDependencies: ["module:Library"]\n'), encoding="utf-8")
            clean = validate(run(*arguments, "--baseline", baseline).stdout, format_name)
            assert not clean["diagnostics"] and len(clean["resolved"]) == 1
            policy.write_text(policy_text, encoding="utf-8")
    print("JSON v1, SARIF 2.1.0 and versioned SARIF properties validated against schemas.")


if __name__ == "__main__":
    main()
