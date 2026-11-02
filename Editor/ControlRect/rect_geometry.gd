@tool
extends RefCounted
# MIT. Editor-only, native Control geometry; no Catalog or runtime dependency.
const PROPERTIES := [&"layout_mode", &"anchor_left", &"anchor_top", &"anchor_right", &"anchor_bottom", &"offset_left", &"offset_top", &"offset_right", &"offset_bottom", &"pivot_offset", &"pivot_offset_ratio", &"scale", &"rotation", &"z_index", &"grow_horizontal", &"grow_vertical"]

func capture(control: Control) -> Dictionary:
	var state := {}
	for key in PROPERTIES:
		state[key] = control.get(key)
	state[&"_anchors_mode"] = state[&"layout_mode"]
	# Native enum values differ across engine revisions; use this Control's metadata.
	for info in control.get_property_list():
		if info.name != "layout_mode": continue
		var options: PackedStringArray = str(info.hint_string).split(",")
		for i in options.size():
			var parts := options[i].split(":")
			if parts[0] == "Anchors": state[&"_anchors_mode"] = int(parts[1]) if parts.size() > 1 else i
	state[&"_position"] = control.position
	state[&"_rtl"] = control.is_layout_rtl()
	state[&"_size"] = control.size
	state[&"_parent_size"] = control.get_parent_area_size()
	state[&"_minimum_size"] = control.get_combined_minimum_size()
	return state

func same(a: Dictionary, b: Dictionary) -> bool:
	for key in PROPERTIES:
		if a.get(key) != b.get(key): return false
	return true

func apply(control: Control, state: Dictionary) -> void:
	if not is_instance_valid(control): return
	# Set anchors before offsets: native setters otherwise compensate the rectangle.
	if state[&"layout_mode"] != null: control.set(&"layout_mode", state[&"layout_mode"])
	for side in 4:
		control.set_anchor(side, state[PROPERTIES[side + 1]], true, true)
	for key in PROPERTIES.slice(5): control.set(key, state[key])
	control.queue_redraw()

func pivot(state: Dictionary) -> Vector2:
	var pixels: Vector2 = state[&"pivot_offset"]
	var dimensions: Vector2 = state[&"_size"]
	return state[&"pivot_offset_ratio"] + Vector2(pixels.x / dimensions.x if dimensions.x != 0 else 0.0, pixels.y / dimensions.y if dimensions.y != 0 else 0.0)

func values(state: Dictionary) -> Dictionary:
	var p := pivot(state)
	var stretched_x: bool = not is_equal_approx(state[&"anchor_left"], state[&"anchor_right"])
	var stretched_y: bool = not is_equal_approx(state[&"anchor_top"], state[&"anchor_bottom"])
	var left: float = state[&"offset_left"]
	var top: float = state[&"offset_top"]
	var right: float = state[&"offset_right"]
	var bottom: float = state[&"offset_bottom"]
	var anchored: Vector2 = state[&"_position"] + state[&"_size"]*p - Vector2((1.0-state[&"anchor_left"] if state[&"_rtl"] else state[&"anchor_left"])*state[&"_parent_size"].x, state[&"anchor_top"]*state[&"_parent_size"].y)
	return {"X": left if stretched_x else anchored.x, "Y": top if stretched_y else -anchored.y,
		"W": -right if stretched_x else state[&"_size"].x, "H": -bottom if stretched_y else state[&"_size"].y,
		"MinX": state[&"anchor_left"], "MinY": 1.0-state[&"anchor_bottom"], "MaxX": state[&"anchor_right"], "MaxY": 1.0-state[&"anchor_top"],
		"PivotX": p.x, "PivotY": 1.0-p.y, "ScaleX": state[&"scale"].x, "ScaleY": state[&"scale"].y, "Rotation": -rad_to_deg(state[&"rotation"]), "Order": state[&"z_index"]}

func edited(state: Dictionary, key: String, value: float) -> Dictionary:
	var next := state.duplicate()
	var p := pivot(state)
	var sx: bool = not is_equal_approx(state[&"anchor_left"], state[&"anchor_right"])
	var sy: bool = not is_equal_approx(state[&"anchor_top"], state[&"anchor_bottom"])
	var old := values(state)
	match key:
		"X", "Y":
			var first: StringName = &"offset_left" if key == "X" else &"offset_top"
			var last: StringName = &"offset_right" if key == "X" else &"offset_bottom"
			if sx if key == "X" else sy: next[first] = value
			else:
				var delta: float = (value-old[key]) * ((-1.0 if state[&"_rtl"] else 1.0) if key == "X" else -1.0)
				next[first] += delta
				next[last] += delta
		"W", "H":
			var first: StringName = &"offset_left" if key == "W" else &"offset_top"
			var last: StringName = &"offset_right" if key == "W" else &"offset_bottom"
			if sx if key == "W" else sy: next[last] = -value
			else:
				var ratio: float = p.x if key == "W" else p.y
				var length: float = maxf(value, state[&"_minimum_size"].x if key == "W" else state[&"_minimum_size"].y)
				var pos: float = old.X if key == "W" else -old.Y
				next[first] = -pos-length*(1.0-ratio) if key == "W" and state[&"_rtl"] else pos-length*ratio
				next[last] = next[first]+length
				# Unity's normalized pivot stays fixed while dimensions change.
				next[&"pivot_offset"] = Vector2.ZERO
				next[&"pivot_offset_ratio"] = p
		"MinX": next[&"anchor_left"] = value
		"MaxX": next[&"anchor_right"] = value
		"MinY": next[&"anchor_bottom"] = 1.0-value
		"MaxY": next[&"anchor_top"] = 1.0-value
		"PivotX", "PivotY":
			if key == "PivotX": p.x = value
			else: p.y = 1.0-value
			next[&"pivot_offset"] = Vector2.ZERO
			next[&"pivot_offset_ratio"] = p
		"ScaleX": next[&"scale"].x = value
		"ScaleY": next[&"scale"].y = value
		"Rotation": next[&"rotation"] = -deg_to_rad(value)
		"Order": next[&"z_index"] = roundi(value)
	if key in ["MinX", "MaxX", "MinY", "MaxY"]: next[&"layout_mode"] = state[&"_anchors_mode"]
	return next

func preset(state: Dictionary, x: int, y: int, shift: bool, alt: bool) -> Dictionary:
	var next := state.duplicate()
	var p := pivot(state)
	next[&"layout_mode"] = state[&"_anchors_mode"]
	next[&"anchor_left"] = 0.0 if x == 3 else x*0.5
	next[&"anchor_right"] = 1.0 if x == 3 else x*0.5
	next[&"anchor_top"] = 0.0 if y == 3 else y*0.5
	next[&"anchor_bottom"] = 1.0 if y == 3 else y*0.5
	for side in 4:
		var anchor: StringName = PROPERTIES[side + 1]
		var offset: StringName = PROPERTIES[side + 5]
		next[offset] += (state[anchor]-next[anchor])*(state[&"_parent_size"].x if side % 2 == 0 else state[&"_parent_size"].y)
	if shift:
		p = Vector2(0.5 if x == 3 else x*0.5, 0.5 if y == 3 else y*0.5)
		next[&"pivot_offset"] = Vector2.ZERO
		next[&"pivot_offset_ratio"] = p
	if alt:
		var dimensions: Vector2 = state[&"_size"]
		next[&"offset_left"] = 0.0 if x == 3 else -dimensions.x*(1.0-p.x if state[&"_rtl"] else p.x)
		next[&"offset_right"] = 0.0 if x == 3 else next[&"offset_left"]+dimensions.x
		next[&"offset_top"] = 0.0 if y == 3 else -dimensions.y*p.y
		next[&"offset_bottom"] = 0.0 if y == 3 else dimensions.y*(1.0-p.y)
	return next
