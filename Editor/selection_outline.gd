@tool
extends Node2D
var extent := Vector2.ZERO
var tint := Color(0.2, 0.7, 1.0)
func configure(value: Vector2, color: Color) -> void:
	extent = value
	tint = color
	queue_redraw()
func _draw() -> void:
	draw_rect(Rect2(Vector2.ZERO, extent), tint, false, 2.0)
