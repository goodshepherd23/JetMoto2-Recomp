// JetMoto2.exe - first-run builder and launcher for Jet Moto 2 Recompiled.
//
// Contains no game code. On first run it recompiles the game from the disc
// image in disc\ with the bundled RecompOne sources and builds it; after that
// it just starts build\JetMoto2Recomp.exe.
//
//   JetMoto2.exe            play (build first if needed)
//   JetMoto2.exe --rebuild  force a recompile + build, then play
//
// The kit's version.txt is copied to build\.kit-version after a successful
// build. When an update brings a different version.txt (or the stamp is
// missing, as in builds made before v0.1.2), the game is rebuilt automatically
// so fixes in the new kit actually reach the exe.
using System.Diagnostics;
using System.Runtime.InteropServices;

const string CueName = "Jet Moto 2 - Championship Edition (USA).cue";

var root = AppContext.BaseDirectory;
var game = Path.Combine(root, "build", "JetMoto2Recomp.exe");
var cue = Path.Combine(root, "disc", CueName);
var rebuild = args.Contains("--rebuild", StringComparer.OrdinalIgnoreCase);
var kitVersion = ReadText(Path.Combine(root, "version.txt"));
var stampPath = Path.Combine(root, "build", ".kit-version");
var outdated = File.Exists(game) && kitVersion != null && ReadText(stampPath) != kitVersion;

if (rebuild || outdated || !File.Exists(game))
{
    AllocConsole();
    Console.Title = "Jet Moto 2 Recompiled - setup";
    if (outdated && !rebuild)
    {
        Console.WriteLine($"Updated to {kitVersion}: rebuilding the game so the update takes effect.");
        Console.WriteLine("Your settings, cheats and memory cards in build\\ are kept.");
        Console.WriteLine();
    }

    if (Build())
    {
        if (kitVersion != null) File.WriteAllText(stampPath, kitVersion);
    }
    else if (outdated && !rebuild && File.Exists(game))
    {
        // an update shouldn't lock anyone out of a build that worked
        Console.WriteLine();
        Console.WriteLine("The rebuild did not finish, so the previous build will start instead.");
        Console.WriteLine("Run JetMoto2.exe --rebuild to try again. Press Enter to continue.");
        Console.ReadLine();
    }
    else
    {
        Console.WriteLine();
        Console.WriteLine("Setup did not finish. Press Enter to close.");
        Console.ReadLine();
        return 1;
    }
}

// the game finds disc\ beside its build folder on its own
var start = new ProcessStartInfo(game) { WorkingDirectory = Path.GetDirectoryName(game)!, UseShellExecute = false };
using var p = Process.Start(start);
try
{
    if (p != null) p.PriorityClass = ProcessPriorityClass.High;
}
catch
{
    // priority is a nicety
}

return 0;

bool Build()
{
    Console.WriteLine("Jet Moto 2 Recompiled - first-run setup");
    Console.WriteLine("This recompiles the game from your own disc image. It takes a few minutes.");
    Console.WriteLine();

    if (!File.Exists(cue))
    {
        Console.WriteLine("Disc image not found. Copy your dump of");
        Console.WriteLine("  Jet Moto 2 - Championship Edition (USA)   [SCUS-94167, 17 tracks]");
        Console.WriteLine("into this folder, keeping the Redump file names:");
        Console.WriteLine($"  {Path.Combine(root, "disc")}");
        Console.WriteLine($"The cue must be named: {CueName}");
        return false;
    }

    if (!HasSdk())
    {
        Console.WriteLine("The .NET 10 SDK is required to build the game.");
        Console.WriteLine("Install it from https://dotnet.microsoft.com/download/dotnet/10.0");
        Console.WriteLine("or run:  winget install Microsoft.DotNet.SDK.10");
        return false;
    }

    var recompiler = Path.Combine(root, "RecompOne", "RecompOne.Recompiler");
    if (!Directory.Exists(recompiler))
    {
        Console.WriteLine("RecompOne sources are missing. In a git checkout run:");
        Console.WriteLine("  git submodule update --init");
        return false;
    }

    return Step("1/3  Building the recompiler", "dotnet", "build", recompiler, "-c", "Release", "--nologo", "-v", "q")
           && Step("2/3  Recompiling the game from the disc",
               Path.Combine(recompiler, "bin", "Release", "net10.0", "recompone.exe"),
               Path.Combine(root, "config", "JetMoto2.json"))
           && ApplyFixups()
           && Step("3/3  Building the game", "dotnet", "build", Path.Combine(root, "JetMoto2Recomp.csproj"),
               "-c", "Release", "-o", Path.Combine(root, "build"), "--nologo", "-v", "q")
           && File.Exists(game);
}

bool Step(string title, string exe, params string[] arguments)
{
    Console.WriteLine($"== {title}");
    var psi = new ProcessStartInfo(exe)
    {
        WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var a in arguments) psi.ArgumentList.Add(a);
    using var proc = Process.Start(psi)!;
    var log = Path.Combine(root, "setup.log");
    using var writer = new StreamWriter(log, true);
    writer.WriteLine($"== {title}");
    void Pump(string? line, bool show)
    {
        if (line == null) return;
        lock (writer) writer.WriteLine(line);
        if (show) Console.WriteLine("   " + line);
    }

    // the recompiler is chatty; keep the console to errors and its own summary lines
    proc.OutputDataReceived += (_, e) => Pump(e.Data, e.Data != null && (e.Data.Contains("error", StringComparison.OrdinalIgnoreCase) || e.Data.StartsWith("[RecompOne]")));
    proc.ErrorDataReceived += (_, e) => Pump(e.Data, true);
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    proc.WaitForExit();
    if (proc.ExitCode == 0) return true;
    Console.WriteLine($"   failed with exit code {proc.ExitCode}; details are in setup.log");
    return false;
}

// config/fixups.json: single-statement edits to the recompiler output that
// RecompOne's whole-function patches can't express. Same rules as
// tools/apply_fixups.py: replace the first `find` just after `after`.
bool ApplyFixups()
{
    var fixups = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "fixups.json")));
    foreach (var f in fixups.RootElement.EnumerateArray())
    {
        string Get(string name) => f.GetProperty(name).GetString()!;
        var path = Path.Combine(root, "generated", Get("file"));
        var text = File.ReadAllText(path);
        if (text.Contains(Get("replace"))) continue;
        var anchor = text.IndexOf(Get("after"), StringComparison.Ordinal);
        var at = anchor < 0 ? -1 : text.IndexOf(Get("find"), anchor, StringComparison.Ordinal);
        if (at < 0 || at - anchor > 200)
        {
            Console.WriteLine($"   fixup not found in {Get("file")}: {Get("after")} ... {Get("find")}");
            return false;
        }
        File.WriteAllText(path, text[..at] + Get("replace") + text[(at + Get("find").Length)..]);
    }
    return true;
}

static string? ReadText(string path)
{
    try
    {
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }
    catch
    {
        return null;
    }
}

bool HasSdk()
{
    try
    {
        var psi = new ProcessStartInfo("dotnet", "--list-sdks") { UseShellExecute = false, RedirectStandardOutput = true };
        using var proc = Process.Start(psi)!;
        var text = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return text.Split('\n').Any(l => l.StartsWith("10."));
    }
    catch
    {
        return false;
    }
}

[DllImport("kernel32.dll")]
static extern bool AllocConsole();
