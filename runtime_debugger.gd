extends Node
var host: Node
var registered := false
func setup(value: Node):
	host = value
	if EngineDebugger.is_active() and not EngineDebugger.has_capture("macaca_viewsystem"):
		EngineDebugger.register_message_capture("macaca_viewsystem", _capture)
		registered = true
		EngineDebugger.send_message("macaca_viewsystem:ready", [])
func _capture(command: String, data: Array) -> bool:
	if command != "capture" or data.size() != 1:
		return false
	EngineDebugger.send_message("macaca_viewsystem:object_graph", [host.call("CaptureGraphJson", str(data[0]))])
	return true
func _exit_tree():
	if registered:
		EngineDebugger.unregister_message_capture("macaca_viewsystem")
