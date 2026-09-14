# Scenes — create these by hand in the Editor

This folder is empty on purpose. Unity scenes are generated YAML with file IDs
and GUIDs that only the Editor can allocate correctly; a hand-written one either
fails to open or, worse, opens with silently detached references. So the four
scenes are *specified* here and *created* in the Editor, once, by whoever sets
up the project first. Commit them as soon as they exist.

Everything below is mechanical. Follow it top to bottom and the result is a
project that runs.

---

## Before you start

`AppBootstrap` boots the game from a `[RuntimeInitializeOnLoadMethod]`, not from
an object placed in a scene. That means **no scene needs a bootstrap
GameObject**, and it means Play works from whichever scene happens to be open.
The scenes below are therefore much emptier than they would be in a
conventional project. Do not add a "GameManager" object to any of them.

The one thing the scenes *do* carry is rendering and UI hosting: a camera, and
in Bootstrap the persistent UI document.

---

## 1. `Assets/Scenes/Bootstrap.unity`

The scene the app opens on. Holds the persistent UI surface and nothing else.

Create it: **File > New Scene > Basic (URP)**, then **File > Save As** to
`Assets/Scenes/Bootstrap.unity`.

Contents:

1. Delete the default **Directional Light**. Bootstrap renders no world.
2. Keep **Main Camera**. Set:
   - Transform Position `(0, 0, -10)`, Rotation `(0, 0, 0)`
   - Camera > Environment > Background Type: **Solid Color**
   - Background: `#0A0F0D` (the Vardholm background from
     `ForgottenIsle.UI.Core.Theme` — RGBA `10, 15, 13, 255`)
   - Camera > Rendering > Culling Mask: **Nothing**
   - Audio Listener component: **keep** (exactly one must exist at any time, and
     Bootstrap is the only scene that is always loaded)
3. Create an empty GameObject named **`UI Root`** at the scene root, position
   `(0, 0, 0)`.
   - Add component **UI Document** (`UnityEngine.UIElements.UIDocument`).
   - Panel Settings: assign `Assets/UI/VardholmPanelSettings.asset` — create it
     with **Assets > Create > UI Toolkit > Panel Settings Asset** if it does not
     exist, and configure it as in section 5 below.
   - Source Asset: **leave empty.** `UIService` builds the tree in code; a UXML
     assigned here would be replaced on the first frame and only confuse the
     next person to open the scene.
   - Sort Order: `0`.
4. Nothing else. No EventSystem (UI Toolkit does not use one), no canvas, no
   lights, no geometry.

---

## 2. `Assets/Scenes/MainMenu.unity`


> **⚠ PHASE 1 STATUS — READ BEFORE BUILDING THIS SCENE.**
> **Nothing in Phase 1 loads `MainMenu.unity`.** The main menu is UI Toolkit screens drawn on the
> persistent `UIDocument` created by `UiInstaller`, over a flat themed background. This scene is
> the *future* home of the cinematic backdrop (island coastline, ocean, wind, distant storm) that
> the Mobile UX Plan calls for, and a menu-scene lifecycle owner is scheduled for Phase 2
> (ADR open item **O-9**).
>
> So: **create it if you want the placeholder in Build Settings** (the build order below assumes
> it exists, and `SceneKeys.MainMenu` references it), but do not expect to see it on screen yet,
> and do not spend art time on it until O-9 is resolved.

The menu has no world. The menu *screen* is built by
`ForgottenIsle.UI.Screens.MainMenuScreen` into the Bootstrap `UIDocument`, so
this scene exists only to be something to be loaded while the menu is up — it is
what gets unloaded when a run starts.

Create: **File > New Scene > Empty**, save as `Assets/Scenes/MainMenu.unity`.

Contents:

1. **No camera.** Bootstrap's camera stays alive and is the only one. A second
   camera here would produce two cameras rendering into the same target and a
   warning about multiple audio listeners.
2. Create an empty GameObject named **`MainMenu Backdrop`** at `(0, 0, 0)`.
   Leave it empty for now; art for the menu backdrop lands here in Phase 2.
3. That is the whole scene.

---

## 3. `Assets/Scenes/ZoneRibcage.unity`

> **The file name IS the scene key — no underscore.** `SceneKeys.ZoneRibcage`
> is the string `"ZoneRibcage"`, and `SceneManager.LoadSceneAsync` matches on
> the scene's name as listed in Build Settings, which is the file name without
> its extension. So the file is `ZoneRibcage.unity`, never `Zone_Ribcage.unity`.
> The underscore form appears in older planning documents and is wrong; the same
> goes for Fernmaw. If you have already made an underscore version, rename the
> asset in the Project window (which keeps its GUID) rather than on disk.

Act 1 opening zone, the wreck shelf. Display name comes from
`zone.ribcage.name` in `Assets/Localization/en.csv` — the key is *derived* from
the scene name by `SceneKeys.ZoneDisplayKey`, so a scene renamed here without a
matching CSV row ships a save-slot card reading `#zone.ribcage.name#`.

Create: **File > New Scene > Basic (URP)**, save as
`Assets/Scenes/ZoneRibcage.unity`.

Contents:

1. **Delete Main Camera.** The camera lives in Bootstrap. Zones are loaded
   additively on top of it (ADR-0004) and must not bring their own.
2. Keep the **Directional Light**. Rename it `Sun`. Rotation `(50, -30, 0)`.
   This is the zone's own lighting and it unloads with the zone.
3. Create an empty GameObject named **`Zone Root`** at `(0, 0, 0)`. Everything
   the zone owns is parented under it, so a designer can see at a glance what
   belongs to this zone and what leaked in from another.
4. Under `Zone Root`, create:
   - **`Geometry`** — empty. Terrain and props go here.
   - **`Spawns`** — empty, with one child empty GameObject named
     **`PlayerSpawn`** at `(0, 1, 0)`, rotation `(0, 0, 0)`. `PlayerRig` is
     positioned from `PlayerState`, but a fresh run with no saved pose needs a
     defined starting point and this is it.
   - **`Interactables`** — empty. Phase 2.
5. **Lighting > Scene** tab: uncheck **Auto Generate**, then **Generate
   Lighting** once so the scene has baked data and does not warn on load.

---

## 4. `Assets/Scenes/ZoneFernmaw.unity`

Act 1 second zone, the fern gully inland of the shelf. Identical structure to
ZoneRibcage — same object names, same spawn convention — because `ZoneRegistry`
and `SceneLoader` treat every zone the same and a zone that is shaped
differently is a zone that fails differently.

Create it by **duplicating `ZoneRibcage.unity`** in the Project window
(Ctrl/Cmd+D), renaming the copy to `ZoneFernmaw`, opening it, and:

1. Rename nothing inside. `Zone Root`, `Geometry`, `Spawns/PlayerSpawn`,
   `Interactables` and `Sun` keep their names.
2. Change `Sun` rotation to `(28, 140, 0)` — Fernmaw is a gully and the light
   arrives low and from the far side.
3. Move `PlayerSpawn` to `(0, 1, 0)` if the duplicate moved it. Fernmaw's
   arrival point is the gully mouth.
4. **Generate Lighting** again for this scene.

Display name key: `zone.fernmaw.name`, already present in `en.csv`.

---

## 5. `Assets/UI/VardholmPanelSettings.asset`

Not a scene, but the Bootstrap scene depends on it, so it is specified here.

**Assets > Create > UI Toolkit > Panel Settings Asset**, saved as
`Assets/UI/VardholmPanelSettings.asset`. Configure:

- **Theme Style Sheet**: `UnityDefaultRuntimeTheme` (the default is correct;
  `ForgottenIsle.UI.Core.Theme` applies Vardholm's palette in code)
- **Scale Mode**: `Scale With Screen Size`
- **Reference Resolution**: `X = 390`, `Y = 844`
  — these are `UIService.ReferenceWidth` / `ReferenceHeight` and must match
  exactly, or every layout computed in code lands at the wrong scale
- **Screen Match Mode**: `Match Width Or Height`
- **Match**: `0` (match width — the phone dimension that varies least for a
  portrait game)
- **Sort Order**: `0`
- **Clear Color**: off (the camera clears)

---

## 6. Build Settings — exact order

**File > Build Profiles** (Unity 6 replaced Build Settings with Build Profiles;
the scene list is under **Scene List** in the active profile).

Add the scenes in **exactly this order**:

| Index | Scene              | Enabled |
|-------|--------------------|---------|
| 0     | `Bootstrap`        | yes     |
| 1     | `MainMenu`         | yes     |
| 2     | `ZoneRibcage`      | yes     |
| 3     | `ZoneFernmaw`      | yes     |

The order is not cosmetic:

- **Index 0 must be Bootstrap.** Unity loads index 0 on launch. Bootstrap is
  deliberately near-empty so that load is instant, and `AppBootstrap` composes
  the graph immediately after it, before anything else has to exist.
- **The order mirrors `SceneKeys.All`**, which lists `Bootstrap`, `MainMenu`,
  `ZoneRibcage`, `ZoneFernmaw` in that sequence. An EditMode test compares the
  build scene list against `SceneKeys.All`; a mismatch fails it, which is how a
  scene that was added to the project but never to the build gets caught before
  it 404s at runtime as `ResultCode.SceneNotFound`.

After setting the list, **drag Bootstrap to index 0 explicitly** even if it is
already there. Unity assigns indices by list position and a scene added by
drag-and-drop lands at the end.

---

## 7. Checklist before you commit

- [ ] Four `.unity` files exist under `Assets/Scenes/` with the exact names
      `Bootstrap`, `MainMenu`, `ZoneRibcage`, `ZoneFernmaw`
- [ ] Their `.meta` files are staged too (a scene without its `.meta` gets a new
      GUID on the next machine and drops out of Build Settings)
- [ ] Exactly one camera exists across Bootstrap + any one zone
- [ ] Exactly one AudioListener exists (Bootstrap's)
- [ ] Build Settings order matches the table above
- [ ] Pressing Play from `Bootstrap.unity` reaches the main menu
- [ ] Pressing Play from `ZoneRibcage.unity` also boots without error
      (`AppBootstrap` runs regardless of the open scene — if this fails,
      something scene-bound crept in)
