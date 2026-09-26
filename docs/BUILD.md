# Building STORM OS

## Requirements

| Part | Requirements |
|---|---|
| Desktop (.NET) | .NET SDK 10.0.1xx (`global.json`), Windows 10 22H2+/Windows 11 for the WinUI app and installer; Linux/macOS can build and test the portable projects |
| Installer | Windows, WiX Toolset SDK 5 (restored as an MSBuild SDK package) |
| Cloud | Node.js 22.12+, pnpm 10 (`corepack enable`), PostgreSQL 16 for the end-to-end tests |

## Desktop

```powershell
dotnet build StormOS.slnx -c Release          # everything incl. the WinUI app (Windows)
dotnet test  StormOS.slnx -c Release

dotnet build StormOS.Portable.slnf            # everything except the WinUI app (any OS)
dotnet test  StormOS.Portable.slnf
```

The build treats warnings as errors and runs the .NET analyzers (`latest-recommended`). Package versions are
central (`Directory.Packages.props`).

Mock telemetry for UI work exists only in Debug builds compiled with `-p:StormMockMode=true`, and is active only
when `STORMOS_ENVIRONMENT=Development` and `Development:MockMode=true`; the app then shows a permanent *mock data*
banner. Building Release with `StormMockMode=true` fails (`Directory.Build.targets`), so production builds cannot
contain mock data.

## Installer

```powershell
./scripts/publish.ps1 -Runtime win-x64 -Version 1.2.0            # StormOS-1.2.0-x64.msi + StormOS-Setup-1.2.0-x64.exe (+ .sha256)
./scripts/publish.ps1 -Runtime win-arm64 -Version 1.2.0
./scripts/publish.ps1 -Runtime win-x64 -Version 1.2.0 -SkipInstaller   # publish folder only
```

The script publishes the app, service and CLI self-contained into one folder, applies the shared
`installer/appsettings.json`, removes PDBs, optionally signs (`SIGN_CERT_THUMBPRINT`), builds the MSI
(`installer/`) and the setup executable (`installer/bundle/`, a WiX Burn bundle with the STORM OS theme that embeds
the MSI) and writes their SHA-256 files. When signing, the bundle engine is detached, signed and reattached before the
bundle itself is signed.

`scripts/installer-preview.ps1 -Setup <exe>` is the installer smoke test used by CI on a disposable Windows runner: it
installs through the setup UI (UI Automation), checks the running service, the files, the single Apps & Features
entry and `storm status` over the named pipe, captures screenshots of the setup pages and the first app screens, and
uninstalls silently. Do not run it on a machine where you want to keep STORM OS installed.

## Cloud

```bash
cd cloud
pnpm install --frozen-lockfile
pnpm build        # types → validation → database (prisma generate) → api, web, admin
pnpm typecheck
pnpm test         # + TEST_DATABASE_URL for the API end-to-end suite
```

Container images: `infrastructure/docker/api.Dockerfile` and `next.Dockerfile` (`--build-arg APP=web|admin`),
build context `cloud/`.
