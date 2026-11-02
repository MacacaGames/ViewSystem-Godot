@tool
extends EditorProperty
# MIT. This is a scene property editor, not a ViewSystem placement editor.
const Preset = preload("../anchor_preset.gd")
const Diagram = preload("../anchor_diagram.gd")
var target: Control
var host: EditorInspectorPlugin
var inputs: Dictionary = {}
var labels: Dictionary = {}
var presets: Array[Button] = []
var selector: Button
var popup: PopupPanel
var diagram: Control
var managed: Label
var select_parent: Button
var copy: Button
var paste: Button
var syncing := false
var before: Dictionary = {}
var pending: Dictionary = {}
var observed: Dictionary = {}
var last_clipboard_empty := true
var preset_modifiers: Dictionary = {}

func configure(control: Control, inspector: EditorInspectorPlugin) -> void:
	target = control
	host = inspector
	name = "ControlRectTransform"
	label = "Rect Transform"
	var body := VBoxContainer.new()
	add_child(body)
	set_bottom_editor(body)
	var top := HBoxContainer.new()
	body.add_child(top)
	selector = Preset.new()
	selector.name = "ControlAnchorSelector"
	selector.custom_minimum_size = Vector2(48, 48)
	selector.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	selector.tooltip_text = "Anchor Presets · Shift: Pivot · Alt: Position"
	selector.pressed.connect(_open_presets)
	top.add_child(selector)
	var grid := GridContainer.new()
	grid.columns = 2
	grid.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(grid)
	for key in ["X", "Y", "W", "H"]: _cell(grid, key, key)
	popup = PopupPanel.new()
	popup.name = "ControlAnchorPopup"
	add_child(popup)
	var choices := GridContainer.new()
	choices.columns = 4
	popup.add_child(choices)
	for y in 4:
		for x in 4:
			var button := Preset.new()
			button.name = "ControlAnchorPreset%d%d" % [x, y]
			button.custom_minimum_size = Vector2(48, 48)
			button.toggle_mode = true
			button.configure(x, y)
			button.tooltip_text = ["Left", "Center", "Right", "Stretch"][x] + " / " + ["Top", "Middle", "Bottom", "Stretch"][y]
			button.pressed.connect(_preset_pressed.bind(x, y))
			button.gui_input.connect(_preset_input.bind(x, y))
			choices.add_child(button)
			presets.append(button)
	managed = Label.new()
	managed.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	body.add_child(managed)
	select_parent = Button.new()
	select_parent.text = "選取父 Container"
	select_parent.pressed.connect(_select_parent)
	body.add_child(select_parent)
	var anchors := _fold(body, "Anchors")
	_vector(anchors, "Min", "MinX", "MinY")
	_vector(anchors, "Max", "MaxX", "MaxY")
	diagram = Diagram.new()
	diagram.name = "ControlAnchorDiagram"
	diagram.custom_minimum_size = Vector2(0, 110)
	diagram.anchors_edited.connect(_anchors_edited)
	anchors.add_child(diagram)
	_vector(body, "Pivot", "PivotX", "PivotY")
	_vector(body, "Scale", "ScaleX", "ScaleY")
	var rotation := HBoxContainer.new()
	rotation.add_child(_label("Rotation"))
	body.add_child(rotation)
	_cell(rotation, "Rotation", "Z")
	var actions := HBoxContainer.new()
	body.add_child(actions)
	copy = Button.new()
	copy.name = "ControlRectCopy"
	copy.text = "Copy"
	copy.pressed.connect(_copy)
	actions.add_child(copy)
	paste = Button.new()
	paste.name = "ControlRectPaste"
	paste.text = "Paste"
	paste.pressed.connect(_paste)
	actions.add_child(paste)
	_cell(_fold(body, "Advanced"), "Order", "Z Order")
	_refresh()

func _fold(parent: Control, caption: String) -> VBoxContainer:
	var toggle := Button.new()
	toggle.name = "ControlRect" + caption
	toggle.text = "▸ " + caption
	toggle.toggle_mode = true
	toggle.alignment = HORIZONTAL_ALIGNMENT_LEFT
	parent.add_child(toggle)
	var contents := VBoxContainer.new()
	contents.visible = false
	parent.add_child(contents)
	toggle.toggled.connect(func(open: bool):
		contents.visible = open
		toggle.text = ("▾ " if open else "▸ ") + caption)
	return contents

func _label(caption: String) -> Label:
	var result := Label.new()
	result.text = caption
	result.custom_minimum_size.x = 55
	return result

func _vector(parent: Control, caption: String, x: String, y: String) -> void:
	var row := HBoxContainer.new()
	parent.add_child(row)
	row.add_child(_label(caption))
	_cell(row, x, "X")
	_cell(row, y, "Y")

func _cell(parent: Control, key: String, caption: String) -> void:
	var cell := VBoxContainer.new()
	cell.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	parent.add_child(cell)
	var title := Label.new()
	title.text = caption
	cell.add_child(title)
	labels[key] = title
	var input := SpinBox.new()
	input.name = "ControlRect" + key
	input.min_value = -4096 if key == "Order" else -1000000
	input.max_value = 4096 if key == "Order" else 1000000
	input.step = 1 if key == "Order" else 0
	input.custom_arrow_step = 1
	input.update_on_text_changed = true
	input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	input.custom_minimum_size.x = 60
	input.value_changed.connect(_value_changed.bind(key))
	input.get_line_edit().text_changed.connect(_text_changed.bind(input))
	input.get_line_edit().text_submitted.connect(_submitted)
	input.get_line_edit().focus_entered.connect(_focus_entered.bind(key))
	input.get_line_edit().focus_exited.connect(_focus_exited)
	input.gui_input.connect(_field_input)
	input.get_line_edit().gui_input.connect(_field_input)
	cell.add_child(input)
	inputs[key] = input
	add_focusable(input.get_line_edit())

func _valid() -> bool:
	return is_instance_valid(target) and target.is_part_of_edited_scene()

func _container() -> bool:
	return _valid() and target.get_parent() is Container

func _begin() -> void:
	if before.is_empty():
		before = host.geometry.capture(target)
		pending = before.duplicate()

func _text_changed(_text: String, input: SpinBox) -> void:
	if not syncing and _valid() and input.get_line_edit().has_focus() and not is_read_only(): _begin()

func _value_changed(value: float, key: String) -> void:
	if syncing or not _valid() or is_read_only() or not is_finite(value): return
	if _container() and key in ["X", "Y", "W", "H", "MinX", "MinY", "MaxX", "MaxY"]: return
	_begin()
	pending = host.geometry.edited(before, key, value)
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT): host.geometry.apply(target, pending)
	elif not inputs[key].get_line_edit().has_focus(): _finish.call_deferred()

func _submitted(_text: String) -> void:
	_finish.call_deferred()

func _focus_entered(key: String) -> void:
	# Display rounding must never quantize a native value merely by focusing it.
	syncing = true
	inputs[key].get_line_edit().text = var_to_str(inputs[key].value)
	syncing = false

func _focus_exited() -> void:
	_end_focus.call_deferred()

func _end_focus() -> void:
	_finish(true)
	_format_idle()

func _format_idle() -> void:
	syncing = true
	for input in inputs.values():
		var line: LineEdit = input.get_line_edit()
		line.tooltip_text = var_to_str(input.value)
		if not line.has_focus():
			var display := String.num(input.value, 3)
			if line.text != display: line.text = display
	syncing = false

func _field_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE and not before.is_empty():
		if _valid(): host.geometry.apply(target, before)
		before.clear()
		pending.clear()
		_refresh()
		accept_event()

func _finish(force: bool = false) -> void:
	if before.is_empty(): return
	if not _valid() or is_read_only():
		before.clear()
		pending.clear()
		return
	if not force and Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT): return
	var original := before
	var next := pending
	before = {}
	pending = {}
	_commit(original, next)

func _commit(original: Dictionary, next: Dictionary) -> void:
	if not _valid() or is_read_only() or host.geometry.same(original, next):
		_refresh()
		return
	host.geometry.apply(target, next)
	var actual: Dictionary = host.geometry.capture(target)
	if not host.geometry.same(original, actual):
		# Use the edited Control's scene history, not the Catalog's or a UI object's.
		host.history.create_action("Rect Transform", UndoRedo.MERGE_DISABLE, target)
		host.history.add_do_method(host.geometry, &"apply", target, actual)
		host.history.add_undo_method(host.geometry, &"apply", target, original)
		host.history.add_do_reference(host.geometry)
		host.history.add_undo_reference(host.geometry)
		host.history.commit_action(false)
	_refresh()

func _open_presets() -> void:
	_finish()
	preset_modifiers.clear()
	popup.popup_on_parent(Rect2i(Vector2i(selector.global_position + Vector2(0, selector.size.y)), Vector2i(204, 204)))

func _preset_input(event: InputEvent, x: int, y: int) -> void:
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT and event.pressed or event is InputEventKey and event.pressed:
		preset_modifiers[y*4+x] = Vector2i(int(event.shift_pressed), int(event.alt_pressed))

func _preset_pressed(x: int, y: int) -> void:
	var modifiers: Vector2i = preset_modifiers.get(y*4+x, Vector2i.ZERO)
	preset_modifiers.clear()
	_preset(x, y, modifiers.x != 0, modifiers.y != 0)

func _preset(x: int, y: int, shift: bool = false, alt: bool = false) -> void:
	if not _valid() or is_read_only() or _container(): return
	_finish()
	var state: Dictionary = host.geometry.capture(target)
	_commit(state, host.geometry.preset(state, x, y, shift, alt))
	popup.hide()

func _anchors_edited(minimum: Vector2, maximum: Vector2) -> void:
	if not _valid() or is_read_only() or _container(): return
	_begin()
	pending[&"layout_mode"] = before[&"_anchors_mode"]
	pending[&"anchor_left"] = minimum.x
	pending[&"anchor_top"] = minimum.y
	pending[&"anchor_right"] = maximum.x
	pending[&"anchor_bottom"] = maximum.y
	host.geometry.apply(target, pending)

func _copy() -> void:
	_finish()
	if _valid(): host.clipboard = host.geometry.capture(target)
	_refresh()

func _paste() -> void:
	if not _valid() or is_read_only() or _container() or host.clipboard.is_empty(): return
	_finish()
	_commit(host.geometry.capture(target), host.clipboard.duplicate())

func _select_parent() -> void:
	if not _container(): return
	var parent := target.get_parent()
	EditorInterface.get_selection().clear()
	EditorInterface.get_selection().add_node(parent)
	EditorInterface.edit_node(parent)

func _update_property() -> void:
	if before.is_empty(): _refresh()

func _set_read_only(value: bool) -> void:
	if value and not before.is_empty():
		if _valid(): host.geometry.apply(target, before)
		before.clear()
		pending.clear()
	_refresh.call_deferred()

func _refresh() -> void:
	if not _valid() or inputs.is_empty(): return
	syncing = true
	observed = host.geometry.capture(target)
	var values: Dictionary = host.geometry.values(observed)
	var container := _container()
	for key in inputs:
		inputs[key].set_value_no_signal(values[key])
		inputs[key].editable = not is_read_only() and not (container and key in ["X", "Y", "W", "H", "MinX", "MinY", "MaxX", "MaxY"])
	var sx: bool = not is_equal_approx(observed[&"anchor_left"], observed[&"anchor_right"])
	var sy: bool = not is_equal_approx(observed[&"anchor_top"], observed[&"anchor_bottom"])
	labels.X.text = "Left" if sx else "Pos X"
	labels.Y.text = "Top" if sy else "Pos Y"
	labels.W.text = "Right" if sx else "Width"
	labels.H.text = "Bottom" if sy else "Height"
	inputs.Y.tooltip_text = "Top offset" if sy else "Unity Y ↑"
	inputs.Rotation.tooltip_text = "Unity Z rotation (degrees)"
	selector.disabled = is_read_only() or container
	selector.show_anchors(Vector2(target.anchor_left, target.anchor_top), Vector2(target.anchor_right, target.anchor_bottom))
	for i in presets.size():
		var x := i % 4
		var y := i / 4
		presets[i].set_pressed_no_signal(is_equal_approx(target.anchor_left, 0.0 if x == 3 else x*0.5) and is_equal_approx(target.anchor_right, 1.0 if x == 3 else x*0.5) and is_equal_approx(target.anchor_top, 0.0 if y == 3 else y*0.5) and is_equal_approx(target.anchor_bottom, 1.0 if y == 3 else y*0.5))
	var data := Resource.new()
	for key in ["AnchorLeft", "AnchorTop", "AnchorRight", "AnchorBottom"]: data.set_meta(key, target.get(key.to_snake_case()))
	diagram.set_layout_data(null if container or is_read_only() else data)
	managed.visible = container
	select_parent.visible = container
	if container: managed.text = "由 %s 管理版面" % target.get_parent().get_class()
	copy.disabled = container or is_read_only()
	paste.disabled = container or is_read_only() or host.clipboard.is_empty()
	last_clipboard_empty = host.clipboard.is_empty()
	syncing = false
	_format_idle()

func _process(_delta: float) -> void:
	if not _valid(): return
	if not before.is_empty():
		var focus := get_viewport().gui_get_focus_owner()
		if not Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and not (focus is LineEdit and is_ancestor_of(focus)): _finish()
	elif observed != host.geometry.capture(target) or last_clipboard_empty != host.clipboard.is_empty(): _refresh()
	# Native Inspector refresh can reformat a SpinBox after our update callback.
	if before.is_empty(): _format_idle()

func _exit_tree() -> void:
	_finish()
