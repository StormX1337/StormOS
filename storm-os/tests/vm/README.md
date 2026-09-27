# VM tests

`scripts/14-test-vm.ps1` boots StormOS.iso in a temporary VM and captures screenshots:

- **QEMU** (Windows or Linux): UEFI (OVMF/edk2), KVM/WHPX when available, otherwise TCG. CI runs this on every
  pipeline build (`.github/workflows/storm-os.yml`).
- **Hyper-V**: Generation 2, Secure Boot (Microsoft Windows template) and a virtual TPM, so Windows 11 requirements
  are met without bypasses. Implemented, not yet exercised in CI (hosted runners have no Hyper-V).

A full unattended installation test (disk setup inside the VM, first boot to the desktop) is **NOT IMPLEMENTED** yet.
It will use a *test-only* answer file on separate virtual media; StormOS.iso itself never partitions disks.
