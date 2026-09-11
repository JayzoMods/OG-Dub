# OG Dub

A desk boombox for Windows. You aim it at one app, punch Rec, and a labelled cassette lands in a crate. Dub two tapes onto a mixtape. No cloud. No transcript. **Not a stream ripper.** Free for anyone who wants it, and a public portfolio piece by [Jayden O'Grady](https://ogdigitaldesigns.com.au) / OG Digital Designs — a demo of the work for possible employers, same setup as [OG Job Book](https://github.com/JayzoMods/og-job-book).

**Download:** [latest release](https://github.com/JayzoMods/OG-Dub/releases/latest) — unpack the zip and run `OgDub.exe`.
**Source:** [github.com/JayzoMods/OG-Dub](https://github.com/JayzoMods/OG-Dub)
**Guide:** [HOW-TO-USE.md](HOW-TO-USE.md)
**Contact:** [enquiries@ogdigitaldesigns.com.au](mailto:enquiries@ogdigitaldesigns.com.au)

## Run

```powershell
dotnet test src\OgDub.slnx
dotnet run --project src\OgDub\OgDub.csproj
```

Cassettes: `%USERPROFILE%\Documents\OG Digital Designs\OG Dub\`

Settings: `%LOCALAPPDATA%\OG Digital Designs\OG Dub\`

## v1 stages

Work is gated in [V1-STAGES.md](V1-STAGES.md). v0.9.0 adds optional **Record / Edit** tabs. Edit probes localhost (Ollama, LM Studio-style, LocalAI) for chat and generative audio. Record is unchanged if you never open Edit.

## Locks

- WPF .NET 8 + `OgDub.Core`
- Custom chrome boombox (not WPF-UI)
- NAudio **2.2.1** (NAudio 3 needs .NET 9)
- CommunityToolkit.Mvvm **8.4.2**
- Process loopback first; whole-mix fallback with an honest LCD
- Terms Accept / Decline
- `asInvoker` (no admin)
- Edit tab talks to `localhost` only (Ollama, LM Studio-style, LocalAI) — no cloud APIs, crate WAVs never leave the PC

Do not develop this inside Software Planning.
