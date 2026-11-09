# ARES-Launcher

Player app for ARES servers. It keeps a list of servers, builds each server's bootstrap config
(`server.bin`) from the player's **own** installation at launch time and starts the **original**
game with `-textconfig -IniBootstrap=<server.bin>`. No game files, Steam settings or game memory are
modified, and ordinary Steam launches keep connecting to the official servers.

> **Unofficial project.** ARES is independent and is not affiliated with, endorsed by or sponsored
> by Embark Studios. ARC Raiders and all related names and trademarks belong to their respective
> owners. ARES works only with an installed, unmodified copy of the game.

## Status

- Verified on Linux (Proton, the game's normal prefix): a launcher-started game signs in to an
  ARES-Host, reaches the main menu and joins a Dam raid on ARES-Raid.
- `server.bin` is extracted and generated from the installed game; Steam, game and Proton folders
  are detected automatically. Reading the game's encrypted archive needs its archive keys, which
  the launcher fetches from the chosen ARES-Host (`GET /ares/v1/archive-keys`) unless they are
  entered in Settings. The launcher itself contains no game keys.
- The server list checks each server every 15 s (`GET /ares/v1/status`): online/offline, latency,
  raids active/capacity, players in raids, name and message of the day, and a warning when the
  server's game build differs from the installed one.

Limitations:

- Windows launching has not been tested yet.
- Tested on one PC only (servers on loopback or its own LAN address).
- Settings: "Shared" uses the game's normal Proton prefix / user folder, so options and key
  bindings are shared with official play. Graphics are not read from `GameUserSettings.ini` under
  `-IniBootstrap`; adding graphics settings to `server.bin` is planned. "Copy of official" and
  "Default" (separate settings per profile) are not implemented yet.

## Layout

- `src/Ares.Launcher.Core`: pak/container reading, compiled-config rewrite, profiles,
  Steam/Proton detection and game start.
- `src/Ares.Launcher.App`: Photino.Blazor desktop UI (Servers, Settings).
- `tools/Ares.Launcher.Verify`: checks archive reading and `server.bin` generation against a stock
  install of the supported game build, plus address parsing and crypto self-tests.

## ARES repositories

| Repository | What it is |
| --- | --- |
| [ARES-Server](https://github.com/NobodyByName/ARES-Server) | The servers: ARES-Host (backend: sign-in, main menu, matchmaking) and ARES-Raid (runs the raid) |
| ARES-Launcher | This player app: server list; starts the stock game for the chosen server |

ARES supports one game build at a time: **ARC Raiders 2.4.1, Steam build 26439197**. The launcher depends on no
ARES-Server code and works with any ARES-Host for the game build installed on the player's machine.

## Requirements

- The .NET 10 SDK (version in `global.json`) to build and run from source.
- An installed copy of the supported game build to run the launcher and its self-checks.
- The game's archive keys: provided by the ARES-Host you play on, or entered in Settings. The
  self-checks take them as a file.
- Linux: Steam with Proton, the game launched once from Steam (this creates its Proton prefix), and
  WebKitGTK 4.1 for the window (often packaged as `webkit2gtk-4.1` or `libwebkit2gtk-4.1-0`).
- Windows: not tested yet.

## Build and run

```sh
dotnet build ARES-Launcher.slnx
dotnet run --project tools/Ares.Launcher.Verify -- --keys <archive keys file> [<game directory>]
dotnet run --project src/Ares.Launcher.App
```

The verifier uses the auto-detected Steam install unless a game directory is given, and stops if the
installed build is not the supported one. Its checks need no server.

In the launcher, add a server by address (`host`, `host:port` or `http://host:port`; the default
port is 18080), enter the account name and password the server's operator gave you, then press
Play. Game, Steam and Proton folders can be overridden in Settings.

Each Play signs in to the server with SRP-6a (`POST /ares/v1/sign-in/start` and `/finish`): the
password never leaves your computer and is never stored; only the account name is remembered per
server. The server returns a launch key for this one game start, which goes into that server's
`server.bin` as the game's client secret.

## Configuration and data

Data lives in `~/.local/share/ARES-Launcher` (Linux) or `%APPDATA%\ARES-Launcher` (Windows):
`profiles.json`, `settings.json`, `profiles/<id>/server.bin`. `XDG_DATA_HOME` overrides the Linux
base directory.

The desktop executable is `ARES-Launcher` (`ARES-Launcher.exe` on Windows). Assemblies, namespaces
and embedded resource names use `Ares.Launcher`. Builds map authored source paths to
`/_/ARES-Launcher/`.

## When the game updates

Steam updates ARC Raiders automatically, and ARES supports one build at a time. After an update:

- ARES-Raid refuses players whose game build differs from the supported one (connection handshake).
- ARES-Launcher warns when a server's game build differs from the installed one.
- Players on the new build cannot join until the server runs an ARES release for it. Players update
  ARES-Launcher if that release includes a new one.

For a new build, run the verifier with the new build's archive keys (it reports keys that do not open
the game archives) and check that a generated `server.bin` works with the new game. Then update the
supported build id and the two expected fingerprints in `tools/Ares.Launcher.Verify`.

## Versions

Versions are release dates with a counter: `YYYYMMDDNN`, for example `2026110201` for the first
release on 2 November 2026 and `2026110202` for the second. The version is set in one place,
`AresVersion` in `Directory.Build.props`. It is the only version shown to people. The .NET assembly
and file versions stay `1.0.0.0`, because .NET limits each version part to 65534.

## Contributing

See [CONTRIBUTE.md](CONTRIBUTE.md).

## License

ARES contributions in this repository are licensed under the GNU General Public License,
version 3 or (at your option) any later version (`GPL-3.0-or-later`). See [LICENSE](LICENSE).
Third-party material retains its applicable licenses and notices.
