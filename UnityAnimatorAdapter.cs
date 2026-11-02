using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
namespace MacacaGames.ViewSystem;

/// <summary>Conditional flat Animator playback on native AnimationPlayer. Unsupported Unity semantics are reported by the importer.</summary>
[GlobalClass]
public partial class UnityAnimatorAdapter:Node
{
    [Export] public bool AutoStart {get;set;}=true;
    [Export] public NodePath PlayerPath {get;set;}="../AnimationPlayer";
    [Export(PropertyHint.MultilineText)] public string ControllerJson {get;set;}="";
    [Export(PropertyHint.MultilineText)] public string StateAnimationsJson {get;set;}="{}";
    public string CurrentState {get;private set;}="";
    private readonly Dictionary<string,Variant> parameters=new(StringComparer.Ordinal);
    private readonly HashSet<string> triggers=new(StringComparer.Ordinal);
    private readonly Dictionary<string,string> animations=new(StringComparer.Ordinal);
    private JsonDocument? document;
    private JsonElement layer;
    private AnimationPlayer player=null!;
    private double elapsed,previousNormalized;
    private static string Text(JsonElement e,string key,string fallback="")=>e.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()??fallback:fallback;
    private static double Number(JsonElement e,string key,double fallback=0)=>e.TryGetProperty(key,out var p)&&p.TryGetDouble(out var n)?n:fallback;
    private static bool Flag(JsonElement e,string key)=>e.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.True;
    private static IEnumerable<JsonElement> Items(JsonElement e,string key)=>e.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.Array?p.EnumerateArray():Enumerable.Empty<JsonElement>();
    public override void _Ready()
    {
        player=GetNode<AnimationPlayer>(PlayerPath);document=JsonDocument.Parse(ControllerJson);var root=document.RootElement;layer=Items(root,"layers").First();
        using(var mapping=JsonDocument.Parse(StateAnimationsJson))foreach(var pair in mapping.RootElement.EnumerateObject())animations[pair.Name]=pair.Value.GetString()!;
        foreach(var p in Items(root,"parameters")){string name=Text(p,"name"),type=Text(p,"type");parameters[name]=type is "Bool" or "Trigger"?Variant.From(Flag(p,"boolean")):type=="Int"?Variant.From((long)Number(p,"number")):Variant.From(Number(p,"number"));if(type=="Trigger")triggers.Add(name);}
        if(AutoStart)PlayState(Text(layer,"defaultState"));else SetProcess(false);
    }
    public void SetParameter(string name,Variant value)
    {if(!parameters.TryGetValue(name,out var old))throw new ArgumentException("Unknown Animator parameter: "+name);if(old.VariantType!=value.VariantType&&!(old.VariantType==Variant.Type.Float&&value.VariantType==Variant.Type.Int))throw new ArgumentException("Animator parameter type mismatch: "+name);parameters[name]=value;}
    public Variant GetParameter(string name)=>parameters.TryGetValue(name,out var value)?value:throw new ArgumentException("Unknown Animator parameter: "+name);
    public void SetTrigger(string name){if(!triggers.Contains(name))throw new ArgumentException("Unknown trigger: "+name);parameters[name]=true;}
    public void ResetTrigger(string name){if(!triggers.Contains(name))throw new ArgumentException("Unknown trigger: "+name);parameters[name]=false;}
    public void PlayState(string state,double blend=0,double normalizedOffset=0)
    {
        if(!animations.TryGetValue(state,out var animation))throw new InvalidOperationException("State requires a motion adapter: "+state);
        var definition=Items(layer,"states").First(s=>Text(s,"id")==state);double speed=Number(definition,"speed",1);
        string speedParameter=Text(definition,"speedParameter");if(speedParameter.Length>0)speed*=GetParameter(speedParameter).AsDouble();
        SetProcess(true);CurrentState=state;elapsed=0;previousNormalized=0;player.SpeedScale=(float)speed;player.Play(animation,blend);
        double offset=normalizedOffset+Number(definition,"cycleOffset");player.Seek(offset*player.GetAnimation(animation).Length,true);elapsed=offset*player.GetAnimation(animation).Length;previousNormalized=offset;
    }
    public override void _Process(double delta)
    {
        if(document==null||CurrentState.Length==0||!animations.TryGetValue(CurrentState,out var current))return;
        var state=Items(layer,"states").First(s=>Text(s,"id")==CurrentState);double speed=Number(state,"speed",1);string speedParameter=Text(state,"speedParameter");if(speedParameter.Length>0)speed*=GetParameter(speedParameter).AsDouble();
        player.SpeedScale=(float)speed;double length=player.GetAnimation(current).Length;elapsed+=delta*speed;double normalized=elapsed/length;
        // Unity evaluates Any State before the current state's ordered transitions.
        foreach(var transition in Items(layer,"transitions").Where(t=>Text(t,"from")=="*").Concat(Items(layer,"transitions").Where(t=>Text(t,"from")==CurrentState)))
        {
            string next=Text(transition,"to");if(!animations.ContainsKey(next)||next==CurrentState&&!Flag(transition,"canTransitionToSelf"))continue;
            if(Flag(transition,"hasExitTime")){double exit=Number(transition,"exitTime");double threshold=exit>=1?exit:Math.Floor(previousNormalized)+exit;if(threshold<previousNormalized)threshold+=1;if(!(previousNormalized<=threshold&&normalized>=threshold))continue;}
            var conditions=Items(transition,"conditions").ToArray();
            bool Pass(JsonElement c)
            {var value=GetParameter(Text(c,"parameter"));double threshold=Number(c,"threshold");return Text(c,"mode") switch{"If"=>value.AsBool(),"IfNot"=>!value.AsBool(),"Greater"=>value.AsDouble()>threshold,"Less"=>value.AsDouble()<threshold,"Equals"=>value.AsDouble()==threshold,"NotEqual"=>value.AsDouble()!=threshold,_=>false};}
            if(!conditions.All(Pass))continue;
            foreach(var c in conditions){string name=Text(c,"parameter");if(triggers.Contains(name))parameters[name]=false;}
            double duration=Number(transition,"duration")*(Flag(transition,"fixedDuration")?1:length);PlayState(next,duration,Number(transition,"offset"));return;
        }
        previousNormalized=normalized;
    }
    public override void _ExitTree(){document?.Dispose();document=null;parameters.Clear();animations.Clear();triggers.Clear();}
}
