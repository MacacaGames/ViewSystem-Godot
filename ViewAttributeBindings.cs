using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public enum InjectScope { PageFirst, SharedFirst, PageOnly, SharedOnly }
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property)]
public sealed class ViewElementInjectAttribute : Attribute
{
    public ViewInjectScope Scope { get; }
    public string Key { get; set; } = "";
    public ViewElementInjectAttribute(InjectScope scope=InjectScope.PageFirst) => Scope=scope switch
    {InjectScope.PageOnly=>ViewInjectScope.PageOnly,InjectScope.SharedFirst=>ViewInjectScope.SharedFirst,InjectScope.SharedOnly=>ViewInjectScope.SharedOnly,_=>ViewInjectScope.PageFirst};
}
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property,AllowMultiple=true)]
public sealed class OverridePropertyAttribute : Attribute
{
    public string Path { get; }
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties|DynamicallyAccessedMemberTypes.PublicFields)]
    public Type Component { get; }
    public string Property { get; }
    public OverridePropertyAttribute(string path,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties|DynamicallyAccessedMemberTypes.PublicFields)] Type component,string property)
    {Path=path;Component=component;Property=property;}
}
[AttributeUsage(AttributeTargets.Method,AllowMultiple=true)]
public sealed class OverrideButtonEventAttribute : Attribute
{
    public string Path { get; }
    public OverrideButtonEventAttribute(string path) => Path=path;
}
[AttributeUsage(AttributeTargets.Method)]
public sealed class ViewSystemEventAttribute : Attribute
{
    public string Group { get; }
    public ViewSystemEventAttribute(string group="Default") => Group=group;
    public string GetGroupName()=>Group;
}
/// <summary>Opt-in marker. Register the concrete type at startup to preserve private members under trimming/AOT.</summary>
public interface IViewAttributeReceiver { }

public static class ViewAttributeBindings
{
    internal sealed record Binding(MemberInfo Member,ViewElementInjectAttribute? Inject,OverridePropertyAttribute[] Overrides);
    internal sealed record Event(MethodInfo Method,OverrideButtonEventAttribute Attribute);
    internal sealed record ActionMethod(MethodInfo Method,string Group);
    internal sealed record Schema(Binding[] Members,Event[] Events,ActionMethod[] Actions);
    private static readonly Dictionary<Type,Schema> schemas=new();
    private static readonly Dictionary<Type,Schema?> resolved=new();
    private const DynamicallyAccessedMemberTypes Preserved=DynamicallyAccessedMemberTypes.PublicFields|DynamicallyAccessedMemberTypes.NonPublicFields|DynamicallyAccessedMemberTypes.PublicProperties|DynamicallyAccessedMemberTypes.NonPublicProperties|DynamicallyAccessedMemberTypes.PublicMethods|DynamicallyAccessedMemberTypes.NonPublicMethods;
    public static void Register<[DynamicallyAccessedMembers(Preserved)] T>() where T:Node
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
        var type=typeof(T);
        var members=type.GetFields(flags).Cast<MemberInfo>().Concat(type.GetProperties(flags)).Select(m=>new Binding(m,
            m.GetCustomAttribute<ViewElementInjectAttribute>(false),m.GetCustomAttributes<OverridePropertyAttribute>(false).ToArray()))
            .Where(m=>m.Inject!=null||m.Overrides.Length>0).ToArray();
        foreach(var binding in members)
        {
            if(binding.Member is PropertyInfo property&&(property.GetIndexParameters().Length>0||!property.CanRead||binding.Inject!=null&&!property.CanWrite))
                throw new InvalidOperationException("Attribute property must be readable, non-indexed and writable for injection: "+property.Name);
            if(binding.Member is FieldInfo field&&binding.Inject!=null&&field.IsInitOnly)
                throw new InvalidOperationException("Injection cannot target a readonly field: "+field.Name);
        }
        var events=type.GetMethods(flags).SelectMany(m=>m.GetCustomAttributes<OverrideButtonEventAttribute>(false).Select(a=>new Event(m,a))).ToArray();
        foreach(var binding in events)
        {
            var args=binding.Method.GetParameters();
            if(binding.Method.ReturnType!=typeof(void)||args.Length>1||args.Length==1&&!args[0].ParameterType.IsAssignableFrom(typeof(Control)))
                throw new InvalidOperationException("Button handler must return void and take no argument or a Node/Control origin: "+binding.Method.Name);
        }
        var actions=type.GetMethods(flags).Select(m=>(Method:m,Attribute:m.GetCustomAttribute<ViewSystemEventAttribute>(false))).Where(p=>p.Attribute!=null).Select(p=>new ActionMethod(p.Method,p.Attribute!.Group)).ToArray();
        foreach(var action in actions){var args=action.Method.GetParameters();if(action.Method.ReturnType!=typeof(void)||args.Length>1||args.Length==1&&args[0].ParameterType!=typeof(ViewActionEvent)&&!args[0].ParameterType.IsAssignableFrom(typeof(Control)))throw new InvalidOperationException("ViewSystemEvent must accept no argument, Node/Control, or ViewActionEvent: "+action.Method.Name);}
        schemas[type]=new Schema(members,events,actions);resolved.Clear();
    }
    internal static Schema? For(Node node)
    {
        if(resolved.TryGetValue(node.GetType(),out var cached))
        {if(cached==null&&node is IViewAttributeReceiver)throw new InvalidOperationException("Register attribute receiver before creating views: "+node.GetType().Name);return cached;}
        var parts=new List<Schema>();
        for(Type? type=node.GetType();type!=null;type=type.BaseType)if(schemas.TryGetValue(type,out var registered))parts.Add(registered);
        if(parts.Count>0)return resolved[node.GetType()]=new(parts.AsEnumerable().Reverse().SelectMany(p=>p.Members).ToArray(),parts.AsEnumerable().Reverse().SelectMany(p=>p.Events).ToArray(),parts.AsEnumerable().Reverse().SelectMany(p=>p.Actions).ToArray());
        if(node is IViewAttributeReceiver)throw new InvalidOperationException("Register attribute receiver with ViewAttributeBindings.Register<"+node.GetType().Name+">() before creating views; this roots its AOT members.");
        resolved[node.GetType()]=null;return null;
    }
    internal static Type ValueType(MemberInfo member)=>member is FieldInfo f?f.FieldType:((PropertyInfo)member).PropertyType;
    internal static object? Read(MemberInfo member,object owner)=>member is FieldInfo f?f.GetValue(owner):((PropertyInfo)member).GetValue(owner);
    internal static void Write(MemberInfo member,object owner,object? value)
    {if(member is FieldInfo field)field.SetValue(owner,value);else ((PropertyInfo)member).SetValue(owner,value);}
    internal static MemberInfo TargetMember(OverridePropertyAttribute attribute)
        => (MemberInfo?)attribute.Component.GetProperty(attribute.Property)??attribute.Component.GetField(attribute.Property)
            ??throw new InvalidOperationException("Override member not found: "+attribute.Component.Name+"."+attribute.Property);
}
