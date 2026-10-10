# Fog reconstruction development tool

This tool is development-only. It is not referenced by either packaged distribution and must not be used to copy Fog or game data into the product.

The reference Fog cache is an oracle only. Keep its files and all generated reports outside the repository. The checked-in tests use synthetic semantic models and contain no game or Fog binaries.

Commands:

```text
FogReconstruction self-test
FogReconstruction verify-reference <root-containing-dist>
FogReconstruction verify-inputs <pristine-dsr-root> <root-containing-dist>
FogReconstruction inspect-paramdefs <pristine-dsr-root>
FogReconstruction extract <pristine-dsr-root> <root-containing-dist> <report-json> [ds1-common.emedf.json]
```

`verify-inputs` authenticates the 19 map/GameParam files used by Fog against the pinned hashes and verifies the 59-file reference root. It also requires the DSR ParamDef binder. It does not replace the application's signed-off pristine snapshot authority.

`extract` writes the semantic diff, including game-derived instruction/evaluator values, only to the caller-provided report path. Use a location outside the repository, do not commit or ship the report, and delete it when analysis is complete. The extractor identifies candidate vanilla event sources but does not decide that an added event is safe to reproduce; added-event provenance still requires review of the pinned FogMod code.
