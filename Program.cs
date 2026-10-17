using RecompOne.Runtime.Config;
using RecompOne.Runtime.Dispatch;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Memory;
using Recompiled;

var title = "Jet Moto 2 Recompiled";

Dispatcher.Tolerant = true;
JetMoto2.Diag.Start();
JetMoto2.TestInput.Start();
JetMoto2.Cheats.Register();
JetMoto2.Widescreen.Register();

// Enhancements on by default (all changeable under Settings > Display): PGXP
// fixes the PS1 polygon wobble and is what lets RecompOne interpolate the
// game's 30 fps up to 60; races render 16:9. Only applied where the setting
// has never been chosen, so a player's own choice sticks.
RecompOne.Runtime.Runtime.Defaults(v =>
{
    v.Default("pgxp.enable", true);
    v.Default("display.fps", 60);
    v.Default("jm2.widescreen", 16f / 9f);
});
// JM2_VIEW=key=value;key=value presets runtime view settings for a test run
if (Environment.GetEnvironmentVariable("JM2_VIEW") is { Length: > 0 } viewTest)
    RecompOne.Runtime.Runtime.Defaults(v =>
    {
        foreach (var pair in viewTest.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=', 2) is [var key, var value]) v.Values[key] = value;
    });

// Use the disc beside the build (or the one named on the command line) unless
// the saved setting already points at a file that exists, so a fresh install
// starts without the disc picker.
Event.AddListener<RuntimeReadyEvent>(_ =>
{
    var saved = ConfigManager.Game.CdPath;
    if (!string.IsNullOrWhiteSpace(saved) && File.Exists(saved)) return;
    var candidates = new[]
    {
        args.Length > 0 ? Path.GetFullPath(args[0]) : null,
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "disc", "Jet Moto 2 - Championship Edition (USA).cue"))
    };
    if (candidates.FirstOrDefault(p => p != null && File.Exists(p)) is not { } cue) return;
    ConfigManager.Game.CdPath = cue;
    ConfigManager.SaveGame();
});

RecompOne.Runtime.Runtime.Run(() => Entry.Run(new PSMemory(), null, title));
return 0;
