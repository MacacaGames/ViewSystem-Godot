@tool
extends RefCounted

# GodotSharp4.7.2 cannot represent bound native Callables. Keep their connection
# records here; every callback targets the C# editor, never this transient helper.
var bindings: Array = []

func connect_native(owner: Object, signal_name: StringName, target: Object, method: StringName, arguments: Array) -> int:
	var callback := Callable(target, method).bindv(arguments)
	var error := owner.connect(signal_name, callback)
	if error == OK:
		bindings.append({"owner": owner, "signal": signal_name, "callback": callback})
	return error

func disconnect_native(only: Object = null) -> void:
	for index in range(bindings.size() - 1, -1, -1):
		var binding: Dictionary = bindings[index]
		if only != null and binding["owner"] != only:
			continue
		if is_instance_valid(binding["owner"]) and binding["owner"].is_connected(binding["signal"], binding["callback"]):
			binding["owner"].disconnect(binding["signal"], binding["callback"])
		bindings.remove_at(index)
