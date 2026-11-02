@tool
extends EditorProperty

var values: PackedStringArray = []
var selector := OptionButton.new()

func _init():
	add_child(selector)
	add_focusable(selector)
	selector.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	selector.item_selected.connect(_selected)

func configure(ids: PackedStringArray):
	values = ids
	selector.clear()
	selector.add_item("—")
	for value in values:
		selector.add_item(value)

func _update_property():
	var value: String = get_edited_object().get(get_edited_property())
	while selector.item_count > values.size() + 1:
		selector.remove_item(selector.item_count - 1)
	var index := values.find(value)
	if value.is_empty():
		selector.select(0)
	elif index >= 0:
		selector.select(index + 1)
	else:
		selector.add_item("Missing: " + value)
		selector.set_item_disabled(selector.item_count - 1, true)
		selector.select(selector.item_count - 1)

func _selected(index: int):
	emit_changed(get_edited_property(), "" if index == 0 else values[index - 1])
