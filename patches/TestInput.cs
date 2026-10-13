using RecompOne.Runtime.Events;

namespace JetMoto2;

// Developer aid, off unless JM2_INPUT names a script file. Each line is
// "<vsync frame> <buttons> [frames held]", e.g. "600 start 4" or "900 down+cross".
// Lets a test run drive the menus without touching the keyboard.
public static class TestInput
{
    private static readonly Dictionary<string, int> Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        ["select"] = 0, ["start"] = 3, ["up"] = 4, ["right"] = 5, ["down"] = 6, ["left"] = 7,
        ["l2"] = 8, ["r2"] = 9, ["l1"] = 10, ["r1"] = 11,
        ["triangle"] = 12, ["circle"] = 13, ["cross"] = 14, ["square"] = 15
    };

    private static readonly List<(long From, long To, ushort Mask)> Script = [];
    private static long _frame;

    public static void Start()
    {
        var path = Environment.GetEnvironmentVariable("JM2_INPUT");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var p = raw.Split('#')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) continue;
            var from = long.Parse(p[0]);
            ushort mask = 0;
            foreach (var b in p[1].Split('+')) mask |= (ushort)(1 << Bits[b]);
            var hold = p.Length > 2 ? long.Parse(p[2]) : 6;
            Script.Add((from, from + hold, mask));
        }

        // The game reads the pad through libpad, which samples Controller.State.
        // The host rewrites that field on every keyboard poll, so while a scripted
        // press is active a helper thread keeps the bits held down.
        Event.AddListener<VSyncEvent>(_ => Interlocked.Increment(ref _frame));
        new Thread(() =>
        {
            while (true)
            {
                var f = Interlocked.Read(ref _frame);
                ushort mask = 0;
                foreach (var (from, to, m) in Script)
                    if (f >= from && f < to)
                        mask |= m;
                if (mask == 0)
                {
                    Thread.Sleep(2);
                    continue;
                }

                RecompOne.Runtime.Hardware.Controller.State &= (ushort)~mask;
                Thread.SpinWait(200);
            }
        }) { IsBackground = true, Name = "jm2-input" }.Start();
        Console.WriteLine($"[input] {Script.Count} scripted press(es) from {path}");
    }
}
