# Third-party notices

This project contains modified source code from OpenUtau:

- Project: OpenUtau
- Repository: https://github.com/openutau/openutau
- Base commit: `29e0e16d1623cda79ba7c3724614d6129ba3b9d5`
- Files derived from:
  - `OpenUtau.Plugin.Builtin/SyllableBasedPhonemizer.cs`
  - `OpenUtau.Plugin.Builtin/ArpasingPlusPhonemizer.cs`
- Original ARPA+ author credited by OpenUtau: Cadlaxa
- License: MIT License (`OPENUTAU_LICENSE.txt`)

Modifications include a standalone namespace and assembly, Cz-style English VCCV
alias parsing, an embedded `vccv-to-arpa.yaml` configuration, the `EN VCCV2ARPA`
identifier, and singer-aware `ah -> aa` fallback.
