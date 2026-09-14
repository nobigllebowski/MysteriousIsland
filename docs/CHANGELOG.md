# Changelog

All notable changes to this project are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Part of the project memory system — see [`PROJECT_HANDOFF.md`](PROJECT_HANDOFF.md) for the
reading order. For what is true *right now* rather than what changed, see
[`CURRENT_STATE.md`](CURRENT_STATE.md).

## [Unreleased]

### Fixed — the two console entries left after the menu came right

The menu now lays out correctly (see the panel-height entry below). Two things remained.

- **`NullReferenceException` in `HudScreen.SetPrompt`.** Screens build lazily on first show, and the
  HUD is told to hide its controls the moment the app reaches the main menu — before it has ever
  been shown, so before `Build` has run and while every field is still null. Asking a screen that
  does not exist yet to hide something is a reasonable thing for a caller to do; throwing at it is
  not. All four public setters now return early until `IsBuilt`.
- **"No Theme Style Sheet set to PanelSettings".** Added `Assets/Resources/UI/VardholmTheme.tss`,
  which imports Unity's default runtime theme and adds nothing else. It lives under `Resources`
  because the panel settings are built in code and have no inspector for anyone to assign an asset
  in. Every colour and size in this game stays in `Theme.cs` — a look split between a stylesheet and
  code is a look nobody can predict — so the theme exists to supply base control styles, not to
  become a second place where the design is decided.


### Fixed — the unclickable menu: the panel was 219 logical pixels tall

The real cause, arithmetic rather than inference. `CreateFallbackPanelSettings` used
`PanelScreenMatchMode.Shrink`, which picks the **larger** of the two scale factors. On the editor's
2560 × 1440 Game view against a 390 × 844 portrait design that is `max(6.56, 1.71) = 6.56`, so the
layout was handed a logical panel of **390 × 219** — 219 pixels of height for a design needing 844.

Everything downstream follows from that one number:

- Flex children shrink by default, so every element was squeezed to roughly a quarter.
- **A shrunk `Label` does not shrink its text.** The glyphs overflowed the crushed box and drew
  across their neighbours — the title over the subtitle, the slot label over CONTINUE. That is the
  "ghosting", and it was never a frame-buffer problem or a second UI tree.
- **A shrunk `Button` still draws its label at full size**, so the text sat well outside the
  rectangle that actually receives the tap. The menu looked right and could not be pressed.

Three changes, each closing the hole at a different level:

- `PanelSettings` now matches **height** (`MatchWidthOrHeight`, `match = 1`). The scale is
  `height / 844`, the column always gets the height it was designed for, and a wide window simply
  widens the side gutters.
- `Typography` and `Buttons` set `flexShrink = 0`. A line of type is a fixed amount of space or it
  is unreadable, and `minHeight` alone never guaranteed the 48 dp touch target — Yoga goes below a
  minimum to fit a column that is too short. Now no layout anywhere can crush them.
- The menu's spacers keep `minHeight = 0` and absorb a short screen instead.

The diagnostic now prints the logical panel size on every menu show, and errors when it drops below
three quarters of the design height — the number that would have answered this in one Play press.

**Two earlier explanations in this changelog were wrong and are retracted:** the ghost text was not
an uncleared colour buffer, and the UI was not being built twice. The clear-only camera and the
one-panel guard added for those are each correct on their own terms and stay.


### Fixed — the menu was being built twice, which is why nothing was clickable

**And the earlier "uncleared frame buffer" explanation for the ghost text was wrong.** The proof is
simple and was available all along: `MainMenuScreen` adds the title and the subtitle as **siblings
in one flex column**. Two siblings in a column cannot occupy the same pixels. They were overlapping
on screen — so there were two columns, not one, each laid out by a panel with its own scale. Two
copies of the whole UI, the one on top swallowing every tap aimed at the one being looked at.

Two holes let that happen, and both are closed:

- **`CreateDocument` built into a panel that might already hold a tree.** A `UIDocument` keeps what
  it has; building again *adds* rather than replaces. It now clears the root first and says so.
- **The duplicate-install guard only checked the host it was installing on.** A panel from any
  other source — a second host, a document authored into a scene, a play session that did not
  reload the domain — was invisible to it. Every `UIDocument` outside the host is now destroyed,
  loudly.

**And a diagnostic, because this cost two wrong diagnoses.** Entering the menu now logs panel
count, root children, screen depth, whether the stack is stuck transitioning, and whether the
curtain is still up. Each of those four makes a menu that looks perfect and does nothing, none is
visible in a screenshot, and all four are one line of state.


### Fixed — NEW GAME did nothing: the scenes had never been created

Not a bug in the button, the controller, the command or the handler. `Assets/Scenes/` holds only a
README, and `ProjectSettings/EditorBuildSettings.asset` has `m_Scenes: []`. `ZoneRibcage` is not in
the build, `LoadSceneAsync` throws, the loader returns `SceneNotFound`, and the menu stays exactly
where it was. The button worked perfectly; there was nowhere to go.

**Why setup never ran.** `VardholmFirstRunCheck` offered a dialog on editor load. A project that
opens in **Safe Mode never runs `[InitializeOnLoadMethod]` from its own assemblies**, so through
every compile-error round the dialog did not appear once. And a dialog answered "Later" leaves a
project whose NEW GAME silently does nothing.

Setup now runs **automatically and unattended** on first load, and logs what it created. It is safe
to do so: it creates only files that do not exist and overwrites nothing. Phase 1.5's goal was
`clone → open → Play`; a prompt that can be missed or declined was never that.

`SceneLoader` also names the cause now instead of returning a bare code: *"scene 'X' is not in
Build Settings … run Vardholm > Setup Project, then press Play again."*


### Fixed — first runtime failure: "Display 1 - No cameras rendering"

The menu rendered and the game did not. Two separate facts, only one of them a bug.

**The menu did need a camera — this entry originally said it did not, and that was wrong.** UI
Toolkit panels with no `targetTexture` do render in screen-space overlay with no camera, which is
why the menu appeared to work. What has no camera is the **clear**. With nothing clearing the
colour buffer, every frame's UI composited on top of the last one still sitting there, and the menu
smeared into itself — the title and subtitle from an earlier layout pass showing through behind the
current one, at the wrong size, permanently. A screenshot of the running menu is what made it
visible; "renders" and "renders correctly" are not the same claim.

The fix is a clear-only camera on the persistent host: `cullingMask = 0`, so it draws nothing and
costs one clear per frame. It is **not** tagged `MainCamera`, so `Camera.main` can never resolve to
a camera that renders nothing, and it is disabled whenever a zone supplies a real one — by the
state machine on entering `InGame`/`Paused`, and independently by the furnisher's own sweep.

**The gameplay camera was one early-return away from never existing.** `ZoneFurnisher.EnsureCamera`
returned the moment `rig.CameraPivot != null`. **A pivot is not a camera.** Any path producing a
pivot without one — an authored rig, a re-furnish after a camera was disabled or destroyed — left
the zone with no camera and nothing anywhere saying so. The same method also adopted any camera it
found in the scene without checking whether it was enabled or its object active, and never tagged
the camera it built, so `Camera.main` was null even when rendering worked.

Now: a pivot without a camera gets one; an adopted camera is activated, enabled and tagged; the
tag is set *before* the component is added, because `Camera.main` is a cached tag lookup.

**Nothing could fail quietly any more.** `Furnish` returns null unless the player, its
`CharacterController` and an enabled tagged camera all exist, and logs one line naming every one of
them; `PlayerRig.Initialize` refuses to bind silently without a usable camera; `AppBootstrap`
records a zone that came up unplayable. Travel can no longer leave two cameras rendering — during a
transition both zones are briefly resident, and every camera but the current zone's is now
disabled, as is every `AudioListener` but its own.

**Tests.** New `CameraLifecycleTests` (5 PlayMode cases): exactly one enabled camera after New
Game with `Camera.main` resolving to it, still exactly one after travel, and a disabled camera
repaired on re-entry, something always clearing at the menu, and the fallback camera retiring when
a zone opens. Deliberately separate from `GameplayLoopTests`, which asserted only that a
*pivot* existed — the assertion that let this through.

**Not verified.** None of this has been run. The `⚠ VERIFY` note in `Commission` stands: under URP
a runtime-added `Camera` also needs `UniversalAdditionalCameraData`, which URP is documented to add
on demand rather than this code referencing the URP assembly for one component.


### Fixed — sixth editor open: CS0234 `ForgottenIsle.UI.Components` in `HudScreen.cs`

`using ForgottenIsle.UI.Components;` names a namespace that does not exist. The **folder**
`Assets/Scripts/UI/Components/` is real, but `Typography.cs` and `Buttons.cs` deliberately declare
`ForgottenIsle.UI.Core` — their own header comments say so, *"because the contract's namespace
list has no UI.Components entry"*. `HudScreen.cs` already imported `ForgottenIsle.UI.Core`, so the
line was pure surplus; deleting it is the whole fix.

**Validator gained a `PHANTOM` check.** The `USING` check asks whether a *type's* namespace is in
scope; nothing asked whether a namespace *named in a using directive* exists at all. Only
namespaces under `ForgottenIsle.` are checked — `UnityEngine.*`, `System.*` and `NUnit.*` live in
assemblies this validator cannot see, and guessing at them would cry wolf.

One subtlety cost a round: the first version exempted anything whose parent namespace was real, to
accommodate `using static Some.Namespace.Type;`. That exemption swallowed this very bug —
`ForgottenIsle.UI` is real, so the wrong leaf passed. It now applies only when `static` is actually
present. Regression-tested by reintroducing the exact line; clean across all 105 files.


### Fixed — fifth editor open: CS0103 `ResultCode` in `InteractionSystem.cs`

`CommandResult.Fail(ResultCode.NoHandler)` with only `ForgottenIsle.Core.Commands` imported —
`ResultCode` lives in `.Core.Primitives`. One missing `using`.

**The `USING` check should have caught this and did not**, which is the more useful finding. It
collected references only from *type positions* (`new X`, `typeof(X)`, `X field;`, generic
arguments) and never from **static member access** — `ResultCode.NoHandler`,
`LogCode.MissingLocKey`, `ContentIds.ZoneRibcage` — where the type name is a qualifier rather than
a type. Unity reports that form as **CS0103** ("the name does not exist in the current context")
rather than CS0246, which is part of why it read as a different class of problem. Worse, the
check's own fully-qualified guard (*"if the raw token carries a dot, the author qualified it
deliberately"*) would have suppressed exactly these references, since a dot always follows.

The check now scans both streams and applies that guard only to type positions. Extending it
immediately surfaced **a second instance of the same bug** in `InteractionTests.cs`, which Unity
had not reported because the test assembly had not been reached. Regression-tested by removing the
fix and confirming it names the file, the line and the `using` to add; clean across all 105 files.


### Fixed — fourth editor open: 6 × CS0246 in `CommandHandlers.cs`

`InspectCommand` and `CollectCommand` "could not be found", at six call sites. Both types existed
and both were spelled correctly. The cause was in `GameCommands.cs`: when the two structs were
added, the insertion landed one line early and **swallowed the closing brace of
`TravelToZoneCommand`**, so they were parsed as *nested types of it* —
`TravelToZoneCommand.InspectCommand`. The file's own trailing brace compensated, so brace counts
balanced and every existing check passed while nothing that used them could compile.

**Validator gained a `NESTING` check.** It compares what the author indented against what the
braces actually say: a deliberately nested type is indented past its parent, an accidentally
nested one sits at namespace-level indentation because its author believed it was one. The check
was regression-tested by reintroducing the exact bug — it reports both sites and exits 1 — and
then confirmed clean across all 105 files, so it is not trading one silent failure for a noisy one.

This is the second time a brace-level insertion error has shipped from this environment (the first
left an orphaned fragment, which `BALANCE` caught). `BALANCE` cannot catch this one by
construction, because the damage is brace-neutral.


### Added — Phase 2: the playable vertical slice

The technical prototype became a game you can walk around in. Full account, including everything
that is written but unverified, in [`PHASE_2_REPORT.md`](PHASE_2_REPORT.md).

**Progression.** `WorldProgress` (Core, engine-free) holds inspected markers, collected
discoveries and unlocked zones as ordinal string sets with idempotent mutators.
`ProgressService` (Game) owns it and is the **third `ISaveParticipant`**, per the rule that every
new system saves in the PR that adds it. Objectives are **derived, never stored** —
`Objectives.Current(progress, zoneId)` is a pure function, so a player who finds the tag before
reading the stone still gets an objective line that is true.

**Interaction.** An `Interactable` base plus a registry-based proximity scan. The nearest eligible
target each `LateUpdate` becomes a prompt signal; pressing interact builds an `ICommand` and
dispatches it. Three kinds ship: `AncientMarker` (re-readable), `DiscoveryPickup` (once, then
gone), `ZoneGate` (travels, or narrates why it will not). Two new commands, `Inspect` and
`Collect`, with handlers. `TravelToZone` now refuses a locked zone with `NotAllowedInState` —
the lock is a rule at the command layer, not a hidden button in the view.

**Two zones, built at runtime.** `ZoneMeshes` generates ground (33 × 33, two Perlin octaves, the
spawn apron faded flat, vertex colours), rocks and ribs; `ZoneBuilder` holds one recipe per zone —
the Ribcage with its six-rib arch, Fernmaw with its cut aqueduct wall. The scene assets stay
empty, exactly as `SCENE_CONTRACT.md` says: the scene is the contract, the builder is the content.

**A player that walks.** `PlayerRig` now drives a real `CharacterController` with acceleration,
gravity and ground following, and pitches the camera pivot alone so the body never tips. Touch
controls arrived as a floating joystick and a look pad in UI Toolkit, with look consumed on read
and the stick released whenever the controls hide — a pause can no longer leave the player
walking.

**HUD.** Objective, interaction prompt and narration, every string through `LocKey`; 28 new rows
in `en.csv`. `HudController` is the only place that knows both a signal and a `VisualElement`.

**Audio, wired and silent.** `AudioDirector` resolves clips by convention through `Resources`.
**No audio files were fabricated.** Every lookup may return null and every play is then a no-op,
so the day a `.wav` lands in `Resources/Audio` it plays with no code change — and until then
nothing pretends the audio works.

**Dev shortcuts.** F5 travels to the other zone, F6 takes the brass tag, F7 resets progression.
Each goes through the same command the game itself uses, so a shortcut that works is evidence the
real path works rather than a back door around it, and all of it compiles out of release.

**Tests.** 28 new cases — 14 progression, 9 interaction, 5 PlayMode loop tests covering furnishing,
the unlock chain, persistence-by-rebuild, and save/quit/continue. **None has been executed.** The
PlayMode suite self-skips with `Assert.Ignore` when the scenes are absent, so a green run means
nothing until you check it was not simply ignored.

### Changed

- `SaveSections` gained `Progress`, and `ProgressService` now takes its section id from there.
  The file's own docstring says these ids live as constants rather than as literals scattered
  through the participants; the new participant was the one exception, and no longer is.
- `ZoneFurnisher` calls `ZoneBuilder` and adds the `CharacterController`.
- `DevOverlay` reports position, discoveries found, registered interactables and the objective.
- `ci/validate-structure.py` whitelist extended for the Unity types Phase 2 introduced.

### Verified, and not

`validate-structure.py` exit 0 · `check-layering.sh` exit 0 · namespace, symbol-existence, asmdef,
duplicate-type, save-participant, localization, scene-key and entry-point audits all clean.
**Still never compiled, never executed, never played.**


### Fixed — the last two compile errors

- **`InputActionSetupExtensions.AddAction` has no `expectedControlType` parameter** (CS1739, 2
  sites in `VardholmControls.cs`). The named argument is **`expectedControlLayout`**. Verified
  against the Input System API docs rather than guessed.

This lands in the file the risk audit named HIGH RISK, which is some vindication of the audit —
but the failure mode was *better* than predicted. The audit warned that a mistake here "compiles
cleanly and produces an action that silently never fires". This one did not compile at all, which
is the good outcome. **The binding strings themselves are still unverified**: `"2DVector(mode=2)"`,
`<Keyboard>/w`, the processor strings. Those are parsed at runtime and only pressing a key proves
them.

The validator's `DEPRECATED` table gained the CS1739 named argument, so this exact mistake cannot
return silently.

### Fixed — third editor open: all 11 warnings

The project code now compiles far enough to produce warnings rather than stopping. Two errors
remain and are not yet diagnosed (Console was filtered to warnings).

- **`FindObjectsByType<T>(FindObjectsInactive, FindObjectsSortMode)` is obsolete** in Unity 6.x
  (CS0618, 4 sites). Switched to the overload without a sort mode.
- **The `DEVELOPMENT_BUILD` preprocessor symbol is deprecated** (UAC0009, 6 sites). Replaced with
  `DEBUG`, which Unity defines in the editor and in development builds and omits from release —
  the same semantics the code wanted.

**Validator gained a `DEPRECATED` check** carrying a table of Unity APIs this editor reports as
obsolete, each entry earned by actually appearing in the Console rather than guessed. It scans
`code_with_strings` rather than the string-stripped source, because a first pass missed
`Conditional("DEVELOPMENT_BUILD")` and `#if DEVELOPMENT_BUILD` — both live in text the normal
scanner blanks out. Regression-tested on both forms.

These were warnings, not errors, which is exactly why they needed a gate: a warning scrolls past,
and the next editor version turns it into CS0619 — which is precisely what happened to the Input
System package.

### Fixed — second editor open: the first real compile of project code

88 errors became 3, and for the first time they were ours.

- **`GameContext.cs` used `SaveSlotService` without `using ForgottenIsle.Game.Saves;`** (CS0246,
  2 sites). Introduced when the shared slot service was added and the using was not.
- **`SessionService` declared both a `PlayerParticipant` property and a nested
  `PlayerParticipant` class** (CS0102). The nested type is now `PlayerSaveParticipant`; the public
  property keeps its name, since four call sites use it.

**The validator missed both, so it gained two checks:**

- `USING` — a project type referenced whose namespace is not in scope. The existing contract check
  only asked "is this type declared anywhere", so a type that exists but was never imported passed
  cleanly and failed in Unity. Fully-qualified references are ignored.
- `COLLISION` — a member and a nested type sharing a name inside the same type. Owner is resolved
  by brace depth, not regex proximity: a naive first version reported six false positives on
  legal code like `public readonly Severity Severity;` inside a *different* nested type, and a
  check that cries wolf gets ignored.

Both were regression-tested by reintroducing the original errors and confirming each is caught at
the right line with the right fix named.

### Fixed — first editor open

- **`com.unity.inputsystem` pinned at `1.14.0`, which targets Unity `6000.1`, not `6000.6.0f1`.**
  The first attempt to open the project produced 88 × CS0619 inside the package's own
  `HIDDescriptorWindow.cs`, which uses `TreeViewState` / `TreeView` / `TreeViewItem` — deprecated
  in Unity 6.3 and treated as obsolete-as-error. Bumped to `1.19.0`, the version released for
  `6000.6`.
- Not a single error was in project code. Unity halts at the first failing assembly and package
  assemblies compile first, so **our C# still has not been compiled** — the risk audit's verdicts
  are unchanged, neither confirmed nor cleared.

### Added — Phase 1.5: Unity integration

Closes the gap between "the code exists" and "clone, open Unity, press Play".

- **`ForgottenIsle.Editor`** — ADR-0001's fifth assembly, which Phase 1 never created.
- **`Vardholm → Setup Project`** — one idempotent menu command that creates the four scene assets
  through `EditorSceneManager`, writes Build Settings in `SceneKeys.All` order, and verifies the
  localization resource. Plus `Validate Project` (read-only) and `Open Bootstrap Scene`.
- **First-run prompt** — a fresh clone offers setup on first editor load rather than failing
  mysteriously. Offered, not forced: silently writing assets on project open is the kind of
  surprise that makes a toolchain untrustworthy.
- **`ZoneFurnisher`** — builds ground, directional light, entry anchor, player capsule and camera
  for any zone scene that does not provide them. This is what makes an empty scene playable, and
  it is why the scene assets can stay empty and therefore un-corruptible. Authored art always
  wins; the furnisher only fills gaps.
- **`VardholmStartupValidator`** — a development-only startup self-check printing PASS / WARN /
  FAIL for services, scenes in Build Settings, localization, and duplicate bootstrap hosts.
  Editor and development builds only, compiled out of release by `[Conditional]`.
- **Runtime camera-pivot attachment** on `PlayerRig`, removing the last dependency in the project
  that required Inspector assignment. Nothing now needs manual wiring.
- **`docs/SCENE_CONTRACT.md`** — the dependency graph and what each scene must contain.
- **`docs/UNITY_RISK_AUDIT.md`** — per-subsystem compile risk, honestly rated.
- Two new test files: `StartupValidatorTests` (EditMode) and `FirstPlayableFlowTests` (PlayMode),
  the latter walking new game → furnished zone → pause → resume → save → quit → continue →
  restored, which is the loop that had no coverage at all.

### Fixed

- `AppBootstrap` searched a loaded zone for a player rig, found none in an empty Phase 1 scene,
  and **returned silently** — leaving the player in a zone with no body, no camera and nothing
  underfoot. It now furnishes the zone instead.
- The structural validator did not know `UnityEditor` types, so the new Editor assembly tripped
  its undeclared-type check. It now carries a separate `UNITY_EDITOR_TYPES` set, kept distinct so
  the editor-only boundary stays visible.
- `Assets/Scenes/README.md` and `docs/dev-setup.md` told the reader to build four scenes and their
  contents by hand. Both now point at the menu command.

### Added — Project memory system

Eight documents that make the repository the authoritative source of truth, so a session can be
picked up cold without re-deriving the project.

- **`CLAUDE.md`** (root) — operating rules, hard invariants and the source-of-truth ordering.
- **`docs/PROJECT_HANDOFF.md`** — the cold-start entry point and the exact next task.
- **`docs/CURRENT_STATE.md`** — verified present state, the verification ledger, and six live
  CONFLICTS.
- **`docs/DECISIONS.md`** — the decision ledger: 6 owner-locked decisions, 14 ADRs, 9 recovered
  design decisions, 4 producer amendments, 9 open items, and a list of decisions deliberately
  *not* made.
- **`docs/ARCHITECTURE.md`**, **`docs/GAME_VISION.md`**, **`docs/ROADMAP.md`** — the map, the
  pitch, the plan.
- The changelog moved from the repository root to `docs/`, with a pointer left behind.

**Recovered, not invented.** Every decision cites the instruction or document that established it.
Uncertain facts are marked `UNCONFIRMED`; contradictions are marked `CONFLICT` rather than being
silently resolved.

**The headline finding:** ADRs 0001–0014 assigned their document edits to "Phase 1 Task 0", and
**Task 0 was never executed**. Six contradictions are therefore live in the documentation,
including one where the shipped code contradicts the specification — the world clock is 60× in
`Ticker.cs` and 30× in the design documents, and the survival formulas were tuned at 30×.

### Added — Phase 1: Project Spine

The first code in the project. Five assemblies, ~16,000 lines of C# across 79 files.

- **`ForgottenIsle.Core`** — engine-free (`noEngineReferences: true`), so the rules of the game
  are a library that can be tested without opening Unity. Primitives (`Vec3`, `LocKey`,
  `ResultCode`), a seeded PCG random whose state serializes into the save, a signal bus, the
  command pattern with a validate/execute split, game state, the versioned save envelope with a
  real migration seam, engine-free JSON and CSV parsing, and localization.
- **`ForgottenIsle.Game`** — bootstrap and composition root, the state machine, the single
  `Update()` in the project, additive scene loading with a 20-second watchdog, the zone registry,
  the session service, an atomic save store (flush-then-rename with `.bak` rotation), input,
  a placeholder player rig and the dev overlay.
- **`ForgottenIsle.UI`** — UI Toolkit framework ported from NATION: WORLD ORDER and rethemed,
  plus the menu, pause and settings screens and their controllers.
- **Tests** — 7 EditMode files, 1 PlayMode file.
- **`ci/validate-structure.py`** — a static validator standing in for the compiler this
  environment does not have: brace balance, namespace conformance, the engine-free Core rule,
  asmdef validity, undeclared-type detection across files, LocKey coverage and duplicate types.
- **`ci/check-layering.sh`** — the architecture gate. It has already caught a real violation.

### Changed

- Title is **VARDHOLM**; the prior working title is retired from all document titles.
- ADR-0014 reverses the Phase 1 plan's uGUI decision in favour of UI Toolkit, because the team
  already owns a working code-built UI Toolkit framework in the Nation project.
- The Phase 1 plan's state-machine table was wrong: it had no edge into `MainMenu` from `InGame`
  or `Paused`, which made QUIT TO MENU unreachable. Corrected to 10 edges, routing quit through
  `Loading` so the curtain can cover scene unloading.

### Added — Phase 0: Design, Architecture, MVP, Story Foundation

Initial design and architecture documentation. **No game code yet** — Phase 0 is design only,
and implementation is blocked pending approval of the Phase 1 plan.

- **Story Bible v1.0** — canonical original IP: the island of Vardholm, protagonist Nadia Vesk,
  four occupation layers, the five-act structure, both endings, and a 10-rule anti-inconsistency
  contract. Establishes the project's naming authority and glossary.
- **Game Vision** — one-page pitch: player fantasy, audience, differentiation, session length and
  the free-prologue/premium-unlock commercial model.
- **Core Game Loop** — the loop diagram, three nested loops (micro/mid/macro), the no-arrow rule
  and the three-tier diegetic hint ladder.
- **World Structure** — nine zones fully specified, the gating graph, a capability/backtracking
  table, a 25-beat critical path and the anti-softlock rule set.
- **First 30 Minutes** — minute-by-minute prologue shooting script with the complete prologue item
  and recipe manifest, and the radio puzzle specified end to end.
- **Mobile UX Plan** — first-person recommendation with a cinematic Body Camera, full portrait
  control scheme, HUD, inventory and combine flow, and accessibility commitments.
- **Technical Architecture** — five assemblies with an engine-free `Core`, the command pattern,
  event flow, DI, scene streaming, performance and localization architecture, and CI enforcement.
  Opens with a verification ledger tagging every unconfirmed Unity 6 claim.
- **Core Systems** — all 17 systems specified with APIs, owned state, dependency graph, tick
  order, shipping-first-pass survival formulas and a named test matrix.
- **Data & Save Architecture** — ScriptableObject authoring baked to immutable records consumed by
  the engine-free Core, ten definition types, runtime state mirrors, and a versioned save envelope
  with a chain-of-migrations framework.
- **Title Evaluation** — five candidates scored against live store searches. Recommends renaming
  from *The Forgotten Isle* to **Vardholm**, with a clearance checklist.
- **MVP Scope & Phase Roadmap** — MVP content manifest, exclusions, risk register and Phases 0–16.
- **Phase 1 Plan** — the executable plan: preconditions, file manifest, code sketches, test list
  and acceptance criteria.

### Fixed — blockers found by adversarial review

A three-way review pass (IP risk, Unity 6 technical accuracy, cross-document consistency) raised
67 issues, 14 of them blockers. Resolved in this commit:

- **Title research error corrected.** The title evaluation claimed no exact "The Forgotten Isle"
  app existed. That was wrong: at least five games use the exact title on itch.io, including a
  $4.99 "mysterious survival-exploration experience" and one built "around immersive sound."
  Recorded with URLs, risk re-scored 2 → 1, and the working title is now recommended for
  retirement rather than carried.
- **"The Mercator Trust" renamed to "The Orrimond Trust"** throughout. The original collided with
  a real, live Guernsey trust company — and the fiction depicts an identically-named trust as
  negligent, which makes it worse than an ordinary name collision.
- **Core temperature formula corrected.** `k = 0.34` is an exponential rate that reaches
  hypothermia in ~3 minutes, not the ~55 the surrounding prose claimed. Re-derived to
  `k = 0.0187`, verified numerically, and a CI balance test now pins the three stated durations.
- **13 binding ADRs added** (`docs/architecture/00-decisions.md`) resolving the structural
  contradictions between the three architecture documents: one assembly layout, an engine-free
  Core with no exceptions, a C# language-level polyfill for `record`/`init` (the definition layer
  as written did not compile under Unity 6), one streaming model, one `SurvivalStat` enum, one
  inventory model, one locomotion scheme, a complete save participant list, and closure of two
  anti-softlock holes on the critical path.

### Known open items

Tracked in `docs/architecture/00-decisions.md` — unacknowledged LOST/DHARMA structural
convergence, proper-noun clearance, unbudgeted foliage overdraw, over-promised volumetric
effects, no privacy-manifest plan, and one unreconciled world-clock scale.

### Notes

- No trademark register has been searched. No name in this repository is cleared for use.
- All science in the fiction is dramatised, not claimed.
