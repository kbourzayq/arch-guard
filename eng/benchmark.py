#!/usr/bin/env python3
"""Prepare reproducible repositories and run BenchmarkDotNet with measured regression budgets."""

import argparse
import hashlib
import json
import math
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests/Fixtures/Performance/ArchitectureGuardian.Performance.csproj"
WORKER = PROJECT.parent / "bin/Release/net10.0/ArchitectureGuardian.Performance.dll"
RULES = """rules:
  AG001: error
  AG002: error
  AG003: error
  AG004: error
  AG005: error
  AG006: error
"""


def run(arguments, timeout=300):
    result = subprocess.run(arguments, cwd=ROOT, capture_output=True, text=True,
                            timeout=timeout, check=False)
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {arguments}\n{result.stdout}\n{result.stderr}")
    return result.stdout.strip()


def digest(directory):
    """Hash authored inputs with portable relative paths; exclude restore/build products."""
    hashed = hashlib.sha256()
    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory)
        if path.is_file() and not {"bin", "obj"}.intersection(relative.parts):
            hashed.update(relative.as_posix().encode() + b"\0" + path.read_bytes() + b"\0")
    return hashed.hexdigest()


def implementation_digest():
    hashed = hashlib.sha256()
    paths = [ROOT / name for name in ("global.json", "Directory.Build.props", "Directory.Packages.props", "eng/benchmark.py")]
    for directory in (ROOT / "src", PROJECT.parent):
        paths.extend(path for path in directory.rglob("*") if path.is_file()
                     and not {"bin", "obj"}.intersection(path.relative_to(directory).parts)
                     and (path.suffix in (".cs", ".csproj") or path.name == "packages.lock.json"))
    for path in sorted(paths):
        hashed.update(path.relative_to(ROOT).as_posix().encode() + b"\0" + path.read_bytes() + b"\0")
    return hashed.hexdigest()


def prepare(directory):
    references = directory / "references"
    shutil.copytree(ROOT / "tests/Fixtures/ReferenceSolutions", references,
                    ignore=shutil.ignore_patterns("bin", "obj"))
    shutil.copy(ROOT / "global.json", references / "global.json")
    cases = []
    for name in ("CleanArchitecture", "ModularMonolith"):
        folder = references / name
        policy = folder / "architecture.guardian.yaml"
        # Exercise the complete solution rule inventory using the existing ownership model.
        policy.write_text(policy.read_text().split("rules:")[0] + RULES, encoding="utf-8")
        cases.append((name, folder / f"{name}.slnx", policy, references))

    generated = directory / "generated"
    generated.mkdir()
    for name in ("global.json", "Directory.Build.props", "Directory.Packages.props", "NuGet.Config"):
        shutil.copy(references / name, generated / name)
    modules = []
    projects = []
    for number in range(24):
        name = f"Module{number:03}"
        folder = generated / name
        folder.mkdir()
        previous = f"Module{number - 1:03}"
        reference = (f'<ItemGroup><ProjectReference Include="../{previous}/{previous}.csproj" /></ItemGroup>'
                     if number else "")
        (folder / f"{name}.csproj").write_text(f'<Project Sdk="Microsoft.NET.Sdk">{reference}</Project>\n', encoding="utf-8")
        projects.append(f'<Project Path="{name}/{name}.csproj" />')
        modules.append(f'  - id: {name}\n    projects: ["{name}/*.csproj"]\n'
                       + (f'    allowedDependencies: ["module:{previous}"]\n' if number else ""))
        for index in range(40):
            methods = []
            for method in range(8):
                methods.append(f"    public {previous}.Type000 Call{method}() => new();" if number else
                               f"    public int Call{method}() => {index + method};")
            (folder / f"Type{index:03}.cs").write_text(
                f"namespace {name};\npublic class Type{index:03}\n{{\n" + "\n".join(methods) + "\n}\n", encoding="utf-8")
    (generated / "Generated.slnx").write_text("<Solution>\n" + "\n".join(projects) + "\n</Solution>\n", encoding="utf-8")
    policy = generated / "architecture.guardian.yaml"
    policy.write_text("version: 1\nmodules:\n" + "".join(modules) + """layers:
  - id: All
    projects: ["**/*.csproj"]
    forbiddenNamespaces: [Example.Forbidden]
    domainInfrastructure:
      forbiddenNamespaces: [Example.Infrastructure]
""" + RULES, encoding="utf-8")
    cases.append(("Generated24x40", generated / "Generated.slnx", policy, generated))
    for name, input_path, _, context in cases:
        print(f"Preparing {name}", file=sys.stderr, flush=True)
        run(["dotnet", "restore", str(input_path), "--configfile", str(context / "NuGet.Config"),
             "-p:Configuration=Release", "--disable-parallel", "--disable-build-servers"])
    return cases


def summarize(benchmarks):
    summary = {}
    for benchmark in benchmarks:
        method = benchmark["Method"]
        stats = benchmark["Statistics"]
        memory = benchmark["Memory"]["BytesAllocatedPerOperation"]
        if not stats or stats["N"] < 3 or memory is None or memory <= 0:
            raise RuntimeError(f"Missing BenchmarkDotNet measurements: {method}")
        summary[f"{method}.elapsedMilliseconds"] = {
            "median": stats["Median"] / 1_000_000,
            "max": stats["Max"] / 1_000_000,
            "budget": math.ceil(stats["Max"] / 1_000_000 * 1.5),
        }
        summary[f"{method}.allocatedBytes"] = {"measured": memory, "budget": math.ceil(memory * 1.5)}
    if set(summary) != {f"{method}.{metric}" for method in ("Workspace", "Graph", "Rules", "FullScan")
                        for metric in ("elapsedMilliseconds", "allocatedBytes")}:
        raise RuntimeError("Expected all four BenchmarkDotNet methods once per input.")
    return summary


def compare(report, baseline):
    failures = []
    if report["schemaVersion"] != baseline["schemaVersion"] or report["cases"].keys() != baseline["cases"].keys():
        raise RuntimeError("Benchmark schema/cases differ from the baseline.")
    for name, case in report["cases"].items():
        previous = baseline["cases"][name]
        if case["inputSha256"] != previous["inputSha256"]:
            raise RuntimeError(f"Benchmark inputs changed: {name}; review and measure a new baseline.")
        if case["validation"] != previous["validation"]:
            raise RuntimeError(f"Analysis results changed: {name}; review correctness before updating budgets.")
        if case["summary"].keys() != previous["summary"].keys():
            raise RuntimeError(f"Benchmark metrics changed: {name}")
        for key, summary in case["summary"].items():
            value = summary.get("median", summary.get("measured"))
            budget = previous["summary"][key]["budget"]
            if value > budget:
                failures.append(f"{name} {key}: measured {value:.1f} exceeds {budget}")
    return failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/performance/current.json")
    parser.add_argument("--check", type=Path, help="Compare with previously measured budgets.")
    args = parser.parse_args()
    if args.check and args.output.resolve() == args.check.resolve():
        parser.error("Comparison output must not overwrite its baseline.")
    run(["dotnet", "restore", str(PROJECT), "--locked-mode"])
    run(["dotnet", "build", str(PROJECT), "-c", "Release", "--no-restore"])
    report = {
        "schemaVersion": 2,
        "revision": run(["git", "rev-parse", "HEAD"]),
        "dirty": bool(run(["git", "status", "--porcelain"])),
        "implementationSha256": implementation_digest(),
        "method": "BenchmarkDotNet 0.15.8; Monitoring; launch=1 warmup=1 iteration=5 invocation=1; MemoryDiagnoser; budgets = measured max/allocated bytes * 1.5",
        "cases": {},
    }
    artifact_root = ROOT / "artifacts/performance"
    artifact_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="run-", dir=artifact_root) as temporary:
        directory = Path(temporary)
        cases = prepare(directory)
        manifest = directory / "inputs.json"
        manifest.write_text(json.dumps({name: {"input": str(input_path), "policy": str(policy)}
                                        for name, input_path, policy, _ in cases}), encoding="utf-8")
        environment = dict(os.environ)
        environment["ARCHGUARD_BENCHMARK_INPUTS"] = str(manifest)
        environment.pop("ARCHGUARD_TELEMETRY", None)
        artifacts = directory / "bdn"
        print("Running BenchmarkDotNet (12 benchmarks); detailed output is retained in artifacts/performance/benchmark.log", file=sys.stderr, flush=True)
        with (artifact_root / "benchmark.log").open("w", encoding="utf-8") as log:
            result = subprocess.run(["dotnet", str(WORKER), "--filter", "*", "--artifacts", str(artifacts)],
                                    cwd=ROOT, env=environment, stdout=log, stderr=subprocess.STDOUT,
                                    timeout=1800, check=False)
        if result.returncode:
            raise RuntimeError(f"BenchmarkDotNet failed ({result.returncode}); see artifacts/performance/benchmark.log")
        exports = list((artifacts / "results").glob("*-report-full.json"))
        if len(exports) != 1:
            raise RuntimeError("Expected one full BenchmarkDotNet JSON export.")
        raw = json.loads(exports[0].read_text(encoding="utf-8"))
        if len(raw["Benchmarks"]) != 12:
            raise RuntimeError("BenchmarkDotNet did not complete all twelve benchmarks.")
        report["benchmarkDotNet"] = raw
        for name, _, _, context in cases:
            benchmarks = [item for item in raw["Benchmarks"] if item["Parameters"] == f"Input={name}"]
            report["cases"][name] = {
                "inputSha256": digest(context),
                "validation": json.loads((directory / (name + ".validation.json")).read_text(encoding="utf-8")),
                "summary": summarize(benchmarks),
            }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    failures = compare(report, json.loads(args.check.read_text(encoding="utf-8"))) if args.check else []
    print(f"Measurements: {args.output}")
    for failure in failures:
        print(failure, file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
