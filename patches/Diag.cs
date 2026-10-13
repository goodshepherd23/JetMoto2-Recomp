
namespace JetMoto2;

// Developer aid, off unless JM2_TRACE=1: every few seconds print where the
// guest is (return address plus the call chain recovered from the guest stack).
public static class Diag
{
    public static void Start()
    {
        Snapshots();
        if (Environment.GetEnvironmentVariable("JM2_TRACE") != "1") return;
        var seen = new HashSet<string>();
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.DispEnvEvent>(e =>
        {
            var env = e.Context.A0;
            var line = $"[dispenv] disp={e.X},{e.Y} {e.W}x{e.H} screen={(short)e.Memory.ReadU16(env + 8)}," +
                       $"{(short)e.Memory.ReadU16(env + 10)} {(short)e.Memory.ReadU16(env + 12)}x{(short)e.Memory.ReadU16(env + 14)}";
            if (seen.Add(line)) Console.WriteLine(line);
        });
        new Thread(Loop) { IsBackground = true, Name = "jm2-trace" }.Start();
    }

    // JM2_SNAP=<dir>:<frame>,<frame>,... writes the 2 MB of guest RAM to
    // <dir>/ram-<frame>.bin at those vsync counts, for offline RAM searches.
    private static void Snapshots()
    {
        var spec = Environment.GetEnvironmentVariable("JM2_SNAP");
        if (string.IsNullOrEmpty(spec)) return;
        var cut = spec.LastIndexOf(':');
        var dir = spec[..cut];
        var frames = spec[(cut + 1)..].Split(',').Select(long.Parse).ToHashSet();
        long frame = 0;
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.VSyncEvent>(e =>
        {
            if (!frames.Contains(++frame)) return;
            var ram = new byte[0x200000];
            for (uint i = 0; i < ram.Length; i += 4)
                BitConverter.TryWriteBytes(ram.AsSpan((int)i), e.Memory.ReadU32(0x80000000u + i));
            File.WriteAllBytes(Path.Combine(dir, $"ram-{frame}.bin"), ram);
            Console.WriteLine($"[snap] frame {frame}");
        });
    }

    private static void Loop()
    {
        while (true)
        {
            Thread.Sleep(3000);
            var c = RecompOne.Runtime.Runtime.Cpu;
            var m = RecompOne.Runtime.Runtime.Mem;
            if (c == null || m == null) continue;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[trace] vbl={RecompOne.Runtime.Interrupts.VBlankCount} ra={c.RA:X8} sp={c.SP:X8} v0={c.V0:X8} a0={c.A0:X8} chain:");
                var sp = c.SP & ~3u;
                var n = 0;
                for (var a = sp; a < 0x80200000u && a >= 0x80000000u && n < 14; a += 4)
                {
                    var w = m.ReadU32(a);
                    if (w < 0x800C0000u || w >= 0x801E48B8u || (w & 3) != 0) continue;
                    var call = m.ReadU32(w - 8);
                    var op = call >> 26;
                    if (op != 3 && !(op == 0 && (call & 0x3F) == 9)) continue;
                    sb.Append($" {w:X8}");
                    n++;
                }
                Console.WriteLine(sb.ToString());
                // JM2_PEEK=addr,addr,... (hex) also prints those 16-bit RAM values
                var peek = Environment.GetEnvironmentVariable("JM2_PEEK");
                if (!string.IsNullOrEmpty(peek))
                {
                    var pb = new System.Text.StringBuilder("[peek]");
                    foreach (var a in peek.Split(','))
                        pb.Append($" {a}={m.ReadU16(Convert.ToUInt32(a, 16)):X4}");
                    Console.WriteLine(pb.ToString());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[trace] {e.Message}");
            }
        }
    }
}
