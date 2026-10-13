using System.Text.Json;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;

namespace JetMoto2;

// The Cheats menu in the top bar. Each cheat is a RAM write repeated every
// frame, the same thing a GameShark does.
//
// The published GameShark codes for SCUS-94167 are for the 1997 Jet Moto 2 and
// do not fit this disc: Championship Edition is a different build under the
// same serial, with its rider records 0xF6C bytes apart instead of 0xF68, and
// those old addresses land on program code here. The addresses below were
// found in this build by comparing RAM across turbo presses during a race.
public static class Cheats
{
    // Rider records, one per character (Li'l Dave first), 0xF6C bytes each.
    private const uint Turbo0 = 0x8016F8B8u;      // turbo meter, 0x4000 = full (4 boosts)
    private const uint RiderStride = 0xF6Cu;
    private const int RiderCount = 11;
    private const uint GrappleOffset = 0x32Eu;    // grapple charges, 6 = full
    private const uint PadOffset = unchecked((uint)-0xA0); // controller index, -1 for CPU riders

    private sealed class State
    {
        public bool InfiniteTurbo { get; set; }
        public bool InfiniteGrapple { get; set; }
        public bool CpuRidersToo { get; set; }
    }

    private static State _state = new();
    private static string _path = "";

    public static void Register()
    {
        _path = Path.Combine(AppContext.BaseDirectory, "cheats.json");
        try
        {
            if (File.Exists(_path)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State();
        }
        catch
        {
            _state = new State();
        }

        // JM2_CHEATS=turbo,grapple,cpu forces cheats on for a test run
        foreach (var name in (Environment.GetEnvironmentVariable("JM2_CHEATS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            switch (name.Trim().ToLowerInvariant())
            {
                case "turbo": _state.InfiniteTurbo = true; break;
                case "grapple": _state.InfiniteGrapple = true; break;
                case "cpu": _state.CpuRidersToo = true; break;
            }

        RecompOne.Runtime.Runtime.AddLanguages("""
            {"strings":{
              "Cheats":{"en":"Cheats"},
              "Infinite Turbo":{"en":"Infinite Turbo"},
              "Infinite Grapple":{"en":"Infinite Grapple"},
              "Apply to CPU riders too":{"en":"Apply to CPU riders too"}}}
            """);

        MenuRegistry.Menu("Cheats")
            .Check("Infinite Turbo", () => _state.InfiniteTurbo, v => Set(() => _state.InfiniteTurbo = v))
            .Check("Infinite Grapple", () => _state.InfiniteGrapple, v => Set(() => _state.InfiniteGrapple = v))
            .Separator()
            .Check("Apply to CPU riders too", () => _state.CpuRidersToo, v => Set(() => _state.CpuRidersToo = v));

        Event.AddListener<VSyncEvent>(e => Apply(e.Memory));
    }

    private static void Set(Action change)
    {
        change();
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // a read-only folder only costs the setting its persistence
        }
    }

    private static void Apply(IMemory m)
    {
        if (!_state.InfiniteTurbo && !_state.InfiniteGrapple) return;
        for (uint k = 0; k < RiderCount; k++)
        {
            var turbo = Turbo0 + k * RiderStride;
            if (!_state.CpuRidersToo && m.ReadU32(turbo + PadOffset) == 0xFFFFFFFFu) continue;
            if (_state.InfiniteTurbo) m.WriteU16(turbo, 0x4000);
            if (_state.InfiniteGrapple) m.WriteU16(turbo + GrappleOffset, 6);
        }
    }
}
