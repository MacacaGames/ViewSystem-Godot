#if TOOLS
using Godot;
using System;
namespace MacacaGames.ViewSystem;
[Tool]
public partial class UnityAssetImportRunner:Node
{
    public override void _Ready()
    {
        var args=OS.GetCmdlineUserArgs();string Arg(string name){int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:throw new ArgumentException("Missing "+name);}
        try{var result=new UnityAssetConverter().Import(Arg("--unity-manifest"),Arg("--unity-output"));GD.Print($"Imported {result.Converted} assets; {result.Issues.Count} items require adapters. Report: {result.Output}/conversion-report.json");GetTree().Quit(result.Complete?0:2);}
        catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
}
#endif
