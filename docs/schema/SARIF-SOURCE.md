# SARIF schema source

`sarif-2.1.0.schema.json` is an unmodified copy of the OASIS TC's
[SARIF 2.1.0 schema](https://github.com/oasis-tcs/sarif-spec/blob/main/sarif-2.1/schema/sarif-schema-2.1.0.json),
retrieved on 2026-10-25 for offline validation. Its declared schema ID is the
[OASIS errata01 schema](https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json).
The upstream [license terms](SARIF-LICENSE.md) are retained verbatim.

SHA-256: `c3b4bb2d6093897483348925aaa73af03b3e3f4bd4ca38cef26dcb4212a2682e`.

`report.v1.schema.json` is authored in this repository and is the authoritative
Architecture Guardian JSON v1 export contract. Update it together with the
Reporting adapter, report guide and validation cases when changing that contract.
