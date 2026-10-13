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
