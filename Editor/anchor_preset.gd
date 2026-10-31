@tool
extends Button
var mode_x := 1
var mode_y := 1
var anchor_min := Vector2(.5,.5)
var anchor_max := Vector2(.5,.5)
func configure(x: int,y: int) -> void:
 mode_x=x;mode_y=y
 anchor_min=Vector2(0 if x==3 else x*.5,0 if y==3 else y*.5)
 anchor_max=Vector2(1 if x==3 else x*.5,1 if y==3 else y*.5)
 queue_redraw()
func show_anchors(a: Vector2,b: Vector2) -> void:
 anchor_min=a;anchor_max=b;queue_redraw()
func _draw() -> void:
 var area:=Rect2(Vector2(9,9),size-Vector2(18,18))
 draw_rect(area,Color(.65,.65,.65),false,1)
 var a:=area.position+area.size*anchor_min
 var b:=area.position+area.size*anchor_max
 var center:=(a+b)*.5
 var span:=Vector2(area.size.x*.45 if is_equal_approx(a.x,b.x) else absf(b.x-a.x),area.size.y*.45 if is_equal_approx(a.y,b.y) else absf(b.y-a.y))
 var origin:=Vector2(area.position.x+(area.size.x-span.x)*anchor_min.x if is_equal_approx(a.x,b.x) else a.x,area.position.y+(area.size.y-span.y)*anchor_min.y if is_equal_approx(a.y,b.y) else a.y)
 var body:=Rect2(origin,span)
 draw_rect(body,Color(.25,.65,1,.55))
 var red:=Color(1,.42,.28) if not disabled else Color(.45,.45,.45)
 for point in [a,Vector2(b.x,a.y),b,Vector2(a.x,b.y)]:
  draw_line(point-Vector2(3,0),point+Vector2(3,0),red,2)
  draw_line(point-Vector2(0,3),point+Vector2(0,3),red,2)
