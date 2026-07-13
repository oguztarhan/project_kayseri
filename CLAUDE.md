# Project: [GAME_NAME] — Intake Entertainment

Unity 6.4.9f1 · URP · Target: Android (Google Play) · Dev machine: Windows

## Hard rules — never break these

- NEVER hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` files. Use Unity MCP tools or ask me.
- NEVER touch `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `*.keystore`, `*.csproj`, `*.sln`.
- Expose tunable values as `[SerializeField] private` fields. I wire references in the Inspector myself.
- Do not refactor, rename, or "clean up" code I didn't ask you to touch.
- Do not add packages or Asset Store dependencies without asking first.

## Workflow

1. For anything non-trivial: plan first, wait for my approval, then implement.
2. After editing C#: wait for Unity to recompile, then read the Unity console. Do not report success while errors or new warnings exist.
3. Verify before claiming done. "It compiles" is not "it works" — say what you actually checked.
4. If you're unsure about intent, ask. Do not guess and build the wrong thing.
5. One task at a time. Finish and confirm before starting the next.

## Code conventions

- C# naming: `PascalCase` methods/properties, `_camelCase` private fields, `PascalCase` public fields.
- Cache component references in `Awake()`. Never call `GetComponent` or `Find` in `Update()`.
- No LINQ or allocations in per-frame code paths — this is a mobile game.
- Use object pooling for anything spawned repeatedly.
- No premature abstraction. Three similar lines beats a helper used once. Abstract at 3+ real use cases.
- Deleting means deleting. No `_unused` renames, no commented-out code, no compat shims.
- One class per file, filename matches class name (Unity requires this for MonoBehaviours).

## Mobile / URP constraints

- Target 60fps on mid-range Android. Draw calls and GC allocations are the enemy.
- URP only — no Built-in pipeline shaders. Use Shader Graph or URP Lit/Unlit.
- Keep the SRP Batcher happy: no per-renderer `MaterialPropertyBlock` unless justified.
- UI: Canvas splitting matters. Don't put dynamic elements on the same canvas as static ones.

## Project layout

```
Assets/
  Scripts/     Gameplay/ · UI/ · Systems/ · Data/
  Prefabs/
  Scenes/
  Art/
  Audio/
  Settings/    URP assets, render pipeline configs
```

## Commands

- Tests: run via Unity Test Runner (Unity MCP `run_tests`), not manually.
- Logs: `adb logcat -s Unity` for on-device debugging.
- Git: never `git push` without asking. Never `git reset --hard`, never force-push.

## Notes

- This file is the always-true context. Anything task-specific belongs in a skill or a spec doc, not here.
- Keep this file under ~70 lines. If it's growing, something belongs elsewhere.
