#if TOOLS
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Security.Cryptography;
namespace MacacaGames.ViewSystem;

public sealed record UnityConversionIssue(string Asset,string Location,string Message);
public sealed record UnityConversionResult(string Output,int Converted,IReadOnlyDictionary<string,string> Assets,IReadOnlyList<UnityConversionIssue> Issues)
{public bool Complete=>Issues.Count==0;}
/// <summary>Fresh-output importer; never edits Unity sources or overwrites authored Godot assets.</summary>
public sealed class UnityAssetConverter
{
    public readonly Dictionary<string,Func<JsonElement,IReadOnlyDictionary<string,Resource>,Resource>> Assets=new(StringComparer.Ordinal);
    public readonly Dictionary<string,Action<JsonElement,Node>> Components=new(StringComparer.Ordinal);
    public readonly Dictionary<string,Func<JsonElement,Node,(string Property,double Scale,double Offset)?>> Bindings=new(StringComparer.Ordinal);
    private readonly Dictionary<string,JsonElement> source=new(StringComparer.Ordinal);
    private readonly Dictionary<string,Resource> resources=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> outputs=new(StringComparer.Ordinal);
    private readonly List<UnityConversionIssue> issues=new();
    private string output="",bundle="",asset="";
    internal static string Text(JsonElement e,string key,string fallback="")=>e.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()??fallback:fallback;
    internal static double Number(JsonElement e,string key,double fallback=0)=>e.TryGetProperty(key,out var p)&&p.TryGetDouble(out double value)?value:fallback;
    internal static bool Flag(JsonElement e,string key,bool fallback=false)=>e.TryGetProperty(key,out var p)&&p.ValueKind is JsonValueKind.True or JsonValueKind.False?p.GetBoolean():fallback;
    internal static IEnumerable<JsonElement> Items(JsonElement e,string key)=>e.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.Array?p.EnumerateArray():Enumerable.Empty<JsonElement>();
    internal static string Id(JsonElement e)=>Text(e,"guid")+":"+(e.TryGetProperty("fileId",out var id)?id.GetInt64():0).ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string SafeName(string value)=>string.Concat(value.Select(c=>char.IsLetterOrDigit(c)||c is '_' or '-'?c:'_'));
    private string PathFor(JsonElement a,string extension)=>output+"/"+Text(a,"guid")+"_"+a.GetProperty("fileId").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)+extension;
    private void Issue(string location,string message)=>issues.Add(new(asset,location,message));
    private void Save(Resource resource,string path){var error=ResourceSaver.Save(resource,path);if(error!=Error.Ok)throw new IOException("Save failed: "+path+" ("+error+")");}
    public UnityConversionResult Import(string manifestPath,string destination)
    {
        if(source.Count>0)throw new InvalidOperationException("Use a new converter for each bundle.");
        if(!destination.StartsWith("res://",StringComparison.Ordinal)||destination.Contains("..")||destination.Length<=6)throw new ArgumentException("Output must be a fresh res:// subdirectory.");
        bundle=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifestPath))!;output=destination.TrimEnd('/');
        string absolute=ProjectSettings.GlobalizePath(output);if(Directory.Exists(absolute)&&Directory.EnumerateFileSystemEntries(absolute).Any())throw new IOException("Output already contains files: "+output);
        using var doc=JsonDocument.Parse(File.ReadAllText(manifestPath));var manifest=doc.RootElement;
        if(Number(manifest,"schema")!=1)throw new InvalidDataException("Unsupported Unity bundle schema.");
        foreach(var a in Items(manifest,"assets")){string id=Id(a);if(Text(a,"guid").Length!=32||!Text(a,"guid").All(Uri.IsHexDigit)||source.ContainsKey(id))throw new InvalidDataException("Invalid or duplicate asset identity: "+id);source.Add(id,a.Clone());}
        // Validate every copied file before producing output.
        foreach(var a in source.Values.GroupBy(a=>Text(a,"file")).Select(g=>g.First()))
        {string file=BundleFile(Text(a,"file"));string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();if(hash!=Text(a,"sha256"))throw new InvalidDataException("Source hash mismatch: "+Text(a,"path"));}
        Directory.CreateDirectory(absolute);Directory.CreateDirectory(absolute+"/_source");File.WriteAllText(absolute+"/_source/.gdignore","");
        File.Copy(manifestPath,absolute+"/_source/manifest.json");
        foreach(var a in source.Values.GroupBy(a=>Text(a,"file")).Select(g=>g.First()))File.Copy(BundleFile(Text(a,"file")),absolute+"/_source/"+System.IO.Path.GetFileName(Text(a,"file"))+(System.IO.Path.GetExtension(Text(a,"file"))==".cs"?".txt":""));
        foreach(var text in Items(manifest,"issues"))issues.Add(new("Unity exporter","",text.GetString()??"Unknown export error"));
        foreach(var a in source.Values)
        {
            asset=Text(a,"path")+"#"+Number(a,"fileId");
            UnitySourceAsset metadata=Text(a,"kind")=="Resource"?new UnitySerializedResource():new UnitySourceAsset();
            metadata.SourceGuid=Text(a,"guid");metadata.SourceFileId=a.GetProperty("fileId").GetInt64();metadata.SourcePath=Text(a,"path");metadata.SourceType=Text(a,"type");metadata.SourceJson=a.GetRawText();
            resources[Id(a)]=metadata;Save(metadata,PathFor(a,".source.tres"));
            if(Text(a,"kind")=="File")ConvertFile(a);
        }
        foreach(var a in source.Values.Where(a=>Text(a,"kind")=="Sprite"))ConvertSprite(a);
        foreach(var a in source.Values)
            if(Assets.TryGetValue(Text(a,"type"),out var adapter)){asset=Text(a,"path");var converted=adapter(a,resources);resources[Id(a)]=converted;string path=PathFor(a,".adapter.tres");Save(converted,path);outputs[Id(a)]=path;}
        foreach(var a in source.Values.Where(a=>Text(a,"kind") is "Prefab" or "Scene"))if(!outputs.ContainsKey(Id(a)))ConvertScene(a);
        foreach(var a in source.Values.Where(a=>Text(a,"kind")=="Resource"))if(!outputs.ContainsKey(Id(a)))ConvertResource(a);
        foreach(var a in source.Values.Where(a=>Text(a,"kind") is "Animation" or "Animator" or "Source"))
        {asset=Text(a,"path");if(!outputs.ContainsKey(Id(a))){outputs[Id(a)]=PathFor(a,".source.tres");Issue(Text(a,"name"),Text(a,"kind")=="Animation"?"Standalone clip needs a target hierarchy; source curves retained for an Animator or custom binding adapter.":"Source metadata retained; this asset needs a registered adapter or native replacement.");}}
        foreach(var a in source.Values)if(!outputs.ContainsKey(Id(a)))outputs[Id(a)]=PathFor(a,".source.tres");
        var result=new UnityConversionResult(output,outputs.Values.Count(path=>!path.EndsWith(".source.tres",StringComparison.Ordinal)),outputs,issues);
        File.WriteAllText(absolute+"/conversion-report.json",JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return result;
    }
    private string BundleFile(string relative)
    {var path=System.IO.Path.GetFullPath(System.IO.Path.Combine(bundle,relative));if(!path.StartsWith(bundle+System.IO.Path.DirectorySeparatorChar,StringComparison.Ordinal)||!File.Exists(path))throw new InvalidDataException("Invalid bundle file: "+relative);return path;}
    private void ConvertFile(JsonElement a)
    {
        asset=Text(a,"path");string file=BundleFile(Text(a,"file")),ext=System.IO.Path.GetExtension(Text(a,"path")).ToLowerInvariant();Resource? value=null;
        if(ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tga")
        {var image=Image.LoadFromFile(file);if(image!=null&&!image.IsEmpty())value=ImageTexture.CreateFromImage(image);}
        else if(ext==".wav")value=AudioStreamWav.LoadFromFile(file);
        else if(ext==".ogg")value=AudioStreamOggVorbis.LoadFromFile(file);
        else if(ext==".mp3")value=new AudioStreamMP3{Data=File.ReadAllBytes(file)};
        else if(ext is ".ttf" or ".otf"){var font=new FontFile();if(font.LoadDynamicFont(file)==Error.Ok)value=font;}
        if(value==null){Issue("file","No native file adapter for "+ext);return;}
        resources[Id(a)]=value;string path=PathFor(a,".tres");Save(value,path);outputs[Id(a)]=path;
    }
    private void ConvertResource(JsonElement a)
    {
        asset=Text(a,"path");var resource=(UnitySerializedResource)resources[Id(a)];
        foreach(var p in Items(a,"properties"))
        {
            string path=Text(p,"path"),type=Text(p,"type");if(path=="m_Script")continue;
            Variant value;
            switch(type)
            {
                case "Integer":case "Enum":case "LayerMask":value=p.TryGetProperty("integer",out var integer)?integer.GetInt64():(long)Number(p,"number");break;
                case "Float":value=Number(p,"number");break;
                case "Boolean":value=Flag(p,"boolean");break;
                case "String":value=Text(p,"text");break;
                case "Vector2":var v2=Vector(p,"vector",2);value=new Vector2(v2[0],v2[1]);break;
                case "Vector3":var v3=Vector(p,"vector",3);value=new Vector3(v3[0],v3[1],v3[2]);break;
                case "Vector4":var v4=Vector(p,"vector",4);value=new Vector4(v4[0],v4[1],v4[2],v4[3]);break;
                case "Color":var color=Vector(p,"vector",4);value=new Color(color[0],color[1],color[2],color[3]);break;
                case "ObjectReference":
                    value=default;if(p.TryGetProperty("reference",out var reference)&&Text(reference,"guid").Length>0){if(resources.TryGetValue(Id(reference),out var referenced))value=referenced;else Issue(path,"Resource reference needs an adapter: "+Id(reference));}break;
                case "Generic":case "ArraySize":continue;
                default:Issue(path,"Serialized data retained in source JSON; property type needs an adapter: "+type);continue;
            }
            resource.Fields[path]=value;
        }
        string outputPath=PathFor(a,".tres");Save(resource,outputPath);outputs[Id(a)]=outputPath;
    }
    private void ConvertSprite(JsonElement a)
    {
        asset=Text(a,"path");if(!a.TryGetProperty("reference",out var reference)||!resources.TryGetValue(Id(reference),out var texture)||texture is not Texture2D t){Issue(Text(a,"name"),"Sprite texture reference is missing.");return;}
        var rect=Items(a,"rect").Select(v=>(float)v.GetDouble()).ToArray();if(rect.Length!=5){Issue("sprite","Invalid sprite rectangle");return;}
        var sprite=new AtlasTexture{Atlas=t,Region=new(rect[0],rect[4]-rect[1]-rect[3],rect[2],rect[3])};resources[Id(a)]=sprite;var path=PathFor(a,".tres");Save(sprite,path);outputs[Id(a)]=path;
    }
    private void ConvertScene(JsonElement a)
    {
        asset=Text(a,"path");if(!a.TryGetProperty("root",out var tree)||tree.ValueKind!=JsonValueKind.Object){Issue("root","Missing hierarchy");return;}
        var nodes=new Dictionary<string,Node>(StringComparer.Ordinal);var animators=new List<(Node Node,JsonElement Component)>();
        Node Build(JsonElement data,string path)
        {
            var components=Items(data,"components").ToArray();bool Has(string suffix)=>components.Any(c=>Text(c,"type").EndsWith(suffix,StringComparison.Ordinal));
            bool ui=Flag(data,"ui");Node node=ui?Has(".Button")?new Button():Has(".Text")||Has(".TextMeshProUGUI")?new Label():Has(".Image")||Has(".RawImage")?new TextureRect():new Control():new Node3D();
            node.Name=SafeName(Text(data,"name","Node"));if(node.Name.ToString().Length==0)node.Name="Node";nodes[path]=node;
            var position=Vector(data,"position",3);var scale=Vector(data,"scale",3,1);var rotation=Vector(data,"rotation",3);
            if(node is Control control)
            {
                var anchors=Vector(data,"anchors",4);var offsets=Vector(data,"offsets",4);var pivot=Vector(data,"pivot",2);
                control.AnchorLeft=anchors[0];control.AnchorTop=anchors[1];control.AnchorRight=anchors[2];control.AnchorBottom=anchors[3];control.OffsetLeft=offsets[0];control.OffsetTop=offsets[1];control.OffsetRight=offsets[2];control.OffsetBottom=offsets[3];control.PivotOffset=new Vector2(pivot[0],pivot[1])*control.Size;control.Scale=new(scale[0],scale[1]);control.Rotation=-Mathf.DegToRad(rotation[2]);control.Visible=Flag(data,"active",true);
            }
            else if(node is Node3D spatial){spatial.Position=new(position[0],position[1],-position[2]);spatial.Scale=new(scale[0],scale[1],scale[2]);spatial.RotationDegrees=new(-rotation[0],-rotation[1],rotation[2]);spatial.Visible=Flag(data,"active",true);}
            foreach(var c in components)
            {
                string type=Text(c,"type");if(Components.TryGetValue(type,out var custom)){custom(c,node);continue;}
                if(type=="UnityEngine.Animator"){animators.Add((node,c));continue;}
                if(!ApplyComponent(c,node))Issue(path+"/"+type,"Component retained in source metadata; register a component adapter.");
            }
            foreach(var child in Items(data,"children")){string childPath=path.Length==0?Text(child,"name"):path+"/"+Text(child,"name");var nested=Build(child,childPath);if(node.HasNode(nested.Name.ToString())){Issue(childPath,"Duplicate/normalized sibling name; animation paths require a manual mapping.");nested.Name=nested.Name+"_"+node.GetChildCount();}node.AddChild(nested);}
            return node;
        }
        var root=Build(tree,"");try
        {
            foreach(var pair in animators)AttachAnimator(root,pair.Node,pair.Component,nodes);
            foreach(var child in ViewComposer.Walk(root).Skip(1))child.Owner=root;
            var packed=new PackedScene();if(packed.Pack(root)!=Error.Ok)throw new InvalidOperationException("Cannot pack imported hierarchy");var path=PathFor(a,".tscn");Save(packed,path);resources[Id(a)]=packed;outputs[Id(a)]=path;
        }finally{root.Free();}
    }
    private static float[] Vector(JsonElement e,string name,int count,float fallback=0){var values=Items(e,name).Select(v=>(float)v.GetDouble()).ToArray();return Enumerable.Range(0,count).Select(i=>i<values.Length?values[i]:fallback).ToArray();}
    private static JsonElement Property(JsonElement c,string path)=>Items(c,"properties").FirstOrDefault(p=>Text(p,"path")==path);
    private static double PropNumber(JsonElement c,string path,double fallback=0){var p=Property(c,path);return p.ValueKind==JsonValueKind.Object?Number(p,"number",fallback):fallback;}
    private static bool PropBool(JsonElement c,string path,bool fallback=false){var p=Property(c,path);return p.ValueKind==JsonValueKind.Object?Flag(p,"boolean",fallback):fallback;}
    private static string PropText(JsonElement c,string path,string fallback=""){var p=Property(c,path);return p.ValueKind==JsonValueKind.Object?Text(p,"text",fallback):fallback;}
    private Resource? PropResource(JsonElement c,string path){var p=Property(c,path);return p.ValueKind==JsonValueKind.Object&&p.TryGetProperty("reference",out var reference)&&resources.TryGetValue(Id(reference),out var r)?r:null;}
    private bool ApplyComponent(JsonElement c,Node node)
    {
        string type=Text(c,"type");if(type=="UnityEngine.RectTransform"){var value=Property(c,"m_AnchoredPosition");if(value.ValueKind==JsonValueKind.Object){var v=Vector(value,"vector",2);node.SetMeta("unity_anchored",new Vector2(v[0],v[1]));}return true;}
        if(type is "UnityEngine.Transform" or "UnityEngine.CanvasRenderer")return true;
        if(type=="UnityEngine.Canvas"){if(PropNumber(c,"m_RenderMode")!=0)Issue(node.Name,"Camera/world-space Canvas requires a viewport adapter.");return true;}
        if(type=="UnityEngine.UI.CanvasScaler"){Issue(node.Name,"CanvasScaler: set ViewCatalog.ReferenceHeight or project stretch to match the target design.");return true;}
        if(type=="UnityEngine.UI.GraphicRaycaster")return true;
        if(type=="UnityEngine.CanvasGroup"&&node is CanvasItem group){var color=group.Modulate;color.A=(float)PropNumber(c,"m_Alpha",1);group.Modulate=color;if(node is Control ctrl)ctrl.MouseFilter=PropBool(c,"m_BlocksRaycasts",true)?Control.MouseFilterEnum.Stop:Control.MouseFilterEnum.Ignore;return true;}
        if(type=="UnityEngine.UI.Button"&&node is Button button){button.Disabled=!PropBool(c,"m_Interactable",true);if(Items(c,"properties").Any(p=>Text(p,"path").Contains("m_PersistentCalls.m_Calls.Array.data")))Issue(node.Name,"UnityEvent calls need ViewEventBinding actions or a method adapter.");return true;}
        if((type=="UnityEngine.UI.Text"||type=="TMPro.TextMeshProUGUI")&&node is Label label)
        {
            bool tmp=type.StartsWith("TMPro",StringComparison.Ordinal);label.Text=PropText(c,tmp?"m_text":"m_Text");label.AddThemeFontSizeOverride("font_size",(int)PropNumber(c,tmp?"m_fontSize":"m_FontData.m_FontSize",14));
            var color=Property(c,"m_Color");if(color.ValueKind==JsonValueKind.Object){var v=Vector(color,"vector",4,1);label.AddThemeColorOverride("font_color",new(v[0],v[1],v[2],v[3]));}
            if(PropResource(c,"m_FontData.m_Font") is Font font)label.AddThemeFontOverride("font",font);return true;
        }
        if(type is "UnityEngine.UI.Image" or "UnityEngine.UI.RawImage")
        {
            if(node is TextureRect image){image.Texture=PropResource(c,type.EndsWith("RawImage",StringComparison.Ordinal)?"m_Texture":"m_Sprite") as Texture2D;image.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;image.StretchMode=PropBool(c,"m_PreserveAspect")?TextureRect.StretchModeEnum.KeepAspectCentered:TextureRect.StretchModeEnum.Scale;var color=Property(c,"m_Color");if(color.ValueKind==JsonValueKind.Object){var v=Vector(color,"vector",4,1);image.SelfModulate=new(v[0],v[1],v[2],v[3]);}if(PropNumber(c,"m_Type")!=0)Issue(node.Name,"Sliced/tiled/filled Image needs a NinePatchRect or custom shader adapter.");}
            else Issue(node.Name,"Image on a selectable needs a native Theme/StyleBox mapping.");return true;
        }
        return false;
    }
    private void AttachAnimator(Node root,Node target,JsonElement component,Dictionary<string,Node> nodes)
    {
        var refProperty=Property(component,"m_Controller");if(refProperty.ValueKind!=JsonValueKind.Object||!refProperty.TryGetProperty("reference",out var reference)||!source.TryGetValue(Id(reference),out var controller)||Text(controller,"kind")!="Animator"){Issue(target.Name,"Animator controller is missing or requires an override-controller adapter.");return;}
        var layers=Items(controller,"layers").ToArray();if(layers.Length==0){Issue(target.Name,"Animator has no layers");return;}
        if(layers.Length>1)Issue(target.Name,"Additional layers, AvatarMasks and additive blending require a native AnimationTree adapter.");
        var layer=layers[0];if(Items(layer,"machines").Count()>1)Issue(target.Name,"Nested state-machine entry/exit routing requires an adapter; states and direct transitions are retained.");
        var player=new AnimationPlayer{Name="AnimationPlayer"};target.AddChild(player);player.RootNode="..";var library=new AnimationLibrary();
        var stateAnimations=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var state in Items(layer,"states"))
        {
            string stateId=Text(state,"id");if(!state.TryGetProperty("motion",out var motion)||!source.TryGetValue(Id(motion),out var clip)||Text(clip,"kind")!="Animation"){Issue(target.Name+"/"+stateId,"State motion is absent or a BlendTree requiring a motion adapter.");continue;}
            string name="state_"+stateAnimations.Count;stateAnimations[stateId]=name;var animation=ConvertClip(clip,target,nodes);library.AddAnimation(name,animation);outputs[Id(clip)]=PathFor(clip,".tres");Save(animation,outputs[Id(clip)]);
            if(Flag(state,"mirror")||Text(state,"timeParameter").Length>0||Text(state,"mirrorParameter").Length>0||Text(state,"cycleOffsetParameter").Length>0||Items(state,"behaviours").Any())Issue(stateId,"Mirroring, motion-time/offset parameters and StateMachineBehaviour need adapters.");
        }
        player.AddAnimationLibrary("",library);
        var adapter=new UnityAnimatorAdapter{Name="Animator",PlayerPath="../AnimationPlayer",AutoStart=stateAnimations.ContainsKey(Text(layer,"defaultState")),ControllerJson=controller.GetRawText(),StateAnimationsJson=JsonSerializer.Serialize(stateAnimations)};target.AddChild(adapter);
        var graph=new AnimationNodeStateMachine();var names=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var pair in stateAnimations){string name=SafeName(pair.Key);if(name is "Start" or "End"||names.Values.Contains(name))name+="_"+names.Count;names[pair.Key]=name;var motion=new AnimationNodeAnimation{Animation=pair.Value};motion.SetMeta("unity_state",pair.Key);graph.AddNode(name,motion);}
        foreach(var t in Items(layer,"transitions"))
        {
            string from=Text(t,"from"),to=Text(t,"to");if(stateAnimations.ContainsKey(from)&&stateAnimations.ContainsKey(to)&&from!=to&&!graph.HasTransition(names[from],names[to]))graph.AddTransition(names[from],names[to],new AnimationNodeStateMachineTransition{XfadeTime=(float)Number(t,"duration"),AdvanceMode=AnimationNodeStateMachineTransition.AdvanceModeEnum.Enabled});
            if(Flag(t,"isExit")||Text(t,"interruption","None")!="None")Issue(from+" → "+to,"Exit/interruption semantics require an adapter; basic conditional transition is available.");
        }
        if(stateAnimations.ContainsKey(Text(layer,"defaultState")))graph.AddTransition("Start",names[Text(layer,"defaultState")],new AnimationNodeStateMachineTransition());
        var tree=new AnimationTree{Name="AnimationTree",TreeRoot=graph,AnimPlayer="../AnimationPlayer",Active=false};target.AddChild(tree);tree.SetMeta("migration_note","Editable native graph. Animator adapter drives AnimationPlayer; disable the adapter before enabling this tree.");
        outputs[Id(controller)]=PathFor(controller,".source.tres");
    }
    private Animation ConvertClip(JsonElement clip,Node target,Dictionary<string,Node> nodes)
    {
        var animation=new Animation{Length=Math.Max(.001,Number(clip,"length")),LoopMode=Flag(clip,"loop")?Animation.LoopModeEnum.Linear:Animation.LoopModeEnum.None};
        string prefix=nodes.First(p=>p.Value==target).Key;
        foreach(var curve in Items(clip,"curves"))
        {
            string path=Text(curve,"path"),full=prefix.Length==0?path:path.Length==0?prefix:prefix+"/"+path;
            if(!nodes.TryGetValue(full,out var node)){Issue(full,"Animation target missing");continue;}
            string type=Text(curve,"type"),property=Text(curve,"property");(string Property,double Scale,double Offset)? binding=null;
            if(Bindings.TryGetValue(type+":"+property,out var custom))binding=custom(curve,node);
            else binding=DefaultBinding(type,property,node);
            if(binding==null){Issue(full+":"+property,"No animation binding adapter for "+type);continue;}
            string actual=target.GetPathTo(node)+":"+binding.Value.Property;int track=animation.AddTrack(Animation.TrackType.Value);animation.TrackSetPath(track,actual);animation.ValueTrackSetUpdateMode(track,Flag(curve,"discrete")?Animation.UpdateMode.Discrete:Animation.UpdateMode.Continuous);
            foreach(var sample in Items(curve,"samples"))
            {
                Variant value;
                if(Flag(curve,"discrete")){if(!sample.TryGetProperty("reference",out var reference)||!resources.TryGetValue(Id(reference),out var resource)){Issue(actual,"Object key reference missing");continue;}value=resource;}
                else {double number=Number(sample,"value")*binding.Value.Scale+binding.Value.Offset;value=binding.Value.Property=="visible"?Variant.From(number>=.5):Variant.From(number);}
                animation.TrackInsertKey(track,Number(sample,"time"),value);
            }
            if(node is Control control&&property is "m_AnchoredPosition.x" or "m_AnchoredPosition.y")
            {
                bool x=property.EndsWith(".x",StringComparison.Ordinal);int paired=animation.AddTrack(Animation.TrackType.Value);animation.TrackSetPath(paired,target.GetPathTo(node)+":"+(x?"offset_right":"offset_bottom"));
                float extent=x?control.OffsetRight-control.OffsetLeft:control.OffsetBottom-control.OffsetTop;
                foreach(var sample in Items(curve,"samples"))animation.TrackInsertKey(paired,Number(sample,"time"),Number(sample,"value")*binding.Value.Scale+binding.Value.Offset+extent);
            }
        }
        foreach(var e in Items(clip,"events"))Issue(Text(e,"function")+"@"+Number(e,"time"),"AnimationEvent retained; map explicitly to a Godot method or ViewSystem action.");return animation;
    }
    private static (string,double,double)? DefaultBinding(string type,string property,Node target)
    {
        if(property=="m_IsActive")return("visible",1,0);
        if(property=="m_Alpha")return("modulate:a",1,0);
        if(property.StartsWith("m_Color.",StringComparison.Ordinal))return("self_modulate:"+property[^1],1,0);
        if(property is "m_Sprite" or "m_Texture"&&target is TextureRect)return("texture",1,0);
        string? axis=property.EndsWith(".x",StringComparison.Ordinal)?"x":property.EndsWith(".y",StringComparison.Ordinal)?"y":property.EndsWith(".z",StringComparison.Ordinal)?"z":null;
        if(axis==null)return null;
        if(property.StartsWith("m_LocalScale.",StringComparison.Ordinal))return("scale:"+axis,1,0);
        if(target is Node3D&&property.StartsWith("m_LocalPosition.",StringComparison.Ordinal))return("position:"+axis,axis=="z"?-1:1,0);
        if(target is Node3D&&property.StartsWith("localEulerAnglesRaw.",StringComparison.Ordinal))return("rotation_degrees:"+axis,axis=="z"?1:-1,0);
        if(target is Control&&axis=="z"&&property.StartsWith("localEulerAnglesRaw.",StringComparison.Ordinal))return("rotation",-Math.PI/180,0);
        if(target is Control control&&property is "m_AnchoredPosition.x" or "m_AnchoredPosition.y")
        {var initial=control.HasMeta("unity_anchored")?control.GetMeta("unity_anchored").AsVector2():Vector2.Zero;return axis=="x"?("offset_left",1,control.OffsetLeft-initial.X):("offset_top",-1,control.OffsetTop+initial.Y);}
        return null;
    }
}
#endif
