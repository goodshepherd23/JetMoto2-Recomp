using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Sdk;

namespace JetMoto2;

// Lets RecompOne's frame interpolation work in races.
//
// The race loop (func_80169074) caps itself at 30 fps by calling VSync(0)
// over and over until the root counter says enough time has passed:
//     DrawSync(0); do { VSync(0); } while (fps_since_last_frame > cap);
// Each VSync call publishes a frame to RecompOne's presenter, so every other
// published frame was empty and the presenter took the source rate to be 60.
// It never had two real frames to blend, and "60 fps" or "120 fps" still
// looked like 30. Here the first VSync of each wait asks for all the vblanks
// the cap needs at once (VSync(2) for 30 fps), which is the same wait but one
// published frame, so RecompOne sees a 30 fps source and fills in the rest.
public static class FramePacing
{
    private const uint WaitCallSite = 0x80169490u; // RA of VSync(0) in the wait loop
    private const uint DrawSyncCallSite = 0x80169488u; // RA of the DrawSync(0) just before it
    private const uint CapOffset = 0x6ECu; // GP+0x6EC: frame-rate cap (30)
    private const uint RefreshRate = 0x801EE82Cu; // 60, what the cap divides into

    private static bool _waited;

    // replaces VSync
    public static void VSync(CpuContext c, IMemory m)
    {
        if (c.RA == WaitCallSite && (int)c.A0 == 0 && !_waited)
        {
            _waited = true;
            var cap = m.ReadU32(c.GP + CapOffset);
            var refresh = m.ReadU32(RefreshRate);
            if (cap > 0 && refresh > cap) c.A0 = Math.Clamp((refresh + cap / 2) / cap, 1u, 4u);
        }
        LibEtc.VSync(c, m);
    }

    // pre-hook on DrawSync: a new frame's wait is about to start
    public static void BeforeDrawSync(CpuContext c, IMemory m)
    {
        if (c.RA == DrawSyncCallSite) _waited = false;
    }
}
