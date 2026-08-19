# Developing

How to build the Windows app from source, and how the released package is put
together. [App/README.md](App/README.md) covers how the app works inside and how
to run its tests.

## Building From Source

Requires the .NET SDK.

```
dotnet build "App/MCAANewsletter/MCAANewsletter.csproj" -c Release
```

Output lands in `App/MCAANewsletter/bin/Release/MCAA Newsletter.exe`. It has no
dependencies beyond the framework, so that one file is the whole program.

The project pulls `Microsoft.NETFramework.ReferenceAssemblies`, so it also
compiles on macOS and Linux. The resulting `.exe` still only runs on Windows.

A locally built `.exe` is unsigned, so SmartScreen warns on first run. The
released MSIX is signed and does not.

## Packaging

The app ships as an MSIX, signed by Circuit Stitch through Azure Artifact
Signing, with a companion `.appinstaller` that keeps installed copies up to
date. [RELEASING.md](RELEASING.md) covers how a release is built and published.
