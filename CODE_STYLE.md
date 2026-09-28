# Code style

How code in Pawn Editor Forked is written. The goal is code a stranger can open and understand
quickly, and enjoy reading.

## Comments

### Documentation comments (`///`)

Every public or internal type, and every method whose purpose isn't obvious from its name, gets one.

- **`<summary>`: what it is, in one or two lines.** This is the tooltip in the IDE, so keep it short.
- **`<remarks>`: why it exists, decisions, history.** One idea per `<para>`.
- Use `<c>` for code names and `<see cref="..."/>` for links to other members.

```csharp
/// <summary>
/// Error boundaries with context: runs a named piece of work and, if it throws, logs once who failed
/// and on what, instead of taking the whole window down.
/// </summary>
/// <remarks>
/// <para>
/// It does not rethrow, on purpose: the player sees one empty section instead of a dead window, and we
/// still get the log.
/// </para>
/// <para>
/// It logs once per section, subject and exception type. Without that rule one failure would write
/// sixty lines a second and bury everything else.
/// </para>
/// </remarks>
public static class Diagnostics
```

**Partial classes:** only the main file carries the `<summary>`, and its `<remarks>` lists the partial
files. Each partial starts with a short `//` header saying what it holds.

Avoid long `<summary>` blocks that mix what and why, `/* */` for documentation (tooltips won't show
it), and commented-out code (git keeps the history).

### Inline comments (`//`)

Explain **why**, never what the next line does. When a line looks wrong but is deliberate, say so:

```csharp
// The argument is required: for a struct, `new CaretVisibilityScope()` would call the implicit
// default constructor and skip our logic entirely.
using (new CaretVisibilityScope(true))
```

## Language

Code, comments, commit messages and public text are in English. User-facing strings go through
translation keys in `Languages/English/Keyed`, never hardcoded.

## Errors

- Third-party code and mod content run inside `Diagnostics.Run(section, subject, work)`: a failure is
  logged once with the pawn or def and its mod, and the rest of the window keeps working.
- Never `catch (Exception) { }` without saying what failed and on what.

## Layout

UI rects are split with `Utils/Layout.cs` (`TryTakeTop`, `Columns`, `Proportional`), not hand-rolled
arithmetic. Code that draws UI never mutates `windowRect`.

## Structure

- Small methods whose name says what they do. A stack trace should point at the brick, not the
  haystack.
- Big windows are split into `partial` files by responsibility (`Dialog_AppearanceEditor_StylePickers.cs`).

## Tests

- Pure logic: `Source/PawnEditor.Tests` (NUnit, no game needed).
- Behaviour with real pawns: `InGameTests/` (run with `run-ingame-tests.ps1`).
- Before refactoring something, write the tests against the current code, see them pass, then cut.
