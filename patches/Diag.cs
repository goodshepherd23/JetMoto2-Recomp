
namespace JetMoto2;

// Developer aid, off unless JM2_TRACE=1: every few seconds print where the
// guest is (return address plus the call chain recovered from the guest stack).
public static class Diag
{
    public static void Start()
    {
        Snapshots();
        PrimLog();
        if (Environment.GetEnvironmentVariable("JM2_TRACE") != "1") return;
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.DispEnvEvent>(_ => Interlocked.Increment(ref _flips));
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
    // <dir>/ram-<frame>.bin (and the 1024x512 VRAM to vram-<frame>.bin) at those vsync counts, for offline RAM searches.
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
            if (RecompOne.Runtime.Runtime.Gpu is { } gpu)
            {
                var vram = new byte[gpu.Vram.Length * 2];
                Buffer.BlockCopy(gpu.Vram, 0, vram, 0, vram.Length);
                File.WriteAllBytes(Path.Combine(dir, $"vram-{frame}.bin"), vram);
            }
            Console.WriteLine($"[snap] frame {frame}");
        });
    }

    // JM2_PRIMLOG=<frame>,<every>,<x0>,<y0>,<x1>,<y1>: from that vsync count on,
    // every <every> frames log the primitives of one frame that touch the box.
    private static void PrimLog()
    {
        var spec = Environment.GetEnvironmentVariable("JM2_PRIMLOG");
        if (string.IsNullOrEmpty(spec)) return;
        var v = spec.Split(',').Select(int.Parse).ToArray();
        long frame = 0;
        var on = false;
        var n = 0;
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.VSyncEvent>(_ =>
        {
            frame++;
            if (on) Console.WriteLine($"[prim] ---- end frame {frame - 1} ({n} prims in box)");
            on = frame >= v[0] && (frame - v[0]) % v[1] == 0;
            n = 0;
        });
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.RenderPrimEvent>(e =>
        {
            if (!on) return;
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            for (var i = 0; i < e.Count; i++)
            {
                minX = Math.Min(minX, e.X[i]); maxX = Math.Max(maxX, e.X[i]);
                minY = Math.Min(minY, e.Y[i]); maxY = Math.Max(maxY, e.Y[i]);
            }
            if (maxX < v[2] || minX > v[4] || maxY < v[3] || minY > v[5]) return;
            n++;
            var pts = string.Join(" ", Enumerable.Range(0, e.Count).Select(i => $"{e.X[i]},{e.Y[i]}"));
            Console.WriteLine($"[prim] n={e.Count} tex={(e.Textured ? 1 : 0)} semi={(e.SemiTransparent ? 1 : 0)} g={(e.Gouraud ? 1 : 0)} raw={(e.Raw ? 1 : 0)} clut={e.Clut:X4} area={e.DrawLeft},{e.DrawTop}-{e.DrawRight},{e.DrawBottom} pts={pts}");
        });
    }

    private static long _flips;

    private static void Loop()
    {
        var lastFlips = 0L;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            Thread.Sleep(3000);
            var c = RecompOne.Runtime.Runtime.Cpu;
            var m = RecompOne.Runtime.Runtime.Mem;
            if (c == null || m == null) continue;
            try
            {
                var sb = new System.Text.StringBuilder();
                var flips = Interlocked.Read(ref _flips);
                sb.Append($"[trace] game fps={(flips - lastFlips) / clock.Elapsed.TotalSeconds:0.0} interp={RecompOne.Runtime.Interp.Interp.Enabled}/{RecompOne.Runtime.Interp.Interp.EffectiveTarget} pgxp={RecompOne.Runtime.Pgxp.Pgxp.Enabled} centering={JetMoto2.AutoCentering.Corrections} ");
                lastFlips = flips;
                clock.Restart();
                sb.Append($"vbl={RecompOne.Runtime.Interrupts.VBlankCount} ra={c.RA:X8} sp={c.SP:X8} v0={c.V0:X8} a0={c.A0:X8} chain:");
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
