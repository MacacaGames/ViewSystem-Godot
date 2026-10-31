@tool
extends Control
# Small native editor surface, like native_callable.gd. No runtime/business dependency.
signal anchors_edited(minimum: Vector2, maximum: Vector2)
var minimum := Vector2.ZERO
var maximum := Vector2.ONE
var editable := false
var dragging := -1
func set_layout_data(data: Resource) -> void:
 editable = data != null
 minimum = Vector2(data.get_meta("AnchorLeft"),data.get_meta("AnchorTop")) if editable else Vector2.ZERO
 maximum = Vector2(data.get_meta("AnchorRight"),data.get_meta("AnchorBottom")) if editable else Vector2.ONE
 queue_redraw()
func frame() -> Rect2:
 return Rect2(Vector2(30,20),Vector2(maxf(size.x-60,1),maxf(size.y-45,1)))
func _draw() -> void:
 var area := frame()
 draw_rect(area,Color(.15,.17,.2))
 draw_rect(area,Color.GRAY,false,1)
 var a := area.position+area.size*minimum
 var b := area.position+area.size*maximum
 draw_line(a,Vector2(b.x,a.y),Color.CORNFLOWER_BLUE,2)
 draw_line(Vector2(b.x,a.y),b,Color.CORNFLOWER_BLUE,2)
 draw_line(b,Vector2(a.x,b.y),Color.CORNFLOWER_BLUE,2)
 draw_line(Vector2(a.x,b.y),a,Color.CORNFLOWER_BLUE,2)
 draw_circle(a,6,Color.ORANGE if editable else Color.GRAY)
 draw_circle(b,4,Color.CYAN if editable else Color.GRAY)
func _gui_input(event: InputEvent) -> void:
 if not editable:return
 var area := frame()
 if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
  if event.pressed:
   var a := area.position+area.size*minimum
   var b := area.position+area.size*maximum
   dragging = 0 if event.shift_pressed or event.position.distance_to(a)<event.position.distance_to(b) else 1
  else:dragging = -1
  accept_event()
 if event is InputEventMouseMotion and dragging>=0:
  var point: Vector2 = ((event.position-area.position)/area.size).clamp(Vector2.ZERO,Vector2.ONE)
  if dragging==0:minimum=point
  else:maximum=point
  anchors_edited.emit(minimum,maximum)
  queue_redraw()
  accept_event()
