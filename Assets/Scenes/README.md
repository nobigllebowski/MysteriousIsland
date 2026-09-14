# Assets/Scenes

**Do not create these by hand.** Run **`Vardholm → Setup Project`** from the Unity menu bar.

That command creates every scene here, registers them in Build Settings in the correct order, and
verifies the localization resource. It is idempotent — running it again changes nothing.

---

## The scenes

| Scene | Build index | Contents |
|---|---:|---|
| `Bootstrap.unity` | 0 | **Empty.** |
| `MainMenu.unity` | 1 | **Empty.** |
| `ZoneRibcage.unity` | 2 | **Empty** — furnished at runtime. |
| `ZoneFernmaw.unity` | 3 | **Empty** — furnished at runtime. |

Yes, all four are empty, and that is the design.

## Why the scenes are empty

Every object the game needs is created at runtime:

- `AppBootstrap` creates the persistent host, the whole service graph, the `Ticker` and the
  `DevOverlay` — from a `[RuntimeInitializeOnLoadMethod]` hook that fires whichever scene opened.
- `UiInstaller` creates the `UIDocument` and `PanelSettings` and builds the menu.
- `ZoneFurnisher` creates the ground, directional light, entry anchor, player capsule and camera
  for any zone that does not already provide them.

An empty scene holds **no serialized component references**, so it cannot be malformed, cannot
drift out of step with the code, and can be regenerated at any moment without losing authored
work. It also means a scene file never appears in a diff as an unreadable wall of YAML.

## When real art arrives

Author it normally. **The furnisher only fills gaps** — it checks for each object and skips
anything the scene already provides. A hand-built Ribcage with its own terrain, lighting and
`ZoneEntryAnchor` simply leaves the furnisher with nothing to do. No code change, no flag.

The one thing to know: if you add a `PlayerRig` to a scene yourself, give it a camera pivot, or
the furnisher will attach one for you at eye height.

## Why `ZoneFernmaw` exists

A second zone is the only way to exercise zone-to-zone travel and prove `ZoneRegistry`'s
two-resident cap (acceptance criteria 11 and 12). With one zone, streaming is untested.

## Verifying

- **`Vardholm → Validate Project`** — read-only check of scenes, build order and localization.
- Press Play and read the `VARDHOLM STARTUP CHECK` block in the Console.

See [`../../docs/SCENE_CONTRACT.md`](../../docs/SCENE_CONTRACT.md) for the full dependency graph.
