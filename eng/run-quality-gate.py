"""Run independent CLI quality gates and retain both reports before failing CI."""

import argparse
import json
from pathlib import Path
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cli", required=True, type=Path, help="Published ArchitectureGuardian.Cli.dll")
    parser.add_argument("--input", required=True, help="Restored solution or project")
    parser.add_argument("--policy", required=True)
    comparison = parser.add_mutually_exclusive_group(required=True)
    comparison.add_argument("--base", help="Explicit locally available Git commit/ref")
    comparison.add_argument("--baseline", help="Reviewed accepted-debt JSON; never updated here")
    comparison.add_argument("--all-findings", action="store_true", help="Gate every finding without accepted debt")
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--framework")
    parser.add_argument("--trusted", action="store_true", help="Authorize repository build logic in both trees")
    parser.add_argument("--output-directory", required=True, type=Path, help="New directory, outside the scanned Git root")
    args = parser.parse_args()

    # Refuse reuse: reports from a previous successful run must never mask this run.
    try:
        args.output_directory.mkdir(parents=True, exist_ok=False)
    except OSError as error:
        print(f"QUALITY GATE: OPERATIONAL FAILURE: cannot create fresh output directory: {error}", file=sys.stderr)
        return 2

    command = ["dotnet", str(args.cli.resolve()), "scan", args.input, "--policy", args.policy,
               "--configuration", args.configuration]
    if args.base is not None:
        command.extend(["--base", args.base])
    elif args.baseline is not None:
        command.extend(["--baseline", args.baseline])
    if args.framework:
        command.extend(["--framework", args.framework])
    if args.trusted:
        command.append("--trusted")

    runs = []
    for format_name in ("json", "sarif"):
        report = args.output_directory / f"architecture.{format_name}"
        with (args.output_directory / f"{format_name}.stdout.log").open("w", encoding="utf-8") as output, \
                (args.output_directory / f"{format_name}.stderr.log").open("w", encoding="utf-8") as error:
            try:
                result = subprocess.run(
                    [*command, "--format", format_name, "--output", str(report.resolve())],
                    stdout=output, stderr=error, check=False,
                )
                code = result.returncode
            except OSError as failure:
                error.write(f"Cannot launch CLI: {failure}\n")
                code = 2
        stderr = (args.output_directory / f"{format_name}.stderr.log").read_text(encoding="utf-8", errors="replace")
        if stderr:
            print(stderr, file=sys.stderr, end="" if stderr.endswith("\n") else "\n")
        runs.append({"format": format_name, "cliExitCode": code,
                     "report": report.name if report.is_file() else None})
        print(f"{format_name}: CLI exit {code}; report {'retained' if report.is_file() else 'unavailable'}", flush=True)

    codes = [run["cliExitCode"] for run in runs]
    # Infrastructure/coverage failures take precedence over policy failures. Two
    # encodings of the same stable input must agree; disagreement cannot pass.
    if any(code not in (0, 1) for code in codes) or len(set(codes)) != 1 or any(run["report"] is None for run in runs):
        code, outcome = 2, "operational-failure"
        message = "OPERATIONAL FAILURE: workspace, invocation or report failure; no reliable gate approval."
    elif codes[0] == 1:
        code, outcome = 1, "policy-failure"
        message = "POLICY FAILURE: analysis completed; blocking architecture findings."
    else:
        code, outcome = 0, "passed"
        message = "PASSED: analysis completed; severity gate passed."
    summary = {"exitCode": code, "outcome": outcome, "runs": runs}
    (args.output_directory / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(f"QUALITY GATE: {message}", flush=True)
    return code


if __name__ == "__main__":
    try:
        sys.exit(main())
    except OSError as error:
        print(f"QUALITY GATE: OPERATIONAL FAILURE: artifact I/O failed: {error}", file=sys.stderr)
        sys.exit(2)
