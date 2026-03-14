# AI Assistent (WPF)

[![Latest Release](https://img.shields.io/github/v/release/hera83/AI-Tray-App?display_name=tag)](https://github.com/hera83/AI-Tray-App/releases)
![.NET](https://img.shields.io/badge/.NET-net10.0--windows-512BD4)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6)

Native Windows WPF AI-assistent app (C#/.NET, MVVM) med moderne, minimal chat-UI, themes, modelvalg og lokal historik.

## Projektstatus (marts 2026)

- Frameless hovedvindue med runde hjørner, transparent host og custom drag-områder.
- Indstillingsvindue matcher hovedvinduets visuelle shell og interaction-pattern.
- Chat med streaming-svar, markdown-rendering og typing-indikator i selve korrespondancen.
- Modelhåndtering med global standardmodel i indstillinger + sessionsmodel i hovedvinduet.
- Lokal persistence af settings/chat (SQLite) samt startup- og fejl-diagnostics i log.
- Tray-first adfærd med notifications og hurtig adgang via system tray ikon.

## Kørsel & udvikling

- Build: `dotnet build TrayApp.csproj -c Debug -f net10.0-windows`
- App icon/tray icon: `Assets/app.ico`
- Logfil: `%APPDATA%/AIAssistent/logs/ai-assistent.log`
- Versionslog: `CHANGELOG.md`

## Distribution (Windows installer)

Projektet er sat op til Release publish (self-contained, `win-x64`) + Inno Setup installer.

### 1) Build publish-output (Release)

- Hurtig vej (script): `.\scripts\publish-win-x64.ps1`
- Direkte CLI: `dotnet publish TrayApp.csproj -c Release -f net10.0-windows -p:PublishProfile=WinX64SelfContained`

Output mappe:

- `artifacts/publish/win-x64`

### 2) Build installer (.exe)

Forudsætning: Inno Setup 6 installeret (`ISCC.exe`).

- Standard (kører publish + installer): `.\scripts\build-installer.ps1`
- Kun installer fra eksisterende publish-output: `.\scripts\build-installer.ps1 -SkipPublish`
- Overstyr version i installer: `.\scripts\build-installer.ps1 -AppVersion 1.2.3`
- Forberedt til signering senere: `.\scripts\build-installer.ps1 -SignToolCmd 'signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a $f'`
- Direkte Inno Setup (uden script): `& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 .\scripts\installer\TrayApp.iss`

Hvis PowerShell blokerer `.ps1` scripts pga. Execution Policy, så kør midlertidigt i aktiv session:

- `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass`

Installer-script:

- `scripts/installer/TrayApp.iss`

Installer output mappe:

- `artifacts/installer`

### Installer-funktioner

- Bruger app-ikon som setup-ikon og uninstall-display-ikon
- Sætter versionsinfo i setup metadata (`AppVersion`)
- Understøtter installation både pr. bruger og for alle brugere (valg i installer)
- Bevarer eksisterende brugerdata i `%APPDATA%/AIAssistent` ved installation og opgradering
- Opretter Startmenu-genvej
- Tilbyder valgfri skrivebordsgenvej (unchecked som standard)
- Tilbyder valgfri autostart-genvej (unchecked som standard)
- Understøtter uninstall via standard Windows uninstall og lukker kørende app ved install/uninstall

## Design system og themes

Appen bruger nu et fælles designsystem med centraliserede XAML dictionaries:

- `Themes/Theme.Dark.xaml` og `Themes/Theme.Light.xaml` er entrypoints for hvert theme.
- `Resources/Colors.*.xaml` indeholder farvetokens pr. theme.
- `Resources/Brushes.xaml` mapper farver til genbrugelige brushes via `DynamicResource`.
- `Resources/Spacing.xaml` samler sizing, spacing og thickness-tokens.
- `Resources/Corners.xaml` samler `CornerRadius`-tokens.
- `Resources/Typography.xaml` samler font- og teksttokens/stilarter.
- `Styles/Controls.xaml` samler standard control styles (buttons, inputs, cards, separators, osv.).

Theme vælges via gemte settings og anvendes ved startup gennem `ThemeManager`.

### Centrale theme keys

Farvetokens ligger i både `Resources/Colors.Dark.xaml` og `Resources/Colors.Light.xaml` med samme key-navne:

- `Color.Theme.WindowBackground`
- `Color.Theme.SurfaceBackground`
- `Color.Theme.SurfaceElevatedBackground`
- `Color.Theme.TextPrimary`
- `Color.Theme.TextSecondary`
- `Color.Theme.TextMuted`
- `Color.Theme.Accent`
- `Color.Theme.AccentHover`
- `Color.Theme.Border`
- `Color.Theme.Divider`
- `Color.Theme.InputBackground`
- `Color.Theme.InputForeground`
- `Color.Theme.ButtonPrimaryBackground`
- `Color.Theme.ButtonPrimaryForeground`
- `Color.Theme.ButtonSecondaryBackground`
- `Color.Theme.ButtonSecondaryForeground`
- `Color.Theme.Message.UserBackground`
- `Color.Theme.Message.AssistantBackground`
- `Color.Theme.Feedback.Error`
- `Color.Theme.Feedback.Warning`
- `Color.Theme.Feedback.Success`
- `Color.Theme.Feedback.Info`

Disse mappes i `Resources/Brushes.xaml` til `Brush.Theme.*` keys, som bruges direkte af views og styles.

### Hvorfor denne struktur

- Semantiske keys beskriver rolle i UI i stedet for konkrete farvenavne.
- Samme keys i light/dark gør theme-skift sikkert uden at ændre views.
- Alle control styles peger på tokens, så visuel konsistens styres centralt.
- Nye komponenter kan style’s ensartet ved kun at bruge `Brush.Theme.*` + fælles spacing/typografi.

### Globale control styles

`Styles/Controls.xaml` indeholder nu implicit globale styles for:

- `Window`, `UserControl`
- `Grid`, `StackPanel`, `DockPanel`
- `TextBlock`, `Label`
- `Button`, `ToggleButton`
- `TextBox`, `PasswordBox`, `ComboBox`
- `ListBox`, `ListBoxItem`, `ListView`, `ListViewItem`
- `ScrollViewer`, `ScrollBar`, `Thumb`
- `CheckBox`
- `Menu`, `MenuItem`, `ContextMenu`
- `ToolTip`
- `Separator`, `Border` + card base style (`CardBorderBaseStyle`)

Derudover findes afledte app-specifikke styles (`PrimaryButtonStyle`, `SecondaryButtonStyle`, `InputTextBoxStyle`, `InputPasswordBoxStyle`, `StatusCardStyle`, `ErrorBannerStyle`) som bygger ovenpå de globale base styles.

### States og interaktion

States er centraliseret via tokens:

- `Color.Theme.State.HoverBackground`
- `Color.Theme.State.PressedBackground`
- `Color.Theme.State.DisabledBackground`
- `Color.Theme.State.DisabledForeground`
- `Color.Theme.State.FocusRing`

Disse mappes til `Brush.Theme.State.*` og bruges i templates/triggers for hover, pressed, keyboard focus, checked/selected og disabled states.

Resultatet er ens spacing/typografi, tydelige aktive states, og et moderne, roligt desktop-look uden standard-WPF-udtryk på de vigtigste controls.

### Chat UI

Chatoplevelsen er nu stylet særskilt i `Styles/Chat.xaml` og bruger kun theme-baserede tokens.

Særskilt stylede chatdele:

- samtaleflade / conversation surface
- message bubbles
- role badges
- timestamps
- empty state card
- composer / inputområde
- floating action-knap (`Gå til nyeste`)
- typing/loading dots

User og assistant adskilles visuelt ved:

- forskellig placering (højre/venstre)
- forskellige bubble-baggrunde
- forskellige foreground/border-tokens
- små role badges over beskederne

Designet holdes konsistent med resten af appen ved at chatlaget stadig bygger på de samme foundations:

- `Brush.Theme.*` tokens
- fælles spacing i `Resources/Spacing.xaml`
- fælles corner radii i `Resources/Corners.xaml`
- globale control styles i `Styles/Controls.xaml`

På den måde får chatten sin egen karakter uden at bryde den samlede visuelle identitet.

### Theme-valg i settings

Theme-valg gemmes nu som `ThemeMode` i `AppSettings` og persisteres af `SettingsService` sammen med de øvrige app-indstillinger.

Ændringer i settings model/service:

- `ThemeMode` enum med værdierne `Dark` og `Light`
- `AppSettings.ThemeMode`
- load/save af `ThemeMode` i `SettingsService`
- `SettingsViewModel` eksponerer `AvailableThemeModes` og `SelectedThemeMode`

Teknisk theme-skift:

- `Infrastructure/ThemeManager.cs` udskifter den aktive theme dictionary i `Application.Resources.MergedDictionaries`
- ved startup læser `App.xaml.cs` det gemte `ThemeMode` og anvender det, før hovedvinduet oprettes
- når brugeren gemmer i settings, anvendes valgt theme straks via `ThemeManager.ApplyTheme(...)`

Det betyder, at både startup og runtime-skift nu er understøttet i den nuværende arkitektur.

### Central ThemeManager

Theme-håndteringen er nu samlet i:

- `Infrastructure/IThemeManager.cs`
- `Infrastructure/ThemeManager.cs`

Manageren er ansvarlig for at:

- initialisere aktivt theme ved startup
- anvende et valgt theme
- skifte mellem `Light` og `Dark`
- loade korrekt theme dictionary
- fjerne gamle theme dictionaries før nyt theme indsættes

Resource dictionaries swappes ved at manageren:

1. finder alle merged dictionaries, som matcher `Theme.Dark.xaml` eller `Theme.Light.xaml`
2. gemmer første indsætningsposition
3. fjerner alle eksisterende theme dictionaries i omvendt rækkefølge
4. indsætter præcis én ny dictionary for det valgte theme

Det undgår dubletter og overlappende resources, fordi appen aldrig ender med både light og dark theme indlæst samtidig.

Løsningen passer godt til appen, fordi WPF allerede er resource-dictionary-drevet. Ved at holde theme-skift i én manager bliver både startup, settings og fremtidige theme-relaterede features enklere og mere forudsigelige.
