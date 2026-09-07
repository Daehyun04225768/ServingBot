---
name: connect-unity-mcp
description: Connects a Unity project to CoplayDev's "MCP for Unity" (unity-mcp / MCPForUnity) bridge in one shot, so Claude Code gets mcp__unity__* tools (manage_scene, manage_gameobject, execute_code, read_console, find_gameobjects, etc.) and mcpforunity:// resources for that project. Use this whenever the user wants to connect, set up, install, configure, or fix Unity MCP / "MCP for Unity" / unity-mcp for a project — including when they say things like "Unity 프로젝트에 MCP 연결해줘", "hook up Claude to my Unity Editor", "I can't see any Unity tools in Claude Code", mcp__unity__ tool calls are failing/missing, or the Unity console shows "MCP-FOR-UNITY" errors. Also trigger if the user asks to package/hand off this setup as a skill for someone else's Unity project. Do not use this for general Unity scripting/editor questions that don't involve getting Claude connected to the Editor in the first place.
---

# Connect Unity MCP

Wires a Unity project up to CoplayDev's "MCP for Unity" bridge so Claude Code can drive the Unity Editor directly (manage scenes/GameObjects, run editor code, read the console, etc.) instead of only editing files on disk. Two independent pieces have to be in place, one on each side of the bridge:

1. **Unity side** — the project's `Packages/manifest.json` needs the `com.coplaydev.unity-mcp` package dependency. Once Unity resolves that package and the Editor is open on the project, it starts an embedded local HTTP server (you'll see `MCP-FOR-UNITY: Server ready on http://127.0.0.1:<port>` in the Unity console).
2. **Claude Code side** — the project root needs a `.mcp.json` registering an `"unity"` MCP server that points at that same HTTP URL.

Both are just file edits, which is why this can be a single command instead of walking through the package's own `Window > MCP for Unity > Local Setup Window` GUI by hand. But the HTTP server itself only exists once Unity is actually running the project — no file edit can start that part, so the last mile always needs the user to have (or open) Unity.

## Steps

1. **Find the Unity project root.** Look for a directory that has both `Assets/` and `Packages/manifest.json` — that pair is present in every Unity project regardless of version or render pipeline, and neither exists in a non-Unity repo. Start from the current working directory (or a path the user gave you) and walk upward if needed. If you can't find one, don't guess — ask the user for the correct project path.

2. **Run the setup script**, pointed at that root:
   ```bash
   python3 <this-skill-dir>/scripts/setup_unity_mcp.py <project_root>
   ```
   Pass `--port <N>` only if the user tells you they run MCP for Unity on a non-default port (default is 8080). The script:
   - Adds `com.coplaydev.unity-mcp` to `Packages/manifest.json` if it's missing — leaving every other dependency untouched.
   - Adds (or merges in, alongside whatever else is already there) an `"unity"` entry in `.mcp.json` pointing at `http://127.0.0.1:<port>/mcp`.
   - Is safe to run on a project that's already configured — it detects that and reports "already configured" instead of duplicating anything, so you never need to check state by hand before running it.

   Read the script's printed report rather than just checking its exit code — it distinguishes "added" from "already-present" from "failed" for each of the two files, which is exactly what you need to tell the user what happened.

3. **Tell the user Unity itself needs to be running.** The file edits alone don't start the bridge server. Say plainly: open (or bring back into focus) the Unity Editor on this project, and give it a moment to resolve the new package and finish compiling — that's when the console line about the HTTP server ready shows up. If Unity was already open when you edited `manifest.json`, it needs to detect the change (usually automatic, but a manual `Assets > Refresh` or re-focusing the Editor window helps it notice sooner).

4. **Verify the connection.** After telling the user to have Unity open, check whether it's actually live:
   - Try `ToolSearch` for `mcp__unity__*` tools, or check the deferred-tools listing for that prefix.
   - Read the `mcpforunity://instances` resource — a successful read listing at least one instance means the bridge is up and Claude is talking to it.

   If verification succeeds: tell the user which Unity instance you're connected to (name/version from the instances list) and that they're ready to go.

   If it doesn't succeed yet, that's expected right after a fresh setup — don't treat it as a failure. Tell the user concretely what to check: is Unity open on this exact project, has it finished compiling (check the bottom-right spinner / console), and does the port in `.mcp.json` match what Unity's console printed. Offer to re-check once they confirm Unity's up.

5. **Idempotency check-in.** If you ran this on a project that was already fully configured, say so plainly ("already configured — just verifying the connection") rather than narrating file edits that didn't happen.

## Notes

- This only ever touches `Packages/manifest.json` and `.mcp.json`. It never touches Unity Editor state, scenes, or scripts, and it never needs Unity to be running to do its part.
- If a user wants to hand this exact setup to someone else for a *different* Unity project, the whole `connect-unity-mcp/` folder is self-contained (`SKILL.md` + `scripts/`) — copying it into that project's `.claude/skills/` (or packaging it with the skill-creator's `package_skill.py`) is all that's needed.
