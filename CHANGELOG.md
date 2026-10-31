# Changelog

## 0.6.0 — 2026-10-31

- One transactional async navigation contract for native and project animation policies; stage new States before immediate commit, clean up failed candidates and preserve future navigation.
- Explicit post-commit error type, cancellation-aware shutdown and immediate/async mutual exclusion.
- Native searchable Parent/Target/Property/Signal/Action pickers, typed properties, preview location and optional application dispatcher declarations.
- Existing Unity breakpoint/flag, safe-area, whole-item clipboard, parameterized signals and attribute injection gaps remain pending.

## 0.5.1 — 2026-10-31

- Extract the native source plugin to MacacaGames/ViewSystem-Godot. Keep existing script/resource paths and layout metadata schema1.
- Add standalone Git/tag installation, retained MIT notice, deterministic ZIP packaging and clean-consumer runtime/GUI Editor smoke tooling.
- No runtime or editor behavior changes from 0.5.0.

## 0.5.0

- Compact Unity-style RectTransform fields by fixed/stretch axes, graphical anchor presets, live edits and transform clipboard.
- Page/State grouped navigation, native preview, Undo/Redo and hot-reload-safe document state.
