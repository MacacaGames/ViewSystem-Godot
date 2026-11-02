using System;
namespace MacacaGames.ViewSystem;
[AttributeUsage(AttributeTargets.Field|AttributeTargets.Property)]
public sealed class ViewIdAttribute:Attribute
{
    public ViewKind Kind {get;}
    public string CatalogProperty {get;}
    public ViewIdAttribute(ViewKind kind=ViewKind.Page,string catalogProperty="Catalog"){Kind=kind;CatalogProperty=catalogProperty;}
}
