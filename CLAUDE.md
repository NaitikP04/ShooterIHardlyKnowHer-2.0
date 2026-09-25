# ShooterIHardlyKnowHer 2.0

2-player online co-op, endless wave shooter. One oval rail (36x20 m). Each player rides their own
cart on it, on opposite long sides, facing each other across the gap. A/D slides your cart along
your side. The chain between you wants your partner *directly across* from you: slide and you drag
them along with you; run opposite ways and it stretches until it snaps, re-hooking when you get
back across from each other. You can't look behind you (yaw clamp +/-90 from facing your partner),
so each player must shoot *past* the other to kill the enemies coming at their partner's back.
Cartoon fantasy, goofy, rage-bait but fun. Friendly fire is ON. Weapons counter specific enemy
types and must be tossed across the gap to your partner.

If the arena feels too static later: make the whole ring travel along a bigger loop, or rethink.

Rebuilt from scratch after a messy 2024-25 team project (`D:\ShooterIHardlyKnowHer`, Unity 2022.3).
Reuse *ideas* and *art* from it, never code.

## Tech
- Unity **6000.6.3f1**, URP, new **Input System** (`Assets/_Game/Input/InputSystem_Actions.inputactions`)
- **Netcode for GameObjects (NGO) 2.x** for networking, Unity Transport. Relay/Lobby added later.
- **Multiplayer Play Mode** for testing 2 players in one Editor. Never ParrelSync.
- Splines package for the rail, Cinemachine 3 for cameras, ProBuilder for whiteboxing.
- PC only for now. Steam later.

## Networking rules (non-negotiable)
- **Server-authoritative.** The host simulates enemies, health, projectiles, pickups, waves, the cart.
  Clients send input via RPCs (`[Rpc(SendTo.Server)]`) and receive state via `NetworkVariable`s.
- Player movement/aim is owner-authoritative (`NetworkTransform` owner mode) for responsiveness; hits
  are validated on the server.
- **No `GameObject.Find`**, no string lookups, no scene build indices. Use serialized references or
  a small service locator.
- Every gameplay feature must work when hosting alone (1 player) so solo iteration is possible.
- Use `[Rpc(SendTo.X)]` (NGO 2.x style), not the legacy `[ServerRpc]`/`[ClientRpc]`.

## Code conventions
- Namespace `SIHKH.<Area>` matching the folder under `Assets/_Game/Scripts/<Area>`.
- One class per file, file name == class name. PascalCase public, `_camelCase` private fields.
- Data-driven: weapons, enemies, waves are `ScriptableObject`s in `Assets/_Game/Data`.
- Prefer composition (small MonoBehaviours) over inheritance trees.
- Comments explain *why*, not *what*. No dead code, no "- Copy" files, no `InDevJunk`.

## Folder layout
```
Assets/_Game/
  Scripts/{Core,Networking,Player,Rail,Weapons,Enemies,Waves,UI}
  Prefabs/  Scenes/  Data/{Weapons,Enemies}  Art/Whitebox  Input/
Assets/Settings/        URP + quality assets (template)
Assets/Scenes/          template SampleScene (delete once _Game/Scenes has a main scene)
```

## Workflow
- Whitebox first, art last. Every phase must be playable before moving on.
- Work step by step and explain each step; the user wants to learn the architecture, not receive
  a finished dump.
- When a Unity Editor is open, drive it via `unity command` (Pipeline package) instead of hand-editing
  `.unity`/`.prefab` YAML. Run `unity status` first.
- Commit small and often, push after every working step. **No Claude co-author lines** in commits.
- Editor version is pinned in `ProjectSettings/ProjectVersion.txt`; do not change it casually.

## Roadmap
1. Setup (done): project, packages, git, this file.
2. Whitebox core (rail, carts, seats, strafing, chain, first-person look: DONE): next is
   one hitscan gun + cube enemies from outside the oval. Networked from day one.
   Testing note: Multiplayer Play Mode sends keys only to the focused window, so two-player
   input can't be held at once from one keyboard; a "plant" toggle (S) would fix that.
3. Weapon system: ScriptableObject weapons, damage types vs enemy types, toss mechanic.
4. Waves / roguelike loop: wave director, enemy archetypes, unlocks, run end + restart.
5. Art pass: Wizard/Clown player models, enemy models, cartoon shaders, VFX, audio.
6. Relay/Lobby for playing over the internet, then Steam.
