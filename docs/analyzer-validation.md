# Compiler analyzer package validation

The semantic suite covers AG005/AG006 framework/external restrictions and lookalikes, invalid configuration, cancellation, concurrent reuse,
generated sources and policy permissions. The package test checks the NuGet layout,
loads the installed analyzer through a real SDK build, requires located AG001/AG002/AG004
errors for a forbidden dependency, verifies that whole-module permission still yields
AG004, then declares a public contract through the AdditionalFile and requires a passing build.
It also adds an AG005 namespace restriction, requires a located diagnostic and checks
that removing the restriction restores the passing build. It checks an AG006 ban and
then restores the passing build through an explicit namespace exception. Run it locally with an installed SDK version:

```sh
python3 eng/test-analyzer-package.py artifacts/analyzers/ArchitectureGuardian.Analyzers.0.3.0.nupkg --sdk 10.0.402
```

CI results are distinct from local verification; interactive IDE UI behavior remains
a release validation limitation. The [semantic accuracy and parity guide](semantic-accuracy.md)
documents shared AG001/AG002/AG004/AG005/AG006 expectations, framework/configuration
matrix, generated sources, incomplete references and intentional engine differences.
