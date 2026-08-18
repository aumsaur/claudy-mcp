# Claudy TODO

Ordered by dependency, not by how it was requested. Items build on each other in
this order — do them top to bottom unless a later item is specifically wanted first.

## 1. Sprite-based icon system (unblocks #2 and #3) — DONE 2026-08-16

Generated `follow.png`, `prompt.png`, `back.png` via PixelLab `create-image-pixflux`
(32x32, `no_background: true`) into `Assets/icons/`. Toy submenu reuses `ball.png`
directly rather than a separate icon. `RadialItem` (new class in `RadialMenu.xaml.cs`)
carries `SpritePath` and renders via `Image` + `RenderOptions.BitmapScalingMode`,
same pattern as `ToyMarker`. `prompt.png` exists but isn't wired to a menu item yet
- that's #4.

Replace emoji glyphs used as *UI chrome* (radial menu items, submenu/back icons)
with small bitmap icons. This is prep work both the radial menu rework and the
emoji-color fix depend on, so do it first.

- Generate small (32x32-ish) icon sprites via PixelLab for whatever the top-level
  radial items end up being (see #3): a "follow/wave" icon, a "toy" icon, a
  "prompt/chat" icon, plus a "back" icon for leaving a submenu.
- `RadialMenu.xaml.cs` currently renders each item as an emoji `TextBlock` inside
  a ball (`("emoji", "label", action)` tuples). Change the item shape to carry
  either an emoji *or* an image path, and render via an `Image` control the same
  way `ToyMarker` already does when a `spritePath` is supplied (see
  `ToyMarker.xaml.cs` constructor - same pattern, reuse it).
- Toy submenu items (#3) can reuse the *existing* toy sprites (`ball.png` etc.)
  at small size instead of needing new icons.

## 2. Emoji-not-color fix — DONE 2026-08-16

Confirmed the quick test doesn't work (tried `FontFamily="Segoe UI Emoji"` +
`TextOptions.TextFormattingMode="Display"` on `InfoBubble`'s TextBlock,
rebuilt, triggered the pat-reaction heart bubble, screenshotted and zoomed in
- still a flat white glyph). Reverted that and built the real fix:

- `EmojiIcons.cs` (new) - a lookup of emoji character → `Assets/icons/*.png`
  (or `Assets/toys/ball.png` for 🎾, `Assets/icons/follow.png` for 👋 - reused
  existing assets rather than regenerating), plus `SetRichText(TextBlock, text)`
  which scans the string and builds `TextBlock.Inlines` as alternating `Run`
  (plain text) / `InlineUIContainer(Image)` (icon) segments. Falls back to the
  raw emoji character in a `Run` if its icon file is missing, same tolerant-
  loading pattern as the mood/talk sprites.
- Generated 14 icons via `create-image-pixflux` (32x32, `no_background: true`):
  emoji_happy/sleepy/bored/excited/eyes/cookie/sparkle/heart/devil/laugh/
  flushed/party/point, plus emoji_yarn (generated but turned out unused -
  🧶 only appears as a `ToyMarker` emoji fallback, never in bubble text).
- `InfoBubble.xaml.cs` now calls `EmojiIcons.SetRichText(MessageText, text)`
  instead of `MessageText.Text = text` - this covers every mood/casual/social
  bubble in one place. Verified live via the pat-reaction heart bubble
  (screenshotted + zoomed in - hearts render pink now, not a white outline).

**Not covered / left alone:** the tray icon's native WinForms `ContextMenuStrip`
text (`"🧶 Yarn"` etc.) is a completely different rendering pipeline (GDI-based,
not WPF) and wasn't touched - if that also needs fixing later it's a separate,
more invasive job (owner-drawn menu items). `PromptWindow`'s question text
(whatever Claude's `ask_user` call passes in) also isn't run through
`EmojiIcons` - only bubble text was in scope here.

## 3. Radial menu levels (depends on #1) — DONE 2026-08-16

`RadialItem.Children` + `RadialMenu.RenderLevel()` (re-clears and rebuilds
`ItemsCanvas`, re-runs the staggered elastic launch animation) + a `Stack` of
parent levels for Back navigation. Top level is currently **Follow, Toy**
(Prompt deliberately left out until #4 gives it something to do). Toy submenu
is **Ball, Back** only (Yarn/Wand dropped per #5/#6, not carried over).
Verified live: menu opens with real icons, Toy → submenu with correct
animation replay, Back → returns to root, no stray state between levels.

**Bug found and fixed during this**: rapid clicks (click a leaf item, or
click-away right as the window is losing activation) could call `Close()`
on the `RadialMenu` window while a previous `Close()` was still tearing it
down → `InvalidOperationException: Cannot ... Close ... while a Window is
closing`, crashed the whole pet. Fixed with a `_closing` guard flag behind a
`TryClose()` helper that all four close-triggering paths (click-away, Escape,
leaf-item select, Deactivated) now go through instead of calling `Close()`
directly. If any *other* window in this codebase has multiple independent
paths that can call `Close()`, check it for the same race.

**Note for later:** the pet's default spawn corner (bottom-right, 24px margin)
means a submenu item positioned straight down from center (100px radius) can
land inside the Windows taskbar strip, which steals the click before the
topmost pet window sees it. Not fixed - most real usage won't hit this exact
geometry (items aren't always straight-down, and the user can drag the pet
away from the very edge), but worth knowing if a "click did nothing" report
comes in from someone whose pet sits right in the corner.

## 4. Prompt menu item (depends on #3) — DONE 2026-08-16

Built exactly as scoped - no new channel, just a UI affordance over the
already-open socket. `PromptWindow.ShowFreeTextOverride()` (new method) adds
a "type your own answer" text box + Submit button below whatever buttons are
already showing (yesno/choice/text - idempotent, and a no-op focus if the
question was already kind "text"). Radial menu's Prompt item
(`MainWindow.ActivatePromptFreeText()`) calls it when `_activeBubble is
PromptWindow`, otherwise shows a "Nothing's asking right now! 👀" bubble.
Verified live end-to-end: asked a real yes/no question, used Prompt → typed a
custom answer instead of clicking Yes/No, confirmed working.

Top level is now **Follow, Toy, Prompt** (3 items).

**Idle-case detour (2026-08-17, built then explicitly disabled - do not
casually re-enable without reading this):** user separately wanted the idle
case ("nothing pending, message Claude anyway") to do more than show a bubble
- expected it to work "like when I prompt in chat." Two designs were built and
both were rejected after being tried live:
1. `QueueMessageWindow` composer writing `{message, queuedAt}` to
   `.claudy-inbox.json`, picked up by a `CLAUDE.md` instruction Claude would
   check at the start of a turn. Rejected: only gets picked up whenever Claude
   is *next* invoked some other way - "still have to open vscode to trigger
   that prompt."
2. `MainWindow.SpawnClaudeWithMessage()` - immediately spawns `claude -p
   "<message>"` in a new visible PowerShell window. Required adding
   `--session-cwd <process.cwd()>` to `index.js`'s pet-spawn args (alongside
   `--parent-pid`) so the pet knows which repo root to run in -
   `MainWindow._sessionCwd`. Hit and fixed a real bug along the way: the
   spawned `claude` inherited `CLAUDE_CODE_SESSION_ID`/`CLAUDE_CODE_SSE_PORT`/
   `CLAUDECODE`/etc. from this session's own MCP-child-process environment
   (Claude Code → Node → PetOverlay.exe → spawned PowerShell), and hung
   indefinitely trying to negotiate with a "parent" session that wasn't
   actually there - confirmed via a debug log that stalled right after
   resolving the `claude` command, before any output. Fixed by spawning with
   `UseShellExecute = false` (needed so `EnvironmentVariables` is mutable -
   it's read-only when `UseShellExecute = true`) and stripping every
   `CLAUDE*`/`ANTHROPIC*`-prefixed env var before start. Confirmed working
   after the fix (real "Hi!" response, no hang). **Rejected anyway** once the
   user saw it live: every use is a disconnected new session (not the
   conversation they were actually talking to) AND leaves an open terminal
   window behind with no cleanup - repeated use just accumulates windows.
   Considered auto-closing the terminal, falling back to the queue-file
   approach, or `/loop` self-polling (rejected - ongoing token cost, in
   tension with [[feedback_token_budget_cap]]) as fixes; none felt right yet,
   so the user said to just disable it for now rather than pick one.

**Current state**: `ActivatePromptFreeText()`'s idle branch just shows the
"Nothing's asking right now!" bubble again (the original #4 behavior). The
working (env-fixed) `QueueMessageWindow`/`SpawnClaudeWithMessage`/
`QueueMessageForClaude`/`QueueMessageToInboxFile` code is still in
`MainWindow.xaml.cs` and `QueueMessageWindow.xaml(.cs)`, just not called from
anywhere - `CLAUDE.md` was deleted since nothing writes `.claudy-inbox.json`
anymore. If this gets revisited, the hard part (the env-var hang) is already
solved; the open design question is purely which of the three tradeoffs above
(or something else) the user actually wants.

## 5. Toy lock-in: Ball only — DONE for the radial menu 2026-08-16 (tray menu still open)

Radial menu's Toy submenu only lists Ball now (done as part of #3, since there
was no point building the submenu with Yarn/Wand just to remove them next).
**Not yet done:** the tray icon's right-click → Play submenu (`Toys` array in
`MainWindow.xaml.cs`, `CreateTrayIcon()`) still lists Ball/Yarn/Wand and still
works via the old `StartPlay()` path - left alone since it wasn't in scope for
the radial-menu change specifically. Revisit whether that should also drop to
Ball-only for consistency, or stays as a legacy access point.

## 6. Sparkler wand rework (independent, largest, do last)

Current Wand behavior (teleport-chase with an emoji marker, see `StartPlay` in
`MainWindow.xaml.cs`) doesn't match what's wanted: Claudy should *hold* a
sparkler and run around energetically with it, with a particle trail coming
off the sparkler tip itself.

- **New `PetMode.Sparkling`, not a reuse of the toy-chase path.** The sparkler
  is *held*, not an independent target `ToyMarker` the pet walks toward - its
  screen position is derived from the pet's own position + an offset that
  depends on `_facing` (you have 4 directional idle sprites; the "hand"/tip
  offset differs per direction - south/east/west/north each need their own
  offset tuning). Movement itself should look more energetic/erratic than
  normal wander (faster `StepToward` steps, more frequent direction changes)
  to read as "running around excitedly," not calm toy-chasing.
- **Held-sparkler sprite:** likely a new PixelLab-generated pose (or an overlay
  sprite composited near the hand offset) via `/animate-character` per the
  session's prior finding - NOT `/create-character-state` (independently
  generated states drift frame-to-frame and won't look consistent; verify the
  output canvas size matches the existing 64x64 sprites before wiring in,
  center-cropping if not - this session hit that exact bug with the talk
  animation).
- **Particle effect:** a lightweight emitter - small fading dots/streaks
  spawned each tick at the derived sparkler-tip point, short random velocity,
  fade-and-remove after a few hundred ms. Render via a full-screen overlay
  window sized like `AimLine` (`WorkingArea`-sized, `IsHitTestVisible="False"`,
  fixed size) - **not** `SizeToContent`. This session burned real time
  discovering `SizeToContent` + `AllowsTransparency` windows don't reliably
  shrink-to-fit when shown before their position is set; don't rediscover that
  for a particle canvas, just use the `AimLine` shape from the start.
  Keep particle count modest (cap total alive particles) for perf given the
  40ms tick rate this whole app already runs on.

## 7. Avatar customization - reskin (7a) + accessory overlay (7b) (new, planned 2026-08-18)

User wants to be able to change Claudy's avatar via a "Clothing" item on the
radial menu, and wants **both** a full reskin system and an accessory-overlay
system eventually - **reskin first** (explicit reprioritization from the
original plan, which had scoped this down to accessory-only). These are two
genuinely different architectures, not one feature with two settings - do not
conflate them into a single data model.

### 7a. Reskin — DONE 2026-08-18

Built exactly as planned below. First skin: a "dam dam di di cat" (the chubby
loaf-shaped tabby meme cat) reskin, from a user-supplied reference image.

**Reference image pipeline, worth remembering for any future reskin/character
work from a user-supplied image:** the user was explicitly wary going in -
"last time it use the exact image of ref i gave with some rough cutting
background" - a past attempt apparently treated a reference too literally
and left background artifacts. This time: (1) `/remove-background` first
(`background_removal_task: "remove_complex_background"`, plus a `text` hint
describing the foreground subject) on the raw reference - PixelLab's own
dedicated endpoint for exactly this, rather than trusting a generation
endpoint to ignore background pixels on its own; (2) fed the *cleaned*
result as `concept_image` to `/create-character-pro` with
`method: "create_from_concept"` (not `create_with_style` or
`rotate_character` - "concept" is the mode that explicitly means
*reinterpret*, not *copy*) and `template_id: "cat"` (quadruped skeleton -
`create-character-with-4-directions`'s `template_id` field documents
`bear`/`cat`/`dog`/`horse`/`lion` as the quadruped options, same field
exists on `-pro`). Result: clean 4-direction (technically 8, only 4 used)
sprite set, no background bleed-through, proper legs/tail on the side/back
views from the cat skeleton, and it visibly resembles the reference.
**If asked to work from a reference image again, this remove-background →
create_from_concept pipeline is the one to reuse**, not
`/create-character-with-4-directions` (no concept-image input at all, its
`color_image` field is palette-only) or `/create-character-v3`'s
`reference_image` (treats the input as a literal south pose to rotate,
not a concept to reinterpret - fine for "I already have exact pixel art,
just rotate it," wrong for "reinterpret this reference in Claudy's style").

Assets at `Assets/claudy/skins/cat/{south,east,west,north}.png` (no
skin-specific mood/talk - falls back to the base skin's happy/scared/talk
frames per the fallback design below, until the cat-specific mood/talk
animation was added on 2026-08-18, see below). New `SkinDef.cs` (data model),
`MainWindow.LoadSkin()`/`LoadSprites()` (loading, tolerant per-skin same as
mood/talk always were), `ApplySprite()` (rendering, resolves through
`_currentSkin` with fallback to `_baseSkin`), `BuildSkinMenuItems()`/
`SelectSkin()` (radial menu wiring - new "Clothing" top-level item, generated
`clothing.png` icon same as the other UI icons, submenu lists every loaded
skin using each skin's own south sprite as its menu icon). Verified live:
skin switch renders correctly across at least south and east (the pet was
mid-turn when checked), nameplate-below positioning still correct with the
new skin, no crash.

**Cat mood/talk animation added (2026-08-18):** cat had only 4 directional
sprites, so `ApplySprite()`'s fallback made it use the *base slime's*
mood_happy/mood_scared/talk frames when cat was the active skin - visually
inconsistent (a blue blob's expression on a cat body). Generated cat-specific
versions using the character_id from the original `create-character-pro`
generation (persisted in this session's scratchpad, `980c70ab-...`):
`/create-character-state` (`edit_description` per mood,
`use_color_palette_from_reference: true` to stay on the cat's palette) for
`mood_happy.png`/`mood_scared.png` - came back pre-sized at the character's
64x64 canvas, no cropping needed. `/animate-character` (`mode: "v3"`,
`action_description: "talking, mouth opening and closing"`, `frame_count: 8`,
`directions: ["south"]`) for the talk cycle - same endpoint/params as the
base skin's talk animation. Response canvas was 68x68 (padding around the
64x64 character, same phenomenon as the base skin's talk frames originally
hit) - center-cropped 2px per edge back to 64x64. `keep_first_frame` defaults
true so the response includes 9 frames (a static reference frame 0 + 8
generated); dropped frame 0 and used frames 1-8 as `talk_0.png`..`talk_7.png`
to match the base skin's 8-frame convention exactly. No code changes needed -
`LoadSkin()` already tolerantly picks up `mood_*`/`talk_*` files per-skin.

**Verification note - mouse-input automation doesn't work reliably in this
environment**: tried scripting the actual radial-menu click path
(right-click to open, left-click Clothing, left-click cat) via both
`mouse_event` and `SendInput` from a background PowerShell process to
visually confirm live. Neither worked - clicks never reached the pet window.
Root-caused: `[System.Windows.Forms.Cursor]::Position` set to one value read
back as something else entirely, and even the native `GetCursorPos` reported
a third, different value - some remote/virtualized-display layer in this
sandboxed environment doesn't keep synthetic cursor coordinates consistent
with what `CopyFromScreen` actually captures, so clicks land at effectively
undefined screen locations. **If UI automation is needed again, don't trust
`Cursor.Position`/`mouse_event`/`SendInput` coordinate math in this
environment without first cross-checking `GetCursorPos` against where the
click actually landed** - it silently fails rather than erroring.
Worked around it for this verification with a temporary env-var-gated debug
hook in `MainWindow`'s `Loaded` handler (`CLAUDY_DEBUG_SKIN`/
`CLAUDY_DEBUG_MOOD`, forces `_currentSkin` and calls `SetExpression()`
directly, bypassing the menu entirely) - added, used to screenshot both
`mood_happy` and `mood_scared` rendering correctly on the cat, then fully
reverted before the final build. Talk-frame cycling wasn't separately live-
verified (would need a real `PromptWindow` instance, more setup than
seemed worthwhile) - covered instead by the already-correct static frame
images (visually inspected, correctly cropped) plus the fact that
`ApplySprite()`'s talk-frame fallback is the identical `??` pattern just
proven live for mood sprites, on code shared verbatim with the base skin's
already-working talk animation.

### 7b. Accessory overlay (do after 7a, not started)

Deliberately kept separate from 7a's data model - an accessory is a small
object *composited on top of* whichever skin is currently active, not a
replacement sprite set of its own. Building 7a first doesn't block this,
but building this first and then reusing its code for 7a would have been
the wrong direction (an overlay renderer doesn't generalize into a skin-
swapper, the reverse isn't true either - they're just different problems).

**Architecture - overlay layer, not new body sprites:**
- Add a second `Image` (`AccessoryImage`) to `MainWindow.xaml`'s `PetRoot`
  `Grid`, same size as `PetImage`, stacked on top (Grid children overlap by
  default - no extra layout work needed).
- `ApplySprite()` (`MainWindow.xaml.cs`) is already the *one* place that sets
  `PetImage.Source` - and already does this for exactly this project's earlier
  reason (movement code used to clobber expression sprites before everything
  was funneled through here, see the mood/expression system entry above).
  Extend it to also set `AccessoryImage.Source`/`Visibility` from the current
  worn accessory + `_facing` (or a front-facing variant when a mood/talk
  expression is showing - those already replace `PetImage.Source` outright, so
  the accessory only needs a `south` variant to cover every expression state,
  not a full 4-direction set for that case).
- Each direction needs its own manually-tuned pixel offset for where the
  accessory sits relative to the body (a hat sits differently on the
  south-facing vs. side-facing sprite). This is the *same* per-direction-offset
  problem item #6's held sparkler already anticipated - if #6 gets built
  first, reuse whatever offset-tuning approach it lands on rather than
  inventing a second one.

**Data model (new):**
```csharp
class AccessoryDef {
    string Name;
    Dictionary<string, BitmapImage> Sprites; // "south"/"east"/"west"/"north"
    Dictionary<string, Point> Offsets;        // per-direction pixel offset
}
```
`MainWindow._currentAccessory` (nullable - null = wearing nothing).

**Asset generation:** standalone overlay images via `create-image-pixflux`
(same as the toy/UI icons, e.g. `ball.png`) - **not** `/animate-character` or
`/create-character-state` (those are for generating variants of the *character
itself* with PixelLab preserving identity; an accessory is an independent
small object composited in code, not something that needs PixelLab's
character-continuity system at all). Needs one image per direction the
accessory is visible in, sized/positioned via the manual offsets above -
expect a few iterations to get alignment looking right per direction, same as
the sparkler hand-offset will.

**Radial menu:** new top-level item, tooltip "Clothing", icon
generated the same way as `follow.png`/`prompt.png`/`close.png`
(`create-image-pixflux`, 32x32, `no_background: true` - something like a
shirt or hanger). Opens a submenu: the one accessory, plus a "None" item to
unequip, plus Back (existing submenu machinery from item #3 handles this for
free - same pattern as the Toy submenu).

**Explicitly out of scope for the first pass:** persistence across pet
restarts (resets to "wearing nothing" on launch, same as every other pet
state today - mood, position, etc. don't persist either); more than 1
accessory (prove the mechanism first, then it's just "generate more +
add more submenu items" with the hard part already solved).

## 8. Food toy + click-to-place ball throw fix — DONE 2026-08-18

Two related requests: a new "Food" toy the pet walks to and eats, and a fix
to the existing ball's throw flow (it used to spawn next to the pet - now
you click anywhere first and it spawns there instead).

**New `PlacementOverlay.xaml`/`.xaml.cs`** - a full-screen, WorkingArea-sized
click-catcher (fixed-size like `AimLine`, not `SizeToContent` - see the
memory'd shrink-to-fit pitfall for that combo with `AllowsTransparency`), but
unlike `AimLine` it IS hit-test-visible (`Background="Transparent"` on the
root `Canvas`, same reasoning as `ToyMarker.RootBorder` - a null background
doesn't hit-test). Shows a semi-transparent ghost icon of whatever's being
placed, following the cursor. Left-click resolves `Placed(Point)` with the
click's screen position; Escape or right-click resolves `Cancelled` instead.
Either way it closes itself. Generic - both Ball and Food arm one of these
before doing anything else.

**Ball fix**: `StartBallCatch()` no longer creates the `ToyMarker` directly -
it now arms a `PlacementOverlay` and only creates the ball (`PlaceBall()`)
once the user clicks a spot. The drag-to-slingshot mechanic on the placed
ball (`ToyMarker.IsThrowable`, `OnBallThrown`, `TickBallPhysics`) is
completely unchanged - only *where* the ball first appears changed.

**Food (new)**: `ArmFoodPlacement()` (same placement step) → `PlaceFood()`
creates a non-throwable `ToyMarker` with a new `Assets/toys/food.png` sprite
(generated via `create-image-pixflux`, 32x32, `no_background: true` - a dog-
treat bone, matching `ball.png`'s size/style) and sets a new `PetMode.Eating`.
`TickEating()` walks the pet toward the food (`StepToward`, same as every
other walk-to-target flow) until within 4px, then: shows a "nom nom 🍪"
bubble for 3.5s, faces `"north"` (back to the viewer - **not** run through
`SetExpression`/mood sprites, since those are inherently front-facing
close-up art and a turned-back pose has no such asset), and calls
`Activate()` on the pet's own window. That last part matters: the food's
`ToyMarker` window was shown *after* the pet's, so left alone it would sit in
front of the pet in their shared `Topmost` z-band once they overlap at
arrival - re-activating the pet brings it back to the front so the food
visually disappears underneath it, like the pet's hunched over eating it.
After 3.5s the food marker closes and the pet `ReturningHome`s, same pattern
as the ball's catch-and-return.

New `EndAllToys()` helper - every toy-start path (`StartPlay`,
`StartBallCatch`, `ArmFoodPlacement`) now calls this first so switching
toys mid-interaction can't leave a stray marker, in-flight ball, active
eating session, or a still-open placement overlay behind.

Wired into both the radial menu's Toy submenu (Ball, Food, Back) and the
tray icon's Play submenu (Ball, Yarn, Wand, Food) - Food needed its own tray
entry rather than joining the `Toys` array/`StartPlay` loop, since `StartPlay`
is the unrelated teleport-chase mechanic Yarn/Wand still use.

**Verification note (see also the reskin session's write-up on this):**
mouse-click automation is unreliable in this environment, so the placement
click itself wasn't driven by a real simulated click. Instead, reused the
env-var-gated debug hook pattern (`CLAUDY_DEBUG_FOOD_AT`/`CLAUDY_DEBUG_BALL_AT`
`"x,y"` in `MainWindow`'s `Loaded` handler, calling `PlaceFood()`/`PlaceBall()`
directly) to verify live, then fully reverted before the final build. Confirmed:
the ball spawns at the given point (not next to the pet) with the drag bubble
showing; the food renders at its point, the pet correctly walks to it
(cross-checked against a tick-by-tick debug log during troubleshooting - see
below), and at arrival it faces north with the bubble showing and the food
sprite hidden behind it (z-order fix working).

**Debugging note worth keeping**: while chasing this down, several screenshot
checks came back showing neither the pet nor the food marker where expected,
which briefly looked like a real placement bug. It wasn't - it was screenshot
*timing*: chaining separate Bash/PowerShell tool calls (launch, then a
separate wait, then a separate screenshot) has enough round-trip overhead
that a short walk-to-food-and-eat cycle (a few seconds total) could fully
complete *before* the screenshot call even ran, so the marker was correctly
long-gone by the time it was checked. Confirmed via a temporary tick-by-tick
log inside `TickEating()` that the state machine was correct the whole time.
**Fix**: do launch + `Start-Sleep` + screenshot inside a *single* PowerShell
invocation (one process launched via `System.Diagnostics.Process.Start` with
explicit `EnvironmentVariables`, not a separate node/bash hop) so the sleep
duration actually maps to real elapsed time relative to the screenshot - if a
short-lived interaction "isn't appearing" in a screenshot again, suspect
tool-call round-trip latency before suspecting the app.

**Root cause of the placement bug (found on the third attempt - read this
before touching `PlacementOverlay`):** the first two "fixes" below were both
wrong diagnoses that got reported as confirmed on the strength of an
`ask_user` "yes" that the user hadn't actually tested behind. **Do not treat
an `ask_user` answer as verification of a UI fix** - ask for the observed
behavior in chat instead.

The actual bug: **`Topmost` alone does not guarantee a window receives the
mouse.** Other topmost windows exist (the pet, its bubbles, the nameplate,
and crucially whatever fullscreen app the user is really looking at), and
whichever one is above `PlacementOverlay` under the cursor gets the input
instead. Every reported symptom falls out of that one fact: the ghost icon
only tracked the cursor while the overlay happened to win the z-order
("I have to hover the toy sprite"), the placing click landed in some other
app entirely so the toy stayed wherever it was first drawn ("spawns in the
corner"), and it appeared to vanish whenever focus moved ("when mouse focus
somewhere I lost the toy I hover") - that last symptom is what finally
identified it. **Fix**: `RootCanvas.CaptureMouse()` on `Loaded`. Mouse
capture routes every move and click to this window regardless of z-order or
focus until released, which is exactly the "the next click anywhere belongs
to me" semantics this window needs. Also handles `LostMouseCapture` by
cancelling, so a foreign app force-grabbing input can't strand the user in an
invisible modal state where clicks silently do nothing.

**Ball is now one continuous gesture** (was: click to place, then click the
ball *again*, then drag). New `ToyMarker.BeginDrag()` starts the slingshot
pull without waiting for a fresh press on the marker, called right after
`PlaceBall()` shows it - since the placing click's button is still held,
press-drag-release now does place, aim, and throw in one motion.
`PlacementOverlay.Resolve()` releases its capture *before* invoking `Placed`
so the marker can take capture over for the drag. `BeginDrag()` deliberately
reads its start point via `Forms.Cursor.Position` (the same way the existing
`OnMouseDown` does) rather than accepting the overlay's WPF-space point, so
the pull vector stays in one coordinate space with the move/up handlers that
finish the throw - don't "simplify" that by passing the point in, it silently
breaks at non-100% DPI.

**`AimLine` had the same primary-screen-only bug** as the overlay and was
fixed alongside it - now spans the virtual desktop, since a ball placed on a
secondary monitor would otherwise have its whole trajectory preview clipped
away.

**Never ask the user to test pet *behavior* via an `ask_user` prompt** - it
silently invalidates the test. `Tick()` early-returns on
`_activeBubble is PromptWindow` (deliberate: the pet holds still while a real
question is pending), and that return sits *before* the `switch` running
`TickBallPhysics()`/`TickEating()`. So with a prompt open, a thrown ball
never moves and the pet never walks to food, while the parts that don't go
through the tick loop (the placement overlay, the drag, `AimLine`) all still
work - which looks exactly like "placement works but the features are
broken." This wasted a full round: the user tested while the verification
prompt was still open, correctly noticed the projectile drew but nothing
moved, and asked "is it related to how it's waiting for response?" - it was.
Ask for test results in chat, with no prompt pending. (Also worth knowing:
`TryShowCasualBubble` no-ops under a `PromptWindow` too, so bubbles won't
appear either.)

**Earlier changes made while chasing this - #1 and the multi-monitor change
were wrong diagnoses of the placement bug (kept as a record of what the
symptoms were NOT caused by; the code changes themselves are harmless and
were left in), #2 was a genuine, separately-verified fix:**

- **Wrong diagnosis A - "`Cursor.Position` is unreliable"**: that conclusion
  came from *automation* in this sandbox (synthetic clicks / a virtualized
  display), and does NOT describe the real app on a real desktop. Don't
  propagate it as a general claim about this codebase.
- **Wrong diagnosis B - "the overlay only covers the primary monitor"**:
  plausible, and the `SystemParameters.VirtualScreen*` change is correct to
  keep, but it was not the cause.

1. **Placement click landed at the wrong point (looked like "spawns top-left,
   and you have to hover before clicking works")**: `PlacementOverlay`'s
   click handler read the point via `Forms.Cursor.Position` - the exact API
   already caught behaving inconsistently in this environment (see the
   reskin session's note: setting it, reading it back, and native
   `GetCursorPos` gave three different values in one test). A stale read
   would explain both symptoms at once - a click before any mouse-move had
   refreshed it landing near some stale/default point (reads as "top left"),
   and needing a hover first to get a fresh value into it. **Fixed** by
   switching to the click event's own coordinate
   (`MouseButtonEventArgs.GetPosition(this)` + the window's `Left`/`Top`,
   which is how `OnMouseMove`'s ghost-icon tracking already worked, just not
   yet applied to the click handler) - this comes from the actual Win32
   input message WPF received for that window, sidestepping whatever makes a
   separate live `GetCursorPos`/`Cursor.Position` query unreliable here.
   Couldn't verify this specific fix via simulated clicks (same old
   limitation - synthetic input doesn't reliably reach these windows at
   all in this environment), so this one needs the user's own next click to
   confirm. **It did not fix it** - see the capture fix above for the real
   cause.
2. **Food still rendered on top of the pet**: the original `Activate()`
   approach (call it on the pet's own window once eating starts, hoping
   that's enough to bring it back to the front of the shared Topmost
   z-band) turned out not to be reliable - `Activate()` is a request, not a
   guarantee, and something shown afterward (the "nom nom" bubble) could
   still end up back on top. **Fixed** with an explicit, deterministic
   Win32 call instead: new `SetWindowPos`-based `PlaceWindowBehind(window,
   reference)` helper (`hWndInsertAfter` = the reference window's handle,
   `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE`) called once in `PlaceFood()`
   right after showing the food marker, ordering it immediately behind the
   pet's own window for good - not re-asserted per-tick, not dependent on
   activation timing. Verified live via the debug-hook harness: placing
   food with enough separation from the pet's spawn point that arrival is
   unambiguous (placing it exactly on top of the spawn point turned out to
   be a bad test - `UpdateFacing`'s own `dist < 3` early-return combined
   with `ApplySeparation`'s per-tick jitter can leave `_reachedFood` right on
   the boundary for a few ticks, which looked like a facing bug on the first
   attempt but wasn't one) shows the pet correctly facing north with no bone
   sprite visible anywhere behind it.
