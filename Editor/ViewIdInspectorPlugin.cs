#if TOOLS
using Godot;
using System.Reflection;
using System.Linq;
namespace MacacaGames.ViewSystem;
public partial class ViewIdInspectorPlugin:EditorInspectorPlugin
{
    public System.Func<ViewCatalog?>? OpenedCatalog {get;set;}
    public override bool _CanHandle(GodotObject obj)=>obj is Node or Resource;
    public override bool _ParseProperty(GodotObject obj,Variant.Type type,string name,PropertyHint hintType,string hintString,PropertyUsageFlags usageFlags,bool wide)
    {
        if(type!=Variant.Type.String)return false;
        var member=obj.GetType().GetMember(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).FirstOrDefault();
        var attribute=member?.GetCustomAttribute<ViewIdAttribute>();if(attribute==null)return false;
        ViewKind kind=obj is ViewNavigationButton navigation&&name=="Page"?navigation.Operation==ViewButtonOperation.ChangePage?ViewKind.Page:ViewKind.Overlay:attribute.Kind;
        var catalog=obj is ViewNavigationButton nav?nav.Host?.Catalog:null;
        catalog??=obj.GetPropertyList().Any(p=>p["name"].AsString()==attribute.CatalogProperty)?obj.Get(attribute.CatalogProperty).AsGodotObject() as ViewCatalog:null;
        catalog??=OpenedCatalog?.Invoke();
        string path=ProjectSettings.GetSetting("macaca_viewsystem/catalog","").AsString();
        if(catalog==null&&path.Length>0&&ResourceLoader.Exists(path))catalog=GD.Load<ViewCatalog>(path);
        if(catalog==null)return false;
        var script=GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/view_id_property.gd");
        var editor=(EditorProperty)script.New().AsGodotObject();editor.Name="ViewId_"+name;editor.Call("configure",catalog.Entries.Where(e=>e.Kind==kind).Select(e=>e.Id).ToArray());
        AddPropertyEditor(name,editor);return true;
    }
}
#endif
