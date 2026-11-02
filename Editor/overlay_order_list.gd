@tool
extends ItemList
signal item_moved(from: int, to: int)
func _get_drag_data(at: Vector2):
	var index := get_item_at_position(at, true)
	if index < 0:
		return null
	var preview := Label.new()
	preview.text = get_item_text(index)
	set_drag_preview(preview)
	return {"owner": get_instance_id(), "from": index}
func _can_drop_data(_at: Vector2, data) -> bool:
	return data is Dictionary and data.get("owner", 0) == get_instance_id()
func _drop_data(at: Vector2, data):
	var index := get_item_at_position(at, true)
	item_moved.emit(int(data["from"]), item_count - 1 if index < 0 else index)
