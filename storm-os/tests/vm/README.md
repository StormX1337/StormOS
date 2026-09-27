# VM tests

`scripts/14-test-vm.ps1` boots StormOS.iso in a temporary VM and captures screenshots:

- **QEMU** (Windows or Linux): UEFI (OVMF/edk2), KVM/WHPX when available, otherwise TCG. CI runs this on every
  pipeline build (`.github/workflows/storm-os.yml`).
- **Hyper-V**: Generation 2, Secure Boot (Microsoft Windows template) and a virtual TPM, so Windows 11 requirements
  are met without bypasses. Implemented, not yet exercised in CI (hosted runners have no Hyper-V).

The boot test ends early once the display is stable (three identical, non-blank frames), e.g. at Windows Setup.

## Manual installation test (until CI can provide a virtual TPM)

Windows 11 Setup requires TPM 2.0 and Secure Boot; STORM OS never bypasses these checks. Install in a compliant VM:

1. Hyper-V (Windows 11 Pro/Enterprise): New VM, **Generation 2**, 4 GB RAM, 64 GB disk, Security: Secure Boot with the
   *Microsoft Windows* template and **Enable Trusted Platform Module**; attach StormOS.iso; start and press a key.
   (VirtualBox 7 and VMware Workstation 17 also offer TPM 2.0 + Secure Boot.)
2. Install normally: choose the disk yourself, accept the license, sign in, make the privacy choices.
3. Check on the desktop: Storm wallpaper and dark mode (Settings > Personalization > Themes shows "STORM OS"),
   Settings > System > About shows STORM OS as manufacturer and "Storm customization of <edition>", taskbar without
   Task View/Widgets, Game Mode on, `C:\ProgramData\StormOS\Config\storm-image.json` present.
4. With `-EnableStormApps`: STORM OS app in Start, StormOSService running, `%ProgramData%\StormOS\Logs\storm-apps-install.log`.

A full *automated* installation test is **NOT IMPLEMENTED** yet (needs a runner with a virtual TPM). It will use a
test-only answer file on separate virtual media; StormOS.iso itself never partitions disks.
