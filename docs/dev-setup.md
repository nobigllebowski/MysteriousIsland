# Dev setup — clean machine to running build

Follow this top to bottom. Every step is literal. If a step does not produce the
stated result, stop there rather than continuing — every later step assumes the
earlier ones worked.

Estimated time on a clean machine: **45–70 minutes**, most of it the Unity
download and the first asset import.

---

## 0. Prerequisites

| Tool | Version | Why |
|------|---------|-----|
| Unity Hub | 3.8 or newer | installs and launches the pinned editor |
| Unity Editor | **6000.6.0f1**, exactly | pinned in `ProjectSettings/ProjectVersion.txt` |
| Git | 2.30+ | |
| Git LFS | 3.0+ | art and audio are stored in LFS (`.gitattributes`) |

**The editor version is not a suggestion.** `ProjectVersion.txt` pins
`6000.6.0f1` because that is the version the ported Nation code is known-good
on. Opening the project in a different 6000.x will offer to upgrade it and, if
you accept, will rewrite every `.unity` and `.asset` file in the repo into a
format the rest of the team cannot open. If Unity offers to upgrade the project,
the answer is **Quit**, not Continue.

---

## 1. Install Unity 6000.6.0f1

1. Install Unity Hub from <https://unity.com/download>.
2. Sign in with your Unity account and activate a licence
   (**Hub > Preferences > Licenses > Add**). A Personal licence is sufficient.
3. In Hub, go to **Installs > Install Editor > Archive** and click
   *download archive*, or go straight to
   <https://unity.com/releases/editor/whats-new/6000.6.0> and press the
   **Install with Unity Hub** button for **6000.6.0f1**.
4. In the module list, tick:
   - **Android Build Support**
     - **Android SDK & NDK Tools**
     - **OpenJDK**
   - **iOS Build Support** (macOS hosts only)
   - **Windows Build Support (IL2CPP)** or **Mac Build Support (IL2CPP)** for
     your host — this is what lets you make a desktop test build
   - **Documentation** (optional, large)
5. Accept and wait. The download is roughly 8–12 GB with the mobile modules.

Verify: **Hub > Installs** lists `6000.6.0f1` with an Android icon next to it.

---

## 2. Install Git LFS and clone

```sh
# macOS
brew install git-lfs
# Debian / Ubuntu
sudo apt-get install git-lfs
# Windows: included with Git for Windows 2.30+

git lfs install
```

Then clone:

```sh
git clone <repo-url> MysteriousIsland
cd MysteriousIsland
git lfs pull
```

`git lfs install` must run **before** the clone, or LFS-tracked files arrive as
small text pointer files and Unity imports a 130-byte "texture" that renders as
a magenta error. If you have already cloned without it, run `git lfs install`
then `git lfs pull` and the pointers are replaced in place.

Verify:

```sh
git lfs env | head -3      # prints an LFS version, not "not a git command"
```

---

## 3. Configure UnityYAMLMerge

Unity scenes and prefabs are YAML that git can merge textually and will merge
*wrongly* — the usual result is a scene that opens with components attached to
the wrong objects. `.gitattributes` already routes those files to a merge driver
named `unityyamlmerge`, but the driver itself is per-machine and must be
declared in your local git config. **Without these three lines, the routing in
`.gitattributes` silently falls back to the default merge.**

Run these from anywhere (they set your global config). Pick the path for your
platform:

**macOS**

```sh
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Tools/UnityYAMLMerge merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config --global merge.unityyamlmerge.recursive binary
```

**Windows (Git Bash / PowerShell)**

```sh
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config --global merge.unityyamlmerge.recursive binary
```

**Linux**

```sh
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver '"$HOME/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Tools/UnityYAMLMerge" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
git config --global merge.unityyamlmerge.recursive binary
```

Verify the binary exists before trusting the config:

```sh
# macOS
ls -l "/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/Tools/UnityYAMLMerge"
```

If the path does not exist, find it with
`find / -name 'UnityYAMLMerge*' 2>/dev/null` and substitute the real path — Hub
can be configured to install editors somewhere other than the default.

---

## 4. Add the project to Hub and import

1. Open Unity Hub, go to **Projects**.
2. Press **Add > Add project from disk**.
3. Select the **`MysteriousIsland` folder itself** — the one containing
   `Assets/`, `Packages/` and `ProjectSettings/`. Not `Assets/`, not the parent.
4. The project appears in the list with editor version `6000.6.0f1`. If it shows
   a yellow warning triangle, the pinned editor is not installed; go back to
   step 1.
5. Click the project name to open it.

**The first import takes 10–25 minutes.** Unity is building `Library/` from
scratch: importing every asset, resolving every package in
`Packages/manifest.json`, and compiling shaders for URP. The progress bar will
sit on "Importing" and then on "Compiling shader variants" for a long time with
no apparent movement. This is normal. Do not force-quit — a half-built
`Library/` produces import errors that look like real bugs. If you must restart,
delete `Library/` entirely first and let it rebuild.

**Expected on first open:**
- A one-time dialog about the Input System asking to enable the new backend and
  restart. Answer **Yes**. The project uses `com.unity.inputsystem` and
  `ForgottenIsle.Game.Input.VardholmControls`; the old backend cannot drive it.
- The Console may show info-level messages about package resolution. Warnings
  are acceptable; **errors are not** — a red error here means the import did not
  complete and nothing below will work.

Verify: the Console has zero red errors, and the Project window shows
`Assets/Scripts/Core`, `Assets/Scripts/Game` and `Assets/Localization/en.csv`.

---

## 5. Run project setup (one click)

From the menu bar: **`Vardholm → Setup Project`**.

It creates the four scene assets, writes Build Settings in the right order, and verifies the
localization resource. It is idempotent, and it never overwrites anything that exists.

On a fresh clone the editor also offers this automatically the first time it loads.

**You do not create any scenes or GameObjects by hand.** See `SCENE_CONTRACT.md` for why the
scene assets are empty and what the runtime builds instead.

### Verify

- `Vardholm → Validate Project` — read-only check.
- Press Play on `Bootstrap` and read the `VARDHOLM STARTUP CHECK` block in the Console.


## 6. Sync the localization table into Resources

`Assets/Localization/en.csv` is the translator-facing authoring copy.
`LocalizationLoader` reads it directly *in the Editor only*. A player build
reads `Assets/Resources/Localization/en.csv`, because Unity serves
`Resources.Load` from a folder literally named `Resources` and nowhere else.

Both files are committed, and they must be identical. After any edit to the
authoring copy, run from the project root:

```sh
cp Assets/Localization/en.csv Assets/Resources/Localization/en.csv
```

If you skip this, the game looks correct in the Editor and ships with every
string rendered as `#ui.menu.continue#`. That failure mode is the entire reason
a missing key renders as a visible `#key#` marker rather than as blank text.

---

## 7. Open Bootstrap and configure the Game view

1. In the Project window, open **`Assets/Scenes/Bootstrap.unity`**
   (double-click).
2. Select the **Game** tab.
3. In the Game view toolbar, open the resolution dropdown — it usually reads
   *Free Aspect* — and scroll to the bottom of the list.
4. Press **+** to add a new size:
   - **Type**: `Fixed Resolution`
   - **Width**: `390`
   - **Height**: `844`
   - **Label**: `Vardholm Portrait 390x844`
   - Press **OK**.
5. Select `Vardholm Portrait 390x844` in the dropdown.

390 x 844 is an iPhone 13/14 logical viewport and is the reference resolution
baked into `UIService.ReferenceWidth` / `ReferenceHeight` and into
`VardholmPanelSettings`. Testing at any other size is testing a layout the UI
code is not scaling for.

Also set, once, so portrait is what a build produces:

**Edit > Project Settings > Player > Resolution and Presentation**
- Default Orientation: **Portrait**
- Allowed Orientations for Auto Rotation: tick **Portrait** only

---

## 8. Press Play

Press **Play** (Ctrl/Cmd+P) with `Bootstrap.unity` open.

**Expected within two seconds:**
- The Game view goes to the near-black green-black background (`#0A0F0D`).
- The main menu appears with **Continue**, **New Game**, **Settings**, **Quit**.
  Continue is disabled on a machine with no save.
- The dev overlay is visible in the top-left, showing FPS, frame time, the
  current `GameStateId` (`MainMenu`), the current zone (none) and the resident
  scene count.
- The Console shows no errors.

**If every label reads `#ui.menu.continue#`** and so on: the string table did
not load. Check that `Assets/Resources/Localization/en.csv` exists (step 6) and
look in the Console for a `CatalogMissing` warning naming the path it tried.

**If nothing appears at all:** confirm `Bootstrap.unity` has a `UI Root` object
with a `UIDocument` whose Panel Settings field is assigned. That is the single
most common setup miss, and it fails silently because there is nothing to draw
into.

Press **New Game** and the state machine goes `MainMenu -> Loading -> InGame`,
`ZoneRibcage` streams in additively, and the overlay's zone line changes. That
round trip is the Phase 1 acceptance path.

---

## 9. The dev overlay

`ForgottenIsle.Game.Diagnostics.DevOverlay` draws the panel in the top-left. It
reports frame timing plus the three facts Phase 1's acceptance criteria are
written against: the current `GameStateId`, the active zone, and the number of
resident scenes (which must never exceed 2 — ADR-0004).

**Toggle it with `F3`** while in Play mode. The keyboard must have focus in the
Game view, so click inside the Game view once before pressing it.

The whole class is compiled out unless `DEVELOPMENT_BUILD` or `UNITY_EDITOR` is
defined, so:

- **In the Editor**: always available.
- **In a build**: available only if you ticked **Development Build** in the
  build profile before building.
- **In a release build**: the class does not exist. `F3` does nothing, and the
  release frame budget pays nothing for it.

---

## 10. Run the EditMode tests

1. **Window > General > Test Runner**.
2. Select the **EditMode** tab.
3. Press **Run All**.

Everything should be green. These are plain NUnit tests over
`ForgottenIsle.Core` and the parts of `ForgottenIsle.Game` that do not need a
scene: state-machine transition legality, save round-trips, CSV parsing,
`PcgRandom` determinism, and the check that Build Settings' scene list matches
`SceneKeys.All`.

To run one test, expand the tree and press **Run Selected**. To debug one,
right-click it and choose **Debug** with your IDE attached.

**If the EditMode tab is empty**, the test assembly did not compile. Check the
Console; the usual cause is that `com.unity.test-framework` failed to resolve
during import, which means the import in step 4 did not actually finish.

**PlayMode tests** are on the neighbouring tab and are slower because each one
enters and exits play mode. Run them before opening a pull request, not on every
change.

---

## 11. Run the layering checks

These do not need Unity and take under a second:

```sh
sh ci/check-layering.sh
```

Expected output ends with:

```
check-layering: OK — all five layering invariants hold.
```

This is the same gate CI runs. Run it before you push; the five things it
catches — engine references in Core, a loosened Core asmdef, UI reaching past
the command layer, hardcoded player-facing strings — are all things that compile
fine and fail later. See `ci/README.md` for what each check protects and which
ADR it comes from.

---

## 12. Make a build

**File > Build Profiles**, select the platform, confirm the Scene List matches
the order in `Assets/Scenes/README.md` (Bootstrap must be index 0), tick
**Development Build** if you want the overlay, then **Build**.

For Android, the first build also needs:
**Edit > Project Settings > Player > Other Settings**
- Scripting Backend: **IL2CPP**
- Target Architectures: **ARM64** only (ARMv7 is not supported by Unity 6)
- Minimum API Level: **Android 7.0 (API 24)**

Output goes to `Builds/`, which is gitignored.

---

## Troubleshooting

**"The project was created with a different version of Unity"**
Quit. Do not press Continue. Install `6000.6.0f1` and open with that. Pressing
Continue rewrites every YAML asset in the repo.

**Compile errors mentioning `UnityEngine` inside `Assets/Scripts/Core/`**
That is the design working. Core sets `"noEngineReferences": true` (ADR-0002).
Move the offending code into `ForgottenIsle.Game`, or express it with Core's own
`Vec3` / `IClock` / `ICoreLog`.

**Everything renders magenta**
LFS pointers were not resolved. `git lfs install && git lfs pull`, then in Unity
right-click `Assets` and choose **Reimport**.

**Play mode does nothing when started from a zone scene**
It should work — `AppBootstrap` boots from `[RuntimeInitializeOnLoadMethod]`
regardless of the open scene. If it does not, something scene-bound was added to
`Bootstrap.unity` that the rest of the game now depends on. That is a bug, not a
setup problem; report it.

**Changes to `en.csv` do not appear in Play mode**
Unity caches `TextAsset` imports. Select the CSV in the Project window and press
**Reimport**, then re-enter Play mode.

**Second Play press behaves as if the game were already booted**
`Enter Play Mode Options` with domain reload disabled keeps statics alive.
`AppBootstrap.ResetStatics` handles this; if you see it anyway, re-enable domain
reload at **Edit > Project Settings > Editor > Enter Play Mode Settings**.
