#!/usr/bin/env python3
"""Validate batched serializer samples and real CLI exports against report schemas.

Requires Python 3.10+, jsonschema==4.26.0 and a Release build of the solution (including report samples).
Uses isolated package-free inputs; no repository fixtures or user files are changed.
"""

import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

from jsonschema import Draft4Validator, Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll"
SAMPLES = ROOT / "tests/Fixtures/Reports/bin/Release/net10.0/ArchitectureGuardian.ReportSamples.dll"
SCHEMA = json.loads((ROOT / "docs/schema/report.v1.schema.json").read_text(encoding="utf-8"))
SARIF = json.loads((ROOT / "docs/schema/sarif-2.1.0.schema.json").read_text(encoding="utf-8"))
Draft202012Validator.check_schema(SCHEMA)
Draft4Validator.check_schema(SARIF)
JSON_VALIDATOR = Draft202012Validator(SCHEMA, format_checker=FormatChecker())
SARIF_VALIDATOR = Draft4Validator(SARIF, format_checker=FormatChecker())


def run(*arguments, expected=0, label=None):
    started = time.monotonic()
    result = subprocess.run(
        ["dotnet", *map(str, arguments)], cwd=ROOT, capture_output=True,
        text=True, encoding="utf-8", timeout=120, check=False,
    )
    if result.returncode != expected:
        raise AssertionError(f"Expected exit {expected}, got {result.returncode}: {result.stdout}\n{result.stderr}")
    if label:
        print(f"{label}: {time.monotonic() - started:.2f}s", flush=True)
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


def validate_samples(directory):
    samples = directory / "samples"
    run(SAMPLES, samples, label="Generate serialization samples")
    # Exact inventory prevents a missing generator case from silently reducing schema coverage.
    cases = {
        "clean": ("completed", "passed", 0, 0, False),
        "findings": ("completed", "passed", 2, 0, False),
        "gate-failed": ("completed", "failed", 1, 0, False),
        "accepted": ("completed", "passed", 1, 1, True),
        "baseline": ("completed", "failed", 2, 1, True),
        "resolved": ("completed", "passed", 0, 2, True),
        "reference": ("completed", "failed", 1, 0, False),
        "incomplete": ("incomplete", "notEvaluated", 2, 0, True),
        "failed": ("failed", "notEvaluated", 1, 0, True),
        "cancelled": ("cancelled", "notEvaluated", 1, 0, True),
    }
    assert {path.name for path in samples.iterdir()} == {
        f"{case}.{format_name}" for case in cases for format_name in ("json", "sarif")
    }
    for case, (status, gate, diagnostics, resolved, compared) in cases.items():
        for format_name in ("json", "sarif"):
            data = (samples / f"{case}.{format_name}").read_bytes()
            assert not data.startswith(b"\xef\xbb\xbf") and b"\r" not in data
            envelope = validate(data.decode("utf-8"), format_name)
            assert envelope["status"] == status and envelope["gateOutcome"] == gate
            assert len(envelope["diagnostics"]) == diagnostics and len(envelope["resolved"]) == resolved
            assert envelope["baselineApplied"] == compared
            if case == "baseline":
                assert {item["target"]["symbol"]: item["baselineState"] for item in envelope["diagnostics"]} == {
                    "Existing": "existing", "New": "new"
                }
            if case == "accepted":
                assert envelope["diagnostics"][0]["baselineState"] == "existing"
                assert envelope["diagnostics"][0]["severity"] == "error"
            if case == "findings":
                assert {item["severity"] for item in envelope["diagnostics"]} == {"info", "warning"}
            if case == "reference":
                assert envelope["diagnostics"][0]["location"] is None
                assert all(item["location"] is None for item in envelope["diagnostics"][0]["evidence"])
            if case in ("incomplete", "failed"):
                assert envelope["issues"]
    print("Validated 20 serialization samples against JSON, SARIF and properties schemas.", flush=True)


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
        run("restore", input_path, "--source", directory, "--disable-parallel", "--disable-build-servers", label="Restore CLI fixture")
        validate_samples(directory)
        common = [input_path, "--policy", policy, "--trusted"]
        for format_name in ("json", "sarif"):
            arguments = [CLI, "scan", *common, "--format", format_name]
            first = run(*arguments, label=f"CLI {format_name} stdout")
            envelope = validate(first.stdout, format_name)
            assert envelope["status"] == "completed" and envelope["gateOutcome"] == "passed"
            assert len(envelope["diagnostics"]) == 1
            assert envelope["diagnostics"][0]["severity"] == "info"
            path = directory / "report.out"
            written = run(*arguments, "--output", path, label=f"CLI {format_name} file")
            assert not written.stdout
            data = path.read_bytes()
            assert data == first.stdout.encode("utf-8")
            validate(data.decode("utf-8"), format_name)
    print("JSON v1, SARIF 2.1.0 and versioned SARIF properties validated; 4 real CLI scans.")


if __name__ == "__main__":
    main()
