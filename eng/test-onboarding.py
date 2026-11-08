#!/usr/bin/env python3
"""Check copied skill portability and run deterministic onboarding interface scenarios.

These are scripted contract checks, not executions of an LLM/client following prose.
Requires a Release solution build. No authenticated harness or extra Python package.
"""

import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SKILL = ROOT / "skills/architecture-guardian-onboarding"
CLI = ROOT / "src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll"


def check_copy(destination):
    shutil.copytree(SKILL, destination)  # Refuse to overwrite a previously installed folder.
    entry = destination / "SKILL.md"
    text = entry.read_text(encoding="utf-8")
    frontmatter = text.split("---\n", 2)
    assert len(frontmatter) == 3 and not frontmatter[0], "Missing frontmatter"
    fields = dict(line.split(": ", 1) for line in frontmatter[1].splitlines())
    assert fields["name"] == destination.name
    assert re.fullmatch(r"[a-z0-9-]{1,64}", fields["name"])
    assert fields["description"] and len(fields["description"]) <= 1024
    visited = set()

    def visit(path):
        path = path.resolve()
        assert path.is_relative_to(destination.resolve()), f"Non-portable reference: {path}"
        assert path.is_file(), f"Missing resource: {path}"
        if path in visited:
            return
        visited.add(path)
        body = path.read_text(encoding="utf-8")
        for target in re.findall(r"\[[^\]]+\]\(([^)]+)\)", body):
            if "://" not in target and not target.startswith("#"):
                visit(path.parent / target.split("#")[0])

    visit(entry)
    assert visited == {p.resolve() for p in destination.rglob("*.md")}, "Unreachable skill reference"
    original = {p.relative_to(destination): p.read_bytes() for p in destination.rglob("*") if p.is_file()}
    try:
        shutil.copytree(SKILL, destination)
    except FileExistsError:
        pass
    else:
        raise AssertionError("Installation replaced an existing skill")
    assert original == {p.relative_to(destination): p.read_bytes() for p in destination.rglob("*") if p.is_file()}


def check_manual_example(root, cli):
    """Run the bundled YAML example through the actual strict loader and workspace."""
    reference = (SKILL / "references/workflow.md").read_text(encoding="utf-8")
    yaml = re.search(r"```yaml\n(.*?)\n```", reference, re.DOTALL).group(1)
    (root / "global.json").write_bytes((ROOT / "global.json").read_bytes())
    (root / "NuGet.Config").write_text(
        '<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
    projects = []
    for role in ("Domain", "Application", "Infrastructure"):
        project = root / f"src/Shop.{role}/Shop.{role}.csproj"
        project.parent.mkdir(parents=True)
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                           '<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', encoding="utf-8")
        projects.append(project.relative_to(root).as_posix())
    (root / "Shop.slnx").write_text('<Solution>' + ''.join(
        f'<Project Path="{project}" />' for project in projects) + '</Solution>', encoding="utf-8")
    (root / "architecture.guardian.proposed.yaml").write_text(yaml + "\n", encoding="utf-8")
    for arguments in (
        ["dotnet", "restore", "Shop.slnx", "-p:Configuration=Release", "--disable-build-servers"],
        [*cli, "inspect", "Shop.slnx", "--policy", "architecture.guardian.proposed.yaml",
         "--configuration", "Release", "--trusted"],
        [*cli, "scan", "Shop.slnx", "--policy", "architecture.guardian.proposed.yaml",
         "--configuration", "Release", "--trusted", "--format", "json"],
    ):
        result = subprocess.run(arguments, cwd=root, capture_output=True,
                                text=True, encoding="utf-8", timeout=90, check=False)
        assert result.returncode == 0, result.stdout + result.stderr
        if "scan" in arguments:
            report = json.loads(result.stdout)
            assert report["status"] == "completed" and report["gateOutcome"] == "passed"
    assert not (root / "architecture.guardian.yaml").exists(), "Example activated policy"
    print("Bundled manual YAML example passed strict policy and complete inventory inspection.", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable-only", action="store_true",
                        help="Check copied folders and bundled YAML; CI runs the test assemblies separately.")
    parser.add_argument("--legacy-cli", type=Path,
                        help="Also verify manual fallback with an installed archguard executable lacking discovery.")
    options = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix="archguard-onboarding-copy-") as temporary:
        for harness in (".agents", ".claude", ".github"):
            check_copy(Path(temporary) / harness / "skills" / SKILL.name)
        example = Path(temporary) / "example"
        example.mkdir()
        check_manual_example(example, ["dotnet", str(CLI)])
        if options.legacy_cli:
            legacy = str(options.legacy_cli.resolve())
            help_result = subprocess.run([legacy, "init", "--help"], capture_output=True,
                                         text=True, encoding="utf-8", timeout=30, check=False)
            assert "--discover" not in help_result.stdout + help_result.stderr, "Expected CLI without discovery"
            assert "scan" in help_result.stdout, "CLI help unavailable"
            fallback = Path(temporary) / "legacy"
            fallback.mkdir()
            check_manual_example(fallback, [legacy])
            print("Installed legacy CLI lacks discovery; manual candidate inspect/scan passed.", flush=True)
    print("Copied-directory structure, reachable references and installation collision checks passed for all three paths.", flush=True)
    if options.portable_only:
        return
    # Reuse the actual protocol/correction/discovery fixtures. The manual onboarding
    # theory covers clean/existing policies in both CLI-only and MCP-enabled modes.
    selected = "|".join(f"FullyQualifiedName~{name}" for name in (
        "CorrectionWorkflowTests", "CliDiscoveryTests", "PolicyProposalTests"))
    for project in ("ArchitectureGuardian.ArchitectureTests", "ArchitectureGuardian.Tests"):
        subprocess.run([
            "dotnet", "test", str(ROOT / f"tests/{project}/{project}.csproj"),
            "--configuration", "Release", "--no-build", "--no-restore", "-m:1", "--filter", selected,
        ], cwd=ROOT, check=True, timeout=600)
    print("Onboarding interface scenarios passed; no authenticated Codex/Claude/Copilot execution is claimed.")


if __name__ == "__main__":
    main()
