# GitHub preparation

Prepared from the existing Empiro IP Scanner C#/.NET source on 2026-11-24.

Repository: https://github.com/vineetkr/empiro-ip-scanner (public).

Included: current WPF application, core library, tests, branding assets and tools, solution, README, SDK baseline, and installer build scripts.

Excluded: legacy Python scanner and PyInstaller packaging, unrelated website and documents, local SDKs and build tools, caches, binaries, installers, and previous build outputs. The original project folder is preserved.

Before publishing a binary release:

1. Repository owner: vineetkr. Visibility: public.
2. Project license: GPL-3.0-only, selected by the owner on 2026-11-25. Preserve dependency notices.
3. Resolve any failed validation recorded below and verify real-device behavior before calling a binary release tested.
4. Build an installer from this source and attach it to a versioned GitHub Release. Do not reuse an old installer as if it represented this source.

The public GitHub repository was created on 2026-11-25. This source publication includes documentation and five demo screenshots; it is not a binary release.

## Validation on 2026-11-24

- Restored the solution dependencies successfully using .NET SDK 8.0.425.
- Release solution build passed with zero warnings and zero errors.
- Existing test suite passed: 27 passed, zero failed, zero skipped (including loopback).
- Staged whitespace check passed.
- Reviewed source matches for credential-related strings; found runtime credential handling and example placeholders. A scan for common GitHub/OpenAI/AWS token and private-key patterns found no matches. This is a limited preparation check, not a security audit.
- Source, documentation and five demo screenshots are included; generated build files are ignored.
- Real-camera scanning, streaming, installer generation, and the desktop launch issue reported earlier were not verified or resolved by this preparation.

The repository uses branch `main`. Compiled installers are excluded from source control.

## Portfolio and demo update

Added an isolated offline demo, five labeled WPF application renders, architecture and user guides, development/release instructions and a portfolio case study. Demo fixtures use fictional documentation addresses. The full test suite now contains 27 passing tests.
