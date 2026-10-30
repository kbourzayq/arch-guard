# Report validation for contributors

After building the full solution in Release (including its build-only sample fixture), run:

```sh
python3 -m pip install jsonschema==4.26.0
python3 eng/test-reports.py
```

Use a virtual environment where required. A build-only fixture generates 20
serializer samples in one process: both encodings for clean, informational/warning,
gate-failing, accepted, mixed-baseline, resolved, unlocated, incomplete, failed and
cancelled results. The script requires that exact sample inventory and validates
all of it against JSON v1, the complete OASIS schema and the versioned properties
contract. It checks expected states, counts, severities, UTF-8 and LF output.

Four real CLI scans then verify stdout/file equivalence and repeatability for
both formats using isolated package-free inputs. This avoids repeating a full
workspace load for every serializer state. The normal xUnit suite retains actual
CLI baseline and Git comparison behavior, partial coverage, evidence, URI escaping,
cancellation and file-write failure cases. CI runs both layers on Linux, Windows
and macOS, on runners separate from the unit/compiler suite.
