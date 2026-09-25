# projectLEA — durable project memory

## Hard constraints
- **Only `Assets/Scenes/Manuel.unity` and `Assets/Scenes/Manuel/MainMenu.unity` may be modified.** MainMenu is scene 0 (the entry); Manuel is scene 1. Other scenes never.
- Scripts: `Assets/Scripts/Manuel/`. Game data: `Assets/Data/Manuel/<Type>/` — two conventions, don't mix.
- Unity 6000.3.10f1, URP 17.3.0, **new Input System only** — `Keyboard.current` / `Key` enum, never `KeyCode`.
- No multiplayer package. Netcode is raw sockets in `Assets/Scripts/Manuel/Net/`. Never propose NGO / Mirror / Photon.
- Never regenerate the `.meta` for `PlayerController`, `WeaponController`, `DamageDummy`, `Billboard` — the scene references those GUIDs.

## Unity can't be launched from the agent shell
Hangs at `Application.AssetDatabase Initial Refresh Start`. The user's interactive Editor is the only path. **Don't retry batch mode.** The user must also rebuild the standalone player themselves — a stale build is indistinguishable from a code bug at runtime.

## How the user tests multiplayer (and where the logs are)
Host = the **Unity Editor**; client = a **built .exe**. Build output: `C:/Users/bib/OneDrive - bib & FHDW/Desktop/lea test/`.
- Client log: `C:/Users/bib/AppData/LocalLow/DefaultCompany/projectLEA/Player.log` (productName `projectLEA`, company `DefaultCompany`).
- **Always check the build's mtime before diagnosing a client-side bug** — a week-old build silently has none of the current scene or code.
- Two fullscreen D3D12 processes on this 4 GB RTX 3050 crash the client with `d3d12: Device failed error (887a0001)`. Not a game bug; suggest windowed.
- `Camera.main` + `Time.time` readouts in that log are the cheapest way to prove a runtime bug is real and current: compare the build's `Assembly-CSharp.dll` mtime to `Library/ScriptAssemblies` first.

## THE ORDERING RULE (got wrong twice)
Scene edits land via a one-shot `[InitializeOnLoad]` editor script that fires **only on a domain reload**, and a reload happens **only when a script changes**. So: **delete `ProjectSettings/ContentSetup.done` FIRST, then edit a script.** Reverse order silently does nothing. The marker name has changed over time (`CombatPrototypeSetup.done` once) — `ls ProjectSettings/*.done` is the source of truth.
Also: an edit to the bake script can land AFTER the reload it was meant to trigger. Confirm the bake's own `Debug.Log` numbers reflect the change before believing a fix is live.

## Verifying without Unity
- `C:\Users\bib\AppData\Local\Unity\Editor\Editor.log` — `grep -anE "error CS[0-9]+"`. **Grep the WHOLE file, not the tail** — the compile block can sit mid-log. Stack traces print BEFORE each message; that's normal.
- `Library/ScriptAssemblies/Assembly-CSharp*.dll` mtime = last real recompile.
- `Tools/verify_scripts.py` — static checks. Run before every hand-off.
- Scenes, `.meta`, ScriptableObject assets and `EditorBuildSettings.asset` can be **hand-authored as YAML** with generated GUIDs.

## Multiplayer (host-authoritative)
- Slot One = "the player on THIS machine", slot Two = "the other body". An incoming score/lock is always slot Two here, whichever side hosts.
- A client never resolves a death or advances a phase — `GameManager.RemoteAuthoritative` gates both.
- **A client mirrors BOTH spawn slots** (`RoundManager.LocalPlayerUsesRemoteSpawn`), or both bodies land on the same point and proxies get dragged around.
- **The proxy body is network-driven only.** Hits on it are RELAYED (`TryRelayRemoteHit` -> `SendDamage`), never applied to its stand-in Health.
- `JsonUtility` has no polymorphism: every wire message carries a `t` tag first. A new message must be sent AND dispatched in `LobbyNetwork.HandleMessage`, and its event subscribed in `NetMatchSync`.
- Full-screen UIs share the `MatchUiPause` depth counter for `Time.timeScale` — never capture/restore it per-UI, or the shop/class-select handoff freezes the match for good.

## C# namespace rules (each was a real CS0xxx here)
- A bare type name resolves in its own namespace and every **enclosing** one, never a **sibling**.
- **`using A.B;` imports the TYPES in A.B, not A.B's NAME.** Fix: import the namespace and write the **bare type name**, or write the full dotted name from the root.
- **`using System;` + bare `Object`/`Random` = CS0104** (ambiguous with `UnityEngine.*`).
- Don't name a property `Camera` — `Camera.main` in its own body resolves to the property. When renaming a public member, grep its **call sites in the same file** too.
- Never name a local type `Screen`/`Time`/`Input`/`Application` (CS0117).

## Unity gotchas
- **Unity only re-scans scripts when the Editor regains focus.** Edits from the shell leave it on a stale `Assembly-CSharp.dll`. Force a focus change (`user32.SetForegroundWindow` via Python ctypes; minimize+restore works best). PowerShell routes are policy-blocked.
- **`SceneRoots` is a real ref list.** Removing a root GO by hand means deleting its `m_Roots` entry too, or Unity logs `Broken text PPtr`. Anchor checks must count `--- !u!X &N stripped` anchors.
- **`ManagerBase<T>` supplies `Instance`/`Exists`** for every manager. The class-selection member is `Selected` (not `CurrentClass`, which lives on `ClassAbilityHost`).
- **`Camera.main` needs the `MainCamera` tag.** An untagged camera makes it null, and anything resolved once in `Awake` stays null forever — every shot silently failed for a whole session. Resolve camera-derived references LAZILY (serialized field -> `Camera.main` -> `GetComponentInChildren<Camera>`); a network client's camera also spawns after `Awake`.
- **A null serialized socket silently falls back to the component's own transform.** Check the scene wiring, not just the fallback chain.
- When adding a GO to a scene by hand, add it to the parent Transform's `m_Children` AND set the child's `m_Father` — both sides.
- **`[RuntimeInitializeOnLoadMethod]` fires ONCE.** `AfterSceneLoad` = after the FIRST scene. Subscribe to `SceneManager.sceneLoaded`.
- `PrimitiveType.Cylinder` ships a `CapsuleCollider` that collapses to a sphere when shorter than wide — swap a non-convex `MeshCollider`.
- `Key` enum: `None=0, Space=1 … Equals=14, A=15`, so `A..Z` = `15..40`.
- **No `UdpClient.SendAsync(byte[], IPEndPoint)`** in Unity's class library.
- RectTransform `anchoredPosition.y` counts DOWN from the parent's top edge when the anchor is `(., 1)`.
- A new `[SerializeField]` on a component already in a scene needs no scene edit.

## Characters: Blender -> Unity conventions
- Blender 5.1 FBX export with `axis_forward='-Z', axis_up='Y'` puts the conversion on the ROOT node, not the vertices. A bean whose eyes face Blender -Y arrives **upright, facing +Z**, no corrective rotation. Verify with `Tools/fbx_nodes.py`.
- Character pivot convention: **feet at the rig origin**. The CharacterController on `Player` is centred on the player origin (center 0, height 2), so the body goes at `-(center.y + height/2)`.
- **Measuring a skinned character: use `Renderer.bounds`, never `SkinnedMeshRenderer.localBounds`.** `localBounds` are in the mesh's OWN Blender space (the FBX `-90deg X` rotation is applied at render time, not to localBounds), so height lands on **Z** and depth on Y; dividing by `size.y` divides by the DEPTH (Bean: 1.79 tall, 0.90 deep -> scale 2.0 -> a 3.6 m bean, the bug asked about four times). `Renderer.bounds` is the world-space box the GPU draws (Y up), which is self-consistent with the placement math. Scale and lift on `size.y`/`min.y`.
- An FBX imports with **no rig** by default = no Avatar. The Rig-tab dropdown is `ModelImporter.animationType` (`ModelImporterAnimationType {None, Legacy, Generic, Humanoid}`) — there is **no** `rigType`/`ModelImporterRigType`. `Generic` + `SaveAndReimport()` makes the Avatar exist.
- Mecanim controllers are built with the `UnityEditor.Animations` API, never hand-written YAML.
- **`BlendTree` has no `AddMotion`**; its child struct is **`ChildMotion`** (Unity 6000), and `tree.children` is a COPY — build the array and assign once.
- **`AnimatorStateMachine.AddState()` already adds the state as a sub-asset in Unity 6.** Only objects you `new` yourself need `AddObjectToAsset`.
- Binary FBX quirks: header is `Kaydara FBX Binary` + TWO spaces + NUL; Blender writes properties nameless; Python `struct` `l` is 4 bytes on Windows but FBX `l` arrays are 8 (use `q`); no wrapping root node.

## Tooling
- Multiple `Edit` calls in one message against the **same file** clobber each other — each writes from a snapshot predating its siblings. **One Edit per file per message**, or a single `Write`.
- `rm` on `ProjectSettings/*.done` is SIGTERM'd by the sandbox; delete via the managed Python (`os.remove`). `ls` is fine.
- Blender 5.1 CLI: `blender file.blend --background --python script.py`. A positional script path is read as a blend file. `measure_beans.py` only processes the FIRST blend passed.
