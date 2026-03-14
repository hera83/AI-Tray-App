# Changelog

Alle væsentlige ændringer i projektet logges her.

Formatet følger løst principperne fra Keep a Changelog og Semantic Versioning.

## [Unreleased]

- Ingen ændringer endnu.

## [1.1.0] - 2026-03-13

### Changed

- VS Code launch-konfiguration peger nu på den korrekte `AIAssistent.dll` outputfil.
- Autostart holdes nu i sync mellem installerens Startup-genvej og appens egen Windows-opstart-indstilling.
- Installer bevarer altid eksisterende brugerdata ved installation og opgradering.
- Alle appens dropdowns åbner nu ved klik på hele feltet, ikke kun på pilen.

## [1.0.0] - 2026-03-13

### Added

- Release publish-profil for `win-x64` self-contained distribution.
- Inno Setup installer med Startmenu-genvej, valgfri skrivebordsgenvej og uninstall support.
- Valgfri autostart-opgave i installer.
- Valgfri nulstilling af lokal app-data ved installation (fresh database).
- Scripts til publish og installer-build.

### Changed

- Rebrand fra testnavn til officielt navn: **AI Assistent**.
- Produktnavn, tray-visning, installer-metadata og executable-navn opdateret til rebrand.
- App-data-sti ændret til `%APPDATA%/AIAssistent`.

### Security

- Installer ekskluderer database- og logfiler fra publish-payload for at undgå utilsigtet medpakning af lokale data.
