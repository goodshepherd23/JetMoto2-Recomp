// Builds the SDK-named function maps and the RecompOne config for Jet Moto 2.
//
// RecompOne's --autoconfigure guesses overlay load addresses from the code
// alone, and it gets this game wrong: it puts JM2SHELL.BIN at 0x800D0000 and
// gives up on the twenty per-track BINs. The real addresses come from the
// game's own loader (func_80108F14 in SCUS_941.67): index 20 (JM2SHELL) loads
// at 0x800C10B8 and every track module loads at 0x801020B8.
//
// usage: FuncMapGen <cue> <config dir> <signatures psyq.json>
using System.Text.Json;
using RecompOne.Recompiler.AutoConfigure;
using RecompOne.Recompiler.CodeGen;
using RecompOne.Recompiler.Config;
using RecompOne.Runtime.Cdrom;
using Symbols = RecompOne.Recompiler.Symbols;

const string ShellBase = "0x800C10B8";
const string TrackBase = "0x801020B8";

var cue = Path.GetFullPath(args[0]);
var configDir = Path.GetFullPath(args[1]);
var db = SignatureDb.Load(Path.GetFullPath(args[2]));
var funcmaps = Path.Combine(configDir, "funcmaps");
Directory.CreateDirectory(funcmaps);

var fs = DiscFs.Open(cue);
var probe = DiscProbe.Probe(fs, out var boot);
var bootEntry = probe.First(e => e.Path.Equals(boot, StringComparison.OrdinalIgnoreCase));

var overlays = new List<OverlayConfig>();
foreach (var e in probe.Where(e => e.Kind == FileKind.RawCode).OrderBy(e => e.Path, StringComparer.Ordinal))
{
    var stem = Path.GetFileNameWithoutExtension(e.Path).ToLowerInvariant();
    overlays.Add(new OverlayConfig
    {
        Name = stem,
        File = e.Path,
        Base = stem == "jm2shell" ? ShellBase : TrackBase,
        LinearSweep = true
    });
}

var config = new RecompOneConfig
{
    Game = new GameConfig { Id = "SCUS-94167", Name = "JetMoto2", Output = "../generated" },
    Cue = Path.GetRelativePath(configDir, cue).Replace(Path.DirectorySeparatorChar, '/'),
    FuncMap = "funcmaps/main.json",
    LinearSweep = true,
    Overlays = overlays.ToArray()
};

Sweep(new OverlayConfig
{
    Name = "main", File = boot, Skip = bootEntry.Skip, Base = $"0x{bootEntry.Base:X8}", LinearSweep = true
});
foreach (var o in overlays)
    if (Sweep(new OverlayConfig { Name = o.Name, File = o.File, Base = o.Base, LinearSweep = true }))
        o.FuncMap = $"funcmaps/{o.Name}.json";

var json = JsonSerializer.Serialize(new
{
    game = new { id = config.Game.Id, name = config.Game.Name, output = config.Game.Output },
    cue = config.Cue,
    funcMap = config.FuncMap,
    linearSweep = true,
    overlays = overlays.Select(o => new
    {
        name = o.Name, file = o.File, @base = o.Base, funcMap = o.FuncMap, linearSweep = true
    })
}, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(configDir, "JetMoto2.base.json"), json);
return 0;

bool Sweep(OverlayConfig overlay)
{
    var analysis = OverlayWriter.AnalyzeOverlay(config, overlay, fs);
    if (analysis == null) return false;
    var match = SdkMatcher.Name(analysis.Functions, analysis.DiscBin, analysis.ElfInfo.TextBase, db);
    var info = new Symbols.FunctionInfo
    {
        TextBase = analysis.ElfInfo.TextBase,
        LoadAddress = analysis.ElfInfo.LoadAddress
    };
    foreach (var f in analysis.Functions.OrderBy(f => f.Start))
        info.Functions.Add(new Symbols.FunctionEntry { Name = f.Name, Address = f.Start, Size = f.End - f.Start });
    FunctionMapLoader.Save(Path.Combine(funcmaps, $"{overlay.Name}.json"), info);
    Console.WriteLine($"[funcmap] {overlay.Name}: {info.Functions.Count} function(s), {match.Named} named, " +
                      $"{match.Ambiguous} ambiguous [{string.Join(" ", match.Libraries)}]");
    return true;
}
