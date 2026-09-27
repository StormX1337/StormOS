# Validation tests

Build validation runs in `scripts/13-validate-image.ps1` on every build (ISO, hash, El Torito BIOS/UEFI entries,
install.wim/boot.wim, Storm files and answer-file safety inside the image). Its pure checks are unit tested in
`tests/build/Iso.Tests.ps1` and `tests/build/Validation.Tests.ps1`.
