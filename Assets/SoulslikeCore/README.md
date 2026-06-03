# SoulslikeCore — portable player/combat package

Self-contained gameplay package from the Library project. Drag the whole
`SoulslikeCore/` folder into another Unity project (URP, Unity 6000.4+, with the
**Input System** package installed). The animations are re-bound **by filename**,
so they work even though the target project's animation clips have different GUIDs.

## Contents
- `Scripts/` — Player (movement, state machine, combat, dodge, block, stats, input,
  lock-on, camera), Enemy AI, Combat (IDamageable, projectile, status effects),
  Data (weapon/enchant systems), UI (HUD, death screen).
- `Animations/PlayerAnimator.controller` — the player animator (states, transitions,
  parameters). Clip references are re-bound on import (see below).
- `Animations/PlayerAnimator.clipmap.json` — authoritative **state → clip-name** map.
  Used by the relinker; does not depend on GUIDs.
- `Data/ScriptableObjects/` — Rapier weapon + Fire/Ice/Light/Void enchantments.
- `InputActions.inputactions` — input map (PlayerInput uses **Send Messages**).
- `Editor/PlayerAnimatorRelinker.cs` — the GUID-free re-binder.

## How the GUID-free animation binding works
`PlayerAnimator.controller` stores its clip references by GUID. In a different
project those GUIDs won't resolve. `PlayerAnimatorRelinker`:
1. Reads `PlayerAnimator.clipmap.json` (state name → desired clip name).
2. For each controller state, finds an `AnimationClip` **by exact filename**
   (standalone `.anim` or an FBX sub-asset) and assigns it.
3. Only touches states whose clip is missing/wrong, so it's idempotent.

It runs **automatically** after import (`AssetPostprocessor`), and can be re-run
manually: **Tools ▸ Soulslike ▸ Relink Player Animator (by filename)**.

### Requirements for auto-bind to succeed in the target project
- The target must contain AnimationClips whose **names exactly match** those in
  `PlayerAnimator.clipmap.json` (e.g. `1011_women_OnehandSW_walk`,
  `2001_women_OnehandSW_attack_A`, …). These are the OnehandSW (women) set.
- If a clip name isn't found, the relinker logs `clip not found by name: <name>`
  and leaves that state unbound — rename/import the missing clip and re-run the menu.

## Manual setup steps in the target project (one time)
1. Ensure the **Input System** package is installed.
2. Put the player model (humanoid Avatar) in the scene; add an `Animator` on the
   model with `PlayerAnimator.controller` assigned (the relinker fills its clips).
3. On the Player root GameObject add: `CharacterController`, `PlayerInput`
   (Behavior = **Send Messages**, Actions = `InputActions`), then the player
   scripts: `PlayerStateMachine, PlayerStats, PlayerInputHandler, PlayerMovement,
   PlayerCombat, PlayerDodge, PlayerBlock, LockOnSystem, EnchantBar,
   EnchantmentSystem`. (Scripts find the Animator via `GetComponentInChildren`, so
   the Animator can live on a child model.)
4. Assign `WeaponData` (Rapier) on `PlayerCombat`, and the enchant ScriptableObjects
   on `EnchantmentSystem`.
5. Bake a NavMesh for enemies; tag the player `Player` and enemies `Enemy`.

## Tunables worth checking (Inspector)
- `PlayerCombat`: `lightAttackDuration`, `heavyAttackDuration`, `attackCooldown`.
- `PlayerStats`: `hitInvulnerability` (post-hit i-frames), `staggerDuration`.
- Animator Parameters panel: all bool/trigger **default checkboxes must be OFF**
  except `IsGrounded` (ticked defaults make every anim fire on launch).
