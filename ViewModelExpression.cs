using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace MacacaGames.ViewSystem;

internal interface IViewInjectValues
{
    Type ValueType {get;}
    IEnumerable<KeyValuePair<string,object>> Entries {get;}
}
public sealed class ViewInjectDictionary<T>:Dictionary<string,T>,IViewInjectValues where T:notnull
{
    Type IViewInjectValues.ValueType=>typeof(T);
    IEnumerable<KeyValuePair<string,object>> IViewInjectValues.Entries=>this.Select(p=>new KeyValuePair<string,object>(p.Key,p.Value));
}
internal static class ViewModelExpression
{
    private const string ExpressionPattern="^\\{(PageModel|SharedModel|Model|PageFirst|PageOnly|SharedFirst|SharedOnly)\\.([^\\[{}]+?)(?:\\[\"([^\"]*)\"\\])?\\}$";
    internal static bool IsValidExpression(string expression)=>Regex.IsMatch(expression,ExpressionPattern,RegexOptions.CultureInvariant);
    internal static Variant Resolve(ViewContext context,string expression)
    {
        var match=Regex.Match(expression,ExpressionPattern,RegexOptions.CultureInvariant);
        if(!match.Success)throw new InvalidOperationException("Model expression must be {PageModel.Type[\"key\"]} or {SharedOnly.Type}.");
        var scope=match.Groups[1].Value switch {"SharedModel" or "SharedFirst"=>ViewInjectScope.SharedFirst,"SharedOnly"=>ViewInjectScope.SharedOnly,"PageOnly"=>ViewInjectScope.PageOnly,_=>ViewInjectScope.PageFirst};
        string type=match.Groups[2].Value,key=match.Groups[3].Value;
        return ToVariant(context.ModelByName(type,key,scope));
    }
    private static Variant ToVariant(object value)=>value switch
    {
        Variant v=>v,string v=>v,bool v=>v,int v=>v,long v=>v,float v=>v,double v=>v,
        Color v=>v,Vector2 v=>v,Vector3 v=>v,Vector2I v=>v,Vector3I v=>v,
        GodotObject v=>Variant.From(v),Enum v=>Convert.ToInt64(v),
        _=>throw new InvalidOperationException("Model expression result is not a supported Godot Variant: "+value.GetType().Name)
    };
}
