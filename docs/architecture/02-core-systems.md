# THE FORGOTTEN ISLE — Core Systems Specification
## Lead Gameplay Engineer · v1.0 · Unity 6 LTS / C# / URP / New Input System

---

## 0. ARCHITECTURAL GROUND RULES

Six rules that constrain every system below. Violations are blocking review comments.

1. **Layered, acyclic.** Systems live in one of five layers: `L0 Foundation` → `L1 World` → `L2 Domain` → `L3 Progression` → `L4 Presentation`. A system may reference only *downward*. Upward communication is always an event on `IEventBus`. There are zero direct upward references in shipping code.
2. **No `MonoBehaviour` in domain logic.** Every system named here is a plain C# class in an asmdef (`Isle.Domain`) that does not reference `UnityEngine` except for `Vector3`/`Quaternion` math types and `ScriptableObject` for authoring data. That asmdef is testable in a headless NUnit run with no Play Mode.
3. **Ticks are explicit and ordered.** No `Update()` in domain code. A single `GameLoop` MonoBehaviour calls `IDomainTick.Tick(in TickContext)` in the order specified in §19. `TickContext` carries `float RealDeltaSeconds`, `double WorldSeconds`, `double WorldDeltaSeconds`, `uint TickIndex`.
4. **All randomness is seeded and owned.** No `UnityEngine.Random` in domain code. Each system that needs randomness owns a `Pcg32` instance seeded from `SaveHeader.WorldSeed` plus a per-system salt constant. Seed state is serialized.
5. **IDs are content-addressed, never indices.** Every authored thing has an `ItemId`/`RecipeId`/`PuzzleId`/`DiscoveryId` etc., which is a `readonly struct` wrapping a `uint` FNV-1a hash of a stable string key, with the source string retained in editor builds only. Array index changes do not invalidate saves.
6. **Mobile budget is a constraint, not an aspiration.** Domain tick budget is **1.2 ms** on iPhone 12 at 60fps, **2.4 ms** at 30fps low-end. Domain code allocates zero managed memory per frame in steady state; every collection is pre-sized and pooled, every event payload is a `readonly struct`, every callback list is a pre-allocated `List<T>` iterated by index.

### 0.1 The shared primitives

```csharp
public readonly struct ItemId : IEquatable<ItemId> { public readonly uint Value; }
public readonly struct ItemInstanceId : IEquatable<ItemInstanceId> { public readonly ulong Value; } // monotonic, never reused
public readonly struct RecipeId       : IEquatable<RecipeId> { public readonly uint Value; }
public readonly struct DiscoveryId    : IEquatable<DiscoveryId> { public readonly uint Value; }
public readonly struct PuzzleId       : IEquatable<PuzzleId> { public readonly uint Value; }
public readonly struct NodeId         : IEquatable<NodeId> { public readonly uint Value; }   // story graph node
public readonly struct QuestId        : IEquatable<QuestId> { public readonly uint Value; }
public readonly struct ZoneId         : IEquatable<ZoneId> { public readonly uint Value; }
public readonly struct InteractableId : IEquatable<InteractableId> { public readonly uint Value; }
public readonly struct LocKey         : IEquatable<LocKey> { public readonly uint Value; }
```

`IEventBus` is a typed, synchronous, single-threaded dispatcher with a **deferred queue**: publishing during dispatch enqueues rather than re-entering. Drained at the end of the tick phase that published it (§19, phase 11).

```csharp
public interface IEventBus {
    void Publish<T>(in T evt) where T : struct;
    IDisposable Subscribe<T>(EventHandler<T> handler) where T : struct; // handler is delegate void(in T)
    void DrainDeferred(int maxPasses = 4); // throws in dev if not converged; logs + drops in ship
}
```

---

## 1. InventorySystem

**SINGLE RESPONSIBILITY.** Owns the authoritative contents of Nadia's carried containers — which item instances exist in which slot, in what quantity, with what durability/charge — and nothing about what those items mean.

### 1.1 Public API

```csharp
public interface IInventorySystem : IDomainTick {
    // Queries
    int          CountOf(ItemId id, ContainerId container = ContainerId.All);
    bool         TryGetSlot(int slotIndex, out InventorySlot slot);
    ReadOnlySpan<InventorySlot> Slots(ContainerId container);
    InventoryCapacity GetCapacity(ContainerId container);   // slots used/total, grams used/total

    // Mutations — all return a typed result, never throw on gameplay-legal failure
    AddResult    TryAdd(ItemId id, int quantity, in ItemInstanceState state, out int accepted);
    RemoveResult TryRemove(ItemId id, int quantity, RemovalOrder order = RemovalOrder.OldestFirst);
    RemoveResult TryRemoveInstance(ItemInstanceId instance, int quantity);
    bool         TryMove(int fromSlot, int toSlot, int quantity);
    bool         TrySetDurability(ItemInstanceId instance, float normalized01);

    // Events (payloads are structs)
    event EventHandler<InventoryChanged> Changed;          // slot delta, one per affected slot
    event EventHandler<InventoryRejected> Rejected;        // full / overweight / forbidden
    event EventHandler<ItemDurabilityBroke> DurabilityBroke;
}
```

```csharp
public enum ContainerId : byte { DryBag = 0, Belt = 1, Cache = 2, All = 255 }
public enum RemovalOrder : byte { OldestFirst, MostDamagedFirst, NewestFirst }
public readonly struct ItemInstanceState {
    public readonly float Durability01;   // 1 = pristine; NaN for non-durable
    public readonly float Charge01;       // recorder battery, lantern oil; NaN if N/A
    public readonly float WetnessSeconds; // seconds since last full submersion, clamped 0..7200
}
public readonly struct InventorySlot {
    public readonly ItemId Id; public readonly int Quantity;
    public readonly ItemInstanceId Instance; public readonly ItemInstanceState State;
    public readonly uint AcquiredTickIndex;   // the tiebreak for OldestFirst
}
```

### 1.2 Owned state

* `InventorySlot[] _slots` — fixed length **36** (DryBag 0–23, Belt 24–29, Cache 30–35). Cache is the Fold Camp footlocker and is only addressable while `CampSystem` reports the player docked at a camp; InventorySystem does not know that — `CampSystem` calls `SetContainerAddressable(ContainerId.Cache, bool)`.
* `ulong _nextInstanceId` — monotonic counter.
* `float _carriedGrams` — cached derived sum, recomputed on every mutation (36 slots, trivial).
* Nothing else. **It does not own item definitions** (ItemSystem) and **does not own equipped state** (InteractionSystem owns the equipped-tool pointer, which is an `ItemInstanceId` lease — see §1.6).

### 1.3 Dependencies

| Reads | Via | Why |
|---|---|---|
| `IItemCatalog` (ItemSystem) | direct readonly reference, injected | needs `MaxStack`, `Grams`, `IsDurable`, `ForbiddenContainers` per `ItemId`. The catalog is immutable at runtime, so this is a pure data read with no cycle risk. |

Nothing else. InventorySystem is the most depended-upon domain system and must stay dependency-light or the graph knots.

**Cycle flag.** CraftingSystem and CombinationSystem both need to remove and add — they call *into* Inventory (downward). Inventory never calls them. UI reacts to `Changed`. No cycle.

### 1.4 Key algorithms

**Stacking rules.** A slot holds one `ItemId`. Two units stack **iff** all hold:
1. same `ItemId`;
2. `def.MaxStack > 1`;
3. `def.IsDurable == false` (a durable item is always quantity 1 in its own slot — a hatchet at 0.31 durability and one at 0.94 are not fungible);
4. `def.IsCharged == false` (same reason: the field recorder's 9% battery is instance data);
5. `|a.WetnessSeconds - b.WetnessSeconds| <= 60f` — wetness merges by taking the **max** of the two, so stacking never launders a soaked stack dry.

**Canonical stack sizes (first pass, shipping values).**

| Item | MaxStack | Grams each | Durable |
|---|---|---|---|
| Cordage (1 m coil) | 10 | 45 | no |
| Basalt flake (cutting edge) | 5 | 60 | yes → forced stack 1 |
| Fern-fibre bundle | 20 | 25 | no |
| Freshwater (250 ml pouch) | 8 | 260 | no |
| Dry cell (D, salvaged) | 6 | 140 | charged → stack 1 |
| Brass maintenance tag | 40 | 12 | no |
| Reel (7″ tape) | 4 | 380 | no |
| Hydrophone | 1 | 900 | yes |

**Capacity — dual-limit, both hard.** Slots (24 in the dry bag) **and** mass. `DryBagMaxGrams = 11000` (11 kg — a plausible load for a 41-year-old carrying it up Rime Shoulder; we tune this, not the slot count, if Act 2 backtracking feels bad). Belt: 6 slots, `1800 g`, and Belt is the only container InteractionSystem may lease an equipped tool from — this is why the belt exists as a separate container instead of being a UI filter.

**TryAdd — exact semantics.**

```
TryAdd(id, qty, state):
  def = catalog[id]
  if def.Forbidden(container) -> Rejected(Forbidden); accepted=0; return Forbidden
  remainingMass = MaxGrams - _carriedGrams
  massAffordable = floor(remainingMass / def.Grams)        // partial accept is legal
  qty = min(qty, massAffordable)
  if qty == 0 -> Rejected(Overweight); return Overweight

  accepted = 0
  // Pass 1: top up existing compatible stacks, lowest slot index first (stable, testable)
  for slot in container ascending:
      if CanStack(slot, id, state):
          room = def.MaxStack - slot.Quantity
          take = min(room, qty - accepted)
          slot.Quantity += take
          slot.State.WetnessSeconds = max(slot.State.WetnessSeconds, state.WetnessSeconds)
          accepted += take
          if accepted == qty: break
  // Pass 2: fill empty slots
  while accepted < qty and TryFindEmptySlot(out i):
      take = min(def.MaxStack, qty - accepted)     // durable/charged force MaxStack=1
      _slots[i] = new InventorySlot(id, take, NewInstanceId(), state, ctx.TickIndex)
      accepted += take

  _carriedGrams += accepted * def.Grams
  if accepted > 0: raise Changed per touched slot
  if accepted < requested: raise Rejected(Full or Overweight); return Partial
  return Full
```

**What happens when the pack is full — the designed answer, not the default one.** Partial accept always succeeds for what fits; the remainder is **not silently destroyed and not dropped on the ground**. `Rejected` fires with `RejectReason.Full` and the overflow quantity. The pickup interactable that initiated it *does not despawn* — it decrements to the overflow amount and stays in world with a "partially taken" visual state. This is the only correct behaviour for an adventure game where a missed pickup can soft-lock a puzzle. Quest-critical items (`def.Flags.HasFlag(ItemFlags.Critical)`) **bypass both limits entirely** and are always accepted into a reserved slot region (slots 20–23 of DryBag, `ReservedForCritical`); the Field Slate, hydrophone and recorder live there. This is the guard against the classic "I dropped the key item and cannot find it" support ticket.

**TryRemove across stacks — exact semantics.** This is where implementations usually go wrong, so it is specified to the line:

```
TryRemove(id, qty, order):
  total = CountOf(id, All)
  if total < qty: return RemoveResult.Insufficient   // ATOMIC: nothing is removed
  // Build candidate list, ordered:
  //   OldestFirst      -> ascending (AcquiredTickIndex, then slotIndex) — FIFO, deterministic
  //   MostDamagedFirst -> ascending Durability01, then AcquiredTickIndex, then slotIndex
  //   NewestFirst      -> descending AcquiredTickIndex, then ascending slotIndex
  left = qty
  foreach slot in candidates:
      take = min(slot.Quantity, left)
      slot.Quantity -= take; left -= take
      if slot.Quantity == 0: slot = InventorySlot.Empty     // slot is freed, instance id retired
      raise Changed(slot)
      if left == 0: break
  _carriedGrams -= qty * def.Grams
  return RemoveResult.Removed
```

Two invariants worth writing down because they are the ones that break: **removal is atomic** (insufficient stock removes nothing — no partial consumption of craft inputs leaving the player with half a recipe's worth gone), and **ordering is total** (the slot-index tiebreak means removal is byte-identical across runs given the same save, which is what makes replay-based save tests possible).

**Sorting.** Sorting is a *view* concern, never a mutation. `Slots()` returns raw slot order; the UI requests `SortedView(SortMode)` which returns a pooled `int[]` of slot indices ordered by `(def.Category, def.SortRank, localizedName ordinal)`. **Physical slot positions never change from a sort**, because a player who has learned "cordage is slot 3" should not have that relationship broken; and because moving slots invalidates `AcquiredTickIndex` semantics for OldestFirst removal.

### 1.5 Determinism + testability
Pure domain. Zero Unity dependency beyond the catalog `ScriptableObject`'s baked struct array. A unit test constructs `new InventorySystem(FakeCatalog.WithItems(...), capacity)`, calls mutators, asserts on slot spans. No fixture, no scene, no clock — `AcquiredTickIndex` is passed in.

### 1.6 The equipped-item overlap, resolved explicitly
**Overlap:** InteractionSystem wants "what tool is in Nadia's hand." InventorySystem owns item instances. Two systems cannot both own this.
**Resolution:** InventorySystem owns the instance. InteractionSystem owns a single `ItemInstanceId _equipped` — a *lease*, not a copy. Every use re-validates via `TryGetInstance`. If Inventory removes or destroys that instance it publishes `ItemDurabilityBroke` / `InventoryChanged`, and InteractionSystem's subscription clears the lease. Inventory never knows the concept "equipped."

### 1.7 Failure modes

| Failure | Guard |
|---|---|
| Duplication via a rejected craft (items removed, craft fails, nothing given back) | Removal is atomic **and** CraftingSystem uses a two-phase commit: `ReserveInputs` → build → `CommitReservation`/`RollbackReservation`. Reservations are held inside CraftingSystem, not Inventory. |
| `_carriedGrams` drifting from truth after many mutations (float accumulation) | `_carriedGrams` is `float` but recomputed from scratch every 256 mutations and on every save/load; a dev-build assert compares incremental vs. recomputed with `1e-2` tolerance. |
| Critical item lost because a designer forgot the `Critical` flag | Editor validator: any item referenced by a `PuzzleDefinition` requirement or a `QuestStep` objective *must* carry `Critical`; build fails otherwise. |
| Instance id reuse after load producing aliasing bugs | `_nextInstanceId` is serialized and is always `max(serialized, 1 + max id in slots)` on load. |

---

## 2. ItemSystem

**SINGLE RESPONSIBILITY.** Owns the immutable catalog of item definitions and the per-item-instance *behaviour* that is not container bookkeeping — degradation over time (wetness, corrosion, charge drain) and the resolution of "what can this item do."

### 2.1 Public API

```csharp
public interface IItemCatalog {                     // immutable, thread-safe, load-time built
    bool TryGet(ItemId id, out ItemDefinition def);
    ReadOnlySpan<ItemId> WithTag(ItemTag tag);      // e.g. ItemTag.CuttingEdge -> [flake, multitool, machete]
    bool HasTag(ItemId id, ItemTag tag);
    LocKey NameKey(ItemId id);
}

public interface IItemSystem : IDomainTick {
    IItemCatalog Catalog { get; }
    float  GetEffectiveCharge(ItemInstanceId inst);                 // after drain
    bool   TryConsumeCharge(ItemInstanceId inst, float amount01);
    ItemCondition EvaluateCondition(ItemInstanceId inst);           // Pristine/Worn/Failing/Broken
    event EventHandler<ItemConditionChanged> ConditionChanged;
    event EventHandler<ItemCorroded> Corroded;                      // battery contacts, the Act 1 stake
}
```

```csharp
public readonly struct ItemDefinition {
    public readonly ItemId Id;  public readonly LocKey NameKey, DescKey;
    public readonly int MaxStack; public readonly float Grams;
    public readonly ItemFlags Flags;      // Durable, Charged, Critical, Corrodible, WaterSensitive
    public readonly ItemTag Tags;         // [Flags] uint — CuttingEdge, Cordage, Vessel, HeatSource, Electrical, Acoustic...
    public readonly float DurabilityPerUse;   // 0..1
    public readonly float ChargeDrainPerHour; // 0..1 per in-game hour while active
    public readonly float CorrosionHoursWet;  // hours of WetnessSeconds>0 before Corroded fires
    public readonly ItemId BreaksInto;        // ItemId.None or salvage
}
```

### 2.2 Owned state
* `_instanceState : Dictionary<ItemInstanceId, RuntimeItemState>` where `RuntimeItemState { float ActiveHours; float WetHours; bool IsActive; }` — **only for instances that need ticking** (charged, corrodible, or water-sensitive). A brass tag never enters this dictionary. Typical live count: under 20 entries.
* The baked catalog arrays (immutable after load).

**Overlap resolution with InventorySystem.** Inventory owns `Durability01 / Charge01 / WetnessSeconds` *as stored values on the slot*. ItemSystem owns the *rate of change* and is the only system permitted to call `TrySetDurability` / charge writes. Rule: **Inventory is the store, ItemSystem is the mutator.** Any other system wanting to damage an item calls `IItemSystem`, never `IInventorySystem.TrySetDurability`. Enforced by making `TrySetDurability` internal to the assembly and exposed via `InternalsVisibleTo` only for ItemSystem and tests.

### 2.3 Dependencies
`IInventorySystem` (direct — must enumerate live instances and write back state), `ITimeSystem` (query — `WorldDeltaHours`), `IWeatherSystem` (query — ambient humidity raises corrosion rate in Fernmaw/Sweatways). All downward. No cycle.

### 2.4 Key algorithm — degradation

Per domain tick (10 Hz, §19):
```
dh = time.WorldDeltaHours
foreach inst in _instanceState:
    if s.IsActive: charge -= def.ChargeDrainPerHour * dh
    humidityMul = 1 + 1.5 * weather.RelativeHumidity01(zone)   // 1.0 dry .. 2.5 saturated
    if slot.WetnessSeconds > 0:
        slot.WetnessSeconds = max(0, slot.WetnessSeconds - dh*3600 * dryRate(zone))
        s.WetHours += dh * humidityMul
        if def.Corrodible && s.WetHours >= def.CorrosionHoursWet: Publish(ItemCorroded)
```
The field recorder ships with `ChargeDrainPerHour = 0.11` while recording and `CorrosionHoursWet = 14`. That is the Act 1 clock: 14 in-game hours of wet before the contacts are gone, against a 36-hour water deadline — so the dry box must be built *before* the Catchment is finished, which is exactly the tension the bible asks for.

### 2.5 Failure modes
Dictionary growing unboundedly as the player picks up and drops thousands of items → entries are removed on `InventoryChanged` when a slot empties, and a dev assert fires if `_instanceState.Count > 64`. Second: designers setting `ChargeDrainPerHour` on a non-`Charged` item → editor validator rejects at bake.

---

## 3. CombinationSystem

**SINGLE RESPONSIBILITY.** Resolves a player-assembled multiset of held items into exactly one recipe outcome, or into a specific, legible failure reason.

Combination = "put these things together in the inspect view, right now." Crafting (§4) = "build a thing at a station over time from a known plan." They are separate because combination is *discovery-driven and instant*, crafting is *knowledge-driven and timed*, and conflating them produces a system that can neither surprise nor schedule.

### 3.1 Public API

```csharp
public interface ICombinationSystem {
    CombinationProbe  Probe(ReadOnlySpan<ItemInstanceId> inputs);   // no mutation — drives UI affordance
    CombinationResult TryCombine(ReadOnlySpan<ItemInstanceId> inputs);
    bool IsRecipeKnown(RecipeId id);
    ReadOnlySpan<RecipeId> RecipesProducing(ItemId output);         // for the Field Slate "what makes this"
    event EventHandler<CombinationSucceeded> Succeeded;
    event EventHandler<CombinationFailed>    Failed;
}

public readonly struct CombinationProbe {
    public readonly bool AnyRecipeMatchesShape;   // ingredients match a recipe's multiset
    public readonly bool Blocked;                 // matches, but gated
    public readonly CombinationFailReason Reason;
    public readonly LocKey HintKey;               // never the answer; a nudge
}
public enum CombinationFailReason : byte {
    None, NoSuchRecipe, MissingTool, ToolTooWorn, NotDiscovered,
    RequiresStation, WrongZone, InsufficientQuantity, OutputWouldNotFit
}
```

### 3.2 Owned state
* `HashSet<RecipeId> _known` — recipes the player has learned. (Contrast: `DiscoverySystem` owns *discoveries*; a discovery may unlock a recipe, but the set of known recipes is the CombinationSystem's business, populated by reacting to `DiscoveryUnlocked`. This is a deliberate one-way flow — see §3.4.)
* `Dictionary<ulong, RecipeId[]> _shapeIndex` — the match acceleration structure, rebuilt at load, not saved.

### 3.3 Key algorithms

**Recipe shape and order-independence.** A recipe declares inputs as a **multiset**, so order never matters (cordage+flake == flake+cordage). Matching is done by hashing the *canonical shape*: sort the input `ItemId`s ascending by `uint`, run-length encode into `(id, count)` pairs, FNV-1a the byte sequence. `_shapeIndex` maps that 64-bit hash → candidate recipes (usually one; a list handles genuine shape collisions where two recipes share ingredients but differ by gate).

Three **input roles**, which is the distinction most combination systems get wrong:

| Role | Consumed? | Matched by |
|---|---|---|
| `Consumable` | yes, `Quantity` units removed | exact `ItemId` |
| `Tool` | no — takes `DurabilityPerUse` damage instead | `ItemTag` (any `CuttingEdge` works), so a basalt flake and the multitool are interchangeable |
| `Catalyst` | no, untouched | `ItemId` or `ItemTag`; used for "must be holding the Field Slate" style gates |

So the Act 1 cordage recipe is authored as: `Consumable ×3 FernFibreBundle` + `Tool ItemTag.CuttingEdge (minDurability 0.15)` → `Output ×1 Cordage(1m)`. A fully-blunted flake produces `ToolTooWorn`, not `NoSuchRecipe` — the player is told the shape was right and the tool was not.

**Multi-output.** `RecipeDefinition.Outputs` is an array of `(ItemId, int Quantity, float Chance01)`. `Chance01 < 1` entries roll against the CombinationSystem's own `Pcg32` (salt `0x43_4F_4D_42`), and — critically — **the roll is made from a seed derived from `(WorldSeed, RecipeId, inputInstanceIds sorted)`**, so save-scumming an unlucky salvage yields the same result. That is a design decision, not just a determinism one: it means "reload for a better roll" is not a strategy, and we never have to balance around it. Deterministic salvage is the reason the tape-splice recipe can safely produce `1× SplicedReel + (0.4) 1× ScrapLeader`.

**The exact commit sequence** (this ordering is what prevents every dupe bug):
```
1. Resolve shape hash -> candidates
2. For each candidate in authored priority order:
     a. gate check  (Discovery, Station, Zone)  -> collect the *most specific* failure
     b. tool check  (tag present, durability >= min)
     c. quantity check
   First fully-passing candidate wins. If none pass, report the failure from the
   candidate that got FURTHEST through a–c (ranked NoSuchRecipe < NotDiscovered <
   RequiresStation < WrongZone < MissingTool < ToolTooWorn < InsufficientQuantity).
3. Dry-run the outputs against Inventory capacity -> OutputWouldNotFit aborts here, pre-mutation
4. Inventory.TryRemove all consumables  (atomic; any Insufficient aborts the whole thing)
5. ItemSystem.ApplyToolWear on each tool
6. Inventory.TryAdd outputs
7. Publish CombinationSucceeded { RecipeId, Outputs }  — StorySystem/QuestSystem listen here
```
Step 3 before step 4 is non-negotiable: without it, a full pack eats your ingredients.

**Gating on a Discovery.** `RecipeDefinition.RequiredDiscovery : DiscoveryId` (`None` = always available). At load, CombinationSystem populates `_known` with every recipe whose `RequiredDiscovery == None`, then queries `IDiscoverySystem.IsUnlocked` for the rest; thereafter it subscribes to `DiscoveryUnlocked` and adds incrementally. A gated recipe that the player attempts anyway reports `NotDiscovered` with a `HintKey` pointing at the *category* ("These fibres want twisting, but I don't know the pattern yet") — never the recipe. This is how the Tolo Vardh cordage pattern in Fernmaw becomes a thing you *learn from a wear pattern on a stone* rather than a thing you brute-force at hour one.

**Invalid combination reporting.** Never a generic "that doesn't work." The `CombinationFailed` event carries the reason enum, the `HintKey`, and `InputInstanceIds`. The UI plays one of eight distinct Nadia lines keyed off the reason — `MissingTool` gets "I need an edge for this"; `NoSuchRecipe` gets a flat, self-critical "No. That's me wanting it to work." Failure text is a characterisation opportunity and we spend it.

### 3.4 Dependencies
`IInventorySystem` (direct — remove/add), `IItemSystem` (direct — tool wear, tag lookup), `IDiscoverySystem` (**event only**, `DiscoveryUnlocked`), `IWorldSystem` (query — current `ZoneId` for `WrongZone`), `ICampSystem` (query — station availability).

**Cycle flag and break.** Combination → Discovery (needs gate state) and Discovery → Combination (combining a novel thing *is* a discovery) is a real cycle. **Break:** Combination never calls Discovery. It publishes `CombinationSucceeded`; DiscoverySystem subscribes and decides whether that constitutes a discovery. Combination's read of gate state is via a cached set updated by the `DiscoveryUnlocked` event, plus one query at load. Edge is: `Discovery --event--> Combination` only. Acyclic.

### 3.5 Determinism + testability
Fully pure given fake Inventory/Item/Discovery. Tests build a 4-recipe catalog and assert on `CombinationResult`, and specifically assert inventory is *unchanged* on every failure path.

### 3.6 Failure modes
Shape-hash collisions between genuinely different recipes → `_shapeIndex` values are arrays and step 2 disambiguates by gate; a bake-time validator warns when two recipes share a shape and neither has a gate (that is an authoring error). Second: a recipe whose output is also an input (infinite loop / free mass) → bake-time validator rejects any recipe where an `Output.ItemId` appears as a `Consumable`.

---

## 4. CraftingSystem

**SINGLE RESPONSIBILITY.** Executes known, station-bound, time-consuming builds — reserving inputs, advancing a build timer against world time, and committing or rolling back.

### 4.1 Public API

```csharp
public interface ICraftingSystem : IDomainTick {
    CraftFeasibility CanCraft(RecipeId id, int count = 1);
    bool  TryBeginCraft(RecipeId id, int count, out CraftJobHandle handle);
    bool  TryCancelCraft(CraftJobHandle handle);          // full refund of reserved inputs
    float GetProgress01(CraftJobHandle handle);
    ReadOnlySpan<CraftJobHandle> ActiveJobs { get; }
    event EventHandler<CraftStarted>   Started;
    event EventHandler<CraftCompleted> Completed;
    event EventHandler<CraftFailed>    Failed;            // station lost, inputs vanished
}
```

### 4.2 Owned state
* `CraftJob[] _jobs` (max 4 concurrent) — `{ RecipeId, int Count, double WorldSecondsRemaining, ReservationToken Token, StationId Station }`.
* `Reservation[] _reservations` — the withheld inputs. **This is the only place reserved-but-not-yet-consumed items exist.** Inventory sees them as already removed; Crafting returns them on cancel or failure.

### 4.3 Key algorithm — two-phase commit
`TryBeginCraft` immediately `TryRemove`s all consumables into a reservation (so the player cannot craft twice off one stock, and cannot see phantom items). Build time is in **world seconds**, so crafting advances at the same rate as the clock and is affected by the `TimeSystem` scale — a 40-minute build on the Catchment costs 40 minutes of in-world time, which matters against the 36-hour water deadline. Jobs tick only while the player is within `StationRadius = 6 m` of the station **or** the station is `Unattended = true` (the Catchment drips whether or not you are watching; the Reel Deck splice does not). On completion, outputs go to Inventory; if they do not fit, the job enters `AwaitingCollection` and the station holds the output physically — it is never destroyed.

### 4.4 Dependencies
`IInventorySystem`, `IItemSystem`, `ICombinationSystem` (shares the `RecipeDefinition` catalog and the `_known` set — Crafting **queries** `IsRecipeKnown`, never mutates it), `ITimeSystem` (world delta), `ICampSystem` (station presence + radius). All downward except Combination which is a sibling at L2 — resolved by extracting `IRecipeCatalog` into L1 (Foundation) so both depend on data, not each other. **Crafting → Combination becomes Crafting → RecipeCatalog. Acyclic.**

### 4.5 Failure modes
Job surviving a station's destruction → `CampSystem` publishes `StationRemoved`; Crafting fails the job and refunds. Crash mid-craft → reservations are serialized with the job; load restores both or neither (single save record).

---

## 5. InteractionSystem

**SINGLE RESPONSIBILITY.** Turns the player's touch input into exactly one resolved interaction against exactly one world interactable, and arbitrates which interactable is the candidate.

### 5.1 Public API

```csharp
public interface IInteractionSystem : IDomainTick {
    InteractionCandidate Current { get; }        // what the reticle/hotspot is on
    bool TryInteract(InteractionVerb verb);      // Look, Take, Use, UseWith, Listen, Combine
    bool TryEquip(ItemInstanceId instance);      // lease from Belt only
    ItemInstanceId Equipped { get; }
    void RegisterInteractable(in InteractableDesc desc);
    void UnregisterInteractable(InteractableId id);
    event EventHandler<InteractionPerformed> Performed;
    event EventHandler<InteractionRefused>   Refused;     // carries LocKey for Nadia's line
}
public enum InteractionVerb : byte { Look, Take, Use, UseWith, Listen, Combine }
```

### 5.2 Owned state
`InteractableDesc[] _registered` (spatially bucketed into a 16 m uniform grid), `InteractionCandidate _current`, `ItemInstanceId _equipped` (the lease, §1.6), `float _holdSeconds` (for hold-to-take).

### 5.3 Key algorithm — candidate arbitration on a portrait phone
Portrait touch with a 400 px-wide screen means hotspots overlap constantly. Scoring, evaluated at 10 Hz not per-frame:
```
score = 0.55 * (1 - clamp01(screenDistPx / 180))      // proximity to touch point / screen centre
      + 0.25 * (1 - clamp01(worldDist / 4.0))          // closer is better, 4 m cutoff
      + 0.15 * priorityWeight                          // authored; story-critical outranks scenery
      + 0.05 * (wasCurrentLastTick ? 1 : 0)            // hysteresis: stops flicker between two hulls
candidate = argmax score, requires score >= 0.30 and an unobstructed raycast (1 ray, LayerMask Interactable|Occluder)
```
The 0.05 hysteresis term is load-bearing on mobile — without it the candidate flickers when the player's thumb rests between two objects, and every playtest reports "it never picks the one I want."

`UseWith` is the bridge to CombinationSystem: dragging an inventory item onto a world interactable calls `TryInteract(UseWith)` with `Equipped` + target, which forwards to `ICombinationSystem` if the target is an item-like interactable, or to `IPuzzleSystem.SubmitInput` if the target is a puzzle element.

### 5.4 Dependencies
`IInventorySystem` (lease validation), `ICombinationSystem`, `IPuzzleSystem`, `IWorldSystem` (occlusion + zone). All downward from L3. Publishes upward.

### 5.5 Failure modes
An interactable destroyed while it is `_current` → `UnregisterInteractable` clears `_current` and publishes `InteractionRefused(TargetGone)` rather than dereferencing. Doubled input from a touch that lands on both UI and world → InteractionSystem consumes input only when `IUiRouter.WorldInputAllowed` is true; the UI layer owns that flag.

---

## 6. DiscoverySystem

**SINGLE RESPONSIBILITY.** Owns the single, append-only set of things Nadia now knows, and fans each new entry out into the concrete unlocks it grants.

This is the **Field Slate's spine** and the most narratively important system in the document. A discovery is not a collectible; it is a *state change in what the player is permitted to do and understand*.

### 6.1 Public API

```csharp
public interface IDiscoverySystem : IDomainTick {
    bool IsUnlocked(DiscoveryId id);
    DiscoveryResult TryUnlock(DiscoveryId id, DiscoverySource source);
    bool TryRevise(DiscoveryId id, DiscoveryId revisedTo);        // the theme, as an API
    ReadOnlySpan<DiscoveryId> UnlockedInCategory(DiscoveryCategory c);
    int  PendingNotificationCount { get; }
    bool TryDequeueNotification(out DiscoveryNotification n);
    event EventHandler<DiscoveryUnlocked> Unlocked;
    event EventHandler<DiscoveryRevised>  Revised;
}
public enum DiscoverySource : byte { Observation, Document, Audio, Combination, PuzzleSolved, Dialogue, Hydrophone }
public enum DiscoveryCategory : byte { Technique, Location, Person, Mechanism, Chronology, Personal }
```

### 6.2 Owned state
* `BitArray512 _unlocked` (fixed-capacity bitset, indexed by a bake-time dense index — the `DiscoveryId`→index map is baked, so the save blob is 64 bytes).
* `Dictionary<DiscoveryId, DiscoveryId> _revisions` — old → new, so the Field Slate can render a struck-through earlier conclusion above the corrected one. **The old entry is never deleted** (Rule 7 of the fiction contract, enforced in code: `TryRevise` has no path that clears a bit).
* `RingBuffer<DiscoveryNotification> _queue` (capacity 16).
* `uint[] _unlockTick` — when each was learned, for chronological Slate ordering.

### 6.3 What a discovery unlocks
`DiscoveryDefinition` carries five unlock arrays, applied in this order on unlock:

| Field | Effect | Consumer |
|---|---|---|
| `RecipeId[] UnlocksRecipes` | adds to `_known` | CombinationSystem (via event) |
| `AbilityFlag UnlocksAbilities` | e.g. `ReadRegister`, `AimHydrophone`, `OperateDamperConsole` | InteractionSystem gates verbs on these |
| `ZoneId[] RevealsLocations` | map markers, fast-travel eligibility | WorldSystem |
| `InteractableId[] EnablesInteractions` | the steel ladder becomes climbable once you know it is bolted through carvings and where it leads | InteractionSystem |
| `NodeId[] AdvancesStory` | story graph nodes become eligible | StorySystem |

So "the gully is cut stone at a constant 1.5°" is a `Mechanism` discovery that unlocks nothing mechanical at all — it unlocks a story node and a Slate entry. And "the notch-and-crescent is a title, not a name" (`Chronology`) unlocks `AbilityFlag.ReadRegister`, which is what makes the Act 4 Combs puzzles legible. Abilities are unlocked by *understanding*, never by pickup. That is the whole game's progression philosophy in one table.

### 6.4 Key algorithms

**Dedup.** `TryUnlock` on an already-set bit returns `DiscoveryResult.AlreadyKnown` and **enqueues nothing** — no notification, no event, no audio sting. This matters: the player will point the hydrophone at the same Sweatways baffle twenty times and must not be congratulated twenty times. Sources are recorded only on first unlock.

**Revision.** `TryRevise(old, new)` requires `IsUnlocked(old) && !IsUnlocked(new)`, sets `new`, records the mapping, and fires `Revised` with both ids. The Slate UI renders the old text struck through. Act 2's "the mast is down because a storm took it" → Act 3's "the mast was *stripped*, deliberately, by someone still here" is exactly this call. Revisions are chainable (`a→b→c`) and the Slate shows the full chain; a cycle (`c→a`) is rejected at bake time by a graph validator.

**Notification queue — the anti-spam design.** Discoveries frequently arrive in bursts (reading a journal page can unlock four). The ring buffer is drained by the UI at **most one notification per 2.5 s**, and consecutive notifications in the same `DiscoveryCategory` within a 6 s window are **coalesced** into one card reading "4 new entries — Chronology." Additionally, notifications are **suppressed entirely** while `IAudioSystem.IsNarrativeAudioPlaying` — we never pop a toast over a Sabo reel. Suppressed ones stay queued and drain after. If the queue overflows 16, the oldest non-`Critical` entry is dropped from the *notification* (the discovery itself is never dropped).

### 6.5 Dependencies
`ITimeSystem` (timestamps), `IAudioSystem` (query — narrative-audio suppression flag). Subscribes to `CombinationSucceeded`, `PuzzleSolved`, `DocumentRead`, `HydrophoneResolved`. Note it *subscribes* rather than *references* those systems — Discovery sits at L3 and receives events from L2. **Acyclic.**

### 6.6 Determinism + testability
Pure. A test unlocks a discovery twice and asserts one event and one queue entry.

### 6.7 Failure modes
A content patch reordering the bake index would corrupt saves → the save stores `(DiscoveryId hash, bool)` pairs for any id not found in the current bake index, and the bitset only for known ids; a bake-time hash-stability test compares against a checked-in golden id table. Second: a discovery that unlocks a recipe whose ingredients do not exist yet in the act → editor validator cross-checks against the zone-availability table and warns.

---

## 7. SurvivalSystem

**SINGLE RESPONSIBILITY.** Integrates Nadia's five body stats against world time and environment, and publishes threshold crossings — without ever deciding what the game does about them.

The bible is explicit: *survival is the first hour, not the game*, and the tone is *not a hardcore survival sim*. Every number below is chosen so that a competent player stops thinking about meters by hour three, and a careless one is inconvenienced, never killed without warning.

### 7.1 Public API

```csharp
public interface ISurvivalSystem : IDomainTick {
    float Get(SurvivalStat stat);                        // 0..100 for all five
    SurvivalBand GetBand(SurvivalStat stat);             // Fine, Low, Critical, Failing
    void Apply(SurvivalStat stat, float delta, SurvivalSource source);   // eating, drinking, resting
    void SetExertion(ExertionLevel level);               // Idle/Walk/Climb/Haul — set by locomotion
    SurvivalModifiers CurrentModifiers { get; }          // move speed, hand steadiness, camera effects
    event EventHandler<SurvivalBandChanged> BandChanged;
    event EventHandler<SurvivalCollapse>    Collapse;    // the failure state — NOT death
}
public enum SurvivalStat : byte { Health, Energy, Hydration, Satiation, CoreTemp }
public enum SurvivalBand : byte { Fine, Low, Critical, Failing }
```

### 7.2 Owned state
`float[5] _stats`, `ExertionLevel _exertion`, `float _collapseDebtHours`, `SurvivalBand[5] _lastBands`, `float _accumulatorHours` (for sub-tick accumulation). Nothing else. It does not own the clock (TimeSystem), the temperature of the air (WeatherSystem) or the act of eating (InteractionSystem calls `Apply`).

### 7.3 The tick formulas — shipping first-pass numbers

All stats are `0..100`. `Δh` = in-game hours elapsed this tick. Integration is **explicit Euler at 10 Hz against world time**, which at the shipping time scale (§13: 1 real second = 30 world seconds) means Δh ≈ 0.000833 per tick — small enough that Euler error is irrelevant and we avoid the complexity of anything else.

**Exertion multiplier** `E`:

| Exertion | E | When |
|---|---|---|
| Idle | 0.6 | standing, reading, listening |
| Walk | 1.0 | default traversal |
| Climb | 1.9 | Rime Shoulder ropes, Combs ladder |
| Haul | 1.5 | carrying > 8 kg (InventorySystem grams) |

**Hydration** (the Act 1 stake):
```
dHydration/dh = -(2.9 * E + 1.6 * heatFactor)
heatFactor = clamp01((ambientC - 22) / 14)     // 0 at 22 °C, 1 at 36 °C — Ash Throat sits at 0.9
```
At Idle in Fernmaw (26 °C, heatFactor 0.29): −2.2/hr → **45 hours** from full to zero. At Walk: −3.4/hr → 29 hours. **Design intent: a player who walks continuously hits Hydration `Low` (30) at ~20 hours and `Critical` (12) at ~26 hours, comfortably inside the bible's 36-hour water deadline but not trivially so.** Drinking a 250 ml pouch: `+18`. The Catchment's one litre per ~9 hours of operation is therefore `+72`, i.e. roughly a day's walking — which is exactly the "you are now stable, the game can start" moment Act 1 needs.

**Satiation** (deliberately slow — hunger is texture, not threat):
```
dSatiation/dh = -(0.85 * E + coldHungerCoupling)
coldHungerCoupling = 0.9 * clamp01((36.6 - coreTempC) / 2.2)
```
Cold raises hunger drain by up to +0.9/hr — the explicit stat-interaction the brief asks for, and the reason Rime Shoulder wants you fed before you climb. At Walk, warm: −0.85/hr → **117 hours** from full. Satiation reaching 0 does not kill; it caps `Energy` regeneration at 40 and applies a −8% move-speed modifier. Shellfish from the Ribcage: `+22`.

**CoreTemp** — stored as °C, 30.0..40.0, displayed as a band not a number:
```
dCoreTemp/dh = (ambientEffectiveC - coreTempC) * k
k = 0.0187 (base) ; ×2.1 if Wetness01 > 0.5 ; ×0.55 if sheltered ; ×0.35 if near a HeatSource
// CORRECTED v1.1: was 0.34, which is an exponential rate that reaches hypothermia in ~3 MINUTES,
// not the ~55 minutes this section's prose claims. k = 0.0187 satisfies the stated design intent.
// Derivation: c(t) = amb + (c0-amb)*e^(-k*t). For 36.6 -> 36.0 at amb 1.2 in 0.9167 h:
//   k = ln(35.4/34.8) / 0.9167 = 0.0187 /h  ->  36.0 at 54.8 min, 35.0 at 2.47 h. Verified numerically.
ambientEffectiveC = ambientC - windChillC(weather) + 3.2 * E     // exertion warms you
```
Rime Shoulder ambient is 4 °C with up to −6 °C wind chill. Dry and walking: `ambientEffective = 4 - 6 + 3.2 = 1.2`, so core decays exponentially toward 1.2 °C at k = 0.0187/hr — from 36.6 it takes ~**55 minutes** to reach 36.0 (`Low`) and ~**2.5 hours** to reach 35.0 (`Critical`). Wet (k × 2.1 = 0.0393), it is 2.1× faster: ~**26 minutes** to `Low`.

> **Balance test (required, CI-enforced):** assert these three durations to ±10%. A designer who changes `k` without changing the prose breaks the build. This formula shipped wrong once already — see the correction note above. That is a real reason to dry off before climbing and a real reason to build the fire, expressed in minutes the player can feel, not in an opaque meter.

**Energy**:
```
dEnergy/dh = -(3.1 * E) + regen
regen = +11.0/hr while Resting, +3.0/hr while Idle and Satiation>40 and Hydration>40, else 0
```
Continuous walking: −3.1/hr → 32 hours awake, which is right for a woman who is not going to sleep the first night. Resting to full from 20 takes ~7.3 in-game hours = **~15 real minutes at 30× scale**, and we do not make the player sit through it — `CampSystem.Rest` fast-forwards the clock (§12).

**Health** — the only stat that is not directly drained by time:
```
dHealth/dh = -sum(penalties) + 1.4 (if all four other stats are in Fine)
penalties:  Hydration<12   -> 4.5/hr
            Satiation==0   -> 1.1/hr
            CoreTemp<35.0  -> 6.0/hr
            CoreTemp>38.5  -> 3.5/hr
            UntreatedWound -> 2.0/hr  (falls; see §7.5)
```
Health regenerates at +1.4/hr only when everything else is green. Worst realistic case (critical thirst + hypothermia) is −10.5/hr: **9.5 hours of total neglect from full**, with escalating warnings throughout. You cannot die in a surprise.

**Bands.** `Fine ≥ 55`, `Low 30–54`, `Critical 12–29`, `Failing < 12` (CoreTemp uses °C thresholds: Fine 36.2–37.6, Low 35.0/37.6+, Critical 34.0/38.5+, Failing <34/>39.5).

### 7.4 The failure state — collapse, not death
When `Health ≤ 0`, `SurvivalSystem` publishes `Collapse` and **clamps Health at 1**. It never kills. `CampSystem`/`WorldSystem` handle collapse by fading out, advancing the clock **4–7 in-game hours**, and waking Nadia at the nearest safe shelter with Health 35, Hydration/Satiation +25, and — the cost — **one randomly selected non-Critical inventory stack dropped at the collapse site**, recoverable, plus a Field Slate entry in her own voice about the gap in the record. Permadeath is wrong for this game: it punishes the exploration the game is made of, and a lost hour of record is thematically the *worst* thing that can happen to Nadia. There is no Game Over screen in this product.

### 7.5 Design guardrails (these are requirements, not suggestions)
1. **No meter ever drains while a document, reel, dialogue or cinematic is on screen.** `SurvivalSystem.SetPaused(true)` is called by StorySystem for the duration. Reading a Poulter journal must never cost you water.
2. **No stat drains in Act 5.** The crafting HUD is stripped per the bible; so is survival. `SetPaused(true)` for the whole act, permanently.
3. **Thirst-to-collapse from full is never under 30 in-game hours** at any exertion. Assert in a balance test.
4. **The player is warned three times before any penalty**: band change to `Low` (Slate line), `Critical` (Slate line + desaturation vignette), `Failing` (audible breath + a direct "I need water. Now." line).
5. **Meters are hidden by default.** The HUD shows a stat only when it is `Low` or worse, and hides it again 20 s after it recovers. A player in good shape sees no survival UI at all, which is what "not a hardcore survival sim" means in practice.
6. **No food spoilage, no carrying-capacity-driven starvation, no sleep requirement.** Energy is a soft gate on Climb, nothing more.
7. **Fall damage is a flat authored value per fall height band** (≤3 m: none; 3–5 m: −12 Health + `UntreatedWound`; >5 m: blocked by level design, no fatal falls exist).

### 7.6 Dependencies
`ITimeSystem` (Δh), `IWeatherSystem` (ambientC, wind, precipitation), `IWorldSystem` (shelter flag, zone base temperature), `IInventorySystem` (carried grams → Haul exertion; item wetness → wet multiplier). All L1/L2 reads, all downward. Publishes upward only.

### 7.7 Testability
Entirely pure: `new SurvivalSystem(fakeTime, fakeWeather, fakeWorld, fakeInv)`, then `Advance(hours: 30)` in a loop and assert on band transitions. Balance assertions live in the same test assembly and run in CI — a designer who changes `2.9` to `9.2` breaks the build, which is the point.

### 7.8 Failure modes
Δh spikes after an app resume on mobile (backgrounded for 40 minutes) → `TimeSystem` clamps a single tick's Δh to 0.05 h and the resume path is handled explicitly by `CampSystem` (§12), not by draining the player to death in one frame. Second: pause-reference leak — `SetPaused` is refcounted (`PushPause`/`PopPause` with a token), so two overlapping pausers cannot unpause each other.

---

## 8. CampSystem

**SINGLE RESPONSIBILITY.** Owns the player-established and world-authored safe sites — their stations, fire state, stored cache and rest affordance.

### 8.1 Public API

```csharp
public interface ICampSystem : IDomainTick {
    bool TryEstablishCamp(ZoneId zone, Vector3 position, out CampId id);
    bool TryAddStation(CampId camp, StationType type);          // consumes inputs via Inventory
    CampState GetState(CampId id);
    bool TryRest(CampId id, float hours, out RestOutcome outcome);
    bool TryStokeFire(CampId id, ItemId fuel);
    ReadOnlySpan<StationType> AvailableStationsNear(Vector3 p, float radius = 6f);
    event EventHandler<CampEstablished> Established;
    event EventHandler<FireStateChanged> FireChanged;
    event EventHandler<StationRemoved>   StationRemoved;
}
public enum StationType : byte { Fire, DryingRack, Catchment, Workbench, ReelDeck, ChargingBench }
```

### 8.2 Owned state
`Camp[] _camps` (max 6 — four authored, two player-placed), each `{ ZoneId, Vector3, StationFlags, float FireFuelHours, float ShelterQuality01, bool CacheUnlocked }`. It does **not** own the cache contents (Inventory, `ContainerId.Cache`) and does not own rest's clock effect (TimeSystem).

### 8.3 Key algorithm — rest
```
TryRest(camp, hours):
  requires FireFuelHours > 0 OR ShelterQuality01 >= 0.5   // else refuse with reason
  hours = clamp(hours, 1, 10)
  TimeSystem.FastForward(hours)       // emits hourly sub-steps so Survival integrates properly
  Survival.Apply(Energy, +11*hours)
  Survival.Apply(CoreTemp -> toward 36.6 at the sheltered rate)
  ItemSystem.DryAll(inventory, hours * dryRateNear(camp))   // the drying rack: the fix for a wet recorder
```
`FastForward` is **not** a clock jump — it runs the domain tick loop at an accelerated step so weather transitions, crafting jobs and survival drains all occur. This is why a player cannot rest away a storm's consequences or skip a craft's cost.

Fire consumes `FireFuelHours` at 1.0/hr and modifies `SurvivalSystem`'s k-multiplier within 4 m. Fire in rain without a shelter goes out: `FireFuelHours -= 3.0/hr` under `Precipitation > 0.5` and `ShelterQuality01 < 0.4`.

### 8.4 Dependencies
`IInventorySystem`, `IItemSystem`, `ITimeSystem`, `ISurvivalSystem`, `IWeatherSystem`, `IWorldSystem`. Downward only; L3.

### 8.5 Failure modes
Player establishes a camp inside geometry or on a slope → `TryEstablishCamp` runs a 3-ray ground check with max 12° slope and a 1.2 m clearance capsule, refusing with a reason. App suspend during a `FastForward` → fast-forward is resumable (it stores `_ffRemainingHours` and continues on the next tick), and a suspend mid-rest resumes correctly on load.

---

## 9. PuzzleSystem

**SINGLE RESPONSIBILITY.** Runs the state machine for every authored puzzle — validating submitted inputs against declared requirements, persisting partial progress, and escalating hints.

### 9.1 Public API

```csharp
public interface IPuzzleSystem : IDomainTick {
    PuzzleState GetState(PuzzleId id);
    SubmitResult SubmitInput(PuzzleId id, in PuzzleInput input);
    bool TryReset(PuzzleId id);                            // returns to Available, refunds consumed
    float GetProgress01(PuzzleId id);
    HintLevel CurrentHintLevel(PuzzleId id);
    bool TryRequestHint(PuzzleId id, out LocKey hintKey);
    event EventHandler<PuzzleStateChanged> StateChanged;
    event EventHandler<PuzzleSolved>       Solved;
}
public enum PuzzleState : byte { Locked, Available, InProgress, Solved, Failed }
```

### 9.2 The state machine

```
        requirements met
Locked ─────────────────► Available ──first valid input──► InProgress
   ▲                          ▲  │                            │ │
   │ requirement revoked      │  │ TryReset                   │ │ all steps satisfied
   │ (rare; Sump reflooding)  │  └────────────────────────────┘ ▼
   └──────────────────────────┴─────────────────────────── Solved (terminal)
                                        │
                      soft-fail (damper over-tension, tape snapped)
                                        ▼
                                     Failed ──auto after 3 s──► Available (progress reset to last checkpoint)
```
`Failed` is always transient and always returns to `Available`. **No puzzle in this game is permanently failable** — Rule: a puzzle that can be made unsolvable is a bug, and the bake-time validator rejects any `PuzzleDefinition` that consumes a non-renewable item.

### 9.3 How a puzzle declares requirements (authoring without code)

`PuzzleDefinition` is a `ScriptableObject` with four lists, all data:

```csharp
[CreateAssetMenu] public sealed class PuzzleDefinition : ScriptableObject {
    public string Key;                                   // -> PuzzleId
    public PuzzleRequirement[] Unlock;                   // ALL must hold to leave Locked
    public PuzzleStep[] Steps;                           // ordered or unordered (flag)
    public bool StepsOrdered;
    public PuzzleOutcome[] OnSolved;                     // discoveries, story nodes, world changes
    public HintLadder Hints;                             // 4 rungs
}
[Serializable] public struct PuzzleRequirement {
    public RequirementKind Kind;    // HasDiscovery, HasItem, HasAbility, PuzzleSolved, TimeOfDay, WeatherIs, TideBand
    public uint IdA; public float ValueA; public float ValueB;
}
[Serializable] public struct PuzzleStep {
    public StepKind Kind;           // PlaceItem, SetDial, AimHydrophoneAt, MatchFrequency, SequencePress, HoldValue
    public uint TargetId;
    public float Target; public float Tolerance;     // e.g. MatchFrequency target 241.0, tol 3.5
    public float HoldSeconds;                        // HoldValue: keep within tolerance this long
}
```

A concrete shipping example — **the Sweatways baffle puzzle (Act 2)**: `Unlock = [HasAbility(AimHydrophone)]`; `Steps = [AimHydrophoneAt(baffle_03), MatchFrequency(target 241.0 Hz, tol 3.5, hold 2.5 s), SetDial(drip_comb_valve, target 0.62, tol 0.05)]`, `StepsOrdered = true`. A designer authors that in the inspector. **Zero code.** New `StepKind` values are the only thing requiring engineering, and there are eleven of them total for the whole game.

### 9.4 Partial progress + persistence
Each puzzle's runtime state is `{ PuzzleState State; ulong StepBitmask; float[] StepValues; byte HintLevel; uint LastInputTick; }`. `StepValues` is only allocated for puzzles with `HoldValue`/`SetDial` steps (continuous state, e.g. the four damper gates' ballast positions). The whole set serializes as a flat array keyed by `PuzzleId` — **saving mid-puzzle restores dial positions exactly**, which is mandatory on mobile where the player is interrupted constantly.

Checkpointing: for `StepsOrdered` puzzles, a step marked `IsCheckpoint` means a `Failed` transition rewinds to that step, not to zero. The Act 4 damper console has a checkpoint after each gate, so failing gate four does not cost you gates one through three. Without this, the console is the puzzle that makes people quit.

### 9.5 The hint ladder — four rungs, time-gated and never automatic
| Rung | Unlocks after | Content | Example (Sweatways baffle) |
|---|---|---|---|
| 0 Ambient | immediate | environmental art/audio only, no text | the baffle drips louder on the beat |
| 1 Observation | 90 s in `InProgress` with no valid input | Nadia states what she notices | "The pressure shifts on a cycle. Eleven minutes, near enough." |
| 2 Direction | 240 s, **and** the player requested a hint | names the tool and the target, not the value | "The hydrophone will tell me where the tube narrows. Point it at the baffle." |
| 3 Method | 420 s **and** a second explicit request | names the procedure; still not the number | "Match the tone to the standing wave, then hold it while I open the comb." |

**Rungs 2 and 3 are never delivered without an explicit player request.** Rung 1 is automatic because it is in-character observation rather than help. There is no rung that solves the puzzle — a fifth "just do it for me" rung would break the only thing this game sells, which is the feeling of having worked it out. Hint timers pause when the puzzle is not on screen and when the app is backgrounded.

### 9.6 Dependencies
`IDiscoverySystem` (query, requirements), `IInventorySystem` (query + consume), `ITimeSystem` (TimeOfDay/tide requirements, hint timers), `IWeatherSystem` (WeatherIs requirements), `IAudioSystem` (query `GetSpectralPeakHz` for `MatchFrequency` — the hydrophone puzzles read *actual live audio analysis*, which is why this game's puzzles feel physical). Publishes `PuzzleSolved`; DiscoverySystem and QuestSystem listen. **Puzzle never calls Discovery's mutators. Acyclic.**

### 9.7 Determinism + testability
Pure given a fake audio analyser. A test drives `SubmitInput` sequences and asserts state transitions and the exact `StepBitmask`. The `MatchFrequency` step is testable because `IAudioSystem.GetSpectralPeakHz` is an interface — tests feed 241.2 Hz directly.

### 9.8 Failure modes
A puzzle whose `Unlock` requirements reference a discovery only granted by that same puzzle → bake-time cycle validator over the requirement graph rejects it. Second: float drift in `HoldValue` across a save/load boundary → `StepValues` are quantised to 1/1024 on save, and tolerances are never tighter than 1/256.

---

## 10. StorySystem

**SINGLE RESPONSIBILITY.** Owns the act/beat graph and decides which narrative content is eligible to play right now, without playing it.

### 10.1 Public API

```csharp
public interface IStorySystem : IDomainTick {
    ActId CurrentAct { get; }
    bool  IsNodeComplete(NodeId id);
    bool  TryAdvance(NodeId id, AdvanceReason reason);
    ReadOnlySpan<NodeId> EligibleNodes { get; }          // gates satisfied, not yet played
    bool  TryPlayNode(NodeId id, out StoryPlayback playback);  // hands a manifest to the presentation layer
    void  SetEnding(EndingChoice choice);                // Record | Relief — Act 5 only
    event EventHandler<ActChanged>      ActChanged;
    event EventHandler<StoryNodePlayed> NodePlayed;
    event EventHandler<DocumentRead>    DocumentRead;    // Discovery listens
}
```

### 10.2 Owned state
`ActId _act`, `BitArray256 _completedNodes`, `EndingChoice _ending`, `NodeId _activePlayback`, `int _lorvikAvoidanceStage` (0–4; the Act 5 approach is staged, per the bible's "avoiding Nadia for eleven days").

### 10.3 Key algorithm — eligibility, not scripting
Story nodes are a DAG. A node is eligible iff every `NodeGate` passes: `RequiresDiscovery`, `RequiresPuzzleSolved`, `RequiresAct`, `RequiresZoneVisited`, `RequiresNodeComplete`, `MinWorldHours`. Eligibility is recomputed **only on event** (discovery unlocked, puzzle solved, zone entered) and cached — never per tick — because with ~180 nodes and 5 gates each a per-frame sweep is 900 comparisons of pure waste on a phone.

Act advance is not a designer-authored trigger; it is a **quorum**: `Act N → N+1` when all nodes flagged `ActCritical` for act N are complete. This means the act structure cannot desync from content.

**Act 5's special handling.** `SetAct(Act5)` publishes `ActChanged`, and CampSystem, CraftingSystem, CombinationSystem and SurvivalSystem all subscribe and disable themselves. The bible says "protect this in design review" — we protect it in code: there is a CI test named `Story_Act5_StripsAllSurvivalAndCraftingVerbs` that fails the build if any of those four systems report themselves enabled in Act 5.

### 10.4 Dependencies
`IDiscoverySystem`, `IPuzzleSystem`, `IWorldSystem`, `ITimeSystem` — all **query-only** interfaces. Subscribes to their events for cache invalidation. L4 in the dependency layering; nothing below it references it. Acyclic by construction.

### 10.5 Failure modes
A node whose gates can never all be satisfied → bake-time reachability analysis from the start node proves every `ActCritical` node is reachable; build fails otherwise. This is the single most valuable validator in the project — it is what prevents the unwinnable-save class of bug entirely.

---

## 11. QuestSystem

**SINGLE RESPONSIBILITY.** Tracks the player's current objectives and their completion, translating story eligibility into a single readable "what am I doing" line.

Deliberately thin. This game does not have a quest log with twelve side missions; it has **one active objective and up to three background threads**, matching the bible's per-act goals.

### 11.1 Public API

```csharp
public interface IQuestSystem : IDomainTick {
    QuestId  Active { get; }
    ObjectiveView GetObjective(QuestId id);        // localized line + optional world marker
    ReadOnlySpan<QuestId> Background { get; }      // max 3
    bool TrySetActive(QuestId id);                 // player choice from the Slate
    QuestStatus GetStatus(QuestId id);
    event EventHandler<QuestStateChanged> Changed;
    event EventHandler<ObjectiveCompleted> ObjectiveCompleted;
}
```

### 11.2 Owned state
`QuestRuntime[] _quests` — `{ QuestStatus Status; byte StepIndex; uint StartedTick; }`. `QuestId _active`.

### 11.3 Key algorithm
Objectives complete on declarative conditions identical in shape to `PuzzleRequirement` (`HasDiscovery`, `PuzzleSolved`, `ZoneVisited`, `HasItem`, `NodeComplete`) evaluated on event, never polled. Auto-activation: when the active quest completes, the highest-priority `Available` quest becomes active, so the player is never objective-less. Markers are **zone-level, never a waypoint arrow** — "Rime Shoulder" not a floating diamond 340 m away; the game is about reading the island.

### 11.4 Dependencies
`IStorySystem`, `IDiscoverySystem`, `IPuzzleSystem`, `IWorldSystem` — all query + event. L4, alongside Story. Quest reads Story; Story does not read Quest. Acyclic.

### 11.5 Failure modes
Two quests both auto-activating on the same event → activation is a single pass over a priority-sorted array taking the first match, so it is deterministic by construction. Objective text referencing content the player has not seen (spoilers in the objective line) → a loc-key review pass plus a validator that flags any objective string containing a proper noun gated behind a later act.

---

## 12. WorldSystem

**SINGLE RESPONSIBILITY.** Owns the streaming, zone occupancy and persistent world-object state of Vardholm's nine zones.

### 12.1 Public API

```csharp
public interface IWorldSystem : IDomainTick {
    ZoneId CurrentZone { get; }
    bool   IsZoneVisited(ZoneId z);
    Vector3 PlayerPosition { get; }
    bool   TryQueryEnvironment(Vector3 p, out EnvironmentSample s);  // baseTempC, sheltered, indoor, tideDepth
    bool   TryGetWorldFlag(WorldFlagId f);
    void   SetWorldFlag(WorldFlagId f, bool value);                  // "ladder extended", "sump drained"
    ZoneLoadState GetLoadState(ZoneId z);
    event EventHandler<ZoneChanged>     ZoneChanged;
    event EventHandler<WorldFlagChanged> FlagChanged;
}
```

### 12.2 Owned state
`ZoneId _current`, `BitArray64 _visited`, `BitArray256 _worldFlags`, `Dictionary<InteractableId, ObjectPersistentState> _objects` (opened/taken/moved — the reason a drawer you opened is still open after a reload), `ZoneLoadState[9]`.

**Overlap resolution:** WorldSystem owns *world-object* state; InventorySystem owns *carried* state. The moment an item is taken, ownership transfers: the world object is marked `Taken` (a bit) and the item instance is created in Inventory. There is never a frame in which both own it, because `TryTake` does both mutations in one call and rolls back the world flag if `TryAdd` fails.

### 12.3 Key algorithm — streaming on a phone
Nine zones, addressable, loaded on an adjacency graph: the current zone at full detail, directly adjacent zones at a **proxy LOD** (collision + silhouette + ambience only, ~18 MB each), everything else unloaded. Transition is via authored chokepoints (the Fernmaw trench mouth, the Sweatways entry) that hide a 1.5–3 s async load behind a constrained corridor. Target resident budget: **1.4 GB** on a 4 GB device, hard-failing the build at 1.6 GB via an automated memory-profile test. No open-world seamless streaming — the caldera's geography gives us natural walls and we use every one of them.

### 12.4 Dependencies
`ITimeSystem` (tide depth is a function of world time), `IWeatherSystem` (visibility for LOD bias). L1. Nothing above it is referenced.

### 12.5 Failure modes
A player standing in a chokepoint when the app is suspended → load state is not saved; on load, the player is placed at the *destination* zone's entry anchor, never in the corridor. Second: world flags and object state diverging after a content patch adds objects → unknown `InteractableId`s in a save are dropped with a logged warning, and objects absent from the save default to their authored initial state.

---

## 13. TimeSystem

**SINGLE RESPONSIBILITY.** Owns the authoritative world clock and the tide phase derived from it, and is the only source of elapsed in-game time.

### 13.1 Public API

```csharp
public interface ITimeSystem : IDomainTick {
    double WorldSeconds { get; }            // monotonic since world start
    double WorldDeltaHours { get; }         // this tick
    WorldClock Clock { get; }               // Day, Hour, Minute, normalized TimeOfDay01
    float  TidePhase01 { get; }             // 0 = low, 0.5 = high
    TideBand TideBand { get; }              // Low, Rising, High, Falling
    void   SetScale(TimeScale scale);       // Normal, Paused, Cinematic(0), FastForward(x12)
    bool   TryFastForward(double hours);    // stepped, not jumped
    event EventHandler<HourElapsed>  HourElapsed;
    event EventHandler<DayElapsed>   DayElapsed;
}
```

### 13.2 Owned state
`double _worldSeconds`, `TimeScale _scale` (refcounted pause), `double _ffRemainingHours`, `double _accumulator`.

### 13.3 Numbers
**Day length: 48 real minutes = 24 in-game hours**, i.e. **1 real second = 30 world seconds**. Rationale: the bible's 36-hour water deadline becomes ~72 real minutes of Act 1, which is the right length for the survival phase to feel like a real pressure and then be over. Day/night split is 15 h light / 9 h dark (equatorial-ish, and we do not want long unplayable nights on a phone screen in daylight).

Tide: a two-component sum, `TidePhase01 = frac(0.5 + 0.5*sin(2π t/T_semi) * 0.82 + 0.5*sin(2π t/T_diurnal) * 0.18)` with `T_semi = 12.42 h` and `T_diurnal = 24.84 h` — a semidiurnal-dominant mixed tide. This is a *dramatised* model chosen because it produces unequal successive high waters (which makes the Fold Camp tidal flat readable as a schedule rather than a metronome); **it is not a claim about real South Atlantic tidal behaviour and is flagged for a consultant pass if we ever make a realism claim publicly.**

Determinism: `_worldSeconds` is a `double` advanced by a **fixed** step, never by `Time.deltaTime` directly. The domain tick runs at exactly 10 Hz via an accumulator; a frame delivering 0.3 s runs three domain ticks. Δh per tick is therefore always exactly `30 * 0.1 / 3600 = 1/1200 h`, or the fast-forward step. This is what makes "advance 30 hours and assert" a valid unit test.

`TryFastForward` sets `_ffRemainingHours` and switches the accumulator to consume up to 0.25 h per domain tick for as many ticks as needed, so Weather, Survival, Crafting and ItemSystem all see plausible step sizes rather than one 8-hour Δh that would break every integrator.

### 13.4 Dependencies
**None.** L0. This is deliberate and worth defending: the clock must be the one system with zero inbound coupling, because everything else integrates against it.

### 13.5 Failure modes
Mobile app resume after 40 minutes backgrounded → on `OnApplicationPause(false)`, TimeSystem does **not** apply the wall-clock gap; it applies `min(gap, 0)` — i.e. zero. In-game time does not pass while the app is closed. (The alternative, real-time passage, would mean opening the app to find Nadia collapsed, which is hostile.) Second: `double` precision over a 60-hour playthrough is trivially fine (216,000 s, ~15 significant digits available).

---

## 14. WeatherSystem

**SINGLE RESPONSIBILITY.** Owns Vardholm's current and forthcoming atmospheric state per altitude band, and exposes the sampled values other systems integrate against.

### 14.1 Public API

```csharp
public interface IWeatherSystem : IDomainTick {
    WeatherState Current { get; }
    WeatherState Forecast(double hoursAhead);          // deterministic — the barometer is readable
    float AmbientCelsius(Vector3 worldPos);
    float RelativeHumidity01(ZoneId z);
    float Precipitation01 { get; }
    float WindChillCelsius(Vector3 worldPos);
    float VisibilityMetres(Vector3 worldPos);
    event EventHandler<WeatherChanged> Changed;
}
public enum WeatherState : byte { Clear, Overcast, Mist, Drizzle, Squall, ColdFront }
```

### 14.2 Owned state
`WeatherState _current`, `double _stateEnteredWorldSeconds`, `double _nextTransitionWorldSeconds`, `Pcg32 _rng` (salt `0x57_54_48_52`), `WeatherState _next` (pre-rolled, enabling `Forecast`).

### 14.3 The state machine — real transition probabilities

Evaluated once per **in-game hour** (on `HourElapsed`), not per tick. Rows sum to 1.0.

| From \ To | Clear | Overcast | Mist | Drizzle | Squall | ColdFront |
|---|---|---|---|---|---|---|
| **Clear** | 0.62 | 0.24 | 0.08 | 0.04 | 0.01 | 0.01 |
| **Overcast** | 0.18 | 0.44 | 0.14 | 0.18 | 0.05 | 0.01 |
| **Mist** | 0.10 | 0.30 | 0.46 | 0.10 | 0.02 | 0.02 |
| **Drizzle** | 0.06 | 0.30 | 0.12 | 0.42 | 0.09 | 0.01 |
| **Squall** | 0.04 | 0.34 | 0.06 | 0.28 | 0.26 | 0.02 |
| **ColdFront** | 0.12 | 0.28 | 0.10 | 0.14 | 0.06 | 0.30 |

Minimum dwell time **1.5 h** (a transition rolled before that is ignored), maximum **9 h** (a forced re-roll excluding the current state). Expected stationary distribution, computed from the chain: roughly Clear 24%, Overcast 33%, Mist 15%, Drizzle 19%, Squall 6%, ColdFront 3%. Squall is rare and short by design — it is an event, not a texture.

**The cloud lid is not weather.** Per the bible, the orographic cloud column is semi-permanent. It is a fixed `WorldSystem` property of the altitude band 60–200 m, not a `WeatherState`. Rime Shoulder is *above* it, so Rime Shoulder is the only zone where `Clear` actually means you can see sky — which is exactly the Act 2 payoff. Weather modifies conditions *within* that constraint.

**Altitude bands.** Ambient temperature is `zoneBaseC + lapse * (altitude) + stateOffset`:
`lapse = -0.0065 °C/m`; `stateOffset`: Clear +1.5, Overcast 0, Mist −0.5, Drizzle −1.5, Squall −3.0, ColdFront −6.5. The Ribcage base is 24 °C at sea level; Rime Shoulder at 240 m under a ColdFront is `24 - 1.56 - 6.5 = 15.9`... which is wrong for the fiction, so Rime Shoulder carries an authored `ZoneTempOverrideC = 4.0` with the state offsets still applied. **Authored override beats simulation wherever fiction requires it** — a general principle in this codebase.

**How weather gates gameplay (four concrete gates, no more):**
1. **Rope bridges on Rime Shoulder** are traversable only when `Precipitation01 < 0.35` and `WindChill > -8` — a `Squall` closes the crossing and you wait or take the long way. `WorldSystem` publishes the block; the player is never yanked off mid-crossing.
2. **The Catchment yields** `0.9 + 2.4 * RelativeHumidity01` litres per 8 h. Mist is the best condensation weather, which is a lovely inversion: the weather that hides the island feeds you.
3. **Hydrophone signal floor.** `Squall` raises the ambient noise floor by 14 dB, making `MatchFrequency` puzzle steps impossible outdoors. `PuzzleSystem` reports `WeatherIs` requirement failure with a clear line. Sweatways puzzles are indoors and unaffected — which teaches the player where to work.
4. **Fire.** As §8.3.

That is the complete list. Weather that gates more than this becomes a system that makes the player wait, and waiting is not a mechanic.

### 14.4 Determinism + save-restore
The chain is driven entirely by `_rng`, whose 128-bit state is serialized. Restoring a save restores `_current`, `_next`, both transition timestamps and the RNG state — so the weather that follows a load is byte-identical to the weather that would have followed without the load. This is what makes `Forecast()` honest and lets us ship an in-fiction barometer in Fold Camp that is actually correct.

### 14.5 Dependencies
`ITimeSystem` (event `HourElapsed`, and `WorldSeconds`), `IWorldSystem` (zone base temps, altitude sampling). L1. Nothing above referenced.

**Cycle flag:** WorldSystem reads Weather (visibility for LOD) and Weather reads World (zone base temps). **Break:** the zone base temperature table is *static authored data* and is extracted into `IZoneDataCatalog` at L0, which Weather reads instead of WorldSystem. World→Weather remains the only edge. Acyclic.

### 14.6 Failure modes
A transition landing mid-cinematic and cutting a cutscene from Clear to Squall → transitions are deferred while `TimeScale.Cinematic`, queued, and applied with a 20 s cross-blend after. Second: a designer wanting "always Squall for this beat" → `WeatherSystem.PushOverride(state, hours, token)`, refcounted like the pause, restoring the chain on pop with the RNG untouched.

---

## 15. SaveSystem

**SINGLE RESPONSIBILITY.** Serializes and restores the complete owned state of every other system atomically and version-tolerantly, and owns nothing about gameplay.

### 15.1 Public API

```csharp
public interface ISaveSystem {
    SaveResult SaveAsync(SaveSlot slot, SaveReason reason, CancellationToken ct);  // returns handle; completes off main thread
    LoadResult Load(SaveSlot slot);
    bool  TryGetHeader(SaveSlot slot, out SaveHeader header);   // for the slot UI; no full deserialize
    void  Register(ISaveParticipant p);                         // every system registers at boot
    bool  HasValidSave(SaveSlot slot);
    void  RequestAutosave(AutosaveTrigger trigger);
    event EventHandler<SaveCompleted> Completed;
    event EventHandler<SaveFailed>    Failed;
}

public interface ISaveParticipant {
    SaveComponentId ComponentId { get; }
    ushort SchemaVersion { get; }
    void Write(ref SaveWriter w);
    SaveReadResult Read(ref SaveReader r, ushort writtenVersion);   // must handle older versions
}
```

### 15.2 Owned state
`ISaveParticipant[] _participants`, `SaveHeader[] _slotHeaders` (cached), `byte[] _scratch` (pre-allocated 512 KB write buffer). **No gameplay state whatsoever.** SaveSystem is a librarian.

### 15.3 Format and the mobile-specific requirements

**Layout.** `[Header 128 B][ComponentTable N×16 B][Component blobs...][CRC32 4 B]`. Header is fixed-size and plain so the slot-select screen reads day, act, zone, playtime and a thumbnail path without touching the body. Each component table entry is `{ SaveComponentId (4), SchemaVersion (2), Offset (4), Length (4), Reserved (2) }`. Body is **binary, little-endian, LZ4-compressed per component** — not JSON. A full save is ~180 KB uncompressed, ~40 KB compressed; JSON would be 900 KB and take 300 ms to parse on a low-end Android, which is visible as a hitch.

**Atomicity.** Write to `slot_N.tmp`, `File.Flush(true)`, then `File.Replace` onto `slot_N.sav` with `slot_N.bak` as the backup destination. Load tries `.sav`, verifies CRC, falls back to `.bak` on failure, and reports `LoadResult.RecoveredFromBackup` so the UI can say so honestly. This three-file dance is the entire defence against the phone dying mid-write, which *will* happen and which is the most common one-star review cause in mobile adventure games.

**Threading.** Collecting state (`Write`) happens **on the main thread during one domain tick, with the domain paused**, into `_scratch` — this guarantees a consistent snapshot across systems without any locking. Compression and file I/O then happen on a background thread. The main-thread cost is the only cost the player can feel and it is budgeted at **8 ms**.

**Autosave triggers**: zone transition, puzzle solved, act change, camp rest completion, 4-minute idle timer, and `OnApplicationPause(true)` (Android in particular gives you no guarantees after this — the pause save is synchronous, skips compression, and is budgeted at 30 ms). Three rotating autosave slots plus one manual.

**Version tolerance.** Each participant handles `writtenVersion < SchemaVersion` by reading the old layout and defaulting new fields. A participant returning `SaveReadResult.Incompatible` does not fail the load — it resets **only that component** to defaults and the load reports `PartialRestore` with the component named. Losing your weather RNG state is survivable; losing your save is not.

**What gets saved, by participant** (this table is the contract — every system's owned state appears exactly once):

| Component | Owner | Payload |
|---|---|---|
| `Time` | TimeSystem | `_worldSeconds`, scale, ff remainder |
| `Weather` | WeatherSystem | current, next, timestamps, RNG state |
| `World` | WorldSystem | zone, visited bits, world flags, object states, player transform |
| `Inventory` | InventorySystem | 36 slots, `_nextInstanceId` |
| `Items` | ItemSystem | live instance runtime states |
| `Survival` | SurvivalSystem | 5 stats, exertion, pause refcount |
| `Camp` | CampSystem | camps, stations, fire fuel |
| `Crafting` | CraftingSystem | jobs + reservations (one record) |
| `Combination` | CombinationSystem | `_known` recipe bits |
| `Discovery` | DiscoverySystem | unlocked bitset, revisions, unlock ticks |
| `Puzzles` | PuzzleSystem | per-puzzle state, bitmasks, quantised step values, hint levels |
| `Story` | StorySystem | act, completed nodes, ending choice, avoidance stage |
| `Quest` | QuestSystem | quest runtimes, active id |
| `Audio` | AudioSystem | persistent mix snapshot id, unplayed-once-only set |
| `Settings` | (separate file) | never in the save; loc, volume, quality |

### 15.4 Dependencies
Every system, but **only through `ISaveParticipant`** — SaveSystem has zero knowledge of any system's type. It sits at L0 and is referenced by nobody in domain code; the bootstrapper registers participants. This inversion is the only reason the dependency graph stays acyclic.

### 15.5 Determinism + testability
`SaveWriter`/`SaveReader` work on `Span<byte>`, so a round-trip test needs no filesystem: write all participants to a span, read back into fresh instances, assert deep equality. The highest-value test in the project is `Save_RoundTrip_AllParticipants_ProducesIdenticalState`, run over 200 randomly-generated game states in CI.

### 15.6 Failure modes
| Failure | Guard |
|---|---|
| Corrupt save from a kill during write | `.tmp`→`.sav`→`.bak` rotation + CRC32 |
| Storage full (common on cheap Android) | Pre-check `DriveInfo.AvailableFreeSpace` for 4× the blob size; on failure, `SaveFailed(OutOfSpace)` and the game keeps running with a persistent non-blocking banner. Never a silent failure. |
| iOS killing the app before the async save flushes | The `OnApplicationPause` path is synchronous and uncompressed by design |
| A system forgetting to register | Boot asserts `_participants.Count == 14`, the compile-time-known count |
| Save written mid-frame with half-applied state | Save collection is a dedicated tick phase (§19, phase 12) after all mutation phases |

---

## 16. AudioSystem

**SINGLE RESPONSIBILITY.** Owns playback, the mix state and the **live spectral analysis** that the hydrophone gameplay reads from.

This system is unusual in this project: in most games audio is presentation-only, but here the *signature verb is listening*, so AudioSystem exposes a domain-facing query surface that PuzzleSystem depends on.

### 16.1 Public API

```csharp
public interface IAudioSystem : IDomainTick {
    AudioHandle Play(AudioCueId cue, in AudioPlayParams p);
    void  Stop(AudioHandle h, float fadeSeconds = 0.25f);
    void  SetMixSnapshot(MixSnapshotId id, float blendSeconds);
    bool  IsNarrativeAudioPlaying { get; }                      // DiscoverySystem reads this

    // The hydrophone surface — this is gameplay, not presentation
    void  SetListenMode(ListenMode mode, Vector3 aimDirection);  // Ambient | Hydrophone
    bool  TryGetSpectrum(Span<float> bins64, out float dominantHz, out float snrDb);
    float GetSpectralPeakHz(SourceId source);                   // PuzzleSystem.MatchFrequency
    event EventHandler<NarrativeAudioFinished> NarrativeFinished;
}
```

### 16.2 Owned state
`AudioHandle[] _voices` (pooled, max **28 simultaneous** on mobile), `MixSnapshotId _snapshot`, `ListenMode _mode`, `float[64] _spectrumBins`, `HashSet<AudioCueId> _playedOnce` (the "don't replay this barks line" set — saved).

### 16.3 Key algorithm — the hydrophone
The hydrophone overlay is **not** an FFT of the game's output mix — that would be expensive, nondeterministic and would pick up UI sounds. Instead every acoustically-significant world object registers an `AcousticSource { Vector3 pos; float fundamentalHz; float harmonicRolloff; float sourceDb; float occlusionDb; }`. When `ListenMode.Hydrophone` is active, at 10 Hz we:
1. gather sources within 120 m (spatial grid query, typically 3–9 sources);
2. for each, compute received level `= sourceDb - 20*log10(max(1,dist)) - occlusionDb * wallCount - weatherFloorDb`;
3. apply a directional gain from the aim vector: `gain = 0.25 + 0.75 * pow(max(0, dot(aim, toSource)), 2.5)` — a cardioid-ish pattern with a usable but clearly directional lobe;
4. splat each source's fundamental and first three harmonics into the 64 log-spaced bins (30 Hz–4 kHz);
5. the *rendered* audio is a real filtered playback of authored stems, but the *spectrogram the player reads and the puzzle checks* comes from this analytic model.

This means `GetSpectralPeakHz` is deterministic, testable, and identical on every device regardless of audio driver latency or DSP buffer size — which is the only way a frequency-matching puzzle can ship on Android's audio stack. It is the single most important engineering decision in this document.

### 16.4 Dependencies
`IWorldSystem` (source positions, occlusion), `ITimeSystem`, `IWeatherSystem` (noise floor). L2. PuzzleSystem and DiscoverySystem read *from* it; it reads nothing from them.

### 16.5 Failure modes
Voice starvation cutting a narrative reel to play a footstep → voices carry a priority class and `Narrative` is never evicted; `Footstep`/`Ambient` are evicted first. Second: Bluetooth headset latency desyncing a `HoldValue` puzzle → the puzzle checks the *analytic* model, not what the player hears, so latency affects feel but never correctness; additionally we detect output latency > 120 ms and widen `MatchFrequency` hold windows by 0.4 s.

---

## 17. LocalizationSystem

**SINGLE RESPONSIBILITY.** Resolves a `LocKey` to display text and the matching localized audio cue for the active locale.

### 17.1 Public API

```csharp
public interface ILocalizationSystem {
    Locale Current { get; }
    bool  TrySetLocale(Locale l);                    // async table load; returns false while loading
    string Get(LocKey key);                          // never returns null — returns "#KEY" in dev, "" in ship
    string Format(LocKey key, in LocArgs args);      // plural- and gender-aware
    AudioCueId LocalizedCue(AudioCueId neutral);
    bool  IsRightToLeft { get; }
    event EventHandler<LocaleChanged> Changed;
}
```

### 17.2 Owned state
`Locale _current`, `Dictionary<uint,string> _table` (the active locale only — one table resident, ~2.1 MB for this word count), `Dictionary<AudioCueId,AudioCueId> _cueRemap`.

### 17.3 Key considerations specific to this game
The game is **document-heavy** — Poulter's 1883 copperplate, Sabo's profane engineering logs, Solheim's counting, 11,000 of Lorvik's identical logbook entries. Three consequences:
1. **Handwriting is art, text is text.** Documents render as a scanned-paper background plus a *live text layer* in a period-appropriate font, never as a baked image. Otherwise localisation cost is 5 languages × 340 documents of art. This constrains the art direction and must be agreed at pre-production, not discovered in month nine.
2. **Voice/text divergence is expected.** Sabo's reels are voiced. Localised builds ship subtitles over the original VO for reels (in-fiction: they are recordings, and hearing a 1974 Hungarian-French engineer in her own voice is *better*), while Lorvik's Act 5 dialogue is fully localised VO in tier-1 languages. Flagged as a budget decision, not a technical one.
3. **Layout expansion.** German and Russian run +35% over English. Every document and Slate panel uses a scrolling text container with a minimum 11 pt at 400 px portrait width; no fixed-height text boxes anywhere. Validator: a build-time pass renders every `LocKey` into its declared container at every locale and fails on overflow.

Launch locales (proposal, needs a market call): EN, FR, DE, ES-419, PT-BR, JA, KO, ZH-Hans, RU. PT-BR is non-obvious but Project Wellhead is Dutch-Brazilian and Castellar's calcs are partly in Portuguese — there is a genuine hook.

### 17.4 Dependencies
None (L0). Read by presentation and by any system producing a `LocKey`-bearing event. Domain systems **never resolve strings** — they emit `LocKey`s. That is what keeps the domain assembly free of localisation and testable.

### 17.5 Failure modes
A missing key shipping silently → CI diffs the key set against the EN table and fails on any key used in code or data that is absent. Second: font fallback for JA/KO/ZH pushing the atlas over budget → CJK uses a separate dynamic SDF atlas loaded only for those locales, and the build is per-locale for mobile stores anyway.

---

## 18. SYSTEM DEPENDENCY DIAGRAM

Read edges as "depends on" (downward only). `─e→` denotes an event subscription, which is an *inverted* edge — the subscriber depends on the publisher's payload type, which lives in L0, so it introduces no reference cycle.

```
╔═══════════════════════════════════════════════════════════════════════════════════════╗
║ L4  PROGRESSION / NARRATIVE        (references L3, L2, L1, L0 — referenced by nobody)  ║
╠═══════════════════════════════════════════════════════════════════════════════════════╣
║      ┌──────────────┐        ┌──────────────┐                                         ║
║      │ StorySystem  │◄───────│ QuestSystem  │                                         ║
║      └──────┬───────┘        └──────┬───────┘                                         ║
╚═════════════╪════════════════════════╪════════════════════════════════════════════════╝
              │                        │
╔═════════════▼════════════════════════▼════════════════════════════════════════════════╗
║ L3  PLAYER-FACING DOMAIN           (references L2, L1, L0)                             ║
╠═══════════════════════════════════════════════════════════════════════════════════════╣
║  ┌────────────────┐   ┌──────────────┐   ┌──────────────┐   ┌────────────────┐         ║
║  │DiscoverySystem │   │ PuzzleSystem │   │  CampSystem  │   │InteractionSys. │         ║
║  └───────┬────────┘   └──────┬───────┘   └──────┬───────┘   └───────┬────────┘         ║
║          │                   │                  │                   │                  ║
║          │  (Discovery ─e→ Combination : the ONLY upward-ish edge, event-only)          ║
╚══════════╪═══════════════════╪══════════════════╪═══════════════════╪══════════════════╝
           │                   │                  │                   │
╔══════════▼═══════════════════▼══════════════════▼═══════════════════▼══════════════════╗
║ L2  CORE DOMAIN                    (references L1, L0)                                 ║
╠═══════════════════════════════════════════════════════════════════════════════════════╣
║  ┌───────────────┐  ┌───────────────┐  ┌────────────────┐  ┌──────────────┐            ║
║  │CraftingSystem │─►│CombinationSys.│─►│InventorySystem │◄─│ ItemSystem   │            ║
║  └───────┬───────┘  └───────┬───────┘  └────────────────┘  └──────┬───────┘            ║
║          └──────────────────┴───────────────┬───────────────────── ┘                   ║
║                                             │                                          ║
║  ┌─────────────────┐                        │                                          ║
║  │  AudioSystem    │  (exposes GetSpectralPeakHz upward to L3 Puzzle)                   ║
║  └────────┬────────┘                        │                                          ║
║  ┌─────────────────┐                        │                                          ║
║  │ SurvivalSystem  │────────────────────────┘                                          ║
║  └────────┬────────┘                                                                    ║
╚═══════════╪═════════════════════════════════════════════════════════════════════════════╝
            │
╔═══════════▼═════════════════════════════════════════════════════════════════════════════╗
║ L1  WORLD SIMULATION               (references L0 only)                                 ║
╠═════════════════════════════════════════════════════════════════════════════════════════╣
║        ┌──────────────┐                 ┌───────────────┐                               ║
║        │ WorldSystem  │────────────────►│ WeatherSystem │                               ║
║        └──────┬───────┘                 └───────┬───────┘                               ║
║               └──────────────┬──────────────────┘                                       ║
╚══════════════════════════════╪══════════════════════════════════════════════════════════╝
                               │
╔══════════════════════════════▼══════════════════════════════════════════════════════════╗
║ L0  FOUNDATION — no gameplay dependencies, referenced by everything                      ║
╠═════════════════════════════════════════════════════════════════════════════════════════╣
║  TimeSystem │ SaveSystem (via ISaveParticipant inversion) │ LocalizationSystem           ║
║  IEventBus  │ IItemCatalog │ IRecipeCatalog │ IZoneDataCatalog │ Pcg32 │ Id structs      ║
╚═════════════════════════════════════════════════════════════════════════════════════════╝
```

### 18.1 The four cycles that exist in the naive design, and their breaks

| Naive cycle | Break |
|---|---|
| Combination ↔ Discovery | Combination publishes `CombinationSucceeded`; Discovery subscribes and decides. Discovery publishes `DiscoveryUnlocked`; Combination subscribes to update `_known`. Neither holds a reference to the other's interface; both reference the event structs in L0. |
| Crafting ↔ Combination (shared recipes) | `IRecipeCatalog` extracted to L0. Both read data, neither reads the other. |
| World ↔ Weather | Zone base temperature / altitude tables extracted to `IZoneDataCatalog` in L0. Weather reads the catalog, not WorldSystem. Only World→Weather remains. |
| Everything ↔ Save | Inverted via `ISaveParticipant`. Systems implement an L0 interface; SaveSystem knows only that interface. The bootstrapper wires them. |

**Proof of acyclicity:** every reference edge points strictly down a layer index, and the two event edges (`Discovery ─e→ Combination`, `Puzzle ─e→ Discovery`) carry no type reference in the upward direction because payload structs live in L0. A topological sort therefore exists; it is exactly the tick order in §19. This is verified in CI by an assembly-reference test (`Architecture_NoAssemblyReferencesUpward`) and a Roslyn analyzer that fails any `using` of a higher-layer namespace.

---

## 19. TICK ORDER SPECIFICATION

Domain tick rate: **10 Hz fixed**, decoupled from render. `GameLoop.FixedDomainUpdate()` runs an accumulator; at most **3 catch-up ticks** per frame (beyond that, time is discarded — a 4-second frame hitch must not produce a 4-second simulation surge).

| # | Phase | System | Why it must be here |
|---|---|---|---|
| 1 | **Clock** | TimeSystem | Everything integrates against Δh. Nothing may read a stale clock; nothing may advance before it. |
| 2 | **Atmosphere** | WeatherSystem | Weather transitions on the hour TimeSystem just crossed. Must precede anyone sampling ambient temperature. |
| 3 | **World** | WorldSystem | Player transform, zone occupancy, streaming and tide-driven water level resolve before anything queries "where am I / how cold is here". Zone change events fire here so downstream systems see the new zone this same tick. |
| 4 | **Acoustics** | AudioSystem | Builds the spectrum from World positions (phase 3) and the Weather noise floor (phase 2). Must precede PuzzleSystem, which reads `GetSpectralPeakHz`. |
| 5 | **Items** | ItemSystem | Applies drain/corrosion using this tick's Δh, humidity and zone. Must precede Survival, because a recorder that just died and a garment that just soaked change Survival's wetness input. |
| 6 | **Survival** | SurvivalSystem | Integrates against the now-final clock, weather, world and item wetness. Must precede Interaction, so a `Failing` band can refuse a Climb verb this tick rather than next. |
| 7 | **Input / Interaction** | InteractionSystem | The only phase that reads player input. Runs after simulation so candidate arbitration uses this tick's world state, and before the systems it drives. |
| 8 | **Combination + Crafting** | CombinationSystem, CraftingSystem (in that order) | Both consume interaction intents raised in phase 7 within the same tick — no one-frame lag on "I combined two things." Combination first because a craft may be begun by an item combination produced this tick. |
| 9 | **Camp** | CampSystem | Stations tick after crafting so a completed craft's output can be held by a station the same tick; fire fuel burns after Survival has already sampled the fire's warmth (deliberate: you get the warmth of the fuel in the tick it burns, not the tick after). |
| 10 | **Puzzles** | PuzzleSystem | Consumes phase-7 inputs and the phase-4 spectrum. Hint timers advance here. Must be after Audio and Interaction, before Discovery — a puzzle solved this tick must produce its discovery this tick. |
| 11 | **Event drain** | IEventBus | All deferred events published during phases 1–10 are dispatched now, up to 4 convergence passes. This is where `DiscoverySystem`, `StorySystem` and `QuestSystem` actually do their work — they are *reactive*, so they have no simulation phase of their own. Convergence is guaranteed because L4 systems publish only terminal notification events. |
| 12 | **Persistence** | SaveSystem | Last. Every mutation for this tick is complete and every event has been dispatched, so a snapshot taken here is internally consistent by construction. Autosave triggers evaluated here; actual write dispatched to a background thread. |
| 13 | **Presentation sync** | (non-domain) | UI, VFX, animation read the now-frozen domain state. Domain is read-only for the rest of the frame — enforced in dev builds by a write-guard flag that asserts on any domain mutation outside phases 1–12. |

**Three ordering decisions that will be questioned in review, pre-answered:**
* *Why is Interaction after Survival rather than first?* Because input-driven verbs must be validated against this tick's stats. Putting input first means a player at Energy 2 can start a climb that Survival then invalidates one tick later, which reads as the game changing its mind.
* *Why is Discovery in the event drain rather than its own phase?* Discovery has no time-dependent state. Giving it a phase would create an artificial ordering constraint with Puzzle and Combination that the event drain resolves naturally, with convergence.
* *Why is Save last rather than at frame end?* Frame end is after presentation, which can mutate nothing but *can* be mid-animation; a save taken at phase 12 needs no knowledge of presentation state at all, which is what keeps the save format free of view data.

---

## 20. TEST MATRIX

Assembly `Isle.Domain.Tests`, NUnit, no Play Mode, full suite target **under 20 s** in CI.

**InventorySystem**
- `Inventory_AddToPartialStack_TopsUpLowestSlotIndexFirst`
- `Inventory_RemoveAcrossStacks_ConsumesFromOldestFirst`
- `Inventory_RemoveMoreThanHeld_RemovesNothingAndReturnsInsufficient`
- `Inventory_AddWhenOverMassLimit_AcceptsPartialAndReportsOverflowQuantity`
- `Inventory_AddCriticalItemWhenFull_BypassesLimitsIntoReservedSlots`
- `Inventory_StackTwoWetnessStates_ResultTakesMaxWetnessNeverMin`

**ItemSystem**
- `Item_RecorderWetFourteenHours_PublishesCorrodedExactlyOnce`
- `Item_ChargeDrain_OnlyAppliesWhileActive`
- `Item_HumidSweatwaysZone_CorrodesAtApproximatelyTwoPointFiveTimesDryRate`
- `Item_InstanceStateEvicted_WhenSlotEmptied`

**CombinationSystem**
- `Combine_IngredientOrderReversed_MatchesSameRecipe`
- `Combine_ToolInputNotConsumed_ButLosesDurability`
- `Combine_GatedRecipeBeforeDiscovery_ReturnsNotDiscoveredAndConsumesNothing`
- `Combine_OutputWouldNotFit_AbortsBeforeRemovingInputs`
- `Combine_ProbabilisticOutput_IsIdenticalAcrossSaveLoadOfSameInputs`
- `Combine_BluntedTool_ReportsToolTooWornNotNoSuchRecipe`

**CraftingSystem**
- `Craft_Begin_ReservesInputsImmediatelyAndInventoryReflectsRemoval`
- `Craft_Cancel_RefundsExactReservedQuantitiesAndInstanceStates`
- `Craft_StationDestroyedMidJob_FailsJobAndRefunds`
- `Craft_UnattendedCatchment_ProgressesWhilePlayerIsInAnotherZone`
- `Craft_CompleteWithFullPack_HoldsOutputAtStationNeverDestroysIt`

**InteractionSystem**
- `Interaction_TwoOverlappingHotspots_HysteresisKeepsPreviousCandidate`
- `Interaction_EquippedItemRemovedFromInventory_ClearsLeaseSameTick`
- `Interaction_TargetUnregisteredWhileCurrent_RefusesWithTargetGone`
- `Interaction_UseWithOnPuzzleElement_RoutesToPuzzleNotCombination`

**DiscoverySystem**
- `Discovery_UnlockTwice_RaisesEventOnceAndEnqueuesOneNotification`
- `Discovery_Revise_KeepsOriginalUnlockedAndRecordsChain`
- `Discovery_FourUnlocksSameCategoryWithinSixSeconds_CoalesceToSingleNotification`
- `Discovery_WhileNarrativeAudioPlaying_SuppressesNotificationButNotUnlock`
- `Discovery_UnlocksRecipe_CombinationSystemKnowsItWithoutDirectReference`

**SurvivalSystem**
- `Survival_WalkThirtyHoursFromFull_HydrationReachesCriticalNotZero`
- `Survival_ColdBelowThirtyFiveDegrees_IncreasesSatiationDrainByCouplingTerm`
- `Survival_WetOnRimeShoulder_ReachesLowCoreTempInUnderThirtyMinutes`
- `Survival_HealthReachesZero_PublishesCollapseAndClampsToOne`
- `Survival_DuringDocumentRead_NoStatChangesAtAll`
- `Survival_BalanceGuardrail_ThirstToCollapseNeverUnderThirtyHours`

**CampSystem**
- `Camp_RestWithoutFireOrShelter_RefusesWithReason`
- `Camp_RestEightHours_AdvancesWeatherChainNotJustClock`
- `Camp_FireInSquallWithoutShelter_ExtinguishesWithinOneHour`
- `Camp_RestInterruptedByAppSuspend_ResumesRemainingHoursOnLoad`

**PuzzleSystem**
- `Puzzle_UnlockRequirementsUnmet_StaysLockedAndRejectsInput`
- `Puzzle_OrderedStepsSubmittedOutOfOrder_RejectsWithoutLosingProgress`
- `Puzzle_SoftFailAfterCheckpoint_RewindsToCheckpointNotZero`
- `Puzzle_SaveMidDialAdjustment_RestoresQuantisedValueWithinTolerance`
- `Puzzle_HintLevelTwo_RequiresExplicitRequestNeverAutoDelivers`
- `Puzzle_MatchFrequency_UsesAnalyticSpectrumNotDeviceAudio`

**StorySystem**
- `Story_AllActCriticalNodesComplete_AdvancesActExactlyOnce`
- `Story_NodeEligibility_RecomputedOnEventNotPerTick`
- `Story_Act5_StripsAllSurvivalAndCraftingVerbs`
- `Story_BakeValidation_EveryActCriticalNodeIsReachableFromStart`
- `Story_EndingChoiceSetTwice_SecondCallIsRejected`

**QuestSystem**
- `Quest_ActiveCompletes_NextHighestPriorityAutoActivates`
- `Quest_ObjectiveCondition_EvaluatesOnEventNotPolling`
- `Quest_BackgroundThreads_NeverExceedThree`
- `Quest_MarkerGranularity_IsZoneLevelNeverWorldPosition`

**WorldSystem**
- `World_TakeItemWithFullPack_RollsBackWorldFlagSoObjectRemains`
- `World_ObjectStatePersists_AcrossZoneUnloadAndReload`
- `World_SuspendInChokepoint_LoadPlacesPlayerAtDestinationAnchor`
- `World_UnknownInteractableIdInSave_IsDroppedWithWarningNotThrow`

**TimeSystem**
- `Time_TwentyFourInGameHours_EqualsFortyEightRealMinutes`
- `Time_FastForwardEightHours_EmitsEightHourElapsedEventsInOrder`
- `Time_AppBackgroundedFortyMinutes_AdvancesZeroWorldSeconds`
- `Time_FrameHitchOfFourSeconds_RunsAtMostThreeCatchUpTicks`
- `Time_TidePhase_ProducesUnequalSuccessiveHighWaters`

**WeatherSystem**
- `Weather_TransitionMatrixRows_EachSumToOneWithinEpsilon`
- `Weather_SameSeedAndClock_ProducesIdenticalSequence`
- `Weather_ForecastSixHoursAhead_MatchesWhatActuallyOccurs`
- `Weather_MinimumDwell_IgnoresTransitionRolledBeforeNinetyMinutes`
- `Weather_SquallDuringCinematic_DefersUntilCinematicEnds`
- `Weather_LoadedSave_ContinuesChainIdenticallyToUnsavedRun`

**SaveSystem**
- `Save_RoundTrip_AllParticipants_ProducesIdenticalState`
- `Save_CorruptPrimary_FallsBackToBackupAndReportsRecovery`
- `Save_OlderSchemaVersion_ReadsWithDefaultedNewFields`
- `Save_IncompatibleComponent_ResetsOnlyThatComponentAndReportsPartial`
- `Save_OutOfSpace_FailsLoudlyAndGameContinues`
- `Save_CollectionPhase_MainThreadCostUnderEightMilliseconds`

**AudioSystem**
- `Audio_HydrophoneAimedAwayFromSource_AttenuatesByCardioidCurve`
- `Audio_SpectralPeak_IsDeviceIndependentForIdenticalWorldState`
- `Audio_VoiceStarvation_NeverEvictsNarrativePriority`
- `Audio_SquallRaisesNoiseFloor_ByFourteenDecibelsOutdoorsOnly`
- `Audio_PlayedOnceCueSet_SurvivesSaveLoad`

**LocalizationSystem**
- `Loc_MissingKey_ReturnsHashMarkerInDevNeverThrows`
- `Loc_GermanExpansion_AllDocumentContainersFitAtFourHundredPixelWidth`
- `Loc_EveryKeyUsedInCodeOrData_ExistsInEnglishTable`
- `Loc_RightToLeftLocale_FlipsSlateLayoutWithoutMirroringDocumentArt`
- `Loc_DomainAssembly_ContainsNoLiteralDisplayStrings`

---

## 21. UNVERIFIED ASSUMPTIONS FLAGGED FOR ENGINEERING CHECK

These are stated so nobody treats them as verified fact. I have not confirmed them with a tool in this pass; each needs a spike before it is load-bearing.

1. **Unity 6 LTS API specifics** — that `Addressables` async zone loading, `AudioSource.GetSpectrumData` (if we use it at all for the *rendered* layer), `File.Replace` semantics on iOS sandboxed storage, and `Application.wantsToQuit` / `OnApplicationPause` ordering on Android 14+ behave as assumed. The design deliberately minimises exposure: the hydrophone gameplay uses an analytic model precisely so it does not depend on `GetSpectrumData`'s platform behaviour.
2. **Performance budgets** — the 1.2 ms domain tick, 1.4 GB resident, 28-voice ceiling and 8 ms save-collection figures are engineering *targets* derived from experience with comparable mobile titles, not measurements on this content. Validate with a vertical slice on an actual iPhone 12 and a Snapdragon 6-series Android before locking content scope.
3. **The 30× time scale and every survival constant in §7.3** are a designed first pass intended to be tuned in playtest, not a balance claim. The CI balance guardrails encode the *invariants* we refuse to break, not the final values.
4. **The tide model in §13.3 and the acoustic propagation model in §16.3** are dramatised fiction consistent with the bible's Rule 10. Neither is a claim about real-world physics and both need a consultant pass before any public realism claim.
5. **`System.IO.DriveInfo` availability on iOS/Android under IL2CPP** is assumed; if unavailable, substitute the platform-specific free-space query behind an `IStorageProbe` interface, which the design already isolates.