# Changelog

## 0.7.0-dev — Unreleased

- Consolidate current Godot authoring/runtime workflows into USAGE.md for users and agents; keep README as the installation and document entrypoint. Clarify scene/Catalog ownership, typed versus Variant models, verification scope and unpublished-version boundaries.

- Add a scene Control Rect Transform Inspector using shared preset/diagram visuals: Unity-axis fields, 16 presets with event-captured Shift/Alt, normalized pivot, scale/rotation, copy/paste and scene-owned Undo. No Catalog, placement or attached script required.
- Preserve native Container sizing and scene-instance source assets. Retain Undo callbacks after Inspector disposal and DLL reload; format idle values without quantizing stored precision.

- Commit validated edits on completion, preserving one Undo per interaction, Escape cancellation and native control focus. Hide redundant Apply buttons.
- Share kind-first Sidebar/Graph creation and one placement instance-difference import entry; preserve selection through Undo/Redo. Include Overlays in default/Page filters; keep root Page/State inspectors focused on their settings.
- Show tree values and existing-Override marks, focus newly created values and locate existing bindings. Add receiver/compatible-method Event choices with explicit AOT registration; keep raw fields advanced.
- Add contextual rename/copy/remove/source commands and focus-scoped shortcuts. Position context menus from the actual click event, including embedded windows.

- Replace loose preview dimension inputs with 12 fixed-resolution presets and persistent custom entries. Add named creation, edit/remove, deduplication, rotation and project-local selection across Editor reopen and DLL reload; preserve Catalog history and Overlay selection.

- Default Override editing to native typed fields. Require explicit Raw Value for Model expressions, preserve existing Model bindings, and keep model-shaped native text literal. Retain presentation across DLL reload without serializing UI preferences; validate expressions and preserve raw Resource paths through drafts and Save.

- Keep target group browsing separate from binding edits; snapshot before branch creation and suppress phantom drafts during cancellation restore.
- Share Event target/signal/action validation across Apply, Enter and Save. Validate and stage all pending forms before one document Undo action, merging binding arrays and retaining native Variant precision.
- Route the advanced Inspector through detached document settings and restore its owner through Undo/Redo and DLL reload. Keep inline and advanced values synchronized, preserve visible filtered neighbors and Overlay preview identity, and skip unchanged Page setting actions.

- Unified owner/placement selection across sidebar, graph, filtering, Undo/Redo and live reload. Keep Page/State and placement inspectors exclusive; empty and failed documents clear every authoring surface.
- Added contextual native common settings, detached nested Resource edits, document-only Inspector history, owner/binding drafts, valid-draft Save, inline validation and consistent Enter/Esc. Disable invalid Unique/Container actions before invocation. Preserve native input controls/focus when the field structure is unchanged; external Catalog dirty state no longer leaves the global history unsaved after Save.
- Retain editing location, binding identity, folds and scroll across refresh and Undo; cancelled target/member pickers restore the prior form. Live legacy proxy upgrades initialize detached values safely.

- Consolidated element selection and add/remove/reorder/copy/reference creation into the two-level sidebar. Removed the duplicate inspector scene hierarchy and legacy item list; retained selected-placement settings and hierarchical Parent/Override/Event pickers.

- Added a two-level Views sidebar: Page/State overview, double-click into owned placements, Back with search/filter restoration, context-only inner title, native icons, contextual add/remove/reorder and owner-correct reference drops. Reorder Undo/Redo retains placement selection; other catalog kinds remain accessible through filters.

- Added target Back/Cancel navigation, retained picker windows across guided stages, and deferred native Tree scope/activation rebuilds until after input dispatch. Keep the authoring scroll surface inside the real editor viewport and reveal values after layout. Added a scene-selection reference import button; ignore unowned tool display helpers and reject imported receivers absent from the source scene.
- Restrict recursive resource comparison to authored editor/storage properties, avoiding reads of transient native FontFile texture caches.

- Added Unity-style double-click member creation, disabled Event drafts, native Scene/instance drops on Page/State/element targets, selectable instance diffs against direct inherited-scene sources, atomic import Undo and element-relative native signal receivers.

- Replaced indented authoring lists with native scene/target/binding trees, contextual parent creation, visible add/apply actions and guided Override/Event creation. Property picker selection immediately refreshes native typed controls; invalid drafts no longer expose the raw string editor. Preserve drafts across tree rebuilding/reselection and retain hierarchy state across DLL reload.

- Added Guid handles for individually removing runtime overrides with ordered restoration.
- Added inline Element display-name editing and metadata-driven native Override Inspector controls, including native Variant collections.
- Added independent nested singleton units and requested borrowing, multi-level LIFO, and composed requested templates with Catalog Elements.
- Added per-native-window safe-area capture and continuous monitor/work-area/viewport updates, including the secondary-window origin workaround.
- Added a project-independent Unity Editor bundle exporter, Godot native importer, conditional Animator adapter, editable native state graph, typed ScriptableObject data and explicit adapter reports. See UNITY-CONVERSION.md for the supported subset and manual mappings.


- UnityTiming default navigation, optional transactions, fluent options/hooks and primary-host frame-rate ownership.
- Scene pools, Unique LIFO loans, permanent lifetimes, requested pools, native transitions, safe area, focus and Overlay States.
- Harden requested cancellation, per-loan animation ownership, active Unique prewarm, owner-local visual reset and reentrant disposal; preserve all cleanup errors and invoke leave/return lifecycle once.
- Explicit AOT-preserved attribute registration, typed model expressions, local event handlers and native signal payloads.
- Breakpoints/flags, preset modifiers, whole-rule clipboard/assets, duplicate, ID baking/dropdowns, graph creation, ordering and aggregated verifier.
- Native Node/Resource/type icons in Override rows, fields and authoring pickers, with warning icons for invalid targets.
- Runtime Object Graph debugger capture and evidence-based dry-run Policy Advisor.
- No Unity asset execution or automatic platform compatibility claim; consumer validation remains separate.


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
