@tool
extends EditorInspectorPlugin
# MIT. Registered by ViewSystem, operates only on the edited scene's Controls.
const RectEditor = preload("rect_property.gd")
const Geometry = preload("rect_geometry.gd")
var history: EditorUndoRedoManager
var geometry := Geometry.new()
var clipboard: Dictionary = {}

func _can_handle(object: Object) -> bool:
	return object is Control and object.is_part_of_edited_scene()

func _parse_category(object: Object, category: String) -> void:
	if category != "Control": return
	var editor := RectEditor.new()
	editor.configure(object, self)
	add_property_editor_for_multiple_properties("Rect Transform", Geometry.PROPERTIES, editor)

func _parse_property(_object: Object, _type: Variant.Type, property: String, _hint: PropertyHint, _hint_string: String, _usage: PropertyUsageFlags, _wide: bool) -> bool:
	# Minimum size, Container sizing, grow direction and other native settings stay.
	return property in Geometry.PROPERTIES and property not in ["grow_horizontal", "grow_vertical"] or property in ["position", "size", "rotation_degrees", "anchors_preset"]
