# Theme

`StormOS.theme` is generated during the build (`New-StormThemeContent`, `scripts/05-apply-branding.ps1`) from
`config/branding/branding.json`: Storm wallpaper, dark system and app mode, Storm accent color, Windows default
sounds. It becomes the default theme for new users (`InstallTheme` + the answer file's `CustomDefaultThemeFile`).
