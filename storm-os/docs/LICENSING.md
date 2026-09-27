# Licensing

**STORM OS is a Storm customization and deployment environment built on Windows.** It does not own, replace or
relicense Windows.

## Windows

- STORM OS starts from a Windows 11 image you obtained legitimately (Microsoft download, volume licensing,
  OEM/System Builder, Visual Studio subscription, or evaluation media for evaluation).
- Every installation needs its own valid Windows license and activates normally; STORM OS contains no product keys
  and does not modify activation.
- Redistributing a Windows image (including StormOS.iso, which contains Windows) requires distribution rights from
  Microsoft, e.g. under the OEM System Builder license or an enterprise agreement. Without them, use StormOS.iso only on
  your own licensed devices.
- The Microsoft Software License Terms shown during Windows Setup apply to Windows. STORM OS does not hide or alter them.
- Microsoft, Windows and Windows 11 are trademarks of the Microsoft group of companies. STORM OS uses them only to
  state what it is built on ("built on Windows 11"), never as its own brand. Microsoft logos and Windows Setup assets
  are not modified.

## CI

The pipeline is validated in CI with Microsoft's Windows 11 Enterprise **evaluation** media, downloaded from
Microsoft at build time. Neither the evaluation media nor the StormOS.iso built from it is published; CI publishes
reports, logs and screenshots only.

## STORM OS components

| Component | License |
|---|---|
| Build engine, configuration, documentation | STORM OS project license (repository root) |
| Storm artwork (`branding/`) | original STORM OS artwork, same license |
| Storm apps and service (`../src`) | STORM OS project license; third-party packages are listed with their licenses in the .NET/NuGet and pnpm lockfiles |
| Liberation Sans (used at build time to render artwork, not redistributed) | SIL Open Font License 1.1 |
| Windows ADK tools (oscdimg, DISM) | used on the build machine under Microsoft's ADK license; not redistributed |

## Drivers

STORM OS does not bundle proprietary drivers. Storm apps detect driver state and link to the vendor's official
download pages instead.
