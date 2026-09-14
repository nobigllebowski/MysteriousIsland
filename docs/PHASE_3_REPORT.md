# PHASE 3 REPORT — Invisible world geometry

**Date:** 2026-09-14 · **Branch:** `claude/keen-darwin-frw656`
**Runtime verification: NOT PERFORMED.** There is no Unity, no .NET SDK and no Mono in this
environment. Nothing below has been compiled or played. Phase 4 has not been started.

---

## 1. Root cause

**Two defects, both proven arithmetically from the shipped values. Neither is a culling problem.**

### 1a — The geometry renders and is shaded to 7.7% grey

In Linear colour space Unity converts a material's colour to linear **before** shading. An albedo of
`0.13` is `0.014` linear — near coal. Combined with a sun at 24° elevation (N·L = 0.41) at
intensity 0.85:

| Surface | Screen RGB | Luminance |
|---|---|---:|
| Ribcage ground | (0.07, 0.08, 0.07) | **0.077** |
| Ribcage stone | (0.18, 0.18, 0.17) | 0.178 |
| Fernmaw ground | (0.04, 0.08, 0.05) | 0.070 |

Seven per cent grey is black on any display. The stone at 0.178 is the faint gradient that *was*
visible in the screenshots — and that is the confirmation: nothing was missing or culled, the
surfaces were being shaded to nothing.

Values that read as a reasonable dark palette in an inspector arrive a fifth as bright once
converted. That conversion is the whole trap.

### 1b — The Standing Stone was 2.45 m behind the player's head

The rig spawns at the origin facing **+Z** with no yaw. `BuildRibcageContent` placed the marker at
`(1.5, ·, -1.5)`:

```
camera        (0.00, 2.80, 0.00)   forward (0, 0, 1)
stone         (1.50, 1.58, -1.50)
camera→stone  (1.50, -1.22, -1.50)   length 2.45 m
dot with forward = -1.50              → BEHIND THE CAMERA
angle = 128°
```

Proximity does not care about facing, so `INSPECT` was up from the first frame while the stone was
never once on screen. This is exactly the difference between *a logical object existing* and *a
visible mesh existing*, and it is why the symptom read as incomprehensible rather than as "too
dark".

## 2. Evidence

**From the runtime log:** `renderers 58`, `zone shader 'Standard'`, `pipeline Built-in`,
`colour space Linear`, `camera yes (enabled yes)`, `tagged yes`, `Camera.main yes`,
`enabled cameras 1`, `active scene 'ZoneRibcage'`. Geometry exists, has a real shader, and nothing
is culled — which eliminates every structural explanation and leaves shading and placement.

**Computed from the shipped values** (Python, against the real recipe numbers and the real
coordinates): the luminance table in §1a and the vector arithmetic in §1b.

**Eliminated by reading the code, not by assumption:**

| Hypothesis | Finding |
|---|---|
| Stone has no `MeshRenderer` | **False.** `CreateMarker` builds a `Cube` primitive and assigns the stone material. |
| Objects float or sink | **False.** `ZoneMeshes.SampleHeight` is identical to the function `BuildGround` uses. |
| Reversed triangle winding | **False.** Cross product of the ground's first triangle gives `(0, 1, 0)` — up, clockwise from above. Correct. |
| Missing normals or bounds | **False.** `RecalculateNormals` and `RecalculateBounds` are both called on every generated mesh. |
| Mesh too large for 16-bit indices | **False.** 33 × 33 = 1089 vertices. |
| `ZoneBuilder` never runs | **False.** The root check runs against an empty scene, so the early-out cannot trigger on first entry. |

## 3. Files changed

| File | Change |
|---|---|
| `Game/World/ZoneBuilder.cs` | Recipe values corrected to physical ones; marker moved in front of the spawn |
| `Game/Bootstrap/ZoneFurnisher.cs` | Camera clears to the zone's fog colour; `HasColliderBelow` fixed |
| `Game/Diagnostics/ZoneDiagnostics.cs` | **New in Phase 3** — per-object dump, categorised visibility block, ground-luminance estimate |
| `Game/Bootstrap/AppBootstrap.cs` | Zone owns the active scene; boot restored on exit |
| `Tests/PlayMode/WorldGeometryTests.cs` | **New** — 9 cases |

## 4. Fix

**Lighting.** Ribcage ground **0.406** and stone **0.479** screen luminance; Fernmaw ground
**0.308** and stone **0.381**. Still cold, desaturated and bleak, with Fernmaw still darker than the
shore because that contrast is the zone's character — darker than the shore, not darker than
visible. The camera clears to the zone's fog colour so distance fades into sky rather than into a
hole.

**Placement.** The marker moved to `(2.5, ·, 7.5)`: ahead of the spawn, far enough to be approached
rather than auto-prompted, inside the arch's z range so it reads against the ribs.

**Two further defects found while re-reading**, both real and both fixed:

- `HasColliderBelow` tested `bounds.max.y <= spawn.y` — whether the collider lay *entirely* below
  the spawn. A rolling terrain never does, so the test failed on every zone and a redundant
  120 × 1 × 120 cube was built under the real ground every time.
- `WorldGeometryTests` bounded the player to ±30 m because the ground was assumed to be 60 m
  square. `ZoneBuilder.GroundSize` is **120**. My own test, wrong by assumption.

## 5. Tests written

`WorldGeometryTests`, 9 PlayMode cases. The premise of the suite is that **every test written
before it passed while the screen was black** — they asserted that a player, a controller and a
camera existed, and all three did.

| Case | Asserts |
|---|---|
| `Ribcage_ContainsTheGeometryItsRecipeDescribes` | ≥ 30 renderers, every mesh has vertices |
| `EveryRenderer_HasAMaterialWithARealShader` | No null shader — the failure that draws nothing with no error |
| `TheCamera_ActuallyHasTheWorldInView` | Layer in mask, bounds in frustum |
| `ThePlayer_StandsInsideTheBuiltArea` | Inside ±60 m, not under the terrain |
| `TheStandingStone_IsDrawableGeometryAndNotJustARegistration` | Has a mesh with vertices, not only a registration |
| `TheLoadedZone_IsTheActiveScene` | Active-scene ownership |
| `TheGround_IsShadedBrightEnoughToSee` | Luminance **> 0.18 and < 0.75** |
| `TheCamera_PointsAcrossTheGroundNotIntoIt` | Within 37° of level, above the ground |
| `TheStandingStone_IsInFrontOfTheSpawn` | Within 75° of forward — it shipped at 128° |

The luminance case has an upper bound as well as a lower one on purpose: a test that only pushes
one way invites the fix of turning everything white.

## 6. Tests executed

**None.** No test in this repository has ever been run — not the 9 new ones, not the 243 that
precede them. There is no Unity and no .NET runtime here.

What *was* executed: `ci/validate-structure.py` (exit 0, 12 checks) and `ci/check-layering.sh`
(exit 0), plus the luminance and vector arithmetic in Python against the real shipped values.

## 7. Runtime verification status

**IMPLEMENTED BUT NOT RUNTIME VERIFIED.**

Acceptance criteria C, D and F — "ZoneRibcage renders visible world geometry", "the Standing Stone
is actually visible", "expected geometry is actually visible to the camera" — **cannot be
confirmed from here**. They require the editor. I am not claiming them.

## 8. Remaining risks

| # | Risk |
|---|---|
| 1 | **Nothing has been compiled or played.** Everything above is proven arithmetic and static reading, not observation. |
| 2 | The luminance model is Lambert plus flat ambient, ignoring specular, shadows and fog. All three only subtract, so it is an upper bound — sound for catching "too dark", not a substitute for looking. |
| 3 | `ZoneMeshes` writes vertex colours for height shading, but the **Standard shader ignores vertex colour**. The ground will be flat-shaded by lighting alone — less variation than the code implies. Not a defect; left alone rather than widening this fix. |
| 4 | `ConvexHullTriangles` is star-shaped-only by construction. It is used only on point sets that satisfy that, but it is 26 of the 58 renderers. |
| 5 | **CONFLICT-7** — every document says URP; the project has no URP package and no pipeline asset, and runs on Built-in. Unresolved, and it decides what `CreateMaterial` should target. |
