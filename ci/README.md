# CI checks

Two scripts live here.

| Script | Asks | Needs |
|--------|------|-------|
| `check-layering.sh` | "does this code respect the architecture?" | POSIX `sh` |
| `validate-structure.py` | "is this code even real?" | Python 3.8+ |

`check-layering.sh` enforces the five architectural invariants that nothing else
in the project can enforce. `validate-structure.py` stands in for the C# compiler
itself, which this project is written without. Run both; they overlap only on
Core's engine-freedom, deliberately, because that rule is worth two locks.

---

# check-layering.sh

It enforces the five architectural invariants that nothing else in the project
can enforce — not the C# compiler, not Unity's assembly definition system, not
code review at 11pm.

```sh
sh ci/check-layering.sh            # from the project root
sh ci/check-layering.sh /path/to/MysteriousIsland   # or point it anywhere
```

Exit `0` means clean. Exit `1` means at least one violation, each printed as
`VIOLATION <file>:<line>: <reason> -> <offending source>`. Exit `2` means the
script could not run at all (the path it was given is not a Unity project root).

It is POSIX `sh` plus `grep`, `sed`, `awk` and `find`. No `jq`, no Python, no
Unity. That is deliberate: this must run on a bare CI container in under a
second, on a checkout with no `Library/` folder and no editor licence.

---

## The checks

### 1. `Assets/Scripts/Core/` must not name `UnityEngine` or `UnityEditor`
**Source: ADR-0002 — `ForgottenIsle.Core` is engine-free without exception.**

Core is the simulation. It is the part that can be unit-tested in a plain
`dotnet test` run, ported to a server, or replayed headlessly. That property
survives only as long as nothing in it touches the engine — and the cost of
losing it is not visible on the day it is lost, it is visible two years later
when someone tries to write a headless determinism test and finds Core needs a
`UnityEngine.Vector3` to construct a save.

`Vec3`, `IslandClock` and `ICoreLog` exist precisely so Core never needs
`Vector3`, `Time.time` or `Debug.Log`.

**What it actually matches:** the file with `//` line comments and `/* */` block
comments blanked out first. A doc comment that says "Core cannot reference
UnityEngine" is documentation of the rule, not a breach of it, and four such
comments exist in Core today. String literals are *not* stripped, so
`Type.GetType("UnityEngine.Vector3")` is still caught — reflection is the only
way to get the engine into Core past the asmdef, so it is the case worth
catching.

### 2. `ForgottenIsle.Core.asmdef` must set `"noEngineReferences": true`
**Source: ADR-0001 (assembly layout) and ADR-0002.**

Check 1 catches a file that uses the engine. This catches the *setting* that
would let such a file compile. Both are needed and they fail at different times:
with the flag set, a developer who types `using UnityEngine` in Core gets a red
error in the Editor within seconds. Without it, they get a green build and CI
tells them an hour later. The flag is the fast feedback loop; this check keeps
the flag.

Parsed by collapsing whitespace and matching `"noEngineReferences":true`, so key
order and formatting in the asmdef are free to change.

### 3. `ForgottenIsle.Core.asmdef`'s `"references"` array must be empty
**Source: ADR-0001 — assembly layout.**

Core sits at the bottom of the dependency graph: `UI -> Game -> Core`, never the
other way. An entry in Core's `references` means something exists below Core,
and the moment that happens "Core is self-contained" stops being true. The usual
way this breaks is innocent — someone adds a `ForgottenIsle.Shared` utility
assembly and references it from Core "just for one helper". The helper then
grows an engine dependency and ADR-0002 is gone by transitivity.

If Core genuinely needs something, it goes *in* Core.

### 4. No file under `Assets/Scripts/UI/` may reference `SessionService`
**Source: ADR-0012 (hand-wired composition) and ADR-0014 (UI Toolkit).**

`SessionService` owns `GameState` and is the only thing allowed to mutate it.
Every legal mutation has a command, every command has a handler, and every
handler runs `Validate` before `Execute` — which is where `ResultCode
.IllegalStateTransition` and `ResultCode.NotAllowedInState` come from.

A screen holding a `SessionService` reference bypasses all of that. The bug it
produces is not a crash; it is a save button that works while the game is in
`Loading`, or a travel request that mutates `SessionState.ZoneId` for a zone
that then fails to stream in. Both leave a save file describing a world that was
never entered.

UI talks to a controller in `ForgottenIsle.UI.Controllers`; the controller
dispatches a command; `CommandDispatcher` returns a `CommandResult` the screen
can render. Comments are stripped before matching, so a doc comment explaining
*why* a screen does not use `SessionService` does not fail the build.

### 5. No user-facing string literal in UI
**Source: ADR-0014, and the project-wide rule that all player text is
`LocKey` + `ILocalizedText`.**

This one is a heuristic and is documented as such. It flags `Label("` and
`Button("` — a string literal handed straight to a widget constructor.

Correct code never looks like that: real text arrives as
`_loc.Get(new LocKey("ui.menu.continue"))`, which puts an identifier after the
open paren, not a quote. So the check is quiet on correct code and loud on the
exact shape of mistake it is hunting: someone typing `new Label("Continue")` at
speed because the string table row does not exist yet.

The reason this matters more than it looks: a hardcoded string is not just
untranslatable, it is *invisible*. A missing key renders as `#ui.menu.continue#`
and any tester spots it in one second. A hardcoded English string renders
perfectly in English and is discovered by a French player.

**Opt-out:** a line that genuinely needs a literal — a USS class name passed to
a helper that happens to be called `Label` — can end with

```csharp
// ci:allow-literal
```

The marker is read from the raw line *before* comments are stripped. Use it
rarely and never for text a player reads.

---

## Adding a check to check-layering.sh

Keep the shape: print `VIOLATION <file>:<line>: <reason>`, increment
`VIOLATIONS`, and leave the exit code to the tail of the script. A check that
cannot name the file and line is a check that gets ignored.

A new check needs a new subsection here naming the ADR it comes from. A check
with no ADR behind it is a style preference, and style preferences belong in an
editorconfig, not in a gate that blocks merges.

---

# validate-structure.py

```sh
python3 ci/validate-structure.py                      # from the project root
python3 ci/validate-structure.py /path/to/MysteriousIsland
python3 ci/validate-structure.py . --quiet            # errors only
python3 ci/validate-structure.py . --strict           # warnings fail the build too
```

Exit `0`: no errors (warnings may still have printed). Exit `1`: at least one
error, each printed as `ERROR [CHECK] <file>:<line>: <reason>`. Exit `2`: the
script could not run — the path is not a Unity project root, or a source file
could not be read.

Python 3.8+, standard library only. No `pip install`, no Unity, no `dotnet`.

## Why this exists

This project is being written in an environment with **no C# compiler and no
Unity**. Nothing checks that the code parses. Nothing checks that a type someone
referenced was ever written. Every file is correct only to the extent that the
person who typed it was paying attention, and the first machine to disagree will
be a laptop opening the project for the first time, with forty files of drift to
untangle at once.

This script is the stand-in. It is a lexer plus seven structural rules. It
cannot prove the project compiles — it does not resolve `using` directives, does
not know method signatures, and has no concept of accessibility. But every error
it reports is something that definitively would **not** compile, or would fail at
runtime. There are no style opinions in it.

Run it before every commit. It takes well under a second on the whole project.

## The checks, and what each one substitutes for

### A. `BALANCE` — substitutes for the parser

Every `{}`, `()` and `[]` in every `.cs` file closes, in order.

A missing brace is the single most likely defect in code written without a
compiler, it is invisible in review (the indentation still looks right), and in
Unity it does not fail politely: one unbalanced file fails the whole assembly,
so the Console fills with errors from *other* files that were fine. This check
names the line the unclosed bracket was opened on, which is the only piece of
information that actually shortens the hunt.

**This is a real lexer, not a counter.** It handles line and block comments,
regular strings with backslash escapes, verbatim strings (`@"..."` with `""`
escapes), interpolated strings (`$"..."`, `$@"..."`) including `{{`/`}}` escapes
and arbitrarily nested holes, and char literals. A naive counter reports a false
error on `private const char OpenBrace = '{';`, on `"{\"a\":[1,2]}"`, and on an
XML doc comment containing the word `class` — and a checker that cries wolf is a
checker people stop running.

Preprocessor directive lines (`#if`, `#endif`, `#region`) are dropped before the
count. They carry no brackets. An `#if`/`#else` pair whose branches open a
different number of braces would produce a false error here; it would also be
unreadable, and nothing in this project does it.

### B. `NAMESPACE` — substitutes for the IDE's file/namespace sync

Everything under `Assets/Scripts/Core` declares a namespace starting
`ForgottenIsle.Core`; `Game` → `ForgottenIsle.Game`; `UI` → `ForgottenIsle.UI`;
`Assets/Tests` → `ForgottenIsle.Tests`.

A namespace that does not match its folder compiles perfectly. It fails later,
socially: two `SaveCodec` types drift apart because nobody could find the first
one, and the `using` that would have collided was never written. The one
exemption is `System.Runtime.CompilerServices`, which the `IsExternalInit`
polyfill **must** declare — the C# 9 compiler recognises that type by full name
only, so moving it into a project namespace silently disables every `init`
setter in the codebase (ADR-0003).

### C. `CORE-ENGINE` — substitutes for `noEngineReferences`

No `UnityEngine` or `UnityEditor` token anywhere under `Assets/Scripts/Core`
(ADR-0002).

Comments are stripped first: a doc comment explaining that Core cannot reference
the engine is documentation of the rule, not a breach. String literals and
`#if UNITY_EDITOR` directives are *not* stripped — `Type.GetType("UnityEngine.
Vector3")` and a conditional compilation block are the two ways the engine gets
into Core past the assembly definition, so they are the cases worth catching.

This duplicates check 1 of `check-layering.sh` on purpose. Core's engine-freedom
is the load-bearing property of the whole architecture and it is cheap to check
twice.

### D. `ASMDEF` — substitutes for Unity's assembly definition importer

Every `.asmdef` is valid JSON; its `"name"` matches its filename; and
`ForgottenIsle.Core` has `"noEngineReferences": true` and an empty
`"references"` array.

Unity resolves assembly references by the `name` **field**, not the filename, so
a mismatch produces an assembly that exists but that nothing can reference — and
the error it eventually throws names neither file. A malformed `.asmdef` is
worse: Unity ignores it, silently merges every script in that folder into
`Assembly-CSharp`, and the layering the asmdefs were meant to enforce quietly
stops existing.

`"includePlatforms"` is warned about when it is neither `[]` (ships everywhere)
nor `["Editor"]` (the EditMode test assembly, which is supposed to be excluded
from players).

### E. `CONTRACT` — substitutes for the type checker (the important one)

Every type used as a **base type, field type, parameter type, local declaration,
generic argument, `new` target, `typeof` argument, or `is`/`as` operand** is
declared somewhere in this project, or is a known BCL / UnityEngine / NUnit
type.

This is the check the others exist to support. When a codebase is written by
several people or agents working from a shared contract document, the failure is
never a syntax error — it is one file calling `SaveVault.Write(...)` while
another wrote `SaveFileStore.Save(...)`, both of them internally consistent,
neither of them compiling together. Nothing but a cross-file pass finds that,
and there is no compiler here to do the pass.

**Scope, and why it is narrow.** Member access (`Something.Member`) is
deliberately *not* scanned. A PascalCase property reads exactly like a static
class, so scanning it would report `Context.Session` as an undeclared type
`Context` — and a hundred false positives would bury the one real finding. The
positions listed above are the ones where an identifier can only be a type.

**The whitelist.** Everything the project legitimately gets from outside is
listed in `BCL_TYPES`, `UNITY_TYPES` and `TEST_TYPES` near the top of the
script. Adding a name is how you resolve a false positive — but only after
confirming the type really exists in a referenced assembly. Whitelisting a name
to make a report go away is how this check stops working; if a report surprises
you, the type probably really is missing.

Generic type parameters are recognised by declaration and, as a fallback, by the
project's `T`/`TCommand` naming convention.

### F. `LOCKEY` — substitutes for a translator, six months early

Every literal passed to a `LocKey` constructor that matches the house key shape
(`^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`) has a row in
`Assets/Localization/en.csv`. A missing row is an **error**; a row nothing
references is a **warning**.

A missing key is not a crash — `StringTableLocalization` answers it with
`#ui.menu.continue#` and the game runs. That is the whole problem: it survives
review, it survives QA in English, and it is found by a player. This check is
the only thing that will notice before then.

A key that is *not* lowercase dotted (`ui.menu.newGame`) is reported as a
warning rather than silently skipped — an unchecked key is worse than an ugly
one, and the warning says plainly whether the key is also absent from the CSV.

Unused keys are warnings, not errors, because a key can be composed at runtime:
`SceneKeys.ZoneDisplayKey` builds `"zone." + name + ".name"` and no literal for
the whole key exists anywhere. To keep those out of the noise, any string
literal in the project that looks like a key fragment suppresses the warning for
keys it prefixes or suffixes. The suppression is a heuristic; the check is a
warning precisely because it is one.

Test sources are excluded in both directions. A localization test must construct
keys that deliberately do not exist — proving the `#key#` fallback fires is most
of what there is to test — so including them would make this check permanently
red. And a row used only by a test is a row no player sees, so it still counts as
unused.

The script also compares `Assets/Localization/en.csv` against
`Assets/Resources/Localization/en.csv`. The authoring copy is the one people
edit; the Resources copy is the only one a **player build can load**. A key
present in the first and missing from the second ships as `#key#` to players and
looks perfect in the Editor, so the divergence is an error, not a warning. The
fix is the copy command in the CSV's own header comment.

The CSV parser here is a deliberate port of
`ForgottenIsle.Core.Data.CsvTableParser` — quoted fields, doubled quotes,
whole-line `#` comments, CRLF — so that "the validator sees this key" and "the
game sees this key" cannot drift apart.

### G. `DUPLICATE` — substitutes for CS0101

No public type name is declared twice in the same namespace.

Two files each declaring `public sealed class SaveCodec` in
`ForgottenIsle.Game.Saves` is a guaranteed CS0101, and it is exactly what
happens when two people implement the same contract line independently.
Nesting is resolved by brace depth, so a nested `Entry` inside two different
classes is correctly not a duplicate, and `partial` declarations are allowed to
repeat.

## What it does not check

Not a compiler. In particular it does not know about: `using` directives and
whether a type is actually *reachable* from a file, method signatures and
arity, accessibility (`private` members touched from outside), return types,
nullability, or whether a member exists on a type. A green run means the
project is structurally coherent, not that it builds.

The first person to open this project in Unity is still the real test. This
script exists so that what they find is a short list of typed mistakes rather
than an afternoon of untangling.

## Adding a check to validate-structure.py

Add a `check_*` function that takes `report` as its first argument, calls
`report.error(...)` / `report.warn(...)` with a file and line, and wire it into
`main` with `report.checks_run += 1`. Never `sys.exit` from inside a check:
every check runs to completion so one commit's worth of problems is reported in
one pass.

Then document it here, with the failure it catches and why a human would not
catch it first. A check that cannot answer that belongs in an editorconfig.
