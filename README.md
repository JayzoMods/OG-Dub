# OG Dub

Publisher: **OG Digital Designs**.

A desk boombox for Windows. You aim it at one app, punch Rec, and a labelled cassette lands in a crate. Dub two tapes onto a mixtape. No cloud. No transcript. **Not a stream ripper.**

## Run

```powershell
dotnet test src\OgDub.slnx
dotnet run --project src\OgDub\OgDub.csproj
```

Private GitHub: [JayzoMods/OG-Dub](https://github.com/JayzoMods/OG-Dub). Stay private until Jayden says otherwise.

Cassettes: `%USERPROFILE%\Documents\OG Digital Designs\OG Dub\`

Settings: `%LOCALAPPDATA%\OG Digital Designs\OG Dub\`

## How to use

See [HOW-TO-USE.md](HOW-TO-USE.md).

## v1 stages

Work is gated in [V1-STAGES.md](V1-STAGES.md). v0.8.0 is Stage 7 plus polish: global Rec/Stop hotkeys, How-To tour, crate rename/favourites/folder, and midnight boombox chrome.

## Locks

- WPF .NET 8 + `OgDub.Core`
- Custom chrome boombox (not WPF-UI)
- NAudio **2.2.1** (NAudio 3 needs .NET 9)
- CommunityToolkit.Mvvm **8.4.2**
- Process loopback first; whole-mix fallback with an honest LCD
- Terms Accept / Decline
- `asInvoker` (no admin)

Do not develop this inside Software Planning.
