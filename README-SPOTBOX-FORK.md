# Spotbox — LibreSpotUWP for Xbox Developer Mode

A fork of **[LibreSpotUWP](https://github.com/megabytesme/LibreSpotUWP)** adapted to run on
Xbox in Developer Mode, so music keeps playing in the background while you're on the
dashboard or inside RetroArch.

## Enormous credit to [megabytesme](https://github.com/megabytesme)

Essentially all of this software is theirs. LibreSpotUWP is the hard part — a full Spotify
client for UWP with playlists, search, lyrics, offline downloads, gapless playback, SMTC
integration, and, critically, **librespot compiled as a UWP-safe native DLL**. That last
piece is what makes Spotify playable here at all.

It also builds on **[librespot](https://github.com/librespot-org/librespot)**, the open
source Spotify client library that implements the protocol.

This fork adds a small Xbox presentation layer on top. That's it. If you find this useful,
go and star [their repo](https://github.com/megabytesme/LibreSpotUWP) — the credit belongs
there.

## Licence

Upstream LibreSpotUWP is licensed **CC BY-NC-SA 4.0**
(Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International), and this fork
inherits it. The original `LICENSE.md` is retained unmodified. In practice:

- **Attribution** — credit megabytesme (see above, and keep it if you fork this further).
- **NonCommercial** — personal use only. Do not sell it or bundle it into anything you sell.
- **ShareAlike** — derivatives must carry the same licence, and changes must be stated.

## Changes made in this fork

Kept deliberately small and localised so they're easy to audit:

| File | Change |
| --- | --- |
| `LibreSpotUWP/Helpers/XboxExperience.cs` | **New.** All Xbox presentation setup lives here. |
| `LibreSpotUWP/App.xaml.cs` | One call to `XboxExperience.Apply()` in the constructor. |
| `LibreSpotUWP/LibreSpotUWP.csproj` | Registers the new file. |
| `LibreSpotUWP/Services/SpotifyAuthService.cs` | PKCE login falls back to `DefaultClientId`. |
| `LibreSpotUWP/OobePage.xaml.cs` | Always show browser sign-in; hide QR scanner on Xbox. |
| `LibreSpotUWP/Package.appxmanifest` | Fork identity — see below. |

### Presentation (`XboxExperience`, no-op off Xbox)

1. **Pointer mode.** Xbox enables mouse mode for UWP apps by default. The `UseMouseMode`
   constant selects between that and native XAML directional focus navigation.
2. **Full-bleed layout.** `ApplicationViewBoundsMode.UseCoreWindow`, so the UI uses the
   whole panel rather than only the TV title-safe rectangle.

> **Do not** call `ApplicationViewScaling.TrySetDisableLayoutScaling(true)` here. It reads
> like a safe-area opt-out but actually disables the scale factor that makes UI legible at
> 10 feet — the app then lays out at raw 1080p and the whole interface renders far too
> small. An earlier revision of this fork did exactly that; the comment in the file records
> why it must not come back.

### Login on Xbox

Upstream offers three sign-in routes, and on a console two of them are dead ends:

- **QR scanner** — captures a code via the *webcam*. An Xbox has none, so the button is
  hidden on the Xbox device family rather than left as a control that dead-ends.
- **Paste session details** — requires entering a long JSON blob, which is unusable with a
  controller.
- **Browser PKCE sign-in** — the only practical route, and this fork makes it work on a
  console. Three separate things were wrong:
  1. `BeginPkceLoginAsync` silently returned unless you had supplied your own client ID.
     It now falls back to `SpotifyConfig.DefaultClientId`.
  2. The button was hidden unless a client ID was set, and was authored *last* in a long
     scrolling list — so on a TV it sat below the fold and looked absent entirely. It is
     now always visible and promoted to the top on Xbox.
  3. The flow handed the URL to the system browser and waited to be re-activated through
     the `librespotuwp://` protocol handler. **Xbox Dev Mode apps do not reliably receive
     that activation**, so sign-in hung on "Waiting for Spotify to return..." forever.
     `XboxLoginPage` now hosts the same PKCE flow in an in-app WebView2 and intercepts the
     redirect directly, so nothing has to come back through the OS.

  Point 3 is why the fork needs **WinUI 2.8** (2.7.3 has no `WebView2` control) and hence
  `TargetPlatformMinVersion` 10.0.17763. That raises the floor from upstream's 16299 —
  irrelevant for Xbox, but it does mean the deploy set now carries `Microsoft.UI.Xaml.2.8`
  plus the Desktop VCLibs that WebView2 pulls in, and the output is a `.msix` rather than
  a `.appx`.

#### One button

On Xbox the sign-in screen shows a single **Sign in with Spotify** button that opens
`XboxLoginPage` (the in-app WebView2 flow above). The Login Helper, QR, paste and client-ID
options are hidden on Xbox (`OobePage.ApplyXboxSignInUi`), and the earlier LAN pairing
server (`XboxPairingServer`, plus its `privateNetworkClientServer` capability) was removed:
it depended on the desktop Login Helper, was never confirmed working on a console, and made
the screen confusing.

#### Playback authorization (upstream v1.0.5)

Upstream now authorizes playback separately from library access (`SpotifyPlaybackAuthService`:
the `streaming` scope, its own client ID, redirect `http://127.0.0.1:5588/login`, and a newer
`librespot.dll` that accepts `playback_credentials`). Upstream's UI for it opens the system
browser and asks you to paste the callback address, which cannot work on a console. On Xbox
`XboxLoginPage` therefore runs it as a second step in the same WebView2: after the normal
sign-in it navigates to the playback authorization page, intercepts the `:5588/login`
redirect and calls `CompleteBrowserAuthorizationAsync`. If playback later needs
re-authorizing, `PlaybackAuthorizationDialog` offers one **Authorize** button that reopens
`XboxLoginPage` in playback-only mode. The first redirect (`:8898/login`) is unchanged.

Upstream's account-compatibility warning is shown before sign-in. It does not fix the
audio-key block on newer accounts; that is still unsolved upstream (librespot #1649).

You can still enter your own client ID if you prefer; the built-in one is only a fallback.
Note it belongs to the upstream author's Spotify app registration — if you intend heavy or
long-term use, register your own at
[developer.spotify.com](https://developer.spotify.com/dashboard) with redirect URI
`librespotuwp://callback/` and paste the ID into the sign-in screen.

The package identity was changed so this installs alongside, rather than on top of, an
official LibreSpotUWP build:

```
Name      Spotbox.LibreSpotXbox      (was a GUID)
Publisher CN=Spotbox                 (was CN=MegaBytesMe)
```

## Building

Needs MSBuild with the UWP workload and Windows SDK 10.0.19041.0.

`librespot.dll` is **not** in the repo — upstream builds it from Rust. To avoid a Rust
toolchain, this fork uses the prebuilt DLL from an upstream release: extract
`librespot.dll` from the x64 `.appx` in
[LibreSpotUWP releases](https://github.com/megabytesme/LibreSpotUWP/releases) and drop it
in `LibreSpotUWP\`. Then:

```powershell
$msb = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
& $msb LibreSpotUWP.sln -t:Restore -p:Configuration=Release -p:Platform=x64
& $msb LibreSpotUWP.sln -t:Build   -p:Configuration=Release -p:Platform=x64 -p:AppxBundle=Never
```

Signing uses `LibreSpotUWP_TemporaryKey.pfx` (gitignored upstream). Generate a self-signed
cert whose subject matches the manifest `Publisher` — here `CN=Spotbox` — and export it
without a password.

## Deploying to Xbox

From the repo root:

```powershell
.\Deploy-ToXbox.ps1 -XboxIp <console-ip> -PackageDir .\Deploy-Xbox
```

Or via Device Portal at `https://<console-ip>:11443` — add the app `.appx` and attach all
four framework `.appx` files as dependencies **in the same submission**.

## Notes

- **Spotify Premium is required.** librespot cannot play on free accounts; Spotify gates
  protocol-level playback server-side. The app rejects free sign-ins.
- **No login helper needed on Xbox.** Upstream ships a separate PC helper for device
  families that can't run the browser login flow, but `OSHelper.SupportsBrowserSpotifyLogin`
  is true for `Windows.Xbox`, so login happens on the console.
- **This is an unofficial client** using a reverse-engineered protocol, which is contrary to
  Spotify's terms of service. Your call.
- Upstream notes crash/hang issues immediately after first login, sometimes needing a couple
  of restarts.

## Status

Built and packaged. **Not yet verified on real hardware** — whether `librespot.dll` runs
correctly under Xbox Dev Mode is unconfirmed. The evidence that it should is good
(`VCRUNTIME140_APP`, API-set-only imports, `XAudio2_9`, an explicit `IsXboxFamily` branch
upstream, and a `RingBuffer` audio backend that avoids WASAPI), but evidence is not a test.
