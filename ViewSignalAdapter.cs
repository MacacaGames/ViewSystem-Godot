using Godot;
using System;
namespace MacacaGames.ViewSystem;

public sealed record ViewActionEvent(string Action,Control Origin,Variant[] Arguments);
internal static class ViewSignalAdapter
{
    internal static Callable Create(int count,Action<Variant[]> dispatch) => count switch
    {
        0=>Callable.From(()=>dispatch(Array.Empty<Variant>())),
        1=>Callable.From<Variant>(a=>dispatch(new[]{a})),
        2=>Callable.From<Variant,Variant>((a,b)=>dispatch(new[]{a,b})),
        3=>Callable.From<Variant,Variant,Variant>((a,b,c)=>dispatch(new[]{a,b,c})),
        4=>Callable.From<Variant,Variant,Variant,Variant>((a,b,c,d)=>dispatch(new[]{a,b,c,d})),
        5=>Callable.From<Variant,Variant,Variant,Variant,Variant>((a,b,c,d,e)=>dispatch(new[]{a,b,c,d,e})),
        6=>Callable.From<Variant,Variant,Variant,Variant,Variant,Variant>((a,b,c,d,e,f)=>dispatch(new[]{a,b,c,d,e,f})),
        7=>Callable.From<Variant,Variant,Variant,Variant,Variant,Variant,Variant>((a,b,c,d,e,f,g)=>dispatch(new[]{a,b,c,d,e,f,g})),
        8=>Callable.From<Variant,Variant,Variant,Variant,Variant,Variant,Variant,Variant>((a,b,c,d,e,f,g,h)=>dispatch(new[]{a,b,c,d,e,f,g,h})),
        _=>throw new InvalidOperationException("ViewSystem supports signals with up to eight arguments.")
    };
}
