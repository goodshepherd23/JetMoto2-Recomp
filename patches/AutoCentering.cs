using ImGuiNET;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;

namespace JetMoto2;

// Makes the game's own "Auto Centering: Off" option actually turn it off.
//
// The steering code (func_80153858) levels the bike by firing its corrective
// thrusters whenever either of two angle errors (rider+0xA8, rider+0xAC)
// passes a threshold kept at GP+0x4C8. The Options setting, copied to
// GP+0x4A0 at race start, only gates the first of the two corrections; the
// second one (thruster masks 0x2/0x8) runs whether the option is on or off.
// Players have complained for years that switching it off "doesn't actually
// turn it off". While the option is off, this raises the threshold for the
// duration of that function so the ungated correction can't fire either. The
// threshold is only read inside func_80153858.
public static class AutoCentering
{
    private const uint FlagOffset = 0x4A0u;
    private const uint ThresholdOffset = 0x4C8u;
    private const string KeyFix = "jm2.autocenter_fix";

    private static bool _enabled = true;
    private static bool _active;
    private static uint _saved;
    private static long _corrections;

    public static long Corrections => Interlocked.Read(ref _corrections);

    public static void Register()
    {
        RecompOne.Runtime.Runtime.Defaults(v => v.Default(KeyFix, true));
        RecompOne.Runtime.Events.Event.AddListener<RecompOne.Runtime.Events.RuntimeReadyEvent>(_ =>
            _enabled = RecompOne.Runtime.Runtime.View.GetBool(KeyFix, true));
        SettingsRegistry.Extend("display", Draw);
    }

    // pre-hook on func_80153858
    public static void BeforeSteering(CpuContext c, IMemory m)
    {
        _active = _enabled && m.ReadU32(c.GP + FlagOffset) == 0u;
        if (!_active) return;
        _saved = m.ReadU32(c.GP + ThresholdOffset);
        m.WriteU32(c.GP + ThresholdOffset, 0x7FFFFFFFu);
    }

    // post-hook on func_80153858
    public static void AfterSteering(CpuContext c, IMemory m)
    {
        if (!_active) return;
        m.WriteU32(c.GP + ThresholdOffset, _saved);
        _active = false;
    }

    // pre-hook on the thruster helpers, for JM2_TRACE: counts calls from the
    // six call sites of the ungated correction
    public static void CountCorrection(CpuContext c, IMemory m)
    {
        if (c.RA is 0x801553A4u or 0x801553D4u or 0x801553E4u or 0x801554C8u or 0x801554F8u or 0x80155508u)
        {
            Interlocked.Increment(ref _corrections);
            if (Environment.GetEnvironmentVariable("JM2_TRACE") == "1")
                Console.WriteLine($"[center] ra={c.RA:X8} flag={m.ReadU32(c.GP + FlagOffset)} thr={m.ReadU32(c.GP + ThresholdOffset):X} active={_active} gp={c.GP:X8} rider={c.A0:X8} frame={RecompOne.Runtime.Interrupts.VBlankCount}");
        }
    }

    private static void Draw()
    {
        ImGui.Spacing();
        var fix = _enabled;
        if (ImGui.Checkbox("Auto Centering Off means fully off", ref fix))
        {
            _enabled = fix;
            RecompOne.Runtime.Runtime.View.SetBool(KeyFix, fix);
            RecompOne.Runtime.Runtime.SaveView();
        }

        ImGui.TextDisabled("Only matters when Auto Centering is OFF in the game's Options.");
        ImGui.TextDisabled("The original game kept leveling one axis even then.");
    }
}
