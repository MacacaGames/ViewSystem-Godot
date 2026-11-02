@tool
extends Resource
signal value_changed(value: Variant)
var descriptor: Dictionary = {}
var stored: Variant

func _dont_undo_redo() -> bool:
	return true

func clone_value(value: Variant) -> Variant:
	if value is Resource and not value is PackedScene and not value is Script:
		return value.duplicate(true)
	if value is Array:
		var result: Array = value.duplicate()
		for i in result.size(): result[i] = clone_value(result[i])
		return result
	if value is Dictionary:
		var result: Dictionary = value.duplicate()
		for key in result: result[key] = clone_value(result[key])
		return result
	return value

func configure(info: Dictionary, initial: Variant) -> void:
	descriptor = info.duplicate()
	descriptor.name = "Value"
	descriptor.usage = PROPERTY_USAGE_EDITOR | PROPERTY_USAGE_STORAGE | (int(info.get("usage", 0)) & PROPERTY_USAGE_NIL_IS_VARIANT)
	stored = clone_value(initial)
	notify_property_list_changed()

func _get_property_list() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	if not descriptor.is_empty(): result.append(descriptor)
	return result

func _validate_property(property: Dictionary) -> void:
	if property.name != "Value": property.usage &= ~PROPERTY_USAGE_EDITOR

func _get(property: StringName) -> Variant:
	return stored if property == &"Value" else null

func _set(property: StringName, value: Variant) -> bool:
	if property != &"Value": return false
	stored = clone_value(value)
	value_changed.emit(value)
	return true
