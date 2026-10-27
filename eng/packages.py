"""Pack, verify and smoke-test the three NuGet distribution contracts."""

import argparse
from collections import deque
import hashlib
import json
import os
from pathlib import Path
import queue
import re
import subprocess
import sys
import tempfile
import threading
import time
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
PACKAGES = {
    "ArchitectureGuardian.Cli": "archguard",
    "ArchitectureGuardian.Mcp": "archguard-mcp",
    "ArchitectureGuardian.Analyzers": None,
}


def run(arguments, cwd=ROOT, env=None, expected=0):
    result = subprocess.run(list(map(str, arguments)), cwd=cwd, env=env, capture_output=True,
                            text=True, encoding="utf-8", timeout=240, check=False)
    if result.returncode != expected:
        raise RuntimeError(f"{arguments}: expected {expected}, got {result.returncode}\n"
                           f"{result.stdout}\n{result.stderr}")
    return result.stdout


def source_version():
    version = run(["cz", "version", "--project"]).strip()
    assert ET.parse(ROOT / "Directory.Build.props").findtext(".//Version") == version, \
        "Run Commitizen bump to synchronize the .NET version"
    assert re.search(rf"^## (?:Unreleased|v{re.escape(version)})(?:\s|$)",
                     (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), re.MULTILINE), "Missing generated release notes"
    return version


def payload(package):
    # NuGet's ZIP timestamps and generated core-properties identity are not build payload.
    with zipfile.ZipFile(package) as archive:
        return {name: hashlib.sha256(archive.read(name)).hexdigest() for name in archive.namelist()
                if not name.startswith("package/services/metadata/core-properties/")
                and name not in ("_rels/.rels", "[Content_Types].xml")}


def verify(directory, tag=None, manifest=True):
    version = source_version()
    if tag is not None:
        assert tag == f"v{version}", f"Tag {tag} must match v{version}"
        assert re.search(rf"^## v{re.escape(version)}(?:\s|$)",
                         (ROOT / "CHANGELOG.md").read_text(encoding="utf-8"), re.MULTILINE), \
            "Generate versioned release notes with Commitizen bump before tagging"
    revision = run(["git", "rev-parse", "HEAD"]).strip()
    expected = {f"{name}.{version}.nupkg" for name in PACKAGES}
    assert {path.name for path in directory.glob("*.nupkg")} == expected, "Unexpected package set"
    for name, command in PACKAGES.items():
        package = directory / f"{name}.{version}.nupkg"
        with zipfile.ZipFile(package) as archive:
            paths = archive.namelist()
            assert len(paths) == len(set(paths)), "Duplicate package paths"
            document = ET.fromstring(archive.read(f"{name}.nuspec"))
            # Strip the NuGet XML namespace for explicit metadata checks.
            for node in document.iter():
                node.tag = node.tag.rsplit("}", 1)[-1]
            metadata = document.find("metadata")
            assert metadata.findtext("id") == name
            assert metadata.findtext("version") == version
            assert metadata.findtext("authors") == "Architecture Guardian contributors"
            assert metadata.findtext("description")
            assert metadata.findtext("projectUrl") == "https://github.com/kbourzayq/arch-guard"
            assert metadata.find("license").attrib == {"type": "expression"}
            assert metadata.findtext("license") == "MIT"
            assert metadata.findtext("readme") == "README.md" and "README.md" in paths
            readme_links = re.findall(r"\[[^\]]+\]\(([^)\s]+)\)", archive.read("README.md").decode("utf-8"))
            assert readme_links and all(link.startswith(("https://", "#")) for link in readme_links), \
                "Package README links must be absolute HTTPS URLs or local anchors"
            assert metadata.findtext("releaseNotes") == f"https://github.com/kbourzayq/arch-guard/blob/v{version}/CHANGELOG.md"
            repository = metadata.find("repository")
            assert repository.get("type") == "git" and repository.get("commit") == revision
            assert repository.get("url") == "https://github.com/kbourzayq/arch-guard"
            assert not metadata.findall(".//dependency"), "Packages must install without separate runtime packages"
            if command is None:
                assert [path for path in paths if path.endswith(".dll")] == [
                    "analyzers/dotnet/cs/ArchitectureGuardian.Analyzers.dll"]
                assert not any(path.startswith(("lib/", "tools/", "runtimes/")) for path in paths)
            else:
                assert metadata.find("packageTypes/packageType").get("name") == "DotnetTool"
                prefix = "tools/net10.0/any/"
                settings = ET.fromstring(archive.read(prefix + "DotnetToolSettings.xml"))
                entry = settings.find("Commands/Command")
                assert entry.attrib == {"Name": command, "EntryPoint": name + ".dll", "Runner": "dotnet"}
                for item in (name + ".dll", name + ".deps.json", name + ".runtimeconfig.json",
                             "ArchitectureGuardian.Core.dll", "ArchitectureGuardian.Analysis.dll",
                             "ArchitectureGuardian.Configuration.dll", "YamlDotNet.dll",
                             "BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll"):
                    assert prefix + item in paths, f"{name} missing {item}"
                for item in ("Microsoft.Build.dll", "Microsoft.Build.Framework.dll", "Microsoft.NET.StringTools.dll"):
                    assert prefix + item not in paths, f"{name} must load MSBuild from the selected SDK"
        print(f"Verified package metadata and contents: {package.name}", flush=True)
    if manifest:
        checksums = (directory / "SHA256SUMS").read_text(encoding="utf-8").splitlines()
        assert len(checksums) == len(expected), "Unexpected checksum manifest"
        assert set(checksums) == {
            f"{hashlib.sha256((directory / name).read_bytes()).hexdigest()}  {name}" for name in expected
        }, "Package checksum mismatch"
    return version


def pack(directory):
    source_version()
    directory.mkdir(parents=True, exist_ok=True)
    assert not list(directory.glob("*.nupkg")), "Use an empty package output directory"
    revision = run(["git", "rev-parse", "HEAD"]).strip()
    properties = ["-p:ContinuousIntegrationBuild=true", f"-p:RepositoryCommit={revision}",
                  f"-p:SourceRevisionId={revision}"]
    run(["dotnet", "restore", "ArchitectureGuardian.slnx", "--locked-mode"])
    with tempfile.TemporaryDirectory(prefix="archguard-repack-") as temporary:
        repeated = Path(temporary)
        for name in PACKAGES:
            print(f"Packing and rebuilding {name}", flush=True)
            project = ROOT / "src" / name
            run(["dotnet", "pack", project, "--configuration", "Release", "--no-restore",
                 "--output", directory, *properties])
            # Recompile before packing again so the check covers deterministic build outputs.
            run(["dotnet", "build", project, "--configuration", "Release", "--no-restore",
                 "--no-incremental", *properties])
            run(["dotnet", "pack", project, "--configuration", "Release", "--no-build",
                 "--output", repeated, *properties])
        verify(directory, manifest=False)
        for package in sorted(directory.glob("*.nupkg")):
            assert payload(package) == payload(repeated / package.name), f"Non-reproducible payload: {package.name}"
        (directory / "SHA256SUMS").write_text("".join(
            f"{hashlib.sha256(package.read_bytes()).hexdigest()}  {package.name}\n"
            for package in sorted(directory.glob("*.nupkg"))), encoding="utf-8")
    print("Rebuilt package payloads are reproducible; wrote SHA256SUMS", flush=True)


class StdioClient:
    """Exercise the installed stdio command with bounded, line-delimited JSON-RPC requests."""

    def __init__(self, command, root, env):
        self.process = subprocess.Popen([str(command), "--Workspace:Root", str(root),
                                         "--Workspace:Trusted", "true"], cwd=root, env=env,
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        text=True, encoding="utf-8")
        self.lines = queue.Queue()
        self.errors = deque(maxlen=100)
        self.identifier = 0
        threading.Thread(target=self.read_output, daemon=True).start()
        threading.Thread(target=lambda: self.errors.extend(self.process.stderr), daemon=True).start()

    def read_output(self):
        for line in self.process.stdout:
            self.lines.put(line)
        self.lines.put(None)

    def send(self, method, params, identifier=None):
        message = {"jsonrpc": "2.0", "method": method, "params": params}
        if identifier is not None:
            message["id"] = identifier
        self.process.stdin.write(json.dumps(message) + "\n")
        self.process.stdin.flush()

    def request(self, method, params):
        self.identifier += 1
        self.send(method, params, self.identifier)
        deadline = time.monotonic() + 180
        while True:
            try:
                line = self.lines.get(timeout=max(0, deadline - time.monotonic()))
            except queue.Empty as error:
                raise RuntimeError(f"MCP request timed out: {method}\n{''.join(self.errors)}") from error
            assert line is not None, f"MCP exited during {method}: {''.join(self.errors)}"
            message = json.loads(line)  # Any log or other non-protocol stdout fails the smoke test.
            if message.get("id") == self.identifier:
                assert "error" not in message, message
                return message["result"]

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=15)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=10)


def smoke(directory):
    version = verify(directory)
    with tempfile.TemporaryDirectory(prefix="archguard-install-") as temporary:
        root = Path(temporary).resolve()
        env = dict(os.environ, DOTNET_CLI_HOME=str(root / "home"), NUGET_PACKAGES=str(root / "packages"),
                   NUGET_HTTP_CACHE_PATH=str(root / "http-cache"), DOTNET_CLI_TELEMETRY_OPTOUT="1",
                   DOTNET_NOLOGO="1", MSBUILDDISABLENODEREUSE="1")
        (root / "global.json").write_bytes((ROOT / "global.json").read_bytes())
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="packages-under-test", value=str(directory))
        ET.ElementTree(config).write(root / "NuGet.Config", encoding="utf-8")
        (root / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        (root / "Directory.Packages.props").write_text("<Project />", encoding="utf-8")
        tools = root / "installed tools"
        print("Installing CLI and MCP from the local-only package feed into fresh caches", flush=True)
        for name, command in PACKAGES.items():
            if command:
                run(["dotnet", "tool", "install", name, "--version", version, "--tool-path", tools,
                     "--configfile", root / "NuGet.Config", "--no-cache"], root, env)
        suffix = ".exe" if os.name == "nt" else ""
        cli = tools / ("archguard" + suffix)
        assert run([cli, "--version"], root, env).strip().split("+", 1)[0] == version
        assert "scan" in run([cli, "--help"], root, env)
        assert "AG001" in run([cli, "explain", "AG001"], root, env)
        run([cli, "explain", "AG999"], root, env, expected=2)
        project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>{}</Project>'
        for name in ("App", "Library"):
            folder = root / name
            folder.mkdir()
            reference = '<ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>' if name == "App" else ""
            (folder / f"{name}.csproj").write_text(project.format(reference), encoding="utf-8")
            source = "namespace App; public class Target : Library.Target { }" if name == "App" else "namespace Library; public class Target { }"
            (folder / "Class.cs").write_text(source, encoding="utf-8")
        (root / "Application.slnx").write_text(
            '<Solution><Project Path="App/App.csproj" /><Project Path="Library/Library.csproj" /></Solution>', encoding="utf-8")
        policy = root / "architecture.guardian.yaml"
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
        run(["dotnet", "restore", "Application.slnx", "-p:Configuration=Release", "--disable-build-servers"], root, env)
        scan = [cli, "scan", "Application.slnx", "--policy", policy, "--configuration", "Release", "--trusted", "--format", "json"]
        blocked = json.loads(run(scan, root, env, expected=1))
        assert blocked["status"] == "completed" and blocked["gateOutcome"] == "failed", blocked
        assert any(item["ruleId"] == "AG001" for item in blocked["diagnostics"]), blocked
        client = StdioClient(tools / ("archguard-mcp" + suffix), root, env)
        print("CLI scans passed; checking installed MCP initialization, discovery and scans", flush=True)
        try:
            initialized = client.request("initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                                       "clientInfo": {"name": "package-smoke", "version": "1.0.0"}})
            assert initialized["serverInfo"]["version"] == version, initialized
            client.send("notifications/initialized", {})
            discovered = client.request("tools/list", {})
            assert {tool["name"] for tool in discovered["tools"]} == {
                "get_architecture", "list_rules", "analyze_solution", "analyze_changes", "explain_violation"}, discovered
            arguments = {"inputPath": "Application.slnx", "policyPath": policy.name, "configuration": "Release"}
            for outcome in ("failed", "passed"):
                response = client.request("tools/call", {"name": "analyze_solution", "arguments": arguments})
                assert not response.get("isError"), response
                report = response["structuredContent"]
                assert report["status"] == "completed" and report["gateOutcome"] == outcome, report
                if outcome == "failed":
                    policy.write_text(policy.read_text(encoding="utf-8").replace(
                        "  - id: App\n", "  - id: App\n    allowedDependencies: [module:Library]\n"), encoding="utf-8")
                    passed = json.loads(run(scan, root, env))
                    assert passed["status"] == "completed" and passed["gateOutcome"] == "passed", passed
        finally:
            client.close()
        sdk = json.loads((ROOT / "global.json").read_text(encoding="utf-8"))["sdk"]["version"]
        print(run([sys.executable, ROOT / "eng/test-analyzer-package.py",
                   directory / f"ArchitectureGuardian.Analyzers.{version}.nupkg", "--sdk", sdk], root, env), flush=True)
    print("Clean local-feed installation, CLI scans, MCP stdio scans and analyzer consumer passed", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("pack", "verify", "smoke"))
    parser.add_argument("--directory", type=Path, default=ROOT / "artifacts/packages")
    parser.add_argument("--tag", help="Require an exact v<source-version> release tag during verification")
    args = parser.parse_args()
    directory = args.directory.resolve()
    if args.operation == "pack":
        pack(directory)
    elif args.operation == "verify":
        verify(directory, args.tag)
    else:
        smoke(directory)


if __name__ == "__main__":
    main()
