# Claudy

A desktop pet overlay that acts as the UI for a human-in-the-loop MCP tool. When Claude needs a decision, a choice among options, or free-text input — especially when you might not be reading the chat right now — it asks through an on-screen popup from Claudy instead of blocking silently.

## Components

- **`index.js`** — Node MCP server ([`@modelcontextprotocol/sdk`](https://www.npmjs.com/package/@modelcontextprotocol/sdk), stdio transport). Registers one tool, `ask_user(question, kind: yesno|choice|text, options?, placeholder?, timeoutSeconds?)`. Each server process gets its own session id, named pipe, and pet instance, so multiple concurrent Claude Code sessions each get their own independently-clickable Claudy instead of sharing one.
- **`PetOverlay/`** — the pet itself: a C# WPF app (net10.0-windows) that renders as a transparent, always-on-top desktop overlay and talks to `index.js` over a named pipe.
- **`prompt.ps1`** — a plain PowerShell/WPF toast-style popup, used only as a fallback if the pet can't be reached.

## How it works

1. Claude calls the `ask_user` tool.
2. `index.js` connects to (or launches) `PetOverlay.exe` over a named pipe scoped to that session, and sends the question.
3. Claudy shows a prompt window and waits for you to answer, choose, or type.
4. The answer (or a timeout/dismiss result) is sent back over the pipe and returned to Claude.
5. If the pet can't be reached at all, `index.js` falls back to the plain PowerShell popup.

## Pet features

- Idle animation, 4-direction walking, cursor-follow, and spontaneous wandering.
- Toys: a ball with slingshot-style throw-and-catch physics, and a food treat — both placed by click after arming from the radial menu.
- Mood/expression bubbles, pat detection, and social interactions with other running Claudy instances (hangs out, pranks, pokes).
- A Minecraft-style nameplate above/below the pet, renameable via the radial menu.
- A reskin system (`Assets/claudy/skins/<name>/`) for swapping the whole sprite set — first alternate skin is a cat.
- Right-click radial menu: Follow, Toy, Prompt, Clothing, Pulse, Rename, Close.
- A Pulse HUD radial item showing live Claude Code usage stats.

## Setup

Requires Node.js and the [.NET SDK](https://dotnet.microsoft.com/) (net10.0-windows) on Windows.

```
npm install
cd PetOverlay && dotnet build -c Release
```

Register the MCP server (user-scoped, so it's available in every repo on this machine):

```
claude mcp add ask-user -s user -- node "C:\path\to\Claudy\index.js"
```

Art is generated via the [PixelLab](https://pixellab.ai) API. If regenerating assets, put your token in a gitignored `.env` as `PIXELLAB_API_TOKEN`.

## Notes

- `PetOverlay.exe` holds a file lock while running — kill any running instances before rebuilding (`taskkill /IM PetOverlay.exe /F`).
- Editing `index.js` requires a full Claude Code restart before a live `ask_user` call picks up the change.
- The pet self-closes a few seconds after its parent Claude Code process exits, so it doesn't linger as an orphan.
