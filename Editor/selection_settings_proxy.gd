@tool
extends Resource

signal setting_changed(property: StringName, value: Variant)
var source: Resource
var fields: PackedStringArray

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

func configure(resource: Resource, names: PackedStringArray) -> void:
	source = resource
	fields = names
	var values: Dictionary = {}
	set_meta("_view_values", values)
	if not is_instance_valid(source):
		notify_property_list_changed()
		return
	for property in fields: values[property] = clone_value(source.get(property))
	notify_property_list_changed()

func _get_property_list() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	if not is_instance_valid(source):
		return result
	for property in source.get_property_list():
		if str(property.name) in fields:
			result.append(property.duplicate())
	return result

func _validate_property(property: Dictionary) -> void:
	if not str(property.name) in fields:
		property.usage &= ~PROPERTY_USAGE_EDITOR

func _get(property: StringName) -> Variant:
	# Metadata survives tool-script upgrades without relying on newly introduced
	# script-member slots while an existing Inspector is still reading the proxy.
	if not is_instance_valid(source) or not str(property) in fields:
		return null
	var values: Dictionary = get_meta("_view_values", {})
	if not has_meta("_view_values"): set_meta("_view_values", values)
	if not values.has(str(property)): values[str(property)] = clone_value(source.get(property))
	return values[str(property)]

func _set(property: StringName, value: Variant) -> bool:
	if not is_instance_valid(source) or not str(property) in fields:
		return false
	var values: Dictionary = get_meta("_view_values", {})
	if not has_meta("_view_values"): set_meta("_view_values", values)
	values[str(property)] = clone_value(value)
	setting_changed.emit(property, value)
	return true
