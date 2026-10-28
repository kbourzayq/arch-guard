"""Build the current analyzer, validate this checkout during compilation, then run all six CLI rules."""

import argparse
from pathlib import Path
import shutil
import subprocess


ROOT = Path(__file__).resolve().parents[1]


def run(arguments):
    """Forward output and fail with the original tool exit code."""
    result = subprocess.run(arguments, cwd=ROOT, check=False)
    if result.returncode:
        raise SystemExit(result.returncode)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--configuration", default="Release", choices=("Debug", "Release"))
    args = parser.parse_args()
    configuration = args.configuration
    # Bootstrapping is explicitly outside self analysis; the stable copy also permits Windows rebuilds.
    run(["dotnet", "build", "src/ArchitectureGuardian.Analyzers", "--configuration", configuration, "--no-restore",
         "-p:ArchitectureGuardianSelfAnalysis=false", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"])
    source = ROOT / "src/ArchitectureGuardian.Analyzers/bin" / configuration / "netstandard2.0"
    destination = ROOT / "artifacts/self-analysis"
    destination.mkdir(parents=True, exist_ok=True)
    for assembly in ("ArchitectureGuardian.Analyzers.dll", "ArchitectureGuardian.Policy.dll", "YamlDotNet.dll"):
        shutil.copy2(source / assembly, destination / assembly)
    run(["dotnet", "build", "ArchitectureGuardian.slnx", "--configuration", configuration, "--no-restore", "--no-incremental",
         "-p:ArchitectureGuardianSelfAnalysis=true", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"])
    cli = ROOT / "src/ArchitectureGuardian.Cli/bin" / configuration / "net10.0/ArchitectureGuardian.Cli.dll"
    run(["dotnet", str(cli), "scan", "ArchitectureGuardian.slnx", "--policy", "architecture.guardian.yaml",
         "--configuration", configuration, "--trusted"])


if __name__ == "__main__":
    main()
