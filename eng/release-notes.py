#!/usr/bin/env python3
"""Write GitHub release notes for a tag and wait for its NuGet packages to be listed.

Notes come from the tag's own generated CHANGELOG.md section, so a release page never
describes changes the tag does not contain. Requires Git and Python 3.10+; no packages.
"""

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "https://github.com/kbourzayq/arch-guard"
PACKAGES = ("ArchitectureGuardian.Cli", "ArchitectureGuardian.Mcp", "ArchitectureGuardian.Analyzers")
# Commitizen's change types, renamed to the headings used by published release pages.
HEADINGS = {"Feat": "Features", "Fix": "Fixes", "Perf": "Performance", "Refactor": "Refactoring",
            "BREAKING CHANGE": "Breaking changes"}


def version_of(tag):
    if not re.fullmatch(r"v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)", tag):
        raise SystemExit(f"{tag}: expected a stable vMAJOR.MINOR.PATCH tag.")
    return tag[1:]


def git(*arguments):
    result = subprocess.run(["git", *arguments], cwd=ROOT, capture_output=True, text=True,
                            encoding="utf-8", check=False)
    if result.returncode != 0:
        raise SystemExit(f"git {' '.join(arguments)} failed: {result.stderr.strip()}")
    return result.stdout


def changelog_section(tag):
    """Return the tag's own changelog entry, failing rather than publishing empty notes."""
    changelog = git("show", f"refs/tags/{tag}:CHANGELOG.md")
    match = re.search(rf"^## {re.escape(tag)} \([^)]*\)\n(.*?)(?=^## |\Z)", changelog, re.M | re.S)
    if match is None or not match.group(1).strip():
        raise SystemExit(f"{tag}: CHANGELOG.md at this tag has no entry for the release.")
    return re.sub(r"^### (.+)$", lambda heading: "### " + HEADINGS.get(heading.group(1), heading.group(1)),
                  match.group(1).strip(), flags=re.M)


def notes(tag, run_url):
    version = version_of(tag)
    commit = git("rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}").strip()
    validation = f"Released from commit `{commit}`."
    if run_url:
        validation += (f" The [exact-tag workflow]({run_url}) ran the full CI on the tagged commit"
                       " and published the tested artifacts through NuGet trusted publishing.")
    packages = "\n".join(f"- [{name} {version}](https://www.nuget.org/packages/{name}/{version})" for name in PACKAGES)
    return (f"{changelog_section(tag)}\n\n### Release validation\n\n{validation}\n\n### Packages\n\n{packages}\n\n"
            f"See the [full changelog]({REPOSITORY}/blob/{tag}/CHANGELOG.md).\n")


def listed(name, version):
    url = f"https://api.nuget.org/v3-flatcontainer/{name.lower()}/index.json"
    try:
        with urllib.request.urlopen(url, timeout=20) as response:
            return version in json.load(response).get("versions", [])
    except (urllib.error.URLError, TimeoutError, ValueError):
        return False


def wait_for_packages(tag, timeout, interval):
    """Block until every package version is public; newly pushed packages take minutes to index."""
    version = version_of(tag)
    deadline = time.monotonic() + timeout
    while True:
        missing = [name for name in PACKAGES if not listed(name, version)]
        if not missing:
            print(f"All packages are listed on nuget.org at {version}.")
            return
        if time.monotonic() >= deadline:
            raise SystemExit(f"Not listed on nuget.org after {timeout}s: {', '.join(missing)} {version}.")
        print(f"Waiting for nuget.org to list {', '.join(missing)} {version}...", flush=True)
        time.sleep(interval)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("notes", "wait-nuget"))
    parser.add_argument("--tag", required=True, help="Release tag, for example v0.4.1")
    parser.add_argument("--run-url", help="Release workflow run that validated and published the tag")
    parser.add_argument("--timeout", type=int, default=2400, help="Seconds to wait for package listing")
    parser.add_argument("--interval", type=int, default=30, help="Seconds between package listing checks")
    args = parser.parse_args()
    if args.operation == "notes":
        sys.stdout.write(notes(args.tag, args.run_url))
    else:
        wait_for_packages(args.tag, args.timeout, args.interval)


if __name__ == "__main__":
    main()
