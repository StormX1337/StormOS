# VM tests

`scripts/14-test-vm.ps1` boots StormOS.iso in a temporary VM and captures screenshots:

- **QEMU** (Windows or Linux): UEFI (OVMF/edk2), KVM/WHPX when available, otherwise TCG. CI runs this on every
  pipeline build (`.github/workflows/storm-os.yml`).
- **Hyper-V**: Generation 2, Secure Boot (Microsoft Windows template) and a virtual TPM, so Windows 11 requirements
  are met without bypasses. Implemented, not yet exercised in CI (hosted runners have no Hyper-V).

The boot test ends early once the display is stable (three identical, non-blank frames), e.g. at Windows Setup.

## Manual installation test

Windows 11 Setup requires TPM 2.0 and Secure Boot; STORM OS never bypasses these checks. Install in a compliant VM:

1. Hyper-V (Windows 11 Pro/Enterprise): New VM, **Generation 2**, 4 GB RAM, 64 GB disk, Security: Secure Boot with the
   *Microsoft Windows* template and **Enable Trusted Platform Module**; attach StormOS.iso; start and press a key.
   (VirtualBox 7 and VMware Workstation 17 also offer TPM 2.0 + Secure Boot.)
2. Install normally: choose the disk yourself, accept the license, sign in, make the privacy choices.
3. Check on the desktop: Storm wallpaper and dark mode (Settings > Personalization > Themes shows "STORM OS"),
   Settings > System > About shows STORM OS as manufacturer and "Storm customization of <edition>", taskbar without
   Task View/Widgets, Game Mode on, `C:\ProgramData\StormOS\Config\storm-image.json` present.
4. With `-EnableStormApps`: STORM OS app in Start, StormOSService running, `%ProgramData%\StormOS\Logs\storm-apps-install.log`.

## Automated installation test (CI, Linux + KVM)

`tests/vm/Invoke-StormVmInstallTest.ps1` installs StormOS.iso in a Windows 11-compliant VM: KVM, UEFI Secure Boot with
Microsoft keys (OVMF secboot), TPM 2.0 (swtpm), 6 GB RAM, 80 GB disk. No requirement check is bypassed.

- The test-only answer file `install/autounattend.xml` is attached as a **separate** virtual CD. StormOS.iso is used
  unchanged and never carries an answer file (validation check "No automatic answer file on media").
- `install/storm-vm-tour.ps1` runs at the first sign-in of the test account, opens desktop, Start, Settings (About,
  Personalization, Themes, Gaming) and File Explorer, and signals each screen over COM1; the host captures
  `installed-NN-<screen>.png`. The VM disk is deleted afterwards.
- CI (`vm-install` job): the Windows job builds the ISO and encrypts it for the Linux job (AES-256 with a random key,
  wrapped with the Linux job's one-time RSA key). The encrypted artifact lives at most one day and is deleted as soon
  as the Linux job has it; the decrypted ISO never leaves that runner. Only screenshots and logs are published
  (`refs/ci/storm-os-vm`).
