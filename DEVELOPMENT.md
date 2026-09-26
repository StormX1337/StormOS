# Development

## Repository layout

```
src/            .NET: Core, Security, Infrastructure, Services, Windows, Hardware, Performance, Network,
                Games, Optimization, Benchmark, Overlay, Service, Cli, App (WinUI 3)
tests/          xUnit v3 test projects (unit + integration)
profiles/       bundled game profiles (JSON)
cloud/          pnpm monorepo: apps/api, apps/web, apps/admin, packages/*
installer/      WiX MSI
scripts/        publish/release helpers
docs/           subsystem documentation
```

## Desktop loop

1. Build: `dotnet build StormOS.slnx` (Windows) — or `StormOS.Portable.slnf` elsewhere.
2. Run the service in a console as administrator (it behaves like the Windows service but logs to the console):
   ```powershell
   $env:STORMOS_ENVIRONMENT = 'Development'
   dotnet run --project src/StormOS.Service
   ```
   In Development the app accepts an unverified pipe owner so it can talk to a console-hosted service. Trusted-client
   checks still apply: run the app from the same output folder as the service (e.g. publish both into one folder
   with `scripts/publish.ps1 -SkipInstaller`) to exercise administrator operations.
3. Run the app: `dotnet run --project src/StormOS.App` (non-elevated).
4. CLI: `dotnet run --project src/StormOS.Cli -- status`.

Mock telemetry for UI work: build Debug with `-p:StormMockMode=true`, set `STORMOS_ENVIRONMENT=Development` and
`Development:MockMode=true`. The UI marks it permanently; Release builds refuse the flag.

Data locations can be redirected for experiments with `STORMOS_DATA_ROOT`.

## Conventions

- Warnings are errors; analyzers `latest-recommended`; public APIs of libraries carry XML docs.
- Windows-specific code lives behind interfaces in `StormOS.Core`; tests use in-memory fakes
  (`tests/StormOS.Optimization.Tests/Fakes.cs`).
- Never show invented values: use `Reading.Unavailable(reason)`.
- Every system change is an `IOptimizationRule` executed by the engine — no ad-hoc registry writes in UI code.
- New IPC operations need a policy, validation and tests (`docs/IPC.md`).
- User-facing text: short, concrete, states risk and reversibility.

## Tests

```powershell
dotnet test StormOS.slnx                       # all
dotnet test tests/StormOS.Integration.Tests    # desktop → IPC → service
```

Cloud: `cd cloud && pnpm test` (set `TEST_DATABASE_URL` for the API end-to-end suite).
