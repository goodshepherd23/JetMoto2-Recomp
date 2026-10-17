using ImGuiNET;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;

namespace JetMoto2;

// Widescreen for races (Settings > Display > Widescreen).
//
// Two halves, like the MediEvil port: RecompOne's Display.WideAspect renders
// extra picture either side of the 4:3 frame, and the game's camera presets
// are changed so the 3D view actually fills it. Championship Edition keeps 13
// camera presets 0x60 bytes apart from 0x801981EC; each starts with a 4.12
// horizontal scale (+/-4096) followed by the left and right side-plane normals
// used for culling. The scale is reduced by 4:3 / target aspect (the 1997
// release's 16:9 GameShark code did the same: 0x1000 -> 0x0C00) and the side
// planes are rotated out to the wider half-angle so nothing pops in at the new
// edges. The 2D HUD stays where it was, inside the middle 4:3.
//
// The menus are pre-drawn 640x480 images with nothing beyond their edges, so
// wide rendering is switched on only while the game shows a 320-wide (race)
// display.
public static class Widescreen
{
    private const uint Presets = 0x801981ECu;
    private const uint PresetStride = 0x60u;
    private const int PresetCount = 13;
    private const uint LeftPlane = 0x08u;   // s16 nx, s16 nz
    private const uint RightPlane = 0x10u;

    private const string KeyAspect = "jm2.widescreen";

    private static readonly (string Label, float Value)[] Choices =
    [
        ("Off (4:3)", 0f),
        ("16:10", 16f / 10f),
        ("16:9", 16f / 9f),
        ("21:9", 21f / 9f)
    ];

    private static readonly (short Scale, short LX, short LZ, short RX, short RZ)?[] Original =
        new (short, short, short, short, short)?[PresetCount];

    private static float _aspect;
    private static bool _race;

    public static void Register()
    {
        Event.AddListener<RuntimeReadyEvent>(_ =>
        {
            _aspect = RecompOne.Runtime.Runtime.View.GetFloat(KeyAspect, 0f);
            Display.SourceAspect = 4f / 3f;
            Display.OutputAspect = 4f / 3f;
        });
        Event.AddListener<DispEnvEvent>(e => _race = e.W <= 368);
        Event.AddListener<VSyncEvent>(e => Apply(e.Memory));
        SettingsRegistry.Extend("display", Draw);
    }

    private static void Apply(IMemory m)
    {
        var wide = _aspect > 4f / 3f + 0.01f;
        Display.TargetAspect = wide ? _aspect : 4f / 3f;
        Display.WideAspect = wide && _race ? _aspect : 0f;

        var factor = wide ? (4f / 3f) / _aspect : 1f;
        for (var k = 0; k < PresetCount; k++)
        {
            var p = Presets + (uint)k * PresetStride;
            var scale = (short)m.ReadU16(p);
            if (Original[k] is not { } o)
            {
                // only presets that look untouched: scale +/-4096, matching side planes
                short lx = (short)m.ReadU16(p + LeftPlane), lz = (short)m.ReadU16(p + LeftPlane + 2);
                short rx = (short)m.ReadU16(p + RightPlane), rz = (short)m.ReadU16(p + RightPlane + 2);
                if (Math.Abs((int)scale) != 4096 || lx != -rx || lz != rz || lz <= 0) continue;
                o = (scale, lx, lz, rx, rz);
                Original[k] = o;
            }

            var newScale = (short)MathF.Round(o.Scale * factor);
            // plane normal (-cos t, sin t): widen the half-angle t so tan grows by 1/factor
            var t = MathF.Atan2(o.LZ, o.RX);
            var t2 = wide ? MathF.Atan(MathF.Tan(t) / factor) : t;
            var len = MathF.Sqrt((float)o.RX * o.RX + (float)o.RZ * o.RZ);
            var nx = (short)MathF.Round(MathF.Cos(t2) * len);
            var nz = (short)MathF.Round(MathF.Sin(t2) * len);
            if (!wide) (nx, nz) = (o.RX, o.RZ);

            m.WriteU16(p, (ushort)newScale);
            m.WriteU16(p + LeftPlane, (ushort)(short)-nx);
            m.WriteU16(p + LeftPlane + 2, (ushort)nz);
            m.WriteU16(p + RightPlane, (ushort)nx);
            m.WriteU16(p + RightPlane + 2, (ushort)nz);
        }
    }

    private static void Draw()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Widescreen (races)");
        var current = Array.FindIndex(Choices, c => MathF.Abs(c.Value - _aspect) < 0.01f);
        if (current < 0) current = 0;
        if (ImGui.BeginCombo("##jm2-widescreen", Choices[current].Label))
        {
            for (var i = 0; i < Choices.Length; i++)
                if (ImGui.Selectable(Choices[i].Label, i == current))
                {
                    _aspect = Choices[i].Value;
                    RecompOne.Runtime.Runtime.View.SetFloat(KeyAspect, _aspect);
                    RecompOne.Runtime.Runtime.SaveView();
                }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled("Menus stay 4:3. The HUD keeps its 4:3 position.");
    }
}
