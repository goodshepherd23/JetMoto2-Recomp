using RecompOne.Runtime.Context;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Sdk;

namespace JetMoto2;

public static class CdPatches
{
    // Jet Moto 2's file loader (func_8011E5BC) treats a read as good only when
    // CdReadSync hands back a drive status of 0x22 (motor on + reading), which
    // is what the retail libcd leaves in the result buffer when the last sector
    // arrives. RecompOne's CdRead finishes instantly and leaves the status of
    // the previous command there, so the loader retried forever on a black
    // screen. Report the status the game expects for a completed read.
    public static void CdReadSync(CpuContext c, IMemory m)
    {
        var result = c.A1;
        LibCd.CdReadSync(c, m);
        if (c.V0 == 0u && result != 0u) m.WriteU8(result, 0x22);
    }
}
