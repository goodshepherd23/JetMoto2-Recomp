using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Sdk;

namespace JetMoto2;

public static class GpuPatches
{
    // Outside these the game never renders: menus use one 640x480 buffer at
    // (0,0), races two 320x240 buffers side by side.
    private const int FrameBufferRight = 640;
    private const int FrameBufferBottom = 480;

    // The menus build their glowing cursors by uploading a palette, reading it
    // straight back with StoreImage, fading it with the GTE and uploading the
    // results. RecompOne's GL renderer queues uploads into the frame being
    // recorded, but its readback only flushes frames already presented, so a
    // same-frame read returns stale VRAM (and copies it over the CPU-side VRAM
    // too). The race-type cursor came out as green speckles and the track-dial
    // pointer as a grey square. Palettes and textures live outside the frame
    // buffers, where only uploads, fills and copies write, and all of those keep
    // the CPU-side VRAM current, so read those areas from it directly.
    public static void StoreImage(CpuContext c, IMemory m)
    {
        var gpu = RecompOne.Runtime.Runtime.Gpu;
        int x = (short)m.ReadU16(c.A0), y = (short)m.ReadU16(c.A0 + 2);
        int w = (short)m.ReadU16(c.A0 + 4), h = (short)m.ReadU16(c.A0 + 6);
        if (gpu == null || w <= 0 || h <= 0 || (x < FrameBufferRight && y < FrameBufferBottom))
        {
            LibGpu.StoreImage(c, m);
            return;
        }

        var vram = gpu.Vram;
        var dst = c.A1;
        var n = w * h;
        for (var i = 0; i < n; i += 2)
        {
            uint lo = Pixel(vram, x + i % w, y + i / w);
            uint hi = i + 1 < n ? Pixel(vram, x + (i + 1) % w, y + (i + 1) / w) : 0u;
            m.WriteU32(dst + (uint)(i * 2), lo | (hi << 16));
        }

        c.V0 = 0u;
    }

    private static ushort Pixel(ushort[] vram, int x, int y)
    {
        return vram[(y & (RecompOne.Runtime.Gpu.VramHeight - 1)) * RecompOne.Runtime.Gpu.VramWidth +
                    (x & (RecompOne.Runtime.Gpu.VramWidth - 1))];
    }
}
