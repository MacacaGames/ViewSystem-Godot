#!/usr/bin/env python3
"""Verify a standalone addon checkout in a disposable Godot .NET consumer."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
from xml.sax.saxutils import escape

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--godot', required=True)
parser.add_argument('--dotnet', default=shutil.which('dotnet'))
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--nuget-source', type=Path)
args = parser.parse_args()
source = Path(__file__).resolve().parents[1]
out = args.output.resolve()
project = out / 'Consumer'
if project.exists():
    parser.error('Use a fresh output directory; Consumer already exists')
project.mkdir(parents=True)
shutil.copytree(source, project / 'addons/macaca_viewsystem',
                ignore=shutil.ignore_patterns('.git', '.godot', '.tools', 'bin', 'obj', '__pycache__', 'artifacts'))
(project / 'Portable.csproj').write_text('''<Project Sdk="Godot.NET.Sdk/4.7.2">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><EnableDynamicLoading>true</EnableDynamicLoading><ImplicitUsings>disable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>''')
(project / 'global.json').write_text(json.dumps({'sdk': {'version': '8.0.427', 'rollForward': 'latestPatch'}}))
feed = str(args.nuget_source.resolve()) if args.nuget_source else 'https://api.nuget.org/v3/index.json'
(project / 'NuGet.Config').write_text('<configuration><packageSources><clear/><add key="packages" value="' + escape(feed, {'"': '&quot;'}) + '"/></packageSources></configuration>')
(project / 'HostNamespaces.cs').write_text('using Godot; namespace Consumer; public partial class HostNamespaces : Node { public FileAccess? File; public Timer? Clock; public Environment? World; }')
(project / 'project.godot').write_text('''config_version=5
[application]
config/name="ViewSystem standalone consumer"
run/main_scene="res://Smoke.tscn"
config/features=PackedStringArray("4.7", "C#", "GL Compatibility")
[dotnet]
project/assembly_name="Portable"
[rendering]
renderer/rendering_method="gl_compatibility"
[editor_plugins]
enabled=PackedStringArray("res://addons/macaca_viewsystem/plugin.cfg", "res://addons/smoke/plugin.cfg")
''')
(project / 'Smoke.tscn').write_text('[gd_scene format=3]\n[ext_resource type="Script" path="res://Smoke.cs" id="1"]\n[node name="Smoke" type="Node"]\nscript=ExtResource("1")\n')
(project / 'Smoke.cs').write_text('''using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using MacacaGames.ViewSystem;
public partial class Smoke : Node
{
    public override async void _Ready()
    {
        var checks = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        try
        {
            var catalog = GD.Load<ViewCatalog>("res://addons/macaca_viewsystem/Examples/Catalog.tres");
            Check(catalog.Entries.Count == 1, "Shipped catalog loads");
            var overlay = new ViewEntry { Id="Confirm", Kind=ViewKind.Overlay, Scene=catalog.Entries[0].Scene };
            overlay.Overrides.Add(new ViewPropertyOverride { Target="Title", Property="text", ModelKey="message" });
            catalog.Entries.Add(overlay);
            catalog.Validate();
            var host = new ViewHost { Catalog=catalog };
            AddChild(host);
            var page = await host.ShowPageAsync("Welcome");
            Check(host.CurrentPage == page, "Page activation owns current page");
            Check(page.Root.IsInsideTree(), "Real page is mounted");
            Check(page.Root.GetNode<Label>("Title").Text == "Macaca ViewSystem", "Shipped scene renders source label");
            var dialog = await host.ShowOverlayAsync("Confirm", new ViewModels().Set("message", "Ready?"));
            Check(host.Overlays.Count == 1, "Overlay mounted");
            Check(dialog.Root.GetNode<Label>("Title").Text == "Ready?", "Model-backed override applied");
            Check(host.CurrentPage == page, "Overlay preserves underlying page");
            await host.DismissOverlayAsync(dialog);
            Check(host.Overlays.Count == 0, "Dismiss releases overlay");
            bool rejected = false;
            try { await host.ShowPageAsync("Missing"); } catch (InvalidOperationException) { rejected=true; }
            Check(rejected && host.CurrentPage == page, "Unknown page rejects without replacing current page");
            await host.ShutdownAsync();
            Check(host.CurrentPage == null, "Shutdown releases current page");
            host.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://runtime.json"), JsonSerializer.Serialize(new { passed=true, checks }));
            GetTree().Quit();
        }
        catch (Exception error)
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath("res://runtime.json"), JsonSerializer.Serialize(new { passed=false, checks, error=error.ToString() }));
            GD.PushError(error.ToString()); GetTree().Quit(1);
        }
    }
}
''')
driver = project / 'addons/smoke'
driver.mkdir()
(driver / 'plugin.cfg').write_text('[plugin]\nname="Standalone validation driver"\ndescription="Test only"\nauthor="Macaca"\nversion="1"\nscript="probe.gd"\n')
(driver / 'probe.gd').write_text('''@tool
extends EditorPlugin
func _enter_tree():
 call_deferred("run")
func run():
 await get_tree().create_timer(2).timeout
 var plugin = get_tree().get_first_node_in_group("macaca_viewsystem_editor")
 var passed = plugin != null
 if passed:
  plugin.call("OpenCatalog", "res://addons/macaca_viewsystem/Examples/Catalog.tres", false)
  EditorInterface.set_main_screen_editor("ViewSystem")
  plugin.call("SetPreviewSize", 1080, 1920)
  passed = plugin.call("ValidateAllDefinitions") == 1 and plugin.call("GetPreviewError") == ""
 await get_tree().create_timer(2).timeout
 EditorInterface.get_base_control().get_viewport().get_texture().get_image().save_png("res://editor.png")
 var file = FileAccess.open("res://editor.json", FileAccess.WRITE)
 file.store_string(JSON.stringify({"passed":passed,"shippedDefinitions":1,"actualGuiEditor":true}))
 file.close()
 EditorInterface.edit_resource(null)
 await get_tree().process_frame
 get_tree().quit(0 if passed else 1)
''')
env = os.environ.copy()
env['DOTNET_ROOT'] = str(Path(args.dotnet).resolve().parent)
env['PATH'] = env['DOTNET_ROOT'] + os.pathsep + env.get('PATH', '')
report = {'passed': False, 'stages': [], 'scope': 'Clean consumer build, runtime 10 checks and actual GUI Editor example; no platform export claim'}
commands = [('build', [args.dotnet, 'build', '--nologo']),
            ('import', [args.godot, '--headless', '--editor', '--path', str(project), '--import', '--quit']),
            ('runtime', [args.godot, '--headless', '--path', str(project)]),
            ('editor', [args.godot, '--editor', '--path', str(project), '--windowed', '--resolution', '1200x800'])]
try:
    for name, command in commands:
        with (out / (name + '.log')).open('w') as log:
            process = subprocess.run(command, cwd=project, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=180)
        text = (out / (name + '.log')).read_text()
        errors = [line for line in text.splitlines() if line.startswith(('ERROR:', 'SCRIPT ERROR:')) or 'ObjectDB instances leaked' in line or 'resources still in use' in line]
        report['stages'].append({'stage': name, 'exitCode': process.returncode, 'errors': errors})
        if process.returncode or errors:
            raise RuntimeError(name + ' failed: ' + text[-5000:])
        print(name + ': PASS', flush=True)
    for name in ('runtime', 'editor'):
        report[name] = json.loads((project / (name + '.json')).read_text())
        if not report[name]['passed']:
            raise RuntimeError(name + ' assertions failed')
    report['passed'] = True
finally:
    (out / 'result.json').write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps({'passed': True, 'evidence': str(out)}, indent=2))
