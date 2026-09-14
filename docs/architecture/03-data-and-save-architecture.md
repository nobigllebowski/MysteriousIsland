# THE FORGOTTEN ISLE — Data & Save Architecture

**Document owner:** Data Architect
**Status:** v1.0, production spec
**Target:** Unity 6 LTS · C# · URP · New Input System · portrait mobile (iOS + Android)
**Companion canon:** `Story Bible v1.0` — all IDs, names and worked examples below are drawn from it and from nowhere else.

---

## 0. The Structural Premise

Three assemblies. This is the constraint everything else bends around.

| Assembly | References | Contains |
|---|---|---|
| `Isle.Core` | **Nothing Unity.** `netstandard2.1`. | Definitions (immutable records), runtime state structs, rules, survival simulation, puzzle solvers, save envelope + migrators, the `IDefinitionRepository` interface. |
| `Isle.Unity` | `Isle.Core`, `UnityEngine`, URP, Input System | MonoBehaviours, presenters, addressables loading, the concrete `BakedDefinitionRepository`, platform IO. |
| `Isle.Authoring` | `Isle.Core`, `UnityEditor` (`Editor` platform only) | ScriptableObject authoring types, the baker, the validator, the editor windows. **Stripped from player builds.** |

`Isle.Core` compiles, runs and is unit-tested in a plain `dotnet test` run with no Unity installed. That is not an aesthetic preference — it is what makes the survival sim, the damper-console tuning maths and the save migration chain testable in a CI job that finishes in seconds instead of a Unity batchmode licence dance. Every decision in Part 1 is scored against whether it preserves that property.

---

# PART 1 — ScriptableObjects vs JSON vs the Hybrid

## 1.1 What is actually being decided

Not "how do we store data." Two separable questions that get conflated:

- **Q1 — Authoring surface.** What does a designer touch when they want the Catchment to yield 1.2 L instead of 1.0 L?
- **Q2 — Consumption surface.** What does the code that computes yield actually hold in memory at runtime?

SOs-vs-JSON is usually argued as if Q1 and Q2 must have the same answer. They don't, and the fact that they don't is where the good architecture lives.

## 1.2 The comparison, criterion by criterion

### a) Designer authoring ergonomics in the Editor

**ScriptableObject: strong win.** Object-field drag-and-drop, `[Range]` sliders, custom property drawers, inline previews of the item icon, right-click → Find References In Scene. A designer tuning `item.catchment_rig` sees the mesh thumbnail, the loc key resolved to English via a drawer, and the four recipes that consume it — without leaving the Inspector.

**Raw JSON: weak.** A designer editing `{"yieldLitres": 1.0}` in a text editor has no autocomplete on item IDs, no way to see that `item.cordage_plaited` exists, and no guard against typing `item.cordage_plait`. You can build a bespoke JSON editor window, but you are then reimplementing the Inspector, badly, and paying for it forever.

Verdict: **SO**, decisively. This is the single strongest argument for SOs and it should not be waved away.

### b) Reference integrity — object refs vs string IDs

**SO object refs:** the reference is a GUID+localId in the `.asset` meta. Rename the file, the reference survives. Delete the asset and the reference becomes `{fileID: 0}` — a silent null at runtime, discovered when a player hits it. Unity gives you no compile-time or build-time failure for a dangling SO reference unless you write the validator yourself. So the "SO refs are safe" claim is half true: they survive *renames* (real benefit) but not *deletions* (no better than strings, and arguably worse because the failure is a null deref in gameplay rather than a lookup miss you can log).

**String IDs:** no rename safety, total transparency. `"item.hydrophone_directional"` in a diff is readable by a writer, a localiser and a git blame. Integrity is enforced by a validator, not by the serializer.

Verdict: **tie on safety, split on ergonomics.** SO wins in-editor, string IDs win everywhere the data leaves the editor. The hybrid takes both: author with object refs, **bake to string IDs**, and make the baker the thing that turns a dangling ref into a hard build failure. The dangling-reference problem is solved by a validator in either world; the only question is whether you get it at build time or at play time.

### c) Diffability and merge conflicts

**SO:** `.asset` files are YAML *if* the project forces text serialization (Project Settings → Editor → Asset Serialization → Force Text). Even then a two-line change produces a diff full of `m_ObjectHideFlags`, `m_CorrespondingSourceObject`, `m_PrefabInstance`, and reference blocks like `{fileID: 11400000, guid: 4a9c…, type: 2}`. A reviewer cannot tell from the diff that the reference points at Poulter's journal. Merge conflicts inside a YAML SO are resolvable but unpleasant, and `.meta` GUID collisions from two people creating assets on parallel branches are a recurring papercut. UnityYAMLMerge (`Tools/UnityYAMLMerge` in the Unity install, configured as a git mergetool) mitigates this materially. **Unverified: exact path/binary name varies by Unity version and platform — confirm against the installed Unity 6 LTS before writing it into the repo's `.gitconfig`.**

**JSON:** clean. One field per line, stable key ordering if you enforce it, a diff a narrative designer can review in a GitHub PR without opening Unity. Text merges behave.

Verdict: **JSON wins** — but note the hybrid neutralises most of the loss: the *baked* catalog is generated output, so it is either gitignored (my recommendation) or committed as a single deterministic artifact that never conflicts because it is regenerated wholesale.

### d) Build size and load cost on mobile

**SO:** each `.asset` becomes a serialized object in a bundle. Unity's loader handles them efficiently and they memory-map well, but you pay per-object overhead and you pay for the type metadata. A catalog of ~900 definitions (see §2 for the counts) as individual SOs is ~900 objects the loader must resolve, plus their reference graph.

**Baked binary catalog:** one file, one read, one deserialize pass. For our scale — I estimate ~900–1,200 definitions totalling **300–600 KB as pretty JSON, 120–250 KB as minified JSON, 60–140 KB as MessagePack, and 25–60 KB after LZ4** — the whole static catalog fits comfortably in a single load that should complete in the low tens of milliseconds on an iPhone 12.

**These numbers are estimates from definition counts × field counts, not measurements.** They are stated to size the decision, not to be quoted. §4.2 specifies exactly how to measure them for real.

Verdict: **baked catalog wins**, and the win is on *load determinism* more than raw bytes. One blocking read with a known cost beats 900 addressable handle resolutions with a long tail.

### e) Addressables and memory behaviour

The important split: **definitions are tiny, assets are huge.** A `ItemDefinition` is ~300 bytes of structured data. Its 1024×1024 icon and its mesh are 2–8 MB.

If definitions are SOs with direct `Sprite`/`GameObject` references, loading the definition drags the asset in — Unity resolves the reference on load unless you route through `AssetReference`. That is the classic mobile memory blowout: you wanted to know the item's weight and you resident-set 400 MB of icons.

The hybrid forces the correct shape: **baked definitions contain only `string` addressable keys**, never asset references. `IconAddress = "ui/icons/item_hydrophone_directional"`. The entire definition catalog is resident for the whole session at a cost of well under a megabyte; assets load and unload on zone transitions through Addressables with explicit ref-counting.

Verdict: **hybrid wins outright.** This is the second-strongest argument after testability.

### f) Hot-reload and live tuning

**SO:** edit in Inspector during Play mode, the change is live immediately. Best-in-class for tuning a survival curve while playing. Caveat every Unity dev knows: edits made during Play to SO assets **persist** after exiting Play (unlike scene/component edits, which revert) — which is a feature for tuning and a footgun when you were experimenting. The team must know this.

**Baked catalog:** dead by default — you'd have to rebake and reload.

**The hybrid must not lose this**, so it doesn't:

- In the Editor, the repository is `LiveBakeDefinitionRepository`. It bakes SOs → records **in memory, on demand**, and subscribes to an editor-side change signal so an edited SO invalidates and rebakes just that definition (single-definition rebake is sub-millisecond).
- A toolbar toggle switches the Editor to `BakedFileDefinitionRepository`, reading the actual shipped catalog, so you can verify the bake is faithful before pushing.
- In a player build only the baked path compiles. `Isle.Authoring` is not in the build.

Verdict: **tie, via engineering.** The hybrid's live-bake path preserves the SO tuning loop; we spend ~200 lines to keep it.

### g) Testability from an engine-free Core

**This is the crux and it is not close.**

`ScriptableObject` derives from `UnityEngine.Object`. You cannot construct one outside the Unity runtime; `ScriptableObject.CreateInstance` in a plain `dotnet test` process throws or crashes. So:

- If `Isle.Core` consumes SOs, `Isle.Core` references `UnityEngine`, and the whole engine-free assembly is gone. Every test of the survival tick, the damper-gate tuning solver, the Field Slate revision logic and the save migration chain now requires Unity batchmode.
- If `Isle.Core` consumes POCOs, a test is `new ItemDefinition(...)` and runs in a 2-second `dotnet test`.

There is a partial escape — define POCO data on the SO in a `[Serializable]` nested class and have Core consume *that* — but you still need an SO instance to reach it in-editor, and you've invented the hybrid anyway, just without the build-time bake or the validation gate.

The cost of getting this wrong is not theoretical. The damper console (Act 4) is four gates × ballast/vent/tension tuned live against a tide table. That is a numerical system with a solution space, and it needs hundreds of property-based test cases run on every commit. Survival needs the same: thirst decay under humidity in the Sweatways vs cold on Rime Shoulder, across a 36-hour Act 1 window. Neither is testable at the required cadence through Unity batchmode.

Verdict: **POCO/baked wins, and this criterion alone is close to decisive.**

### h) Localization pipeline fit

Every user-facing string is a key (`ui.item.hydrophone_directional.name`), resolved at display time by Unity Localization (or an equivalent). The pipeline needs to:

1. **Extract** the full set of keys referenced by content, to hand a translator a complete job.
2. **Verify** every referenced key exists in the source-language string table.

With SOs, extraction means an editor script that walks every asset — fine, but editor-only, so it can't run in a lightweight CI job. With the **baked catalog**, extraction is `grep`-adjacent: a small engine-free tool reads the catalog, pulls every field the schema marks as a loc key, and emits a `.csv`/`.xliff`. It runs in the same CI step as the Core tests, with no Unity.

Verdict: **baked catalog wins.**

### i) Modding / DLC / expansion support

Ship as a single sealed binary catalog and modding means shipping a new build. Ship a *format* the runtime can load additional catalogs of, and DLC becomes "drop `dlc_sweatways.catalog` next to the base one; the repository merges by ID with a declared precedence order."

Note the tension with anti-tamper: a readable catalog is an editable catalog. For a premium single-player narrative game with no economy and no leaderboards, **that is not a threat worth engineering against.** The realistic risk is a player editing yields and then reporting the resulting bug. Mitigation is a cheap integrity hash on the base catalog recorded in the save (§4.6), so support can tell a modified run apart — not DRM, just triage.

Verdict: **baked catalog wins**, with a design that keeps the door open without requiring us to walk through it in v1.

### 1.3 Scorecard

| Criterion | Pure SO | Pure JSON | **Hybrid (SO author → bake → POCO)** |
|---|---|---|---|
| Designer ergonomics | ★★★ | ★ | **★★★** |
| Reference integrity | ★★ | ★★ | **★★★** (validator gates the build) |
| Diff / merge | ★ | ★★★ | **★★** (SO diffs stay, baked output is generated) |
| Mobile build size / load | ★★ | ★★ | **★★★** |
| Addressables / memory | ★ | ★★★ | **★★★** |
| Hot reload / live tuning | ★★★ | ★ | **★★★** (via live-bake path) |
| **Engine-free testability** | **✗** | ★★★ | **★★★** |
| Localization pipeline | ★★ | ★★★ | **★★★** |
| Modding / DLC | ★ | ★★★ | **★★★** |

## 1.4 RECOMMENDATION

> **Author in ScriptableObjects. Bake to immutable C# records. Ship a versioned binary catalog. Core consumes it through `IDefinitionRepository` and never learns that Unity exists.**

The single line that justifies it: **the authoring surface and the consumption surface have different customers.** The authoring surface's customer is a designer in the Inspector, and SOs serve that customer better than anything else Unity offers. The consumption surface's customers are a mobile loader, a localisation pipeline, a CI test runner and a save migrator — and every one of those is better served by a plain immutable record in a single file. Making one artifact serve both means one of those two sets of customers loses, permanently, on every feature for the life of the project.

The bake is the seam. And because the bake is a step, it is a **place to put a gate** — which is the second-order benefit and honestly the one I'd defend hardest. Without a bake step there is no natural moment at which someone asks "does every recipe in this game have a reachable ingredient set?" With one, that question is asked automatically, every build, and the answer blocks the build.

### What we give up, stated honestly

1. **A generated intermediate exists.** Someone will eventually debug a stale catalog. Mitigations: the catalog carries a content hash of its SO inputs; the Editor warns on mismatch at domain reload; the baker is fast enough (§1.5) to run unconditionally on a play-mode entry that touches definitions.
2. **A definition change is two steps in the shipped build path**, not one. Absorbed by the live-bake path in-editor — a designer never types "bake."
3. **Baker complexity.** ~1,500 lines including the validator. Real, and it is the cheapest 1,500 lines in the project.

### 1.5 How the bake works, exactly

**Inputs:** every `*.asset` under `Assets/Data/Authoring/`, typed as a subclass of `DefinitionAuthoringBase`.

**Output:** `Assets/Data/Baked/isle.catalog` (MessagePack, LZ4-block compressed) plus a sidecar `isle.catalog.manifest.json` for human inspection and CI diffing.

**Pipeline, in order:**

```
1. DISCOVER   AssetDatabase.FindAssets("t:DefinitionAuthoringBase") → deterministic sort by
              (definitionType, id) so bytes are reproducible across machines.
2. RESOLVE    Every SO object reference → its target's string ID.
              Null ref  → BakeError.DanglingReference (hard fail).
              Ref to wrong definition type → BakeError.TypeMismatch (hard fail).
3. VALIDATE   Run the full rule set from §2.12. Errors fail; warnings print and continue.
4. PROJECT    Authoring SO → immutable Core record. One `ToRecord()` per authoring type;
              hand-written, not reflection, so a field added to the SO without being added
              to the record is a COMPILE error, not a silent data loss.
5. GRAPH      Precompute derived indices: recipe-by-output, recipe-by-ingredient,
              discovery-by-trigger, interaction-by-location, puzzle-by-zone.
              These are baked, not computed at load — saves ~8–15 ms of startup dictionary
              construction on a mid-range Android (estimate; see §4.2 for measurement).
6. HASH       SHA-256 over the canonical record stream → CatalogHash. Written into the
              catalog header AND into every save file (§4.1).
7. WRITE      MessagePack → LZ4 block → atomic temp-then-rename into Assets/Data/Baked/.
8. REPORT     bake-report.json: counts by type, byte sizes, timings, every warning.
              CI uploads it as a build artifact.
```

**When it runs:**

| Trigger | Behaviour |
|---|---|
| Editor, entering Play mode | Incremental live-bake in memory. Only dirty definitions reproject. Typical <50 ms. |
| Editor, an authoring asset is saved | Mark dirty. No full bake. |
| `Isle/Data/Bake Catalog` menu, or `-executeMethod Isle.Authoring.Baker.BakeAll` | Full bake to disk. |
| `IPreprocessBuildWithReport.OnPreprocessBuild` (order 0) | **Full bake, always, unconditionally.** Never trusts the on-disk catalog. |
| CI, on every PR touching `Assets/Data/**` | Full bake + validate. Fails the PR on any error. |

**How it fails loudly — the non-negotiable part:**

- `OnPreprocessBuild` throws `BuildFailedException` on the **first** error class encountered, after collecting and printing **all** errors. You get the full list, not a whack-a-mole loop of one-error-per-build.
- Every error line is `[BAKE-ERROR] <RuleId> <definitionId> <field>: <message> → <assetPath>`, and the asset path is clickable in the Console.
- **There is no `--force` flag. There is no warning-only mode for errors.** A dangling reference has exactly one remedy: fix the data. The moment a bypass exists, it becomes the default under deadline pressure.
- If the runtime ever loads a catalog whose `SchemaVersion` doesn't match the build's expected constant, it throws at startup with both numbers in the message. A shipped mismatch is a crash-on-launch, which is bad — and correct, because the alternative is silent misreads of a struct layout, which is worse and takes three weeks to diagnose.
- Runtime lookup miss (`repo.GetItem("item.typo")`) throws `DefinitionNotFoundException` in Development builds and logs-and-returns-`null` in Release, guarded by a nullable return contract (`TryGet` is the normal path; `Get` throws).

```csharp
// Isle.Core — the only thing gameplay ever sees.
public interface IDefinitionRepository
{
    int SchemaVersion { get; }
    string CatalogHash { get; }

    bool TryGet<TDef>(string id, out TDef def) where TDef : class, IDefinition;
    TDef Get<TDef>(string id) where TDef : class, IDefinition;   // throws on miss
    IReadOnlyList<TDef> All<TDef>() where TDef : class, IDefinition;

    // Baked indices — O(1), no allocation on the hot path.
    IReadOnlyList<RecipeDefinition>      RecipesProducing(string itemId);
    IReadOnlyList<RecipeDefinition>      RecipesConsuming(string itemId);
    IReadOnlyList<InteractionDefinition> InteractionsAt(string locationId);
    IReadOnlyList<DiscoveryDefinition>   DiscoveriesTriggeredBy(DiscoveryTriggerKind kind);
}

public interface IDefinition
{
    string Id { get; }
    int    ContentRevision { get; }   // bumped by the baker when this record's bytes change
}
```

---

# PART 2 — DEFINITIONS

## 2.0 Universal rules

1. **Immutable.** C# `record` with `init`-only properties. Collections exposed as `ImmutableArray<T>`. No setters, no `List<T>` on the public surface.
2. **Stable string ID.** Assigned once, never changed. Renaming an ID is a save-migration event (§4.3), not a refactor.
3. **All user-facing text is a loc key.** A field whose value a player can read is named `*LocKey` and validated against the source string table. Any literal English in a definition field fails validation.
4. **No runtime state.** No `bool isDiscovered`, no `float currentDurability`. If a field would differ between two save files, it belongs in Part 3.
5. **Asset references are addressable key strings**, never typed asset handles.

## 2.1 ID naming convention

```
<type>.<subject>[.<qualifier>]
```

Lowercase, `snake_case` within a segment, ASCII only, `[a-z0-9_.]{3,64}`, regex-enforced.

| Prefix | Type | Canon examples |
|---|---|---|
| `item.` | ItemDefinition | `item.hydrophone_directional`, `item.field_recorder`, `item.dry_bag`, `item.multitool`, `item.field_slate`, `item.catchment_rig`, `item.brass_tag_dated`, `item.reel_sabo_log_14` |
| `res.` | ResourceDefinition | `res.fresh_water`, `res.recorder_battery`, `res.hemp_fibre`, `res.basalt_shard` |
| `recipe.` | RecipeDefinition | `recipe.cordage_plaited`, `recipe.catchment_rig`, `recipe.dry_box`, `recipe.fire_bundle` |
| `disc.` | DiscoveryDefinition | `disc.gully_cut_stone`, `disc.machete_cuts_fresh`, `disc.reel_sabo_gates`, `disc.grave_maintained` |
| `int.` | InteractionDefinition | `int.ribcage_hull3_locker`, `int.combs_ladder_bolt`, `int.oleander_gate_b_valve` |
| `puz.` | PuzzleDefinition | `puz.sweatways_baffle_order`, `puz.oleander_damper_console`, `puz.combs_register_cell4` |
| `loc.` | LocationDefinition | `loc.ribcage`, `loc.fernmaw`, `loc.fold_camp`, `loc.combs`, `loc.sweatways`, `loc.rime_shoulder`, `loc.ash_throat`, `loc.oleander`, `loc.quiet_room` |
| `story.` | StoryDefinition | `story.act1_first_water`, `story.act2_summit_mast_stripped`, `story.act5_lorvik_first_words` |
| `quest.` | QuestDefinition | `quest.act1_water_36h`, `quest.act3_restore_power`, `quest.act4_restart_cell` |
| `camp.` | CampUpgradeDefinition | `camp.drying_rack`, `camp.reel_deck_bench`, `camp.catchment_array_t2` |
| `ui.` / `doc.` / `vo.` | Localization keys | `ui.item.hydrophone_directional.name`, `doc.poulter.1887_11_02.body` |

**Zone qualifier ordering:** `<type>.<zone>_<subject>` for anything zone-bound (`int.fernmaw_trench_grate`), so an alphabetical listing groups by zone. Ad-hoc after two weeks of content is unrecoverable; enforce from commit one.

**Reserved:** `_test.` prefix for definitions excluded from ship builds (validator errors if present in a non-Development build bake). `dlc_<pack>.` prefix namespaces future catalogs.

---

## 2.2 `ItemDefinition`

Anything that can sit in the inventory. Includes tools, consumables, documents and evidence objects.

```csharp
namespace Isle.Core.Definitions;

public sealed record ItemDefinition : IDefinition
{
    public string Id                    { get; init; } = "";
    public int    ContentRevision       { get; init; }

    // ---- Presentation (all loc keys / addressable keys) ----
    public string NameLocKey            { get; init; } = "";
    public string ShortDescLocKey       { get; init; } = "";   // one line, inventory grid
    public string ExamineLocKey         { get; init; } = "";   // Nadia's voice, examine view
    public string IconAddress           { get; init; } = "";
    public string PrefabAddress         { get; init; } = "";   // "" = no world representation
    public string ExamineModelAddress   { get; init; } = "";   // high-poly examine LOD, "" = reuse prefab

    // ---- Classification ----
    public ItemCategory Category        { get; init; }
    public ImmutableArray<string> Tags  { get; init; } = ImmutableArray<string>.Empty;

    // ---- Inventory physics ----
    public int   MaxStack               { get; init; } = 1;
    public float MassKg                 { get; init; }
    public float VolumeLitres           { get; init; }
    public bool  IsQuestCritical        { get; init; }         // cannot be dropped or destroyed
    public bool  IsPersistentTool       { get; init; }         // survives any inventory wipe event

    // ---- Condition model (definition of the curve, NOT current condition) ----
    public bool  HasCondition           { get; init; }
    public float MaxCondition           { get; init; } = 1f;
    public float CorrosionPerHourWet    { get; init; }         // condition lost/hr while Wet
    public float CorrosionPerHourDry    { get; init; }
    public string DegradedVariantId     { get; init; } = "";   // item this becomes at condition 0, "" = unusable-in-place

    // ---- Power (1960s-era gear behaves its age) ----
    public bool   ConsumesPower         { get; init; }
    public string PowerCellItemId       { get; init; } = "";
    public float  PowerDrawPerHour      { get; init; }         // fraction of one cell per hour of active use

    // ---- Consumable effects ----
    public ImmutableArray<SurvivalEffect> UseEffects { get; init; } = ImmutableArray<SurvivalEffect>.Empty;
    public bool   ConsumedOnUse         { get; init; }
    public string ResidueItemId         { get; init; } = "";   // e.g. empty vessel

    // ---- Evidence hooks ----
    public string GrantsDiscoveryId     { get; init; } = "";   // on first pickup
    public string ReadableDocumentId    { get; init; } = "";   // opens the document reader
    public string PlayableReelId        { get; init; } = "";   // Reel Deck audio, Act 3+

    // ---- Audio ----
    public string PickupSfxAddress      { get; init; } = "";
    public string UseSfxAddress         { get; init; } = "";
}

public enum ItemCategory
{
    Tool, Material, Consumable, Vessel, Document, Reel, Component, Evidence, Key
}

public readonly record struct SurvivalEffect(SurvivalStat Stat, float Delta, float OverSeconds);
public enum SurvivalStat { Hydration, Warmth, Fatigue, Morale, RecorderCharge }
```

**Worked example — the hydrophone, Nadia's signature tool:**

```csharp
new ItemDefinition {
    Id                  = "item.hydrophone_directional",
    ContentRevision     = 7,
    NameLocKey          = "ui.item.hydrophone_directional.name",
    ShortDescLocKey     = "ui.item.hydrophone_directional.short",
    ExamineLocKey       = "ui.item.hydrophone_directional.examine",
    IconAddress         = "ui/icons/item_hydrophone_directional",
    PrefabAddress       = "props/tools/hydrophone_directional",
    ExamineModelAddress = "props/tools/hydrophone_directional_hi",

    Category            = ItemCategory.Tool,
    Tags                = ImmutableArray.Create("acoustic", "act1_loadout", "signature", "salt_sensitive"),

    MaxStack            = 1,
    MassKg              = 1.4f,
    VolumeLitres        = 1.1f,
    IsQuestCritical     = true,
    IsPersistentTool    = true,

    HasCondition        = true,
    MaxCondition        = 1f,
    CorrosionPerHourWet = 0.018f,   // salt eats everything — Rule 5
    CorrosionPerHourDry = 0.0004f,
    DegradedVariantId   = "item.hydrophone_directional_fouled",

    ConsumesPower       = true,
    PowerCellItemId     = "item.recorder_cell_d",
    PowerDrawPerHour    = 0.22f,

    UseEffects          = ImmutableArray<SurvivalEffect>.Empty,
    ConsumedOnUse       = false,

    GrantsDiscoveryId   = "",
    PickupSfxAddress    = "sfx/ui/pickup_metal_small",
    UseSfxAddress       = "sfx/tool/hydrophone_gain_click",
}
```

**Second worked example — Marguerite Sabo's Log 14, the reel that first names "the gates":**

```csharp
new ItemDefinition {
    Id               = "item.reel_sabo_log_14",
    ContentRevision  = 3,
    NameLocKey       = "ui.item.reel_sabo_log_14.name",
    ShortDescLocKey  = "ui.item.reel_sabo_log_14.short",
    ExamineLocKey    = "ui.item.reel_sabo_log_14.examine",
    IconAddress      = "ui/icons/item_reel_quarter_inch",
    PrefabAddress    = "props/media/reel_quarter_inch",

    Category         = ItemCategory.Reel,
    Tags             = ImmutableArray.Create("sabo", "act2", "evidence", "tape_stretch"),

    MaxStack         = 1,
    MassKg           = 0.3f,
    VolumeLitres     = 0.4f,
    IsQuestCritical  = true,
    IsPersistentTool = false,

    HasCondition        = true,
    MaxCondition        = 1f,
    CorrosionPerHourWet = 0.05f,     // tape stretches; wet is fatal fast
    CorrosionPerHourDry = 0f,
    DegradedVariantId   = "item.reel_sabo_log_14_damaged",

    PlayableReelId      = "doc.reel.sabo.log_14",
    GrantsDiscoveryId   = "disc.reel_sabo_gates",
    PickupSfxAddress    = "sfx/ui/pickup_tape_reel",
}
```

---

## 2.3 `ResourceDefinition`

Distinct from `ItemDefinition` because resources are **measured quantities**, not countable objects. Water is litres, not "3 waters". Keeping them separate stops fractional-stack hacks in the inventory.

```csharp
public sealed record ResourceDefinition : IDefinition
{
    public string Id                  { get; init; } = "";
    public int    ContentRevision     { get; init; }

    public string NameLocKey          { get; init; } = "";
    public string UnitLocKey          { get; init; } = "";   // "L", "g", "cell-hours"
    public string IconAddress         { get; init; } = "";

    public ResourceKind Kind          { get; init; }
    public float  MinValue            { get; init; }
    public float  MaxCarry            { get; init; }         // hard carry cap without a vessel
    public float  DecayPerHour        { get; init; }         // spoilage/evaporation
    public string DecaysIntoId        { get; init; } = "";   // "" = vanishes
    public string RequiredVesselTag   { get; init; } = "";   // must hold a vessel with this tag
    public bool   IsPotable           { get; init; }
    public int    DisplayPrecision    { get; init; } = 1;    // decimal places in HUD
    public string HudColorToken       { get; init; } = "";   // palette token, not a hex literal
}

public enum ResourceKind { Liquid, Solid, Charge, Fuel }
```

**Worked example — fresh water, the whole of Act 1:**

```csharp
new ResourceDefinition {
    Id                = "res.fresh_water",
    ContentRevision   = 4,
    NameLocKey        = "ui.res.fresh_water.name",
    UnitLocKey        = "ui.unit.litre_short",
    IconAddress       = "ui/icons/res_fresh_water",
    Kind              = ResourceKind.Liquid,
    MinValue          = 0f,
    MaxCarry          = 4.5f,
    DecayPerHour      = 0.004f,        // evaporation from an imperfect seal
    DecaysIntoId      = "",
    RequiredVesselTag = "vessel_watertight",
    IsPotable         = true,
    DisplayPrecision  = 2,
    HudColorToken     = "hud.resource.water",
}
```

---

## 2.4 `RecipeDefinition`

Crafting and item combination. Covers Act 1 cordage through the Act 3 Reel Deck rebuild.

```csharp
public sealed record RecipeDefinition : IDefinition
{
    public string Id                    { get; init; } = "";
    public int    ContentRevision       { get; init; }

    public string NameLocKey            { get; init; } = "";
    public string MethodLocKey          { get; init; } = "";   // "Plait three lengths against the grain."
    public string ResultIconAddress     { get; init; } = "";

    public ImmutableArray<IngredientSpec> Ingredients { get; init; } = ImmutableArray<IngredientSpec>.Empty;
    public ImmutableArray<ToolSpec>       ToolsNeeded { get; init; } = ImmutableArray<ToolSpec>.Empty;
    public ImmutableArray<OutputSpec>     Outputs     { get; init; } = ImmutableArray<OutputSpec>.Empty;

    public string RequiredStationTag    { get; init; } = "";   // "" = craft anywhere in inventory
    public string RequiredLocationId    { get; init; } = "";   // "" = any location
    public float  CraftSeconds          { get; init; }
    public float  InGameMinutesCost     { get; init; }         // advances TimeState

    // Discovery gating — no recipe list dump; you learn recipes.
    public RecipeUnlockMode UnlockMode  { get; init; }
    public ImmutableArray<string> UnlockedByDiscoveryIds { get; init; } = ImmutableArray<string>.Empty;
    public string GrantsDiscoveryOnFirstCraftId { get; init; } = "";

    public string CraftSfxAddress       { get; init; } = "";
    public int    SortOrder             { get; init; }
}

public enum RecipeUnlockMode { KnownFromStart, ByDiscovery, ByExperiment }

public readonly record struct IngredientSpec(
    string   RefId,              // item.* or res.*
    RefKind  Kind,
    float    Amount,             // count for items, units for resources
    bool     ConsumedEntirely,
    float    MinCondition);      // 0 = any condition

public readonly record struct ToolSpec(
    string RefId,
    float  ConditionCost,        // condition removed from the tool
    bool   ConsumedOnUse);

public readonly record struct OutputSpec(
    string  RefId,
    RefKind Kind,
    float   Amount,
    float   StartingConditionFraction);

public enum RefKind { Item, Resource }
```

**Worked example — the Catchment, the Act 1 rig that is secretly the thesis:**

```csharp
new RecipeDefinition {
    Id              = "recipe.catchment_rig",
    ContentRevision = 11,
    NameLocKey      = "ui.recipe.catchment_rig.name",
    MethodLocKey    = "ui.recipe.catchment_rig.method",
    ResultIconAddress = "ui/icons/item_catchment_rig",

    Ingredients = ImmutableArray.Create(
        new IngredientSpec("item.cordage_plaited",  RefKind.Item, 2f, true, 0f),
        new IngredientSpec("item.tarp_fragment",    RefKind.Item, 1f, true, 0.3f),
        new IngredientSpec("item.basalt_shard",     RefKind.Item, 3f, true, 0f)),

    ToolsNeeded = ImmutableArray.Create(
        new ToolSpec("item.multitool", 0.01f, false)),

    Outputs = ImmutableArray.Create(
        new OutputSpec("item.catchment_rig", RefKind.Item, 1f, 1f)),

    RequiredStationTag  = "",
    RequiredLocationId  = "",
    CraftSeconds        = 6.5f,
    InGameMinutesCost   = 40f,          // meaningful against the 36-hour water clock

    UnlockMode                = RecipeUnlockMode.ByDiscovery,
    UnlockedByDiscoveryIds    = ImmutableArray.Create("disc.fernmaw_leaf_dew"),
    GrantsDiscoveryOnFirstCraftId = "disc.condensation_principle",

    CraftSfxAddress = "sfx/craft/lash_cordage",
    SortOrder       = 120,
}
```

The `GrantsDiscoveryOnFirstCraftId` field is the load-bearing one. Building the Catchment teaches Nadia that this island condenses water — which is the same principle the Tolo Vardh cut into the mountain. Act 4's Register mechanic checks for `disc.condensation_principle`. The theme is wired through the data, not through a cutscene.

---

## 2.5 `DiscoveryDefinition`

The Field Slate's atoms. A Discovery is one revisable claim about the island. This type carries the theme: **entries can be superseded, and the supersession is data.**

```csharp
public sealed record DiscoveryDefinition : IDefinition
{
    public string Id                  { get; init; } = "";
    public int    ContentRevision     { get; init; }

    public string TitleLocKey         { get; init; } = "";
    public string ClaimLocKey         { get; init; } = "";   // Nadia's written entry
    public string CaveatLocKey        { get; init; } = "";   // her own hedge; may be ""
    public string ThumbnailAddress    { get; init; } = "";

    public DiscoveryCategory Category { get; init; }
    public string OwningActId         { get; init; } = "";   // "act1".."act5"
    public string PrimaryLocationId   { get; init; } = "";

    public DiscoveryTriggerKind TriggerKind { get; init; }
    public string TriggerRefId        { get; init; } = "";   // item/interaction/puzzle/location id

    // The revision graph — the theme, expressed as fields.
    public ImmutableArray<string> SupersedesDiscoveryIds { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiresDiscoveryIds   { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> CrossReferenceIds      { get; init; } = ImmutableArray<string>.Empty;

    public string AttributedSourceLocKey { get; init; } = "";  // "Poulter, 2 Nov 1887"
    public int    CanonicalYear          { get; init; }        // 0 = not dated
    public bool   IsActGate              { get; init; }        // required to advance
    public int    SlatePageSortOrder     { get; init; }
    public string LogSfxAddress          { get; init; } = "";
}

public enum DiscoveryCategory { Geology, Acoustics, TooloVardh, Cormorant, Ferrier, Wellhead, Mercator, Personal }
public enum DiscoveryTriggerKind { ItemPickup, ItemExamine, InteractionComplete, PuzzleSolved, LocationEnter, DocumentRead, ReelPlayed, Composite }
```

**Worked example — the Act 1 turn, the beat the whole game pivots on:**

```csharp
new DiscoveryDefinition {
    Id                = "disc.gully_cut_stone",
    ContentRevision   = 6,
    TitleLocKey       = "ui.disc.gully_cut_stone.title",
    ClaimLocKey       = "ui.disc.gully_cut_stone.claim",
    CaveatLocKey      = "ui.disc.gully_cut_stone.caveat",
    ThumbnailAddress  = "ui/slate/thumb_gully_cut_stone",

    Category          = DiscoveryCategory.TooloVardh,
    OwningActId       = "act1",
    PrimaryLocationId = "loc.fernmaw",

    TriggerKind       = DiscoveryTriggerKind.InteractionComplete,
    TriggerRefId      = "int.fernmaw_gully_moss_clear",

    SupersedesDiscoveryIds = ImmutableArray.Create("disc.gully_natural_drainage"),
    RequiresDiscoveryIds   = ImmutableArray.Create("disc.gully_natural_drainage"),
    CrossReferenceIds      = ImmutableArray.Create("disc.machete_cuts_fresh"),

    AttributedSourceLocKey = "",
    CanonicalYear          = 0,
    IsActGate              = true,
    SlatePageSortOrder     = 40,
    LogSfxAddress          = "sfx/slate/entry_revise",
}
```

Note the shape: Nadia first logs `disc.gully_natural_drainage` ("the gully that saved me is a natural drainage cut"). Clearing the moss produces `disc.gully_cut_stone`, which **supersedes** it — graded at a constant 1.5 degrees, therefore cut. The Slate UI renders the superseded entry struck through with the new one beneath. She revises. That is the game.

---

## 2.6 `InteractionDefinition`

Every point of contact with the world. The single biggest definition table (~450 of ~1,100 total).

```csharp
public sealed record InteractionDefinition : IDefinition
{
    public string Id                   { get; init; } = "";
    public int    ContentRevision      { get; init; }

    public string PromptLocKey         { get; init; } = "";   // "Clear the moss"
    public string BlockedLocKey        { get; init; } = "";   // shown when requirements fail
    public string CompletedLocKey      { get; init; } = "";

    public string LocationId           { get; init; } = "";
    public string AnchorKey            { get; init; } = "";   // scene-side named anchor
    public InteractionVerb Verb        { get; init; }
    public InteractionRepeat Repeat    { get; init; }

    // Requirements — ALL must pass.
    public ImmutableArray<string> RequiredItemIds       { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiredDiscoveryIds  { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiredFlagIds       { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> ForbiddenFlagIds      { get; init; } = ImmutableArray<string>.Empty;
    public string RequiredPuzzleSolvedId { get; init; } = "";
    public TimeWindow? RequiredTimeWindow { get; init; }      // e.g. tide-locked Sweatways access

    // Consequences.
    public ImmutableArray<OutputSpec> GrantsItems    { get; init; } = ImmutableArray<OutputSpec>.Empty;
    public ImmutableArray<string>     ConsumesItemIds{ get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string>     SetsFlagIds    { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string>     ClearsFlagIds  { get; init; } = ImmutableArray<string>.Empty;
    public string GrantsDiscoveryId   { get; init; } = "";
    public string TriggersStoryId     { get; init; } = "";
    public string OpensPuzzleId       { get; init; } = "";
    public string OpensDocumentId     { get; init; } = "";

    public float  DurationSeconds     { get; init; }
    public float  InGameMinutesCost   { get; init; }
    public ImmutableArray<SurvivalEffect> SurvivalCost { get; init; } = ImmutableArray<SurvivalEffect>.Empty;

    public string AnimationTriggerKey { get; init; } = "";
    public string SfxAddress          { get; init; } = "";
    public bool   IsOneShotAcrossSave { get; init; } = true;
}

public enum InteractionVerb { Examine, Take, Open, Turn, Pry, Clear, Listen, Repair, Climb, Pour, Read, Play, Speak }
public enum InteractionRepeat { Once, Unlimited, UntilFlagSet, PerInGameDay }
public readonly record struct TimeWindow(float StartHour24, float EndHour24, TideState RequiredTide);
public enum TideState { Any, Low, Rising, High, Falling }
```

**Worked example — the modern steel ladder bolted straight through four-thousand-year-old carvings in the Combs. One of the game's angriest images, and it is an interaction:**

```csharp
new InteractionDefinition {
    Id             = "int.combs_ladder_bolt_examine",
    ContentRevision= 2,
    PromptLocKey   = "ui.int.combs_ladder_bolt_examine.prompt",
    BlockedLocKey  = "",
    CompletedLocKey= "ui.int.combs_ladder_bolt_examine.done",

    LocationId     = "loc.combs",
    AnchorKey      = "combs_terrace_b/ladder_base",
    Verb           = InteractionVerb.Examine,
    Repeat         = InteractionRepeat.Once,

    RequiredItemIds      = ImmutableArray<string>.Empty,
    RequiredDiscoveryIds = ImmutableArray.Create("disc.combs_terrace_function"),
    RequiredFlagIds      = ImmutableArray<string>.Empty,
    ForbiddenFlagIds     = ImmutableArray<string>.Empty,

    GrantsDiscoveryId = "disc.ladder_through_register",
    SetsFlagIds       = ImmutableArray.Create("flag.combs_ladder_read"),
    DurationSeconds   = 3.2f,
    InGameMinutesCost = 4f,
    AnimationTriggerKey = "examine_crouch_low",
    SfxAddress        = "sfx/world/steel_rung_flex",
    IsOneShotAcrossSave = true,
}
```

---

## 2.7 `PuzzleDefinition`

Post-Act-2, all puzzles are acoustic. The definition holds the **shape and win condition**; the solution parameters live in a typed payload so the engine-free solver can validate a state without knowing which puzzle it is.

```csharp
public sealed record PuzzleDefinition : IDefinition
{
    public string Id                   { get; init; } = "";
    public int    ContentRevision      { get; init; }

    public string TitleLocKey          { get; init; } = "";
    public string ObjectiveLocKey      { get; init; } = "";
    public string SolvedLocKey         { get; init; } = "";

    public string LocationId           { get; init; } = "";
    public string OwningActId          { get; init; } = "";
    public PuzzleKind Kind             { get; init; }
    public int    DifficultyTier       { get; init; }    // 1..5, pacing analytics only

    // Entry gating.
    public ImmutableArray<string> RequiredItemIds      { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiredDiscoveryIds { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiredPuzzleIds    { get; init; } = ImmutableArray<string>.Empty;

    // Shape.
    public int   ElementCount          { get; init; }    // gates, baffles, dial positions
    public bool  IsOrderSensitive      { get; init; }
    public bool  AllowsPartialCredit   { get; init; }
    public bool  IsFailable            { get; init; }    // false everywhere in this game; see note
    public float SolveToleranceUnits   { get; init; }    // for continuous-value puzzles

    // The solution, as data. Solver is engine-free.
    public PuzzleSolutionSpec Solution { get; init; } = PuzzleSolutionSpec.None;

    // Escalating hints — surfaced by the Slate, not by a hint button.
    public ImmutableArray<HintStep> Hints { get; init; } = ImmutableArray<HintStep>.Empty;

    // Consequences.
    public string GrantsDiscoveryId    { get; init; } = "";
    public string TriggersStoryId      { get; init; } = "";
    public ImmutableArray<string> SetsFlagIds { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<OutputSpec> GrantsItems { get; init; } = ImmutableArray<OutputSpec>.Empty;

    public string AmbientLoopAddress   { get; init; } = "";
    public string SolveStingerAddress  { get; init; } = "";
}

public enum PuzzleKind
{
    ResonanceNodeAlign,   // aim the hydrophone, null the node
    BaffleSequence,       // order stone baffles by measured damping
    RegisterDecode,       // read notch-and-crescent notation
    DamperTuning,         // 4-gate continuous console vs a tide table
    CircuitRestore,       // 1960s electromechanical repair
    MechanicalLinkage
}

public readonly record struct HintStep(int UnlockAfterSeconds, string HintLocKey, string RequiresDiscoveryId);

public sealed record PuzzleSolutionSpec
{
    public static readonly PuzzleSolutionSpec None = new();
    public ImmutableArray<int>    TargetSequence    { get; init; } = ImmutableArray<int>.Empty;
    public ImmutableArray<float>  TargetValues      { get; init; } = ImmutableArray<float>.Empty;
    public ImmutableArray<float>  PerElementTolerance { get; init; } = ImmutableArray<float>.Empty;
    public string                 TideTableId       { get; init; } = "";  // DamperTuning: solution is time-varying
    public ImmutableArray<string> TargetGlyphIds    { get; init; } = ImmutableArray<string>.Empty;
}
```

> **Design note on `IsFailable`.** It is `false` for every shipped puzzle in v1. The field exists so the runtime doesn't need a special case if a future design wants it, and so the validator can assert `Solution != None` whenever `IsFailable` is true. Canon Rule 3 (no combat, no threat) and Rule 9 (the loud state is never a real-time hazard) mean nothing on Vardholm kills you for a wrong dial setting. A wrong tuning costs **time and water**, which is pressure enough.

**Worked example — the Act 4 damper console, the moment the player is doing Lorvik's job:**

```csharp
new PuzzleDefinition {
    Id             = "puz.oleander_damper_console",
    ContentRevision= 19,
    TitleLocKey    = "ui.puz.oleander_damper_console.title",
    ObjectiveLocKey= "ui.puz.oleander_damper_console.objective",
    SolvedLocKey   = "ui.puz.oleander_damper_console.solved",

    LocationId     = "loc.oleander",
    OwningActId    = "act4",
    Kind           = PuzzleKind.DamperTuning,
    DifficultyTier = 5,

    RequiredItemIds      = ImmutableArray.Create("item.hydrophone_directional", "item.field_slate"),
    RequiredDiscoveryIds = ImmutableArray.Create(
        "disc.register_flow_notation",      // she can read Tolo Vardh schematics
        "disc.sabo_gates_are_dampers",      // she knows what Oleander actually does
        "disc.tide_table_chalked"),         // Lorvik's chalked table on the wall
    RequiredPuzzleIds    = ImmutableArray.Create("puz.combs_register_cell4"),

    ElementCount        = 4,                // Gates A–D
    IsOrderSensitive    = false,
    AllowsPartialCredit = true,             // per-gate green lights
    IsFailable          = false,
    SolveToleranceUnits = 0.04f,

    Solution = new PuzzleSolutionSpec {
        // Ballast fraction per gate at the reference tide phase.
        TargetValues        = ImmutableArray.Create(0.62f, 0.48f, 0.71f, 0.55f),
        PerElementTolerance = ImmutableArray.Create(0.04f, 0.04f, 0.03f, 0.05f),
        TideTableId         = "tide.vardholm_spring",   // targets shift with tide phase
    },

    Hints = ImmutableArray.Create(
        new HintStep(180, "ui.puz.oleander_damper_console.hint1", ""),
        new HintStep(420, "ui.puz.oleander_damper_console.hint2", "disc.tide_table_chalked"),
        new HintStep(900, "ui.puz.oleander_damper_console.hint3", "disc.sabo_log_gate_c_sticks")),

    GrantsDiscoveryId = "disc.forty_hours_a_month",
    TriggersStoryId   = "story.act4_console_held",
    SetsFlagIds       = ImmutableArray.Create("flag.damper_balanced_once"),
    AmbientLoopAddress  = "audio/amb/oleander_hum_loop",
    SolveStingerAddress = "audio/stinger/gates_settle",
}
```

The `TideTableId` is why the solver must live in `Isle.Core`: the target is `f(tidePhase)`, and proving that a solution window always exists, for every tide phase, across a full spring-neap cycle, is a property test. Run it on every commit in 2 seconds, not in a Unity batchmode job.

---

## 2.8 `LocationDefinition`

Zone and sub-zone. The scene graph's data twin, and the addressables loading contract.

```csharp
public sealed record LocationDefinition : IDefinition
{
    public string Id                  { get; init; } = "";
    public int    ContentRevision     { get; init; }

    public string NameLocKey          { get; init; } = "";
    public string SubtitleLocKey      { get; init; } = "";   // the zone card on entry
    public string SlateMapLabelLocKey { get; init; } = "";
    public string MapIconAddress      { get; init; } = "";

    public string ParentLocationId    { get; init; } = "";   // "" = top-level zone
    public string SceneAddress        { get; init; } = "";
    public ImmutableArray<string> AssetGroupAddresses { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> PreloadNeighbourIds { get; init; } = ImmutableArray<string>.Empty;

    public ImmutableArray<ZoneExit> Exits { get; init; } = ImmutableArray<ZoneExit>.Empty;

    // Environment — drives SurvivalState per tick.
    public float AmbientTempC         { get; init; }
    public float RelativeHumidity     { get; init; }         // 0..1
    public float WarmthDrainPerHour   { get; init; }
    public float HydrationDrainMultiplier { get; init; }
    public bool  IsIndoor             { get; init; }
    public bool  IsAboveCloudLid      { get; init; }
    public bool  BlocksTransmission   { get; init; }

    // Acoustics — the signature verb's parameters.
    public float BaseReverbSeconds    { get; init; }
    public float InfrasoundFloorDb    { get; init; }
    public ImmutableArray<ResonanceNode> ResonanceNodes { get; init; } = ImmutableArray<ResonanceNode>.Empty;

    public string AmbientBedAddress   { get; init; } = "";
    public string MusicStateKey       { get; init; } = "";
    public string WeatherProfileId    { get; init; } = "";
    public string FirstEntryStoryId   { get; init; } = "";
    public ImmutableArray<string> RequiredDiscoveryIdsToEnter { get; init; } = ImmutableArray<string>.Empty;
    public int    ActFirstAvailable   { get; init; }
}

public readonly record struct ZoneExit(string ToLocationId, string AnchorKey, string RequiredItemId, string LockedLocKey);
public readonly record struct ResonanceNode(float Hz, float AmplitudeDb, string BearingAnchorKey, string RevealsDiscoveryId);
```

**Worked example — the Sweatways, whose eleven-minute pressure cycle is both an atmosphere and a puzzle clock:**

```csharp
new LocationDefinition {
    Id                = "loc.sweatways",
    ContentRevision   = 14,
    NameLocKey        = "ui.loc.sweatways.name",
    SubtitleLocKey    = "ui.loc.sweatways.subtitle",
    SlateMapLabelLocKey = "ui.loc.sweatways.maplabel",
    MapIconAddress    = "ui/map/icon_sweatways",

    ParentLocationId  = "",
    SceneAddress      = "scenes/zone_sweatways",
    AssetGroupAddresses = ImmutableArray.Create("group/sweatways_geo", "group/sweatways_props", "group/sweatways_audio"),
    PreloadNeighbourIds = ImmutableArray.Create("loc.fernmaw", "loc.ash_throat"),

    Exits = ImmutableArray.Create(
        new ZoneExit("loc.fernmaw",    "sweatways_mouth_upper", "",                    ""),
        new ZoneExit("loc.ash_throat", "sweatways_vent_crawl",  "item.lamp_carbide",   "ui.loc.sweatways.exit_dark_locked"),
        new ZoneExit("loc.oleander",   "sweatways_seam_door",   "item.oleander_key_brass", "ui.loc.sweatways.exit_seam_locked")),

    AmbientTempC            = 31.5f,
    RelativeHumidity        = 0.97f,
    WarmthDrainPerHour      = -0.02f,     // negative: it warms you
    HydrationDrainMultiplier= 1.65f,      // warm, wet, you sweat
    IsIndoor                = true,
    IsAboveCloudLid         = false,
    BlocksTransmission      = true,

    BaseReverbSeconds  = 4.8f,
    InfrasoundFloorDb  = -34f,
    ResonanceNodes = ImmutableArray.Create(
        new ResonanceNode(11.2f, -21f, "sweatways_baffle_3/node", "disc.eleven_minute_cycle"),
        new ResonanceNode(23.7f, -28f, "sweatways_dripcomb_a/node", "disc.dripcomb_ceramic_function")),

    AmbientBedAddress = "audio/amb/sweatways_bed",
    MusicStateKey     = "music.state.sweatways",
    WeatherProfileId  = "",               // no weather indoors
    FirstEntryStoryId = "story.act2_sweatways_entry",
    RequiredDiscoveryIdsToEnter = ImmutableArray.Create("disc.fernmaw_trench_grate_open"),
    ActFirstAvailable = 2,
}
```

---

## 2.9 `StoryDefinition`

A narrative beat: a monologue line, a document reveal, a scripted camera moment. Never a cutscene the player can't skip — but the save system must know when one is playing (§4.4).

```csharp
public sealed record StoryDefinition : IDefinition
{
    public string Id                 { get; init; } = "";
    public int    ContentRevision    { get; init; }

    public string TitleLocKey        { get; init; } = "";   // debug/chapter select only
    public StoryBeatKind Kind        { get; init; }
    public string OwningActId        { get; init; } = "";
    public int    ActSortOrder       { get; init; }

    public ImmutableArray<StoryLine> Lines { get; init; } = ImmutableArray<StoryLine>.Empty;

    public ImmutableArray<string> RequiredDiscoveryIds { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiredFlagIds      { get; init; } = ImmutableArray<string>.Empty;
    public string RequiredLocationId { get; init; } = "";

    public bool   BlocksInput        { get; init; }
    public bool   BlocksAutosave     { get; init; }   // read directly by the autosave gate
    public bool   IsSkippable        { get; init; } = true;
    public bool   IsActTransition    { get; init; }
    public string AdvancesToActId    { get; init; } = "";

    public string TimelineAddress    { get; init; } = "";   // "" = no scripted timeline
    public string CameraRigKey       { get; init; } = "";
    public string MusicStateKey      { get; init; } = "";
    public ImmutableArray<string> SetsFlagIds      { get; init; } = ImmutableArray<string>.Empty;
    public string GrantsDiscoveryId  { get; init; } = "";

    public bool   StripsCraftingHud  { get; init; }   // Act 5. Protect this in design review.
}

public enum StoryBeatKind { InternalMonologue, DocumentReveal, ReelPlayback, ScriptedMoment, Dialogue, ActTransition }

public readonly record struct StoryLine(
    string SpeakerId,        // "nadia", "lorvik", "reel.sabo", "" = no speaker
    string TextLocKey,
    string VoAddress,
    float  HoldSeconds,
    string SubtitleStyleKey);
```

**Worked example — Act 5's opening, where the HUD dies:**

```csharp
new StoryDefinition {
    Id            = "story.act5_quiet_room_threshold",
    ContentRevision = 23,
    TitleLocKey   = "ui.story.act5_quiet_room_threshold.title",
    Kind          = StoryBeatKind.ActTransition,
    OwningActId   = "act5",
    ActSortOrder  = 10,

    Lines = ImmutableArray.Create(
        new StoryLine("nadia", "vo.nadia.act5.001", "vo/en/nadia_act5_001", 0f, "sub.internal"),
        new StoryLine("",      "vo.silence.hold",   "",                     4.5f, ""),
        new StoryLine("nadia", "vo.nadia.act5.002", "vo/en/nadia_act5_002", 0f, "sub.internal")),

    RequiredDiscoveryIds = ImmutableArray.Create("disc.forty_hours_a_month", "disc.mercator_letter_unopened"),
    RequiredFlagIds      = ImmutableArray.Create("flag.damper_balanced_once"),
    RequiredLocationId   = "loc.quiet_room",

    BlocksInput     = false,      // she walks; she is not held
    BlocksAutosave  = true,
    IsSkippable     = true,
    IsActTransition = true,
    AdvancesToActId = "act5",

    TimelineAddress = "timelines/act5_threshold",
    CameraRigKey    = "rig.quiet_room_entry",
    MusicStateKey   = "music.state.quiet_room",
    SetsFlagIds     = ImmutableArray.Create("flag.act5_entered", "flag.hud_crafting_stripped"),
    GrantsDiscoveryId = "",

    StripsCraftingHud = true,
}
```

---

## 2.10 `QuestDefinition`

Objectives. Deliberately thin — this game does not have a quest log, it has a Field Slate. Quests exist to drive the single active-objective line and act gating, nothing more.

```csharp
public sealed record QuestDefinition : IDefinition
{
    public string Id                 { get; init; } = "";
    public int    ContentRevision    { get; init; }

    public string TitleLocKey        { get; init; } = "";
    public string SummaryLocKey      { get; init; } = "";
    public string OwningActId        { get; init; } = "";
    public bool   IsMainline         { get; init; }
    public int    SortOrder          { get; init; }

    public ImmutableArray<QuestStep> Steps { get; init; } = ImmutableArray<QuestStep>.Empty;
    public bool   StepsAreOrdered    { get; init; }

    public ImmutableArray<string> StartedByFlagIds     { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> StartedByDiscoveryIds{ get; init; } = ImmutableArray<string>.Empty;
    public string StartedByLocationId { get; init; } = "";

    public string CompletionStoryId  { get; init; } = "";
    public ImmutableArray<string> SetsFlagsOnComplete { get; init; } = ImmutableArray<string>.Empty;

    // Soft-timed objectives — expiry never kills, it changes tone.
    public float  SoftDeadlineInGameHours { get; init; }   // 0 = untimed
    public string DeadlinePassedStoryId   { get; init; } = "";
}

public readonly record struct QuestStep(
    string           StepId,             // unique within the quest
    string           DescriptionLocKey,
    QuestConditionKind ConditionKind,
    string           ConditionRefId,
    float            ConditionAmount,
    bool             IsOptional,
    string           MapPingLocationId);

public enum QuestConditionKind
{
    HasItem, HasResourceAmount, DiscoveryLogged, PuzzleSolved,
    InteractionComplete, LocationVisited, FlagSet, CampUpgradeBuilt
}
```

**Worked example — Act 1's water clock. Note that the deadline is *soft*: Rule 3 says no combat and no threat, and "you died of thirst on hour 37" is a fail-state this game does not have. Missing it costs a worse version of Act 2, not a game over:**

```csharp
new QuestDefinition {
    Id            = "quest.act1_water_36h",
    ContentRevision = 9,
    TitleLocKey   = "ui.quest.act1_water_36h.title",
    SummaryLocKey = "ui.quest.act1_water_36h.summary",
    OwningActId   = "act1",
    IsMainline    = true,
    SortOrder     = 10,

    StepsAreOrdered = false,
    Steps = ImmutableArray.Create(
        new QuestStep("dry_recorder", "ui.quest.act1_water_36h.step.dry_recorder",
                      QuestConditionKind.FlagSet, "flag.recorder_dried", 0f, false, "loc.ribcage"),
        new QuestStep("build_catchment", "ui.quest.act1_water_36h.step.catchment",
                      QuestConditionKind.HasItem, "item.catchment_rig", 1f, false, "loc.fernmaw"),
        new QuestStep("drink_litre", "ui.quest.act1_water_36h.step.drink",
                      QuestConditionKind.HasResourceAmount, "res.fresh_water", 1.0f, false, "loc.fernmaw"),
        new QuestStep("find_shelter", "ui.quest.act1_water_36h.step.shelter",
                      QuestConditionKind.CampUpgradeBuilt, "camp.lean_to", 1f, true, "loc.ribcage")),

    StartedByFlagIds = ImmutableArray.Create("flag.prologue_wreck_complete"),
    CompletionStoryId = "story.act1_first_water",
    SetsFlagsOnComplete = ImmutableArray.Create("flag.act1_water_secured"),

    SoftDeadlineInGameHours = 36f,
    DeadlinePassedStoryId   = "story.act1_water_late",   // different Nadia. Same island.
}
```

---

## 2.11 `CampUpgradeDefinition`

Camp is a small, deliberate progression — this is not a base-builder. Roughly 14 upgrades across the game.

```csharp
public sealed record CampUpgradeDefinition : IDefinition
{
    public string Id                 { get; init; } = "";
    public int    ContentRevision    { get; init; }

    public string NameLocKey         { get; init; } = "";
    public string DescLocKey         { get; init; } = "";
    public string IconAddress        { get; init; } = "";
    public string BuiltPrefabAddress { get; init; } = "";
    public string GhostPrefabAddress { get; init; } = "";

    public string CampSiteLocationId { get; init; } = "";
    public string AnchorKey          { get; init; } = "";
    public int    MaxLevel           { get; init; } = 1;

    public ImmutableArray<UpgradeLevelSpec> Levels { get; init; } = ImmutableArray<UpgradeLevelSpec>.Empty;

    public ImmutableArray<string> RequiresUpgradeIds   { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> RequiresDiscoveryIds { get; init; } = ImmutableArray<string>.Empty;
    public string UnlocksStationTag  { get; init; } = "";   // satisfies RecipeDefinition.RequiredStationTag
    public string GrantsDiscoveryId  { get; init; } = "";
    public int    ActFirstAvailable  { get; init; }
    public string BuildSfxAddress    { get; init; } = "";
}

public readonly record struct UpgradeLevelSpec(
    int    Level,
    ImmutableArray<IngredientSpec> Cost,
    float  BuildInGameHours,
    ImmutableArray<CampEffect> Effects,
    string LevelDescLocKey);

public readonly record struct CampEffect(CampEffectKind Kind, float Value, string RefId);
public enum CampEffectKind
{
    PassiveResourcePerHour, DryingRatePerHour, InventoryVolumeBonus,
    RestFatigueRecoveryRate, ReducesCorrosionRate, UnlocksRecipe, SetsRespawnAnchor
}
```

**Worked example — the drying rack, which exists because salt eats everything and Sabo's reels are irreplaceable:**

```csharp
new CampUpgradeDefinition {
    Id            = "camp.drying_rack",
    ContentRevision = 5,
    NameLocKey    = "ui.camp.drying_rack.name",
    DescLocKey    = "ui.camp.drying_rack.desc",
    IconAddress   = "ui/icons/camp_drying_rack",
    BuiltPrefabAddress = "props/camp/drying_rack_built",
    GhostPrefabAddress = "props/camp/drying_rack_ghost",

    CampSiteLocationId = "loc.ribcage",
    AnchorKey     = "ribcage_camp/rack_slot_a",
    MaxLevel      = 2,

    Levels = ImmutableArray.Create(
        new UpgradeLevelSpec(1,
            ImmutableArray.Create(
                new IngredientSpec("item.cordage_plaited", RefKind.Item, 3f, true, 0f),
                new IngredientSpec("item.driftwood_spar",  RefKind.Item, 4f, true, 0f)),
            2.0f,
            ImmutableArray.Create(
                new CampEffect(CampEffectKind.DryingRatePerHour, 0.35f, ""),
                new CampEffect(CampEffectKind.ReducesCorrosionRate, 0.5f, "")),
            "ui.camp.drying_rack.lvl1"),

        new UpgradeLevelSpec(2,
            ImmutableArray.Create(
                new IngredientSpec("item.tarp_fragment", RefKind.Item, 2f, true, 0.4f),
                new IngredientSpec("item.brass_wire",    RefKind.Item, 1f, true, 0f)),
            3.5f,
            ImmutableArray.Create(
                new CampEffect(CampEffectKind.DryingRatePerHour, 0.70f, ""),
                new CampEffect(CampEffectKind.ReducesCorrosionRate, 0.8f, ""),
                new CampEffect(CampEffectKind.UnlocksRecipe, 0f, "recipe.reel_rebake")),
            "ui.camp.drying_rack.lvl2")),

    RequiresUpgradeIds   = ImmutableArray.Create("camp.lean_to"),
    RequiresDiscoveryIds = ImmutableArray.Create("disc.salt_corrosion_rate"),
    UnlocksStationTag    = "station_drying",
    ActFirstAvailable    = 1,
    BuildSfxAddress      = "sfx/craft/lash_frame",
}
```

---

## 2.12 Validation rules

Every rule below is implemented in `Isle.Authoring.Validation`, runs on every bake, and **blocks the build** at severity `Error`.

### Identity

| ID | Severity | Rule |
|---|---|---|
| `ID-001` | Error | ID matches `^[a-z][a-z0-9_]*(\.[a-z0-9_]+){1,2}$`, 3–64 chars. |
| `ID-002` | Error | ID prefix matches the definition type (`ItemDefinition` ⇒ `item.`). |
| `ID-003` | Error | No duplicate ID within a type. |
| `ID-004` | Error | No duplicate ID **across** types (IDs are globally unique; makes save data self-describing). |
| `ID-005` | Error | No ID present in the ship bake that exists in `retired_ids.json` — a retired ID may never be reused, because a v3 save may still reference it (§4.3). |
| `ID-006` | Warning | Asset filename stem equals the ID. Cosmetic, but makes the project navigable. |

### References

| ID | Severity | Rule |
|---|---|---|
| `REF-001` | Error | Every non-empty ID-typed field resolves to an existing definition. **The dangling-reference gate.** |
| `REF-002` | Error | Resolved target is the expected type (`RequiredItemIds` may not hold a `res.` ID). |
| `REF-003` | Error | `RefKind` on an `IngredientSpec`/`OutputSpec` matches the prefix of `RefId`. |
| `REF-004` | Error | No self-reference (`SupersedesDiscoveryIds` may not contain own ID). |
| `REF-005` | Error | No cycles in `RequiresDiscoveryIds`, `RequiresPuzzleIds`, `RequiresUpgradeIds`. Reported as the full cycle path. |
| `REF-006` | Error | `ZoneExit.ToLocationId` resolves, and the target has a reciprocal exit or is explicitly tagged `one_way`. |

### Localization

| ID | Severity | Rule |
|---|---|---|
| `LOC-001` | Error | Every `*LocKey` field is non-empty unless explicitly nullable-by-design (the validator holds the allowlist). |
| `LOC-002` | Error | Every loc key exists in the source-language (en) string table. |
| `LOC-003` | Error | No field ending `LocKey` contains a space or a capital letter — catches "Clear the moss" pasted into a key field. **This is the literal-English gate and it fires more often than you'd think.** |
| `LOC-004` | Warning | Every loc key referenced by exactly one definition (duplicates suggest copy-paste error). |
| `LOC-005` | Warning | `StoryLine.VoAddress` non-empty for every line with a non-empty `TextLocKey` in an `ActTransition` beat. |

### Reachability — the expensive, valuable ones

| ID | Severity | Rule |
|---|---|---|
| `RCH-001` | Error | **Unreachable recipe.** Forward-closure from the Act 1 loadout + every `GrantsItems` on every reachable interaction + every recipe output. If a recipe's ingredient set is never satisfiable, fail with the missing ingredient named. |
| `RCH-002` | Error | **Orphan discovery.** Every `DiscoveryDefinition` is the target of at least one `GrantsDiscoveryId` / `GrantsDiscoveryOnFirstCraftId` / `RevealsDiscoveryId` / `ResonanceNode`, or is explicitly tagged `granted_by_script` (allowlisted, with the script path). |
| `RCH-003` | Error | Every `IsActGate == true` discovery is reachable in or before its `OwningActId`. |
| `RCH-004` | Error | Every location is reachable via `ZoneExit` traversal from `loc.ribcage`, respecting `RequiredItemId` availability at that point in the act order. |
| `RCH-005` | Error | Every puzzle's `RequiredDiscoveryIds` are all reachable before the puzzle's location becomes available. **Catches the classic "you need a discovery you can only get past this door".** |
| `RCH-006` | Warning | Every item is obtainable (pickup, recipe output, or interaction grant). Warning not error, because debug items legitimately fail this. |
| `RCH-007` | Error | Every quest's steps are all completable given its `OwningActId`'s available locations. |

### Semantics

| ID | Severity | Rule |
|---|---|---|
| `SEM-001` | Error | `MaxStack >= 1`; `MassKg >= 0`; `VolumeLitres >= 0`. |
| `SEM-002` | Error | `HasCondition == false` ⇒ all corrosion fields are 0 and `DegradedVariantId` is empty. |
| `SEM-003` | Error | `ConsumesPower == true` ⇒ `PowerCellItemId` non-empty and resolves to an item tagged `power_cell`. |
| `SEM-004` | Error | `PuzzleSolutionSpec.PerElementTolerance.Length` equals `TargetValues.Length` when both non-empty. |
| `SEM-005` | Error | `ElementCount` matches `TargetValues.Length` or `TargetSequence.Length` for value/sequence puzzle kinds. |
| `SEM-006` | Error | `Kind == DamperTuning` ⇒ `TideTableId` non-empty and resolves. |
| `SEM-007` | Error | `CampUpgradeDefinition.Levels` are contiguous from 1 to `MaxLevel`, no gaps, no duplicates. |
| `SEM-008` | Error | `IsQuestCritical == true` ⇒ item is not consumed by any recipe with `ConsumedEntirely == true`. **Prevents the softlock where you craft away the Field Slate.** |
| `SEM-009` | Error | `StripsCraftingHud == true` only on definitions with `OwningActId == "act5"`. Canon protection, enforced by the build. |
| `SEM-010` | Error | No `_test.` prefixed definition present when `EditorUserBuildSettings.development == false`. |
| `SEM-011` | Warning | `HintStep.UnlockAfterSeconds` strictly increasing within a puzzle. |
| `SEM-012` | Error | `TimeWindow.StartHour24`/`EndHour24` in `[0,24)`. |

### Canon guards (project-specific, and worth having)

| ID | Severity | Rule |
|---|---|---|
| `CAN-001` | Error | No definition of any type carries a tag in the banned set `{"combat","weapon","enemy","creature","hostile","damage_player"}`. Canon Rule 3, mechanised. |
| `CAN-002` | Error | `IsFailable == true` on any puzzle requires an explicit allowlist entry signed off by the design lead. Currently the allowlist is empty. |
| `CAN-003` | Warning | Any `LocationDefinition` with `ResonanceNodes` empty and `ActFirstAvailable >= 2` — probably a missed acoustic pass. |

---

# PART 3 — RUNTIME STATE

## 3.0 The separation, stated once

| | Definition | State |
|---|---|---|
| Lifetime | Whole process, loaded once | Per save slot |
| Mutability | `record` with `init` only | Mutable `class`, or `struct` with `with`-expressions |
| Identity | `string Id` | `string DefId` + an instance identity where needed |
| Source | Baked catalog | Save file / new-game seed |
| Size | ~1,100 objects, <1 MB | ~4,000 entries, ~200–600 KB |
| Serialized to save? | **Never** | **Always** |

**The invariant, in one sentence: state stores the ID, never the definition.** A `ItemStack` holds `"item.hydrophone_directional"` and a condition float. It does not hold an `ItemDefinition`. This is what makes a save file survive a content patch — and what makes `ItemStack` a 24-byte struct instead of a reference graph.

Resolution is a repository call at the point of use:

```csharp
public static float MassOf(in ItemStack stack, IDefinitionRepository repo)
    => repo.Get<ItemDefinition>(stack.DefId).MassKg * stack.Count;
```

## 3.1 `ItemStack` / `ItemState`

```csharp
namespace Isle.Core.State;

/// Countable, identical, condition-free items. 24 bytes. No allocation.
public readonly record struct ItemStack(string DefId, int Count)
{
    public bool IsEmpty => Count <= 0 || string.IsNullOrEmpty(DefId);
    public ItemStack Add(int n)    => this with { Count = Count + n };
    public ItemStack Remove(int n) => this with { Count = Math.Max(0, Count - n) };
}

/// An individual item that carries per-instance state — condition, charge, or a
/// player-authored note. Only allocated for items where MaxStack == 1 and
/// (HasCondition || ConsumesPower || is a Document/Reel).
public sealed class ItemState
{
    public string InstanceId   { get; init; } = "";   // "inst_" + 8 hex; stable across saves
    public string DefId        { get; init; } = "";
    public float  Condition    { get; set; }          // 0..MaxCondition, from the definition
    public float  WetnessPct   { get; set; }          // 0..1, drives the wet corrosion rate
    public float  ChargeFrac   { get; set; }          // 0..1 of one cell
    public bool   IsEquipped   { get; set; }
    public int    UseCount     { get; set; }
    public double AcquiredAtGameSeconds { get; set; }
    public string ContainerId  { get; set; } = "";    // "inv", "camp.dry_box", "loc.ribcage/cache_a"
}
```

Two types rather than one because most of the inventory is `item.hemp_fibre ×14`, and allocating fourteen `ItemState` objects with a GUID each to represent fourteen identical fibres is how you get GC spikes on a phone. The rule is mechanical: `MaxStack > 1` ⇒ `ItemStack`; `MaxStack == 1 && (HasCondition || ConsumesPower || Category is Document/Reel)` ⇒ `ItemState`. The baker asserts each item falls in exactly one bucket.

## 3.2 `InventoryState`

```csharp
public sealed class InventoryState
{
    public List<ItemStack> Stacks    { get; init; } = new();
    public List<ItemState> Instances { get; init; } = new();
    public Dictionary<string, float> Resources { get; init; } = new();  // res.* → amount

    public string EquippedToolInstanceId { get; set; } = "";
    public float  CarriedVolumeLitres    { get; set; }   // cached, recomputed on mutation
    public float  CarriedMassKg          { get; set; }

    // Vessels are instances with a resource payload; tracked separately so a half-full
    // canteen survives being put down in the Sweatways and picked up in Act 4.
    public Dictionary<string, VesselContents> Vessels { get; init; } = new(); // instanceId → contents
}

public readonly record struct VesselContents(string ResourceId, float Amount, float SpoilageProgress);
```

## 3.3 `SurvivalState`

```csharp
public sealed class SurvivalState
{
    public float Hydration     { get; set; }   // 0..1
    public float Warmth        { get; set; }   // 0..1
    public float Fatigue       { get; set; }   // 0..1, 1 = exhausted
    public float Morale        { get; set; }   // 0..1, affects Slate prose variant only

    public float RecorderChargeFrac { get; set; }   // Act 1 opens at 0.09 — canon
    public float HydrophoneCondition{ get; set; }

    public double LastTickGameSeconds { get; set; }
    public SurvivalTier CurrentTier   { get; set; }

    public bool  IsSheltered   { get; set; }
    public bool  IsWet         { get; set; }
    public float ExposureHours { get; set; }   // continuous hours unsheltered above cloud lid

    // Soft-fail bookkeeping. Canon Rule 3: no death. Critical changes tone and
    // slows movement; it never ends the run.
    public int    CriticalEventCount     { get; set; }
    public double LastCriticalGameSeconds{ get; set; }
}

public enum SurvivalTier { Comfortable, Strained, Critical }
```

The tick lives in `Isle.Core`:

```csharp
public static class SurvivalTick
{
    public static SurvivalState Advance(
        SurvivalState s, LocationDefinition loc, WeatherState weather,
        float deltaGameHours, IDefinitionRepository repo) { /* pure, testable, no Unity */ }
}
```

Pure function, no Unity, no singletons, fully covered by a table of 60 scenario cases in `dotnet test`. This is the payoff of Part 1's recommendation, made concrete.

## 3.4 `PlayerState`

```csharp
public sealed class PlayerState
{
    public string CurrentLocationId  { get; set; } = "loc.ribcage";
    public Vec3   Position           { get; set; }      // Isle.Core.Vec3, NOT UnityEngine.Vector3
    public float  YawDegrees         { get; set; }
    public string LastSafeAnchorKey  { get; set; } = "";

    public string CurrentActId       { get; set; } = "act1";
    public string ActiveQuestId      { get; set; } = "";
    public string ActiveQuestStepId  { get; set; } = "";

    public bool   HasHydrophoneMode  { get; set; }
    public bool   HasSlateOpen       { get; set; }
    public bool   HasReelDeck        { get; set; }
    public bool   CraftingHudStripped{ get; set; }      // set true and never unset in Act 5

    public double TotalPlaySeconds   { get; set; }
    public int    SlateEntriesRevised{ get; set; }      // thematic stat, shown at the end
}

public readonly record struct Vec3(float X, float Y, float Z);
```

`Vec3` exists so `Isle.Core` never touches `UnityEngine`. One conversion helper in `Isle.Unity`, and the discipline holds.

## 3.5 `WorldState`

```csharp
public sealed class WorldState
{
    public HashSet<string> Flags                { get; init; } = new();
    public HashSet<string> VisitedLocationIds   { get; init; } = new();
    public HashSet<string> CompletedInteractionIds { get; init; } = new();
    public Dictionary<string, int> InteractionCounts { get; init; } = new();  // for Unlimited/PerInGameDay
    public Dictionary<string, double> InteractionLastGameSeconds { get; init; } = new();

    // Items left in the world — dropped, cached, or removed from a container.
    public Dictionary<string, WorldItemPlacement> PlacedItems { get; init; } = new();

    // Doors, valves, levers, gates that hold a position between sessions.
    public Dictionary<string, float> MechanismPositions { get; init; } = new();

    // Lorvik's maintenance tags the player has read. Act 3's navigation system.
    public HashSet<string> ReadBrassTagIds { get; init; } = new();
    public string NewestTagIdSeen          { get; set; } = "";
}

public readonly record struct WorldItemPlacement(
    string LocationId, string AnchorKeyOrEmpty, Vec3 Position, float Yaw, string InstanceIdOrEmpty, int Count);
```

## 3.6 `PuzzleState`

```csharp
public sealed class PuzzleState
{
    public string PuzzleDefId       { get; init; } = "";
    public PuzzleStatus Status      { get; set; }
    public int    AttemptCount      { get; set; }
    public double FirstOpenedGameSeconds { get; set; }
    public double SolvedGameSeconds      { get; set; }
    public float  TimeOpenSeconds   { get; set; }     // drives HintStep unlocking
    public int    HintsUnlocked     { get; set; }

    // Live element positions. Length == PuzzleDefinition.ElementCount.
    public float[] ElementValues    { get; set; } = Array.Empty<float>();
    public int[]   ElementSequence  { get; set; } = Array.Empty<int>();
    public bool[]  ElementSatisfied { get; set; } = Array.Empty<bool>();  // partial-credit lights
}

public enum PuzzleStatus { Unseen, Available, InProgress, Solved }
```

Damper console mid-session, one gate still out of tolerance:

```csharp
new PuzzleState {
    PuzzleDefId      = "puz.oleander_damper_console",
    Status           = PuzzleStatus.InProgress,
    AttemptCount     = 3,
    TimeOpenSeconds  = 512f,
    HintsUnlocked    = 2,
    ElementValues    = new[] { 0.61f, 0.49f, 0.66f, 0.55f },   // Gate C is 0.05 low
    ElementSatisfied = new[] { true,  true,  false, true  },
}
```

## 3.7 `DiscoveryState`

```csharp
public sealed class DiscoveryState
{
    public Dictionary<string, DiscoveryEntry> Entries { get; init; } = new();
    public List<string> ChronologicalOrder { get; init; } = new();   // Slate page order
    public string LastLoggedId  { get; set; } = "";
    public int    UnreadCount   { get; set; }
}

public sealed class DiscoveryEntry
{
    public string DefId            { get; init; } = "";
    public double LoggedGameSeconds{ get; set; }
    public bool   IsRead           { get; set; }
    public bool   IsSuperseded     { get; set; }   // struck through in the Slate
    public string SupersededByDefId{ get; set; } = "";
    public bool   PlayerPinned     { get; set; }
}
```

`IsSuperseded` is state, not definition, because whether Nadia has yet revised a belief is a property of *this playthrough*. The definition says what *can* supersede what; the state says what *has*. That split is the whole Field Slate.

## 3.8 `StoryState`

```csharp
public sealed class StoryState
{
    public HashSet<string> PlayedStoryIds     { get; init; } = new();
    public HashSet<string> SkippedStoryIds    { get; init; } = new();
    public Dictionary<string, int> ReelPlayCounts { get; init; } = new();

    public string CurrentActId        { get; set; } = "act1";
    public int    CurrentChapterIndex { get; set; }   // save metadata + chapter select

    public string ActiveStoryId       { get; set; } = "";   // non-empty ⇒ a beat is playing
    public int    ActiveLineIndex     { get; set; }
    public bool   IsBlockingAutosave  { get; set; }         // mirrors the active def's flag

    // Ending selection. Empty until the player commits in the Quiet Room.
    public string ChosenEndingId      { get; set; } = "";   // "ending.record" | "ending.relief"
}
```

## 3.9 `QuestState`

```csharp
public sealed class QuestState
{
    public Dictionary<string, QuestProgress> Quests { get; init; } = new();
    public string ActiveQuestId { get; set; } = "";
}

public sealed class QuestProgress
{
    public string DefId                 { get; init; } = "";
    public QuestStatus Status           { get; set; }
    public HashSet<string> CompletedStepIds { get; init; } = new();
    public double StartedGameSeconds    { get; set; }
    public double CompletedGameSeconds  { get; set; }
    public bool   SoftDeadlineMissed    { get; set; }
}

public enum QuestStatus { Unstarted, Active, Complete, Lapsed }
```

## 3.10 `CampState`

```csharp
public sealed class CampState
{
    public Dictionary<string, CampUpgradeProgress> Upgrades { get; init; } = new();
    public HashSet<string> ActiveStationTags { get; init; } = new();   // derived, cached
    public string RespawnAnchorKey  { get; set; } = "";
    public double LastRestedGameSeconds { get; set; }

    // Passive production accrued since the last visit — computed on arrival,
    // not ticked every frame while the player is three zones away.
    public Dictionary<string, float> PendingResourceAccrual { get; init; } = new();
    public double AccrualLastResolvedGameSeconds { get; set; }
}

public sealed class CampUpgradeProgress
{
    public string DefId        { get; init; } = "";
    public int    Level        { get; set; }
    public double BuiltGameSeconds { get; set; }
    public float  BuildProgress01  { get; set; }   // in-progress builds survive a save
}
```

## 3.11 `TimeState`

```csharp
public sealed class TimeState
{
    public double GameSecondsElapsed { get; set; }   // authoritative clock, since t=0
    public int    DayNumber          { get; set; }   // derived; cached for the HUD
    public float  HourOfDay24        { get; set; }   // derived

    public float  TimeScale          { get; set; } = 60f;   // 1 real sec = 60 game sec

    // Tide — drives the damper console, Sweatways access, Fold Camp's tidal flat.
    public float  TidePhase01        { get; set; }   // 0..1 within the tidal cycle
    public TideState CurrentTide     { get; set; }
    public int    SpringNeapDay      { get; set; }   // 0..13

    // The Sweatways' signature eleven-minute pressure cycle.
    public float  PressureCyclePhase01 { get; set; }
}
```

Storing only `GameSecondsElapsed` as authoritative and deriving everything else means a migration that changes the tidal period doesn't corrupt existing saves — it just recomputes. Anything derivable is derived.

## 3.12 `WeatherState`

```csharp
public sealed class WeatherState
{
    public string CurrentProfileId  { get; set; } = "";
    public string NextProfileId     { get; set; } = "";
    public float  TransitionProgress01 { get; set; }
    public double NextChangeGameSeconds{ get; set; }

    public float  RainIntensity01   { get; set; }
    public float  WindSpeedMps      { get; set; }
    public float  WindBearingDeg    { get; set; }
    public float  FogDensity01      { get; set; }
    public float  AmbientTempC      { get; set; }

    public float  CloudLidThickness01 { get; set; }   // the orographic lid; never fully clears
    public int    WeatherRngSeed      { get; set; }   // saved, so weather is deterministic on reload
}
```

## 3.13 The aggregate

```csharp
public sealed class GameState
{
    public PlayerState    Player    { get; init; } = new();
    public InventoryState Inventory { get; init; } = new();
    public SurvivalState  Survival  { get; init; } = new();
    public WorldState     World     { get; init; } = new();
    public StoryState     Story     { get; init; } = new();
    public QuestState     Quests    { get; init; } = new();
    public CampState      Camp      { get; init; } = new();
    public DiscoveryState Discoveries { get; init; } = new();
    public TimeState      Time      { get; init; } = new();
    public WeatherState   Weather   { get; init; } = new();
    public Dictionary<string, PuzzleState> Puzzles { get; init; } = new();
}
```

---

# PART 4 — SAVE SYSTEM

## 4.1 The envelope

```csharp
namespace Isle.Core.Saves;

public sealed class SaveEnvelope
{
    // ---- Header: readable without deserializing the body ----
    public const string MagicValue = "ISLESAVE";
    public string Magic          { get; set; } = MagicValue;
    public int    SchemaVersion  { get; set; }          // OUR data contract. Migration key.
    public string GameVersion    { get; set; } = "";    // "1.2.3+build.4471" — informational
    public string CatalogHash    { get; set; } = "";    // §1.5 step 6; detects modified content
    public string PlatformTag    { get; set; } = "";    // "ios" | "android"
    public long   SavedAtUnixUtc { get; set; }
    public long   SavedAtLocalOffsetSeconds { get; set; }

    // ---- Integrity ----
    public string BodyChecksum   { get; set; } = "";    // SHA-256 hex of the compressed body
    public int    BodyLengthBytes{ get; set; }
    public CompressionKind BodyCompression { get; set; }

    // ---- Metadata: read for the slot list WITHOUT touching the body ----
    public SaveMetadata Metadata { get; set; } = new();

    // ---- Body ----
    public byte[] Body           { get; set; } = Array.Empty<byte>();   // compressed GameState
}

public sealed class SaveMetadata
{
    public string SlotId            { get; set; } = "";   // "slot_1".."slot_3" | "auto_0".."auto_2" | "quick"
    public SaveKind Kind            { get; set; }
    public string LocationNameLocKey{ get; set; } = "";   // "ui.loc.sweatways.name" — LOC KEY, never "The Sweatways"
    public string ChapterLocKey     { get; set; } = "";   // "ui.chapter.act3.name"
    public string ActId             { get; set; } = "";
    public int    ChapterIndex      { get; set; }
    public double PlaytimeSeconds   { get; set; }
    public int    InGameDay         { get; set; }
    public float  InGameHour24      { get; set; }
    public int    DiscoveriesLogged { get; set; }
    public int    DiscoveriesTotal  { get; set; }         // for "47 / 112" on the slot card
    public string ThumbnailRelPath  { get; set; } = "";   // "thumbs/slot_1.jpg" — sibling file
    public int    ThumbnailWidth    { get; set; }
    public int    ThumbnailHeight   { get; set; }
}

public enum SaveKind { Manual, Auto, Quick, EndingCheckpoint }
public enum CompressionKind { None, Lz4Block, Gzip }
```

**Header/body split, and why it matters on mobile.** The slot-select screen shows three manual slots and three autosaves. Deserializing six full `GameState` bodies to render six cards is hundreds of milliseconds and tens of megabytes of transient allocation on a low-end Android. Instead the file is laid out as:

```
[ 8 B  magic "ISLESAVE"                       ]
[ 4 B  header length, little-endian int32     ]
[ N B  header+metadata, MessagePack, uncompressed ]
[ M B  body, MessagePack + LZ4 block          ]
```

The slot list reads 8 + 4 + N bytes and stops. **Estimate: under 1.5 KB and under 2 ms per slot.** The thumbnail is a **separate `.jpg` sibling file**, never embedded — so the slot list is six small header reads plus six async texture loads, and a corrupt thumbnail cannot poison a save.

**`SchemaVersion` vs `GameVersion`.** `SchemaVersion` is a monotonic integer owned by the data team; it increments only when the shape of `GameState` changes. `GameVersion` is the marketing/build string and is never branched on. Migration keys off `SchemaVersion` exclusively — otherwise you end up writing `if (version.StartsWith("1.2"))` and that code is unmaintainable within two patches.

## 4.2 Serialization format

### The candidates

| | JSON (Newtonsoft / System.Text.Json) | Raw binary (hand-rolled `BinaryWriter`) | **MessagePack (MessagePack-CSharp)** |
|---|---|---|---|
| Size | Largest | Smallest | Near-binary |
| Parse speed | Slowest | Fastest | Near-binary |
| Human-readable | Yes | No | No (but `MessagePackSerializer.ConvertToJson` gives you a readable dump) |
| Schema evolution | Trivial — unknown fields ignorable | Manual and error-prone | Good, with explicit `[Key(n)]` integer keys |
| Codegen / IL2CPP | Reflection-heavy; needs link.xml care | N/A | AOT source generator; works cleanly under IL2CPP |
| Dev ergonomics | Excellent | Terrible | Good with the JSON converter |

### RECOMMENDATION: **MessagePack with explicit integer keys, LZ4-block compressed, plus a JSON debug path.**

Reasoning, in priority order:

1. **IL2CPP/AOT is the binding constraint on mobile.** iOS is AOT-only; runtime reflection-based serializers are a minefield of "works in Editor, throws `ExecutionEngineException` on device." MessagePack-CSharp's source-generated resolver produces AOT-safe formatters at compile time. *(Assumption to verify against the specific MessagePack-CSharp version chosen for Unity 6 LTS — confirm the generator's Unity 6 support and the exact `link.xml`/resolver setup before locking this in. Do not take the version details on trust from this document.)*
2. **Explicit `[Key(int)]` beats string keys for both size and evolution.** Integer keys drop the field-name bytes entirely, and a field added at a new key index is simply absent in old data — which deserializes to the type default and is then fixed up by a migrator. String keys would cost roughly 30–40% of the body on our field-heavy state.
3. **Hand-rolled binary is faster and I am rejecting it anyway.** Manual `Write(x); Write(y);` ordering is a class of bug where a mismatched read/write pair produces plausible garbage instead of an exception. With seven schema versions in flight, that is a support nightmare. The 10–20% speed difference does not buy that risk.
4. **JSON survives as a build-flag-gated debug path.** `ISLE_SAVE_DEBUG_JSON` writes a sibling `.json` next to every save in Development builds. QA attaches it to bug reports; nobody needs a hex editor. It is not the ship format.

### Expected size and parse time — **estimates, to be measured**

Derived from the state field counts in Part 3 against a late-Act-4 save (~2,800 world flags and interactions, ~600 discovery entries, ~180 item stacks/instances, ~40 puzzle states):

| Format | Body size | Parse, iPhone 12 | Parse, mid Android |
|---|---|---|---|
| Pretty JSON | 900 KB – 1.4 MB | 45–90 ms | 120–240 ms |
| Minified JSON | 400–650 KB | 30–60 ms | 80–160 ms |
| MessagePack, string keys | 180–300 KB | 8–18 ms | 25–50 ms |
| **MessagePack, int keys** | **110–190 KB** | **5–12 ms** | **15–35 ms** |
| **+ LZ4 block** | **45–80 KB** | **7–15 ms** | **20–45 ms** |

**Every number in that table is a projection from field counts and general expectations, not a measurement, and must not be quoted anywhere without being measured first.** They are here to size the decision and set the budget.

### How to measure it, concretely

1. **Synthetic state generator** in `Isle.Core.Tests`: `GameStateFactory.Synthetic(SaveProfile.LateAct4)` builds a deterministic state matching the volumes above. Also `EarlyAct1`, `MidAct3`, `Completionist` (worst case: every discovery, every interaction).
2. **Engine-free benchmark first.** BenchmarkDotNet in `dotnet test` gives serialize/deserialize ns and allocated bytes per profile per format. Runs on every PR; regression threshold at +15%.
3. **On-device harness second**, because desktop numbers do not transfer. A hidden `DevSaveBench` scene serializes/deserializes each profile 20 times, reports median and p95, and writes a CSV to `persistentDataPath`. Run on the two reference devices (iPhone 12, and the designated mid-range Android) at every milestone.
4. **The budget, and the fail condition.** Autosave must complete in **under 16 ms of main-thread time** on the mid-range Android, with serialization on a worker thread and only the state snapshot on the main thread. Load must complete in **under 200 ms** total for the largest profile. Exceeding either at a milestone is a P1, not a "we'll look at it."
5. **Snapshot cost is separate and matters more.** The main-thread work is not serialization, it is deep-copying `GameState` so the worker can serialize a stable snapshot. Budget that separately; it is what will actually blow the 16 ms.

## 4.3 Migration framework

### The contract

- A build declares `CurrentSchemaVersion`.
- A save declares the `SchemaVersion` it was written at.
- If `save.SchemaVersion < CurrentSchemaVersion`, run every migrator from `save.SchemaVersion` to current, in order, each stepping exactly one version.
- If `save.SchemaVersion > CurrentSchemaVersion` (player downgraded), **refuse and say so clearly**. Never guess.
- Migrators operate on an **untyped document tree**, not on typed `GameState` — because `GameState` is the *current* shape, and a v3 save is not that shape. Migrating v3 into typed v7 classes throws away exactly the fields the migrator needs.

```csharp
namespace Isle.Core.Saves.Migration;

/// Untyped, migration-safe view of a save body. Backed by the MessagePack
/// document model, so a migrator can touch fields that no longer exist in code.
public interface ISaveDocument
{
    bool TryGetObject(string path, out ISaveDocument child);
    bool TryGetArray (string path, out IReadOnlyList<ISaveDocument> items);
    bool TryGetValue<T>(string path, out T value);

    void Set<T>(string path, T value);
    void Remove(string path);
    void Rename(string fromPath, string toPath);
    void ForEach(string arrayPath, Action<ISaveDocument> visit);

    bool Exists(string path);
}

public interface ISaveMigrator
{
    int FromVersion { get; }          // migrates FromVersion -> FromVersion + 1
    string Description { get; }       // printed in the migration log
    void Migrate(ISaveDocument doc, MigrationContext ctx);
}

public sealed class MigrationContext
{
    public required IDefinitionRepository Repo   { get; init; }
    public required IMigrationLog         Log    { get; init; }
    public required IReadOnlyDictionary<string, string> RetiredIdMap { get; init; }
    public int  ItemsRemapped   { get; set; }
    public int  EntriesDropped  { get; set; }
    public bool EncounteredUnknownId { get; set; }
}
```

### The chain

```csharp
public sealed class SaveMigrationChain
{
    private readonly ISaveMigrator[] _ordered;   // sorted by FromVersion, gap-checked at construction

    public SaveMigrationChain(IEnumerable<ISaveMigrator> migrators, int currentVersion)
    {
        _ordered = migrators.OrderBy(m => m.FromVersion).ToArray();

        // Fail at STARTUP, not at load. A missing link in the chain is a build defect
        // and it must surface in the first CI run, not in a player's crash report.
        for (int v = 1; v < currentVersion; v++)
            if (_ordered.Count(m => m.FromVersion == v) != 1)
                throw new InvalidOperationException(
                    $"Migration chain broken: expected exactly one migrator from v{v} to v{v + 1}, " +
                    $"found {_ordered.Count(m => m.FromVersion == v)}.");
    }

    public MigrationResult Run(ISaveDocument doc, int fromVersion, int toVersion, MigrationContext ctx)
    {
        if (fromVersion > toVersion)
            return MigrationResult.Refused(
                $"Save schema v{fromVersion} is newer than this build (v{toVersion}). " +
                "Update the app to continue this save.");

        foreach (var m in _ordered.Where(m => m.FromVersion >= fromVersion && m.FromVersion < toVersion))
        {
            ctx.Log.Step(m.FromVersion, m.FromVersion + 1, m.Description);
            try { m.Migrate(doc, ctx); }
            catch (Exception ex)
            {
                return MigrationResult.Failed(m.FromVersion, ex);   // caller falls back to the previous good save
            }
            doc.Set("__schemaVersion", m.FromVersion + 1);
        }
        return MigrationResult.Ok(ctx.ItemsRemapped, ctx.EntriesDropped);
    }
}
```

The constructor gap-check is the single highest-value line in this system. A chain with a hole in it fails on the developer's machine, on the first run after someone forgets a migrator — not silently in the field.

### Worked migration — v6 → v7, covering all three change classes

Scenario. Three things happened in the patch that moved the schema from 6 to 7:

1. **An item ID was renamed.** `item.hydrophone` became `item.hydrophone_directional` when the design added a second, omnidirectional unit. The old ID is retired forever (rule `ID-005`).
2. **A stat was added.** `SurvivalState.ExposureHours` is new — it must not default to 0 for a player standing on Rime Shoulder when they saved, or their exposure clock silently resets. It is seeded from `IsSheltered` and location.
3. **A recipe was removed.** `recipe.dry_box_v1` was replaced by `recipe.dry_box` with different ingredients. Saves may hold it in a "known recipes" set, and may hold a partially-built instance.

```csharp
public sealed class Migration_006_to_007 : ISaveMigrator
{
    public int FromVersion => 6;
    public string Description =>
        "Rename item.hydrophone -> item.hydrophone_directional; seed SurvivalState.ExposureHours; " +
        "retire recipe.dry_box_v1.";

    private const string OldHydrophone = "item.hydrophone";
    private const string NewHydrophone = "item.hydrophone_directional";

    public void Migrate(ISaveDocument doc, MigrationContext ctx)
    {
        // ---- 1. ITEM ID RENAME ------------------------------------------------
        // Every place an item ID can appear. Missing one is the classic migration bug,
        // so the list is exhaustive and there is a test asserting it covers every
        // GameState field whose type is "an item id".
        doc.ForEach("inventory.stacks", s => {
            if (s.TryGetValue<string>("defId", out var id) && id == OldHydrophone) {
                s.Set("defId", NewHydrophone); ctx.ItemsRemapped++;
            }
        });
        doc.ForEach("inventory.instances", i => {
            if (i.TryGetValue<string>("defId", out var id) && id == OldHydrophone) {
                i.Set("defId", NewHydrophone); ctx.ItemsRemapped++;
            }
        });
        doc.ForEach("world.placedItems", p => {
            if (p.TryGetValue<string>("defId", out var id) && id == OldHydrophone) {
                p.Set("defId", NewHydrophone); ctx.ItemsRemapped++;
            }
        });
        if (doc.TryGetValue<string>("player.equippedToolDefId", out var eq) && eq == OldHydrophone)
            doc.Set("player.equippedToolDefId", NewHydrophone);

        // ---- 2. NEW STAT WITH A MEANINGFUL DEFAULT ----------------------------
        // The naive default (0) is wrong: a player who saved above the cloud lid after
        // four hours of exposure would have their clock silently reset. Reconstruct
        // a conservative estimate from what v6 DID record.
        if (!doc.Exists("survival.exposureHours"))
        {
            float seeded = 0f;
            doc.TryGetValue<bool>("survival.isSheltered", out var sheltered);
            doc.TryGetValue<string>("player.currentLocationId", out var locId);

            if (!sheltered && !string.IsNullOrEmpty(locId)
                && ctx.Repo.TryGet<LocationDefinition>(locId, out var loc) && loc.IsAboveCloudLid)
            {
                seeded = 1.0f;   // one hour: enough to preserve the HUD warning state,
                                 // not enough to punish a player for our schema change.
                ctx.Log.Note($"Seeded exposureHours=1.0 (unsheltered at {locId}, above cloud lid).");
            }
            doc.Set("survival.exposureHours", seeded);
        }

        // ---- 3. REMOVED RECIPE ------------------------------------------------
        // Drop it from known-recipes, and refund any in-flight craft at full value so
        // the player is never worse off for our change.
        if (doc.TryGetArray("world.knownRecipeIds", out var known))
        {
            var kept = known.Select(x => x.TryGetValue<string>("", out var v) ? v : null)
                            .Where(v => v is not null && v != "recipe.dry_box_v1")
                            .ToList();
            if (kept.Count != known.Count)
            {
                // They knew the old recipe, so grant the replacement. Never silently
                // take away progress.
                if (!kept.Contains("recipe.dry_box")) kept.Add("recipe.dry_box");
                doc.Set("world.knownRecipeIds", kept);
                ctx.EntriesDropped++;
                ctx.Log.Note("Replaced recipe.dry_box_v1 with recipe.dry_box in known recipes.");
            }
        }
        doc.Remove("world.craftInProgress.recipe_dry_box_v1");

        // ---- 4. SWEEP: anything still pointing at a dead ID --------------------
        // Any ID the current catalog does not know, and that RetiredIdMap cannot remap,
        // is logged and dropped. Dropping a stack is recoverable; loading a state that
        // references a nonexistent definition is a crash three zones later.
        SweepUnknownIds(doc, ctx);
    }

    private static void SweepUnknownIds(ISaveDocument doc, MigrationContext ctx) { /* … */ }
}
```

**Migration principles this example encodes, and that every future migrator must follow:**

| Principle | Why |
|---|---|
| A rename is exhaustive across *every* field that can hold that ID kind. | The bug is always the one place you forgot. A reflection-driven test enumerates item-ID-typed fields and asserts each rename migrator touches all of them. |
| A new field gets a *meaningful* seed, not `default`. | `default` is a silent gameplay regression the player will report as a bug in your game, not as a bug in your migration. |
| A removal never leaves the player worse off. | Refund, or substitute. A patch that costs someone their dry box costs you a 1-star review. |
| Unknown IDs are dropped and logged, never loaded. | A dangling ID in state is a `DefinitionNotFoundException` at an arbitrary later moment. Drop it at the door, with a log line. |
| `retired_ids.json` is append-only and committed. | It is the permanent record of what an old ID means. Rule `ID-005` reads it. Never delete a line. |
| Every migrator has a test against a **real captured v(n) save**, committed as a fixture. | Synthetic test data is not old data. Capture a real save at each ship version into `Isle.Core.Tests/Fixtures/saves/v6_late_act3.isle`. |

## 4.4 Slots, autosave policy, atomic writes

### Slots

| Slot | Count | Written by |
|---|---|---|
| `slot_1` … `slot_3` | 3 | Player, from the pause menu |
| `auto_0`, `auto_1`, `auto_2` | 3 rolling | Autosave, newest at `auto_0` |
| `quick` | 1 | Optional quick-save |
| `ending_<id>` | up to 2 | Written immediately before the Quiet Room ending commit |

Seven or eight files, plus siblings. Trivial storage; **estimated under 1 MB total including thumbnails** (measure per §4.2).

The `ending_*` checkpoint deserves defending. The Quiet Room choice — THE RECORD vs THE RELIEF — is the whole game's payload and it is irreversible in fiction. A checkpoint written immediately before the commit lets a player see the other ending without replaying twenty hours. That does not cheapen the choice; it respects that a player who has bought the game has bought both endings, and it makes "which ending did you get" a conversation rather than a purchase decision.

### Autosave triggers

| Trigger | Rationale |
|---|---|
| Zone transition completes (after the new scene is live, before input unlocks) | The single best-defined safe point. |
| Puzzle solved (`Status → Solved`), after the solve stinger | Never lose a solved puzzle. |
| Discovery logged with `IsActGate == true` | Act gates are the progression spine. |
| Story beat with `IsActTransition == true` completes | Chapter boundary. |
| Camp upgrade build completes | Represents real material cost. |
| App backgrounding (`OnApplicationPause(true)`) | **The most important trigger on mobile.** A phone call, a notification, a home swipe. |
| Idle: 8 in-game hours elapsed, and no other autosave in the last 4 real minutes | The floor. Prevents long exploratory stretches from being unprotected. |

### The deferral gate — the part that is usually gotten wrong

Autosave requests are **never** executed immediately. They enter a one-slot queue and are drained by a gate:

```csharp
public sealed class AutosaveGate
{
    private AutosaveRequest? _pending;
    private double _lastAutosaveRealtime;
    private const double MinIntervalSeconds = 90;

    public void Request(AutosaveReason reason) =>
        _pending = (_pending is { } p && p.Reason.Priority() >= reason.Priority()) ? p : new AutosaveRequest(reason);

    /// Called once per frame. Returns true only when it is genuinely safe to snapshot.
    public bool TryDrain(in GameplayContext ctx, out AutosaveRequest req)
    {
        req = default;
        if (_pending is null) return false;

        // --- Hard blocks. Every one of these has burned someone's project. ---
        if (ctx.Story.IsBlockingAutosave)        return false;  // a beat with BlocksAutosave is playing
        if (ctx.ActivePuzzleStatus == PuzzleStatus.InProgress
            && ctx.PuzzleHasUncommittedInput)    return false;  // mid-dial on the damper console
        if (ctx.IsSceneLoading)                  return false;
        if (ctx.IsCraftingAnimationPlaying)      return false;  // mid-recipe: state is in flight
        if (ctx.IsInteractionInProgress)         return false;  // mid-Clear/Pry/Repair
        if (ctx.IsPlayerAirborne || ctx.IsOnRopeBridge)
                                                 return false;  // don't restore mid-fall / mid-bridge
        if (ctx.IsMenuTransitioning)             return false;
        if (ctx.RealtimeSinceStartup - _lastAutosaveRealtime < MinIntervalSeconds
            && _pending.Reason != AutosaveReason.AppBackgrounding) return false;

        req = _pending.Value;
        _pending = null;
        _lastAutosaveRealtime = ctx.RealtimeSinceStartup;
        return true;
    }
}
```

**Backgrounding is the documented exception** to the 90-second throttle, because the alternative is losing the session to an incoming call.

**Backgrounding is also the hard case, and honesty about it matters.** `OnApplicationPause(true)` gives you a short, platform-dependent window before the OS may suspend or kill the process — and the exact budget is not something to take on faith from this document. Design for it:

- Snapshot **synchronously** on the main thread (fast — a deep copy, not a serialize).
- Serialize and write on a worker, but `Join()` with a hard timeout of ~250 ms.
- If the write did not complete, the temp file simply never gets renamed, and the previous good autosave is intact (§4.5). **Fail to not-having-saved, never to having-half-saved.**
- **Unverified and must be measured on device:** the real available window on current iOS and Android versions, and whether the worker reliably gets scheduled during it. Budget a milestone task for this specifically; do not assume it works because it worked in the Editor.

### Atomic write

```
1. Serialize into memory.  No file handle open yet.
2. Write to  <slot>.isle.tmp   — full body, then Flush().
3. Flush to physical storage (FileStream.Flush(flushToDisk: true) on the underlying
   handle; on iOS/Android via the platform IO layer in Isle.Unity).
4. Close the handle.
5. Verify: reopen the temp, read the header, recompute the body checksum, compare.
   Mismatch ⇒ delete the temp, abort, keep the old file, surface a non-fatal warning.
6. If <slot>.isle exists, rename it to <slot>.isle.bak (replacing any prior .bak).
7. Rename <slot>.isle.tmp  ->  <slot>.isle.
8. Write the thumbnail to <slot>.jpg.tmp and rename it.  Thumbnail LAST — it is
   cosmetic, and a save that lands without a thumbnail is fine; a thumbnail without
   a save is not.
```

Every outcome of a kill at any step:

| Killed at | Result |
|---|---|
| 1–4 | Old `.isle` untouched. A `.tmp` is orphaned; swept on next launch. |
| 5 | Same — the temp is discarded by design. |
| 6 | Old save exists as `.bak`, `.isle` momentarily absent. Loader falls back to `.bak`. **This is the one non-atomic window**, and it is one rename wide. |
| 7 | Rename is the atomic operation. Either the old or the new file is there, never a blend. |
| 8 | Save is complete and valid; thumbnail may be stale or missing. Slot card falls back to a location-art placeholder. |

Steps 6+7 as two renames rather than one leaves that one-rename window. The alternative — never keeping a `.bak` — removes the window but removes the recovery path too. **Keeping the `.bak` is the right trade**, because the window is microseconds wide and the recovery path is the thing that saves a player's twenty hours. `.bak` is also what §4.5 recovers from.

Note that `File.Replace` / POSIX `rename` atomicity guarantees vary by platform and filesystem, and **the exact behaviour on iOS and on Android's scoped storage should be verified on device before this is considered done** — including whether Unity's `persistentDataPath` sits on a filesystem where rename-over-existing is atomic. Treat that as an open engineering task, not a settled fact.

## 4.5 Corruption handling and recovery

**Load order for a slot:**

```
1. <slot>.isle          — the current save
2. <slot>.isle.bak      — the previous good save
3. The newest valid auto_N whose ActId is not ahead of the requested slot
4. Refuse, with a clear message. Never auto-start a new game over a corrupt save.
```

**Validation ladder, cheapest first, each failure demoting to the next source:**

| Check | Failure meaning |
|---|---|
| File length ≥ 12 bytes | Truncated at creation |
| Magic == `"ISLESAVE"` | Not our file, or garbage |
| Header length int32 is sane (0 < N < 64 KB) | Header corrupt |
| Header deserializes | Header corrupt |
| `SchemaVersion` within `[1, CurrentSchemaVersion]` | Downgrade, or garbage |
| `BodyLengthBytes` matches actual remaining bytes | Truncated write |
| SHA-256 of body == `BodyChecksum` | Bit rot, or interrupted write |
| Body decompresses | Compression frame corrupt |
| Body deserializes to a document | Structural corruption |
| Migration chain completes | Migration defect |
| Post-load invariants (§below) | Logical corruption |

**Post-load invariants**, checked before handing control to gameplay. These catch logical corruption that passes every byte-level check:

- `Player.CurrentLocationId` resolves in the catalog.
- Every `DefId` in inventory, world placements, puzzles, quests and discoveries resolves. Unresolvable entries are **dropped with a log line**, not loaded.
- `Time.GameSecondsElapsed >= 0` and `Metadata.PlaytimeSeconds >= 0`.
- Survival stats clamped to `[0,1]`.
- `ActiveQuestId`, if non-empty, exists in `Quests`.
- Every `PuzzleState.ElementValues.Length` equals its definition's `ElementCount`. Mismatch ⇒ reset that puzzle to `Available` rather than fail the load — a re-solvable puzzle is a far smaller cost than a dead save.

**What the player sees.** Never a stack trace, never "error 0x8007". One dialog:

> **This save could not be read.**
> We restored your previous save from **Day 14, 09:40, The Combs**.
> *(4 minutes of progress may be missing.)*
> **[Continue]  [Choose another save]**

And a diagnostic block written to the log with the failed check, the file's header (which is usually still readable), and the byte offset — plus the corrupt file **preserved** as `<slot>.isle.corrupt-<timestamp>`, capped at three retained, so a support case has something to analyse. Deleting the evidence is how a corruption bug survives three patches.

## 4.6 What is NOT saved, and why

| Not saved | Why |
|---|---|
| **Any definition data** | Comes from the catalog. Saving it would freeze a patched balance change into old saves and inflate the file tenfold. |
| Derived time fields (`DayNumber`, `HourOfDay24`, `TidePhase01`, `CurrentTide`) | Pure functions of `GameSecondsElapsed`. Storing them invites the two to disagree after a migration. |
| Cached aggregates (`CarriedMassKg`, `CarriedVolumeLitres`, `CampState.ActiveStationTags`) | Recomputed on load in under a millisecond. Storing a cache is storing a future desync. |
| Scene object transforms not explicitly in `WorldState` | Scenes are deterministic from their addressable + `MechanismPositions` + `PlacedItems`. Saving every transform would be megabytes of noise. |
| Audio playback positions, ambient loop phase | Restarting the Sweatways bed on load is imperceptible. Saving it is not worth one byte. |
| Particle, animator, timeline internal state | Reconstructed from the state that drives them. |
| Addressables handles, loaded asset refs, any `UnityEngine.Object` | Process-lifetime, meaningless across sessions, and would drag `Isle.Core` into Unity. |
| UI navigation state, open panels, scroll positions | Load always returns to gameplay. A save that restores you into a sub-menu is a bug. |
| Localization language | A **device/profile** setting in `PlayerPrefs`, not a per-save setting. A player switching language should not have it revert per slot. |
| Graphics quality, audio volumes, input bindings, subtitle size | Same: device settings, separate `settings.json` in `persistentDataPath`. Global across slots. |
| Analytics session ids, crash breadcrumbs | Not game state; separate sink. |
| Hint text already shown | Derivable from `PuzzleState.TimeOpenSeconds` + `HintsUnlocked`. |
| Frame-local input | Obviously. Listed because someone always asks. |

**The rule:** if it is derivable from saved state plus the catalog, derive it. Every stored derivation is a future migration bug and a future desync.

`CatalogHash` **is** stored — not as gameplay state, but as provenance. On load, a mismatch against the running build's catalog is normal (the player patched) and simply logs. It exists so support can distinguish "patched normally" from "content files were edited," which is the difference between a real bug and a modded run, and is worth the 32 bytes.

## 4.7 Cloud save readiness

**Architecture only for v1 — the local system is built so cloud is a later adapter, not a rewrite.**

```csharp
public interface ISaveStore
{
    Task<IReadOnlyList<SaveSlotHandle>> ListAsync(CancellationToken ct);
    Task<SaveEnvelope?> ReadAsync(string slotId, CancellationToken ct);
    Task WriteAsync(string slotId, SaveEnvelope envelope, CancellationToken ct);
    Task DeleteAsync(string slotId, CancellationToken ct);
}

public sealed record SaveSlotHandle(
    string SlotId, long SavedAtUnixUtc, int SchemaVersion,
    double PlaytimeSeconds, string DeviceId, long SequenceNumber);
```

`LocalFileSaveStore` ships in v1. `CloudSyncSaveStore` wraps it later: local stays authoritative for reads during play, cloud syncs on defined boundaries (app launch, app background, manual save).

**Fields the envelope already carries for this**, deliberately, so no schema bump is needed to add cloud:

- `SavedAtUnixUtc` — wall-clock ordering, untrusted (device clocks are wrong and users change them).
- `DeviceId` — a stable per-install GUID, **not** an advertising or hardware identifier, stored in the settings file.
- `SequenceNumber` — a monotonic counter incremented on every write to a slot, per device. **This, not the timestamp, is the primary ordering signal**, precisely because device clocks lie.
- `PlaytimeSeconds` — the tiebreak, and the field a human can actually reason about.

### Conflict resolution policy

Conflict = local and cloud for the same slot have neither a common ancestor `SequenceNumber` nor an identical `BodyChecksum`.

1. **Identical checksums ⇒ not a conflict.** Reconcile metadata, move on.
2. **One is a strict ancestor** (its `SequenceNumber` ≤ the other's, same `DeviceId` lineage) ⇒ take the descendant silently. This is the overwhelmingly common case: same player, two sessions, one device.
3. **True divergence** (two devices both wrote since the last sync) ⇒ **ask the player. Never auto-merge, never silently pick.** Two cards, side by side:

   > **Which save should continue?**
   > **This device** — Day 14, 09:40 · The Combs · 11 h 20 m · *saved 4 minutes ago*
   > **iPad** — Day 12, 17:05 · Fold Camp · 9 h 45 m · *saved yesterday*
   > [thumbnail] [thumbnail]
   > *The other save will be kept as a backup.*

4. **The loser is never deleted.** It is written to `<slot>_conflict_<deviceId>_<seq>.isle` locally and retained. Two retained per slot, oldest evicted.
5. **Auto-resolution heuristic is available but off by default.** If the player has opted into "always keep the furthest progress," the tiebreak order is: higher `ActId` → higher `ChapterIndex` → higher `PlaytimeSeconds` → higher `SavedAtUnixUtc`. Never `SavedAtUnixUtc` first.
6. **Schema mismatch across devices.** If cloud's `SchemaVersion` > local build's, the local build cannot read it: show "This save was made with a newer version. Update the app." and **do not overwrite the cloud copy** — the other device is ahead and its data is the more valuable one.
7. **Sync timing.** Upload on: app background, manual save, ending checkpoint. Download on: app foreground and cold launch. Never mid-gameplay — a cloud write landing while the player is in the Sweatways is a class of bug with no upside.

**Unverified, flagged for the platform engineer:** the specific capabilities, quotas, file-size limits and conflict semantics of iCloud (key-value vs document storage vs CloudKit) and Google Play Games Services Saved Games, and which Unity package is the currently supported path for each on Unity 6 LTS. This document specifies the *architecture the game side needs*; the platform bindings must be confirmed against current first-party documentation before implementation, not inferred from this text.

---

# PART 5 — FOLDER LAYOUT AND RUNTIME ADDRESSING

## 5.1 `Assets/Data/`

```
Assets/Data/
├─ Authoring/                         # ScriptableObjects. Designer-facing. Committed.
│  ├─ Items/
│  │  ├─ Tools/          item.hydrophone_directional.asset
│  │  │                  item.field_recorder.asset
│  │  │                  item.field_slate.asset
│  │  │                  item.multitool.asset
│  │  ├─ Materials/      item.cordage_plaited.asset
│  │  │                  item.hemp_fibre.asset
│  │  │                  item.driftwood_spar.asset
│  │  ├─ Vessels/        item.canteen_steel.asset
│  │  ├─ Documents/      item.poulter_journal_vol2.asset
│  │  ├─ Reels/          item.reel_sabo_log_14.asset
│  │  └─ Components/     item.recorder_cell_d.asset
│  ├─ Resources/         res.fresh_water.asset  res.recorder_battery.asset
│  ├─ Recipes/
│  │  ├─ Act1/           recipe.cordage_plaited.asset  recipe.catchment_rig.asset
│  │  └─ Act3/           recipe.reel_rebake.asset
│  ├─ Discoveries/
│  │  ├─ Act1/           disc.gully_cut_stone.asset
│  │  ├─ Act2/           disc.reel_sabo_gates.asset
│  │  └─ Act4/           disc.forty_hours_a_month.asset
│  ├─ Interactions/
│  │  ├─ Ribcage/        int.ribcage_hull3_locker.asset
│  │  ├─ Fernmaw/        int.fernmaw_gully_moss_clear.asset
│  │  ├─ Combs/          int.combs_ladder_bolt_examine.asset
│  │  └─ Oleander/       int.oleander_gate_b_valve.asset
│  ├─ Puzzles/           puz.sweatways_baffle_order.asset
│  │                     puz.oleander_damper_console.asset
│  ├─ Locations/         loc.ribcage.asset … loc.quiet_room.asset
│  ├─ Story/
│  │  ├─ Act1/ … Act5/   story.act5_quiet_room_threshold.asset
│  ├─ Quests/            quest.act1_water_36h.asset
│  ├─ CampUpgrades/      camp.drying_rack.asset
│  └─ Tables/            tide.vardholm_spring.asset
│                        weather.fernmaw_wet.asset
│
├─ Baked/                             # GENERATED. In .gitignore.
│  ├─ isle.catalog                    #   MessagePack + LZ4. The shipped artifact.
│  ├─ isle.catalog.manifest.json      #   Human-readable index: id, type, revision, byte offset.
│  └─ bake-report.json                #   Counts, sizes, timings, warnings. CI artifact.
│
├─ Schema/                            # Committed. Small. Reviewed like code.
│  ├─ schema_version.txt              #   Single integer. The catalog schema version.
│  ├─ save_schema_version.txt         #   Single integer. The SAVE schema version. Separate on purpose.
│  ├─ retired_ids.json                #   APPEND-ONLY. { "item.hydrophone": "item.hydrophone_directional" }
│  ├─ validation_allowlist.json       #   Named exemptions, each with an owner and a reason.
│  └─ loc_key_fields.json             #   Which fields the loc extractor treats as keys.
│
└─ Editor/                            # Isle.Authoring asmdef lives here.
   ├─ Isle.Authoring.asmdef           #   Editor platform only.
   ├─ Baker/                          #   Baker.cs, Projectors/*.cs (one ToRecord per type)
   ├─ Validation/                     #   One file per rule family: IdRules, RefRules, LocRules,
   │                                  #   ReachabilityRules, SemanticRules, CanonRules
   └─ Windows/                        #   CatalogBrowser, ValidationReport, IdRenameTool
```

**`isle.catalog` is gitignored.** It is generated, it is binary, and committing it guarantees a merge conflict on every branch that touches content. CI regenerates it; the build regenerates it unconditionally. `isle.catalog.manifest.json` is the diffable surface — committed on release branches only, so a release PR shows exactly which definitions changed and by how much.

**`save_schema_version.txt` is separate from `schema_version.txt`** because content and save shape change on different cadences. A patch that adds twelve items bumps the catalog schema and touches no saves. A patch that adds a field to `SurvivalState` bumps the save schema and needs a migrator. Coupling them would force a pointless migrator on every content patch.

## 5.2 Assets outside `Data/`

Definitions hold **addressable key strings**; the assets live in a parallel tree owned by the art and audio teams, addressed by a convention the validator checks against the Addressables catalog at bake time:

```
Assets/Art/     → props/tools/…  props/camp/…  ui/icons/…  ui/slate/…  ui/map/…
Assets/Audio/   → sfx/…  audio/amb/…  audio/stinger/…  vo/<lang>/…  music/…
Assets/Scenes/  → scenes/zone_<zone>
Assets/Localization/ → string tables, one per language
```

Addressable group boundaries follow zone boundaries, which is why `LocationDefinition.AssetGroupAddresses` is a list: entering the Sweatways loads exactly `group/sweatways_geo`, `group/sweatways_props`, `group/sweatways_audio`, and `PreloadNeighbourIds` warms Fernmaw and Ash Throat at low priority. Leaving releases them by ref-count. This is the memory plan, and it works only because definitions never hold typed asset references (§1.2e).

## 5.3 Runtime addressing

**Boot sequence:**

```
1. Load Assets/Data/Baked/isle.catalog from StreamingAssets (or a local addressable).
   Single read.  Estimated 25–60 KB compressed.  (Measure per §4.2.)
2. Assert catalog.SchemaVersion == Isle.Core.SchemaVersion.CatalogCurrent.
   Mismatch ⇒ throw at boot with both numbers. Loud, immediate, unambiguous.
3. Decompress + deserialize into BakedDefinitionRepository.
   All definitions and all precomputed indices are now resident. Estimated 300–700 KB managed.
4. Register the repository in the composition root. Nothing else ever loads a definition.
5. Load Assets/Data/Schema/retired_ids.json into the MigrationContext.
6. Enumerate save slot HEADERS only (§4.1) for the main menu. No bodies.
```

**Access pattern — one interface, injected, never a static singleton:**

```csharp
// Good: injected, testable with a FakeDefinitionRepository in a plain unit test.
public sealed class CraftingService
{
    private readonly IDefinitionRepository _repo;
    public CraftingService(IDefinitionRepository repo) => _repo = repo;

    public CraftResult TryCraft(string recipeId, InventoryState inv, IClock clock)
    {
        if (!_repo.TryGet<RecipeDefinition>(recipeId, out var recipe))
            return CraftResult.UnknownRecipe(recipeId);
        // … pure logic over definitions + state, no Unity, fully unit-tested.
    }
}
```

There is no `DefinitionDatabase.Instance`. A static accessor would let a MonoBehaviour reach the catalog from anywhere, which is convenient for exactly as long as it takes to write the first test that needs a different catalog — and then it is permanent.

**Asset resolution, and where the seam is:**

```csharp
// Isle.Unity only. Core hands out a string; Unity turns it into a thing.
public interface IAssetResolver
{
    UniTask<Sprite>     LoadIconAsync(string address, CancellationToken ct);
    UniTask<GameObject> LoadPrefabAsync(string address, CancellationToken ct);
    void Release(string address);
}
```

Core produces `"ui/icons/item_hydrophone_directional"`. `Isle.Unity` turns that into an Addressables handle, ref-counts it and releases it. Core never learns what a `Sprite` is. That one line of separation is what keeps the whole survival simulation, the damper solver, the Field Slate revision graph and the seven-version save migration chain testable in a two-second `dotnet test` — which is the entire argument of Part 1, arriving back where it started.

---

## Open items requiring verification before implementation

Stated explicitly so none of these ships as an assumption:

1. **MessagePack-CSharp's Unity 6 LTS support, source-generator setup, and IL2CPP/AOT configuration.** Confirm against the package's current documentation and a device build before locking the format in.
2. **`FileStream.Flush(true)` / rename atomicity on iOS and on Android scoped storage**, and the filesystem behind Unity's `persistentDataPath` on each. The atomic write strategy in §4.4 depends on rename-over-existing being atomic; verify, don't assume.
3. **The real `OnApplicationPause(true)` budget** on current iOS and Android, and whether a worker thread is reliably scheduled within it. Measure on the reference devices.
4. **iCloud and Google Play Games Saved Games** capabilities, quotas and conflict semantics, and the currently supported Unity package for each.
5. **UnityYAMLMerge's exact path and invocation** on the team's Unity 6 LTS installs, before it goes into the repo's git config.
6. **Every size and timing figure in §4.2 and §5.3** — projections from field counts, not measurements. Measure before any of them is used for a decision or quoted externally.
7. **Trademark/title clearance** for the naming set, as already flagged in Story Bible §9. Not a data-architecture item, but it touches every ID in this document and none of the IDs here have been cleared.