#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Text.Json;
namespace MacacaGames.ViewSystem;
[Tool]
public partial class ViewSystemDebuggerPlugin:EditorDebuggerPlugin,ISerializationListener
{
    private readonly HashSet<EditorDebuggerSession> ready=new();
    private RefCounted? nativeBinder;
    public List<ViewObjectGraphSnapshot> Snapshots {get;}=new();
    public override bool _HasCapture(string capture)=>capture=="macaca_viewsystem";
    public override bool _Capture(string message,Godot.Collections.Array data,int sessionId)
    {
        if(message=="macaca_viewsystem:ready"){ready.Add(GetSession(sessionId));return true;}
        if(message!="macaca_viewsystem:object_graph"||data.Count!=1)return false;
        try
        {
        var snapshot=JsonSerializer.Deserialize(data[0].AsString(),ViewDiagnosticsJsonContext.Default.ViewObjectGraphSnapshot);
        if(snapshot!=null)Snapshots.Add(snapshot);if(Snapshots.Count>100)Snapshots.RemoveAt(0);return true;
        }catch(JsonException){return false;}
    }
    public override void _SetupSession(int sessionId)
    {
        nativeBinder??=(RefCounted)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/native_callable.gd").New().AsGodotObject();
        using var args=new Godot.Collections.Array {sessionId};
        var error=(Error)nativeBinder.Call("connect_native",GetSession(sessionId),EditorDebuggerSession.SignalName.Stopped,this,nameof(OnSessionStopped),args).AsInt32();
        if(error!=Error.Ok)throw new System.InvalidOperationException("Cannot bind debugger session lifecycle: "+error);
    }
    private void OnSessionStopped(int sessionId)=>ready.Remove(GetSession(sessionId));
    public void ReleaseSessions()
    {
        nativeBinder?.Call("disconnect_native",default(Variant));nativeBinder?.Dispose();nativeBinder=null;ready.Clear();
    }
    public void OnBeforeSerialize()=>ReleaseSessions();
    public void OnAfterDeserialize(){}
    public bool Capture(string tag)
    {
        bool sent=false;
        foreach(var value in GetSessions())if(value.AsGodotObject() is EditorDebuggerSession session&&session.IsActive()&&ready.Contains(session)){session.SendMessage("macaca_viewsystem:capture",new(){tag});sent=true;}
        return sent;
    }
}
#endif
