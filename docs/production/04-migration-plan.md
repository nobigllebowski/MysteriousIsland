# MIGRATION PLAN — NATION: WORLD ORDER → VARDHOLM

## 0. THE CORRECTION THAT MATTERS

The instruction was to inspect "the existing Unity repository" in this project and identify
obsolete Nation / World Order code. **There is no Unity project in this repository, and there
never was.**

`nobigllebowski/MysteriousIsland` was **completely empty** at session start — zero files, zero
commits. Verified against every ref, the reflog, the stash and unreachable objects. Everything in
it today is the Phase 0 documentation written in this session.

**NATION: WORLD ORDER is real, but it lives in a different repository:**
[`nobigllebowski/MobileGame`](https://github.com/nobigllebowski/MobileGame) —
Unity `6000.6.0f1`, 123 C# files, 4 assemblies, 80 EditMode tests.

**This is good news.** "Migration" here means *copying proven infrastructure across*, not
refactoring in place. **Nothing in MobileGame is deleted, renamed or touched.** This session has
read-only access to it, so that outcome is structurally guaranteed, not merely intended. If you
want the Nation project retired, that is a separate decision in its own repository.

---

## 1. WHAT NATION GOT RIGHT (and why this matters)

Nation was independently built to nearly the architecture the Vardholm Phase 0 documents
specify — before those documents existed:

| Vardholm Phase 0 says | Nation already does |
|---|---|
| Engine-free `Core` with `noEngineReferences: true` | `Nation.Core` — exactly this, and its 80 tests "run in milliseconds" |
| Portrait reference 390×844 | `UIService.ReferenceWidth = 390`, `ReferenceHeight = 844` |
| Safe-area handling, nothing hardcodes an inset | `SafeAreaElement` reads real insets |
| No hardcoded user-facing strings | `ILocalizationService` + `MissingKey` event |
| Determinism, seeded and serializable | `DeterministicRandom` with `Derive(worldSeed, streamIndex)` |
| Performance overlay, dev builds only | `PerformanceMonitor`, F3 toggle |
| One ticker, catch-up clamped | `TickScheduler.MaxTicksPerAdvance = 12` |

The convergence is close enough that porting is mostly renaming. **That is a strong independent
validation of the Phase 0 architecture**, and it is the single biggest schedule saving available.

---

## 2. THE DECISION IT FORCES: UI TOOLKIT, NOT uGUI

`03-phase-1-plan.md` §4 chose **uGUI**, on the reasoning that Phase 1 has three screens and we
should not learn a UI framework while proving an architecture.

**That reasoning is now void, and the decision is reversed.** See ADR-0014.

Nation ships a complete, code-built **UI Toolkit** premium-mobile framework: `UIService`,
`ScreenStack` (260 ms transitions, Push/Pop/Replace/ReplaceAll), `UIScreen`, `SafeAreaElement`,
`BottomSheet` (363 lines, drag detents), `ModalLayer`, `ToastLayer`, plus `Buttons`, `Cards`,
`IconElement`, `Inputs`, `Typography` and a design-token `Palette`.

We are not learning a framework — we are **inheriting a working one the same team wrote**, and it
maps directly onto the Mobile UX Plan's bottom-sheet inventory, HUD layers and discovery toasts.
Choosing uGUI now would mean rebuilding, in a less suitable technology, something we already own.

---

## 3. FILE-BY-FILE CLASSIFICATION (123 files)

### 3.1 PORT — take it, rename it, keep the logic (24 files)

| Nation | → Vardholm | Change |
|---|---|---|
| `Core/Signals/SignalBus.cs` + `ISignal.cs` | `Core/Signals/` | Namespace only |
| `Core/Utilities/DeterministicRandom.cs` | `Core/Primitives/PcgRandom.cs` | Namespace; store instance **inside `GameState`** so it serializes |
| `Core/Utilities/NumberFormatting.cs` | `Core/Utilities/` | Namespace only |
| `Core/Data/JsonParser.cs`, `JsonValue.cs`, `LocalizationTableParser.cs` | `Core/Data/` | Namespace only — engine-free JSON is exactly what ADR-0013 needs |
| `Core/Localization/ILocalizationService.cs`, `StringTableLocalizationService.cs` | `Core/Localization/` | Namespace; **missing key returns `#key#` not `[key]`** per Phase 1 §3.2 |
| `Core/Time/TickScheduler.cs` | `Core/Time/` | Namespace; retarget `GameSpeed` → sim-hours |
| `Core/Simulation/{SimulationEngine,ISimulationSystem,TickContext}.cs` | `Core/Simulation/` | Namespace only |
| `Core/Models/GameDate.cs` | `Core/Time/IslandClock.cs` | Calendar → in-fiction day/hour |
| `Game/UI/Core/{UIService,ScreenStack,UIScreen,SafeAreaElement,ToastLayer,ModalLayer,BottomSheet,PanelCoordinates}.cs` | `UI/Core/` | Namespace; strip `GameDataCatalog`/country deps |
| `Game/UI/Components/{Buttons,Typography,Inputs,Cards,IconElement}.cs` | `UI/Components/` | Namespace; restyle to Vardholm theme |
| `Game/UI/Formatting/UiFormat.cs` | `UI/Formatting/` | Strip economy formatters |
| `Game/Performance/PerformanceMonitor.cs` | `Game/Debug/DevOverlay.cs` | Add state + resident-zone count |
| `Game/Scenes/SceneNavigator.cs` | `Game/Scenes/SceneLoader.cs` | **Rewrite to additive** + progress + watchdog |

### 3.2 ADAPT — the pattern is right, the content is not (6 files)

| Nation | → Vardholm | Why |
|---|---|---|
| `Game/Bootstrap/GameContext.cs` | `Game/Bootstrap/GameContext.cs` | Same composition-root shape; every field replaced. **Drop `static Current`** — it is a service locator, and ADR-0012 requires constructor injection. |
| `Game/Bootstrap/GameBootstrap.cs` | `Game/Bootstrap/AppBootstrap.cs` | Same entry-point role |
| `Game/UI/Core/Palette.cs` | `UI/Core/Theme.cs` | **Full recolor.** Nation's cold blue `#05080F`/`#3B82F6` is wrong for a tropical caldera. Vardholm: deep green, dark blue, sunset orange, gold for discoveries, red for danger. |
| `Game/UI/Screens/MainMenuScreen.cs` | `UI/Screens/MainMenuScreen.cs` | Structure kept, content replaced |
| `Game/Time/GameRunner.cs` | `Game/Bootstrap/Ticker.cs` | Becomes the single ticker |
| `Game/Data/StaticDataLoader.cs` | `Game/Data/CatalogLoader.cs` | Retarget to the baked catalog (ADR-0005) |

### 3.3 OBSOLETE — grand-strategy specific, do not port (93 files)

Not "bad code" — **correct code for a different game**, and Vardholm has no use for any of it.

- **Countries / nations** (10): `CountryCatalog`, `CountryDefinition`, `CountryDifficulty`, `CountryFilter`, `CountryRegion`, `CountryStateFactory`, `CountryTiers`, `FlagSpec`, `ICountryDataProvider`, `CountryDataParser`
- **Grand-strategy simulation** (9): `Buildings/`, `Economy/`, `Power/`, `Nation/NationAssessment`, `Models/{CountryState,ResourceBalance,TaxPolicy,WorldState}`, `World/WorldFactory`
- **The entire world-map system** (24): `Core/Map/**` (projection, triangulation, simplification, GeoJSON, catalog, layers, picker, zoom) and `Game/Map/**` (renderer, camera, gestures, labels, capitals, mesh builder). ~2,800 lines. Vardholm has nine authored zones, not 242 territories.
- **The globe** (3): `Game/Globe/**`
- **Strategy UI** (10): `UI/Screens/{CountryPreview,CountrySelect,GameShell}`, `UI/Tabs/**` (Nation, Economy, World, Build, Power), `Components/{CountryCard,FlagElement,GlobeBackdrop,MapVignette,ChartElement,TimeControls,BottomNavBar}`
- **Map tooling** (5): `Editor/NaturalEarthImporter`, `Tools/MapImport/**`, `Tools/MapSource/**`
- **Nation's tests** (18) and **data** (`Assets/Data/Map`, `countries.json`)

**Nothing is deleted.** All 93 remain untouched in MobileGame; they are simply not copied.

---

## 4. WHAT WE DO *NOT* INHERIT

Two Nation patterns are deliberately left behind:

1. **`GameContext.Current` (static singleton).** A service locator lets any class reach any
   service, which is precisely the coupling ADR-0002 and ADR-0012 exist to prevent. Vardholm
   passes dependencies through constructors. This is the one place we knowingly make the port
   *harder* than a copy-paste, and it is worth it.
2. **Save-as-afterthought.** Nation has `GameSession` but no `ISaveParticipant` contract.
   Vardholm ships the save spine in Phase 1 (ADR-0011) with participants registered from the
   first system onward.

---

## 5. RISK

| Risk | Mitigation |
|---|---|
| Ported code carries assumptions from a turn-based strategy game (day ticks, speeds) | `TickScheduler` is retargeted to sim-hours in the port, not later. Its catch-up clamp is exactly what mobile backgrounding needs. |
| UI framework is coupled to `GameDataCatalog` / countries | Dependencies stripped at port time; `UIService` takes `ILocalizedText` and a theme, nothing else. |
| Two codebases drift | They are **not** shared code. This is a one-time copy with attribution in each ported file's header. No submodule, no package dependency. |
| Nation's Unity is `6000.6.0f1`; ours is unpinned | Vardholm pins the same `6000.6.0f1`, so ported code is known-good on a known editor. |

---

## 6. WHAT THIS SAVES

Roughly **3–4 weeks** of Phase 1 + Phase 12 work: a premium mobile UI framework, an engine-free
JSON parser, a localization service, a signal bus, deterministic RNG and a performance overlay —
all written by the same team, in the same style, already proven in a shipping-shaped project.

The Phase 1 file manifest grows from 47 to ~70 files, but roughly a third of those are ports
rather than new code.
