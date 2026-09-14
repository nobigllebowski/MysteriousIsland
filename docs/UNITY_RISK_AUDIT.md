# UNITY COMPILATION RISK AUDIT

Phase 1.5 · Target **Unity 6000.6.0f1** · 88 C# files

> ### ⚠ FIRST REAL EDITOR FEEDBACK — 2026-09-14
>
> The project was opened in Unity `6000.6.0f1` for the first time. It failed with **88 × CS0619**,
> and **not one of them was in our code** — every error was inside the Input System package:
> `com.unity.inputsystem@1.14.0` → `InputSystem/Plugins/HID/HIDDescriptorWindow.cs`, using
> `TreeViewState` / `TreeView` / `TreeViewItem`, which Unity deprecated in 6.3 and treats as
> obsolete-**as-error**.
>
> **Cause:** the pinned version was wrong for the editor. `1.14.2` targets Unity `6000.1`; the
> version released for `6000.6` is **`1.19.0`**. Fixed by bumping the pin.
>
> **What this means for everything below:** Unity halts at the first failing assembly, and package
> assemblies compile before user assemblies. **So none of our C# has been compiled yet** — every
> verdict in this audit remains exactly as unverified as it was before the editor was opened.
> The risks were neither confirmed nor cleared.
>
> **Lesson recorded:** the four packages removed earlier for being unused were the right call but
> the wrong target. The dangerous pin was the one package we actually need. Pinned package versions
> must be checked against the editor version, not merely against whether the package is used.

**This audit does not claim the project compiles.** Nothing here has been through a C# compiler —
there is no Unity, no .NET SDK and no Mono in the environment this was written in. What follows is
a static reading of the API surface against what I can verify, with everything uncertain named.

---

## 1. Namespaces used, and the assembly that must reference them

| Namespace | Files | Required reference | Present in asmdef? | Package in `manifest.json`? |
|---|---:|---|---|---|
| `UnityEngine` | 25 | implicit (engine) | n/a | n/a |
| `UnityEngine.UIElements` | 13 | `UnityEngine.UIElementsModule` | implicit | `com.unity.modules.uielements` ✅ |
| `UnityEngine.SceneManagement` | 4 | implicit | n/a | n/a |
| `UnityEngine.UIElements.Experimental` | 3 | implicit | n/a | n/a |
| `UnityEngine.InputSystem` | 3 | `Unity.InputSystem` | ✅ in `ForgottenIsle.Game.asmdef` | `com.unity.inputsystem` ✅ |
| `UnityEngine.TestTools` | 2 | `UnityEngine.TestRunner` | ✅ in both test asmdefs | `com.unity.test-framework` ✅ |
| `UnityEditor`, `UnityEditor.SceneManagement` | 2 | editor-only asmdef | ✅ `ForgottenIsle.Editor`, `includePlatforms: ["Editor"]` | n/a |
| `NUnit.Framework` | 9 | `nunit.framework.dll` precompiled | ✅ in EditMode asmdef | via test-framework ✅ |

**No namespace is used without a matching asmdef reference.** `ForgottenIsle.Core` uses none of
them — it references no engine assembly at all (`noEngineReferences: true`), verified by gate.

## 2. Risk table by subsystem

| Subsystem | Verdict | Reasoning |
|---|---|---|
| `ForgottenIsle.Core` (36 files) | **LIKELY COMPILE SAFE** | Pure C# against BCL only. No engine types, no reflection, no `dynamic`, no unsafe. The largest single risk in the project is absent here by construction. |
| Save codec & file store | **LIKELY COMPILE SAFE** | Hand-rolled JSON reader/writer (ADR-0013). **No `JsonUtility`, no `Activator.CreateInstance`, no reflection anywhere** — verified by grep. This is the subsystem most likely to break under IL2CPP in a typical project, and it sidesteps the mechanism entirely. |
| State machine, commands, session | **LIKELY COMPILE SAFE** | Plain classes, `in` parameters on readonly structs (42 sites), no generic virtual methods on value types. |
| Scene loading / `ZoneRegistry` | **NEEDS UNITY VERIFICATION** | `SceneManager.LoadSceneAsync` / `UnloadSceneAsync` and `AsyncOperation.progress` are stable API, but the **0.9 progress cap with `allowSceneActivation = false`** is behaviour, not signature, and the loader's activation gate depends on it. Already carries a `⚠ VERIFY` in code. |
| `ZoneFurnisher` (new) | **NEEDS UNITY VERIFICATION** | `GameObject.CreatePrimitive`, `SceneManager.MoveGameObjectToScene`, `Collider.bounds`, `Light.type` are all long-stable. The uncertainty is behavioural: whether `Collider.bounds` is populated in the same frame a scene finishes loading (the code deliberately uses bounds rather than a raycast to avoid needing a physics tick — this needs confirming in the editor). |
| Input (`VardholmControls`) | **HIGH RISK** | Actions are built **in code**, and the composite bindings use string syntax: `AddCompositeBinding("2DVector(mode=2)")` with `<Keyboard>/w` style paths and `processors:` strings. These are parsed at runtime, **not checked by the compiler**. A typo compiles cleanly and produces an action that silently never fires. This is the single most likely "compiles but does not work" area in the project. |
| UI Toolkit framework | **NEEDS UNITY VERIFICATION** | `rootVisualElement`, `AddToClassList`, `pickingMode`, `RegisterCallback`, `schedule` are all stable runtime API. `PanelSettings.scaleMode` / `referenceResolution` / `screenMatchMode` and the enums `PanelScaleMode.ScaleWithScreenSize`, `PanelScreenMatchMode.Shrink` are believed correct but **not verified against 6000.6 docs**. |
| UI animation | **HIGH RISK** | `element.experimental.animation.*` used in `ToastLayer`, `LoadingCurtain`, `ScreenStack`. **It is in a namespace literally named `Experimental`** — Unity reserves the right to change or remove it between versions, with no deprecation contract. If it has moved in 6000.6, three files fail to compile. **Contained by design:** all five call sites are inside transition helpers; the fallback is to set final style values with no tween. |
| `UiInstaller` / runtime `UIDocument` | **NEEDS UNITY VERIFICATION** | Creating `UIDocument` via `AddComponent` and `PanelSettings` via `ScriptableObject.CreateInstance` at runtime is legitimate but uncommon — most projects author both as assets. The enable-cycle workaround (a `UIDocument` only builds a root while enabled *and* holding panel settings) is the kind of thing that is right or badly wrong with nothing in between. |
| `DevOverlay` | **NEEDS UNITY VERIFICATION** | If it uses `ProfilerRecorder`, the counter name strings differ per platform and must degrade when `Valid == false`. Already flagged in the technical architecture ledger. |
| Editor tooling (new) | **NEEDS UNITY VERIFICATION** | `EditorSceneManager.NewScene/SaveScene/OpenScene`, `EditorBuildSettings.scenes`, `AssetDatabase.CreateFolder`, `EditorUtility.DisplayDialog`, `EditorApplication.delayCall` — all long-stable API. Risk is low but the whole setup flow depends on it, so it is not called safe until it has run once. |
| Tests | **NEEDS UNITY VERIFICATION** | `[UnityTest]` + `IEnumerator`, `[UnitySetUp]`, `Assert.Ignore`, `FindAnyObjectByType(FindObjectsInactive)`. The `FindObjectsInactive` / `FindObjectsSortMode` overloads are the modern replacements for the obsolete `FindObjectOfType`; correct for Unity 6, but the exact overload set should be confirmed. |

**No subsystem is rated LIKELY COMPILE SAFE unless it is pure C# with no engine surface.** That is
deliberate: without a compiler, "it looks right" is not evidence.

## 3. Unity serialization review

| Concern | Finding |
|---|---|
| Interfaces serialized | **None.** `ISaveParticipant`, `ICoreLog`, `ILocalizedText`, `IClock` are all constructor-injected into plain classes, never `[SerializeField]`. |
| Constructors on serialized types | **None.** The only `[SerializeField]` members in the project are four fields on `PlayerRig` (three floats and a `Transform`). |
| `record` types | **Zero in use.** The `IsExternalInit` polyfill is present but currently unused — harmless, and it stays because ADR-0005's baked definitions will want it. |
| `readonly` fields | Used widely, but **only in non-serialized classes and structs**. Unity cannot serialize `readonly`, and nothing asks it to. |
| Polymorphic serialization | **None.** No `[SerializeReference]`, no abstract base serialized. |
| `JsonUtility` limits | **Not used at all.** The save path is a hand-rolled reader/writer in engine-free Core, which also means saves are not subject to `JsonUtility`'s no-dictionaries / no-polymorphism / no-null limitations. |
| `ScriptableObject` references | One, created at runtime (`PanelSettings`), never serialized to an asset. |

**Net: the serialization surface is almost nil.** That is the direct payoff of the engine-free
Core decision (ADR-0002) — state lives in plain C# objects that Unity never has to round-trip.

## 4. IL2CPP and managed stripping

| Risk | Assessment |
|---|---|
| Reflection-based serializers | **Not present.** The usual IL2CPP save-system failure cannot occur here. |
| `Activator.CreateInstance` | **Not present.** |
| Generic virtual methods on value types | `CommandDispatcher.Dispatch<TCommand>` stores handlers as `object` in a `Dictionary<Type, object>` and casts to `ICommandHandler<TCommand>`. Commands are **structs**, so this is a generic interface call on a value type — the exact shape AOT can fail to generate. **NEEDS UNITY VERIFICATION on a device build.** Every command type is registered explicitly at the composition root, so the concrete instantiations are statically reachable, which should be enough for IL2CPP to generate them — but "should" is not "verified". |
| Managed stripping | Default **Low** for Phase 1 per the plan. Nothing depends on reflection, so no `link.xml` is expected to be needed. **Must be re-tested if stripping is raised.** |
| `[Conditional]` compile-out | `VardholmStartupValidator.RunAndLog` is `[Conditional("UNITY_EDITOR")]` + `[Conditional("DEVELOPMENT_BUILD")]`, so both the call and its argument evaluation vanish from a release player. |

## 5. What to check first when the editor opens

In this order, because each one blocks the next:

1. **Does it compile at all?** Expect the first failures in `UI/Core` (`experimental.animation`) or
   `PanelSettings` property names.
2. **Run `Vardholm → Setup Project`.** Four scenes, build settings written.
3. **Press Play on `Bootstrap`.** Read the `VARDHOLM STARTUP CHECK` block — it will name any
   missing scene, service or localization table directly.
4. **Run EditMode tests.** Pure logic; failures here are real logic bugs, not integration issues.
5. **Check input actually moves the capsule.** This is where the code-built binding strings get
   their only real test.
6. **Run PlayMode tests.** They self-skip with a clear message if setup was not run.

## 6. Honest summary

| Verdict | Subsystems |
|---|---|
| **LIKELY COMPILE SAFE** | 3 — Core, save codec, state/commands/session |
| **NEEDS UNITY VERIFICATION** | 7 — scene loading, furnisher, UI Toolkit, UiInstaller, DevOverlay, editor tooling, tests |
| **HIGH RISK** | 2 — input binding strings, UI `experimental.animation` |

The two HIGH RISK items are both **runtime-string / experimental-API** problems rather than
structural ones, and both are contained: input bindings live in one file, and the animation calls
are five isolated sites with an obvious non-animated fallback.
