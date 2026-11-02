# Script distribution implementation plan

Goal: Extend the existing Catalog and updater with single-file C# script distribution, immutable versions, explicit updates and rejection of local edits.

Architecture: A scripts/script-index.json contains complete versioned manifests and relative file URLs. A manifest contains schemaVersion, id, version, fileName, description, author, license, amiVersionRange, sha256 and size. Local .gurux.json sidecars retain the installed manifest, index source and original bytes. Rejection restores those original bytes, preserving edits in a unique backup. No script is executed by this feature.

- [x] Add tests for immutable publication, unsafe names, UTF-8 content and index persistence.
- [x] Add Python script registry and bounded JSON upload endpoint; retain standalone script files and manifests.
- [x] Add .NET models/service and tests for verified download, compatibility, local edit detection, explicit update and rejection with backup.
- [x] Add script CLI commands and usage examples.
- [x] Run .NET and Python checks and inspect changes.

The user explicitly deferred AMI changes. The AMI-facing service is implemented in the updater; no AMI source was modified.

Validation: solution build with no warnings; 7 .NET tests and 58 Catalog tests passed. Independent review identified canonical NuGet validation and installation overwrite race; both corrected and covered by regression tests.
