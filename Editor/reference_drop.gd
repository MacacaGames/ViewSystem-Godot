@tool
extends RefCounted
var target: WeakRef
var controls: Array[WeakRef] = []
func setup(plugin: Object) -> void: target = weakref(plugin)
func attach(control: Control, role: String) -> void:
	for i in range(controls.size() - 1, -1, -1):
		if not is_instance_valid(controls[i].get_ref()): controls.remove_at(i)
	if not controls.any(func(value: WeakRef) -> bool: return value.get_ref() == control): controls.append(weakref(control))
	control.set_drag_forwarding(Callable(), can_drop.bind(role, control), drop.bind(role, control))
func can_drop(position: Vector2, data: Variant, role: String, control: Control) -> bool:
	return target != null and is_instance_valid(target.get_ref()) and target.get_ref().call("CanAcceptReference", data, role, position, control)
func drop(position: Vector2, data: Variant, role: String, control: Control) -> void:
	if target != null and is_instance_valid(target.get_ref()): target.get_ref().call("OnReferenceDropped", data, role, position, control)
func clear() -> void:
	for entry in controls:
		var control: Control = entry.get_ref()
		if is_instance_valid(control): control.set_drag_forwarding(Callable(), Callable(), Callable())
	controls.clear()
	target = null
func connections(node: Node) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	for signal_info in node.get_signal_list():
		for connection in node.get_signal_connection_list(signal_info.name):
			if not (int(connection.flags) & CONNECT_PERSIST): continue
			var callback: Callable = connection.callable
			result.append({"signal": str(signal_info.name), "target": callback.get_object(), "method": str(callback.get_method()), "bound": callback.get_bound_arguments_count(), "unbound": callback.get_unbound_arguments_count(), "custom": callback.is_custom()})
	return result
func values_equal(a: Variant, b: Variant) -> bool: return a == b
