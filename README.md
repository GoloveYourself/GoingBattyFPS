# Going Batty

A multiplayer first-person shooter where everyone is blind. You see only by **echolocation**: speak into your mic to send a sonar ping in front of you, scream to send a huge blast. Every player sees every pulse — so if you're facing the right way you'll catch other players' blasts, and where they came from.

Built in **Unity 6.3 LTS** (6000.3.x) for Windows PC.

## How it plays

| | |
|---|---|
| **See** | Talk → sonar ping (narrow cone, short range). Scream → sonar blast (wide cone, long range, slower to recharge). Walls, floor and ceiling always show as black boxes with faint outlines; players only appear as glowing "ghosts" when a wave passes over them. |
| **Hear** | Proximity voice chat — you hear nearby players talking and screaming from where they are. |
| **Shoot** | One-shot-kill blunderbuss (left click), ~1 s cooldown. Right click = blast-jump. Gunshots briefly reveal the shooter. |
| **Move** | Fast, Quake-style: bunny hop (hold Space), air strafe, blast-jump — tuned to feel like Ratz Instagib. Respawn in 2 s. |
| **Lobbies** | Up to 8 players, start with any number. Public, or Private with a password. Set your player name and lobby name; friends find you by name (or join code). |
| **Bots** | The host can add 0–7 AI bots in the lobby. They fill empty slots only — when a real player joins, a bot leaves to make room. Bots play blind too: they only know where you are if a sonar pulse, your gunshot, or the Box reveals you (or you're very close). Difficulty: `Bot*` values in `Tuning.cs`. |

### Modes
- **Deathmatch** — 1 point per elimination. First to the target wins, or the most when time runs out. Targets: 5/10/**15**/20/25/30. Time: 2/**5**/10 min/No limit.
- **Capture the Box** — two teams (randomised and balanced, host can swap/shuffle). Steal the enemy team's Box — it plays elevator music, and it and its carrier are always visible — and bring it back to your base. Targets: **3**/5/7/10 captures. Time: 2/**5**/10 min/No limit.

### Options
Microphone picker (headset, webcam mic, etc.), mic sensitivity with a live meter showing the talk/scream thresholds, mouse sensitivity, FOV, volumes, auto-hop, and a "hold Q" keyboard sonar for testing without a mic.

## First-time setup (once)

1. **Install Unity 6.3 LTS** via Unity Hub, then *Add project from disk* → this folder. Unity will import packages and create the scene automatically (menu **Going Batty → Run Project Setup** if needed).
2. **Link Unity Cloud (free) for online lobbies**: *Edit → Project Settings → Services* → link or create a project. In the [Unity Cloud dashboard](https://cloud.unity.com) make sure **Lobby** and **Relay** are enabled for that project. Then copy the **Project ID** (and Organization ID) into `Assets/Editor/ServicesConfig.cs` — or just give them to Claude — so the automatic builds are linked too.
3. Press **Play**. To test multiplayer on one PC, build (*Going Batty → Build Windows*) and run two copies; each launch signs in as a separate anonymous player.

## Automatic builds & in-game updates

Every copy of the game checks this repo's latest **GitHub Release** on startup. If there's a newer version, the main menu says so and asks **Yes / No**:
- **Yes** downloads the new build, closes the game, swaps the files and relaunches.
- **No** hides the prompt for that session.

Releases are made by `.github/workflows/release.yml`: whenever the `VERSION` file on `main` holds a version that hasn't been released yet, GitHub builds the Windows game and publishes `GoingBatty-Windows.zip` as release `v<VERSION>`.

**So shipping an update from a Claude prompt is:** change the code → bump `VERSION` (e.g. `0.1.0` → `0.2.0`) → push to `main`. Players get the prompt next time they open the game.

One-time: add these repository secrets (*Settings → Secrets and variables → Actions*) so GitHub can run Unity:
- `UNITY_EMAIL`, `UNITY_PASSWORD` — your Unity account.
- `UNITY_LICENSE` — the contents of your license file. With Unity Hub signed in on Windows it's at `C:\ProgramData\Unity\Unity_lic.ulf` (open in Notepad, copy everything). See [game-ci activation docs](https://game.ci/docs/github/activation).

**Players:** download `GoingBatty-Windows.zip` from the latest release, unzip it to a normal folder (e.g. Documents\Going Batty — *not* Program Files, so the updater can write there) and run `GoingBatty.exe`.

## Project layout

Everything is created in code at runtime (`Bootstrap.cs`), so there are no hand-wired scenes or prefabs.

```
Assets/Scripts/
  Core/    Bootstrap (entry point), Tuning (ALL gameplay numbers), Settings, Look (colors/materials), InputWrap
  Net/     Net (message bus over Netcode), ILobbyService + UgsLobbyService (Unity Lobby/Relay)
  Audio/   MicInput (capture, loudness, voice frames), VoiceOut (3D proximity chat), Sfx (synthesized sounds + elevator music)
  Game/    GameSession (host-authoritative rules, both modes), LocalPlayer (movement/weapon/sonar),
           RemotePlayer, SonarSystem (echo visuals/ghosts), MapBuilder + MapNav (ASCII map, bot pathfinding),
           BotDirector (AI bots, host-side), BoxObjective
  UI/      UIRoot (menus, lobby, options, HUD)
  Update/  UpdateChecker (GitHub release check + self-update), BuildInfo (repo name)
Assets/Editor/  ProjectSetup (auto settings/scene), Builder (CI build), ServicesConfig (Unity Cloud IDs)
```

Switching to Steam later: implement `ILobbyService` with Steamworks, put a Steam transport on the NetworkManager in `Bootstrap.CreateNetworking`, change one line. Game code doesn't change.
