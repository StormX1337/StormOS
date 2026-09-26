# Contributing

Thanks for helping make STORM OS better.

## Ground rules

Contributions must respect the product's safety rules (see [SECURITY.md](SECURITY.md)): no kernel drivers, no
injection or hooks into games, no anti-cheat interaction, no disabling of Windows security features, no system
change without user confirmation and a working rollback, no fabricated measurements. Pull requests that add such
behaviour will be declined.

## Workflow

1. Open an issue for larger changes first (new rules, IPC operations, cloud endpoints, profile schema changes).
2. Branch from `main`, keep changes focused, and add tests: unit tests for logic, in-memory fakes for Windows
   access, integration tests for IPC, end-to-end tests for API changes.
3. Run locally before pushing:
   ```powershell
   dotnet build StormOS.slnx -c Release && dotnet test StormOS.slnx -c Release
   cd cloud; pnpm install; pnpm build; pnpm typecheck; pnpm test
   ```
4. Describe *what* and *why* in the pull request, including risk and rollback behaviour for optimizations.

## Game profiles

Profiles are the easiest way to contribute. Follow [docs/PROFILES.md](docs/PROFILES.md), validate against
`docs/schemas/game-profile.schema.json`, reference only existing rules, and explain every recommendation in
`reason`/`detail`. Do not add launch arguments or settings you have not verified for the current game version.

## Reporting bugs

Include Windows version/build, STORM OS version, what you did, what you expected, and the relevant log excerpt
(logs are redacted, but check before posting). Security issues: see [SECURITY.md](SECURITY.md).
