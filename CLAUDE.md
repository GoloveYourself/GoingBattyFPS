# Notes for Claude (and anyone editing this repo)

Going Batty — Unity 6.3 LTS multiplayer FPS where players are blind and see via voice-driven sonar. See README.md for the design.

## How to ship an update to every player
1. Make the code change.
2. Bump `VERSION` (semver, e.g. `0.3.1` → `0.3.2`). The game reads it into `PlayerSettings.bundleVersion` via `Assets/Editor/ProjectSetup.cs`.
3. Commit and push to `main`. `.github/workflows/release.yml` builds Windows and publishes release `v<VERSION>` with `GoingBatty-Windows.zip`.
4. Installed games see the new release on startup and offer to update.
Without a VERSION bump, nothing is released. The commit message becomes the release notes, so make it player-readable.

Note: hosts reject clients on a different version, so every gameplay change needs everyone on the same release.

## Conventions
- No scenes/prefabs: everything is built in code from `Core/Bootstrap.cs`. Don't add inspector-wired assets.
- All gameplay numbers live in `Core/Tuning.cs` — adjust feel there first.
- Network: only `Net/` touches Netcode/Unity services. Messages are `M` ids in `Game/MatchTypes.cs`, serialized with `ByteWriter/ByteReader`. Host is authoritative for kills/score/respawn/clock/teams/box; players are authoritative for their own movement. When adding a message, handle it in `GameSession.OnHostReceive` (client→host) or `OnClientReceive` (host→all).
- Maps: ASCII grids in `Game/MapBuilder.cs` (left half, mirrored). Keep hallways 2 cells (3 m) wide; raised floors are +1.5 m with 2-cell ramps.
- Shaders in `Assets/Resources/Shaders` (Built-in render pipeline, unlit only).
- Unity Cloud IDs for builds: `Assets/Editor/ServicesConfig.cs`.
- Repo owner/name for the updater: `Assets/Scripts/Update/BuildInfo.cs`.
- There is no Unity in the Claude environment: code can't be compiled there. Be careful with APIs and ask the user for console errors after changes.
