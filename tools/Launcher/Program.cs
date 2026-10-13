// JetMoto2.exe - first-run builder and launcher for Jet Moto 2 Recompiled.
//
// Contains no game code. On first run it recompiles the game from the disc
// image in disc\ with the bundled RecompOne sources and builds it; after that
// it just starts build\JetMoto2Recomp.exe.
//
//   JetMoto2.exe            play (build first if needed)
//   JetMoto2.exe --rebuild  force a recompile + build, then play
using System.Diagnostics;
using System.Runtime.InteropServices;

const string CueName = "Jet Moto 2 - Championship Edition (USA).cue";

var root = AppContext.BaseDirectory;
var game = Path.Combine(root, "build", "JetMoto2Recomp.exe");
var cue = Path.Combine(root, "disc", CueName);
var rebuild = args.Contains("--rebuild", StringComparer.OrdinalIgnoreCase);

if (rebuild || !File.Exists(game))
{
    AllocConsole();
    Console.Title = "Jet Moto 2 Recompiled - setup";
    if (!Build())
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
