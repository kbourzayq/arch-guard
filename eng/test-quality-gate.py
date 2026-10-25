"""Exercise the CI runner with real CLI scans in an isolated, package-free Git repository."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll"
RUNNER = ROOT / "eng/run-quality-gate.py"


def run(arguments, cwd, expected=0):
    result = subprocess.run(list(map(str, arguments)), cwd=cwd, capture_output=True,
                            text=True, encoding="utf-8", timeout=240, check=False)
    assert result.returncode == expected, f"Expected {expected}, got {result.returncode}: {result.stdout}\n{result.stderr}"
    return result


def gate(repository, output, comparison, expected, outcome, reports=True):
    result = run([sys.executable, RUNNER, "--cli", CLI, "--input", "Application.slnx",
                  "--policy", "architecture.guardian.yaml", "--trusted", *comparison,
                  "--output-directory", output], repository, expected)
    summary = json.loads((output / "summary.json").read_text(encoding="utf-8"))
    assert summary["exitCode"] == expected and summary["outcome"] == outcome, summary
    assert [item["format"] for item in summary["runs"]] == ["json", "sarif"]
    for item in summary["runs"]:
        assert item["cliExitCode"] == expected, summary
        assert bool(item["report"]) == reports, summary
        assert (output / f"{item['format']}.stderr.log").is_file()
        assert (output / f"{item['format']}.stdout.log").is_file()
    if reports:
        envelope = json.loads((output / "architecture.json").read_text(encoding="utf-8"))
        sarif = json.loads((output / "architecture.sarif").read_text(encoding="utf-8"))
        assert envelope["gateOutcome"] == sarif["runs"][0]["properties"]["gateOutcome"]
        assert envelope["status"] == sarif["runs"][0]["properties"]["status"]
        if expected == 2:
            assert envelope["status"] != "completed" and envelope["gateOutcome"] == "notEvaluated"
            assert envelope["issues"]
        else:
            assert envelope["status"] == "completed"
            assert envelope["gateOutcome"] == ("failed" if expected == 1 else "passed")
    print(f"{output.name}: {outcome}; both formats attempted and artifacts checked", flush=True)
    return result


def main():
    with tempfile.TemporaryDirectory(prefix="archguard-ci-") as temporary:
        root = Path(temporary).resolve()
        repository = root / "repository with spaces"
        repository.mkdir()
        (repository / "global.json").write_bytes((ROOT / "global.json").read_bytes())
        (repository / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        (repository / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
        (repository / ".gitignore").write_text("**/obj/\n**/bin/\n", encoding="utf-8")
        project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        for name in ("App", "Library"):
            directory = repository / name
            directory.mkdir()
            (directory / f"{name}.csproj").write_text(project, encoding="utf-8")
            (directory / "Class.cs").write_text(f"namespace {name}; public class Target {{ }}", encoding="utf-8")
        (repository / "Application.slnx").write_text(
            '<Solution><Project Path="App/App.csproj" /><Project Path="Library/Library.csproj" /></Solution>', encoding="utf-8")
        policy = repository / "architecture.guardian.yaml"
        policy.write_text('''version: 1
modules:
  - id: App
    projects: ["App/*.csproj"]
  - id: Library
    projects: ["Library/*.csproj"]
layers:
  - id: All
    projects: ["**/*.csproj"]
rules:
  AG001: error
''', encoding="utf-8")
        run(["git", "init", "--initial-branch=main"], repository)
        run(["git", "add", "."], repository)
        run(["git", "-c", "user.name=CI Gate Test", "-c", "user.email=ci-gate@example.invalid",
             "-c", "commit.gpgsign=false", "commit", "-m", "Clean base"], repository)
        base = run(["git", "rev-parse", "HEAD"], repository).stdout.strip()
        restore = ["dotnet", "restore", "Application.slnx", "--locked-mode", "-p:Configuration=Release",
                   "--disable-parallel", "--disable-build-servers"]
        run(restore, repository)
        gate(repository, root / "clean", ["--base", base], 0, "passed")

        (repository / "App/App.csproj").write_text(project.replace("</Project>",
            '<ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup></Project>'), encoding="utf-8")
        (repository / "App/Class.cs").write_text("namespace App; public class Target : Library.Target { }", encoding="utf-8")
        run(restore, repository)
        failed = gate(repository, root / "blocking", ["--base", base], 1, "policy-failure")
        assert "POLICY FAILURE" in failed.stdout
        assert json.loads((root / "blocking/architecture.json").read_text(encoding="utf-8"))["diagnostics"]

        baseline = repository / "accepted.json"
        run(["dotnet", CLI, "baseline", "update", "Application.slnx", "--policy", policy,
             "--baseline", baseline, "--configuration", "Release", "--trusted"], repository)
        accepted = baseline.read_bytes()
        gate(repository, root / "accepted", ["--baseline", "accepted.json"], 0, "passed")
        assert baseline.read_bytes() == accepted, "Gate modified accepted debt"
        gate(repository, root / "full", ["--all-findings"], 1, "policy-failure")

        unavailable = gate(repository, root / "missing-base", ["--base", "refs/heads/unavailable"],
                           2, "operational-failure")
        assert "OPERATIONAL FAILURE" in unavailable.stdout
        policy.write_text("version: invalid\n", encoding="utf-8")
        gate(repository, root / "invalid-policy", ["--all-findings"], 2, "operational-failure", reports=False)

        # A previous passing report must survive unchanged and cannot become this run's result.
        previous = (root / "clean/architecture.json").read_bytes()
        reused = run([sys.executable, RUNNER, "--cli", CLI, "--input", "Application.slnx",
                      "--policy", policy, "--all-findings", "--output-directory", root / "clean"], repository, 2)
        assert "fresh output directory" in reused.stderr
        assert (root / "clean/architecture.json").read_bytes() == previous
        print("CI gates passed clean, blocking, accepted-debt, full, missing-base, invalid-policy and stale-output checks.")


if __name__ == "__main__":
    main()
