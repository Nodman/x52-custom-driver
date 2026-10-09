using System.Collections.Generic;
using System.Linq;

namespace X52.CustomDriver.Core.Models
{
    /// <summary>
    /// One place for button names. The ID is what profiles store (unchanged from older versions);
    /// the label is what the user sees everywhere (LIVE tab, mappings table).
    /// </summary>
    public static class ButtonCatalog
    {
        public record ButtonInfo(string Id, string Label);

        /// <summary>Buttons sent to vJoy, in vJoy button order (1..31 within a mode bank).</summary>
        public static readonly IReadOnlyList<ButtonInfo> VJoyButtons = new List<ButtonInfo>
        {
            new("Trigger", "Trigger"),
            new("ButtonFire", "Fire"),
            new("ButtonA", "A"),
            new("ButtonB", "B"),
            new("ButtonC", "C"),
            new("Pinkie", "Pinky"),
            new("ButtonD", "D"),
            new("ButtonE", "E"),
            new("T1", "T1"),
            new("T2", "T2"),
            new("T3", "T3"),
            new("T4", "T4"),
            new("T5", "T5"),
            new("T6", "T6"),
            new("TriggerStage2", "Trigger full"),
            new("Hat1Up", "Hat 1 Up"),
            new("Hat1Right", "Hat 1 Right"),
            new("Hat1Down", "Hat 1 Down"),
            new("Hat1Left", "Hat 1 Left"),
            new("HatRearUp", "Index hat Back"),
            new("HatRearRight", "Index hat Right"),
            new("HatRearDown", "Index hat Down"),
            new("HatRearLeft", "Index hat Left"),
            new("ClutchButton", "i"),
            new("MfdFunction", "Function"),
            new("MfdStartStop", "Start / Stop"),
            new("MfdReset", "Reset"),
            new("MouseLeftClick", "Mouse button"),
            new("MouseWheelClick", "Wheel press"),
            new("MouseWheelDown", "Wheel down"),
            new("MouseWheelUp", "Wheel up"),
        };

        /// <summary>Extra inputs that can trigger key mappings but are not vJoy buttons.</summary>
        public static readonly IReadOnlyList<ButtonInfo> ExtraInputs = new List<ButtonInfo>
        {
            new("Hat2Up", "POV hat Up"),
            new("Hat2Right", "POV hat Right"),
            new("Hat2Down", "POV hat Down"),
            new("Hat2Left", "POV hat Left"),
            new("Rotary1Min", "Rotary 1 min"),
            new("Rotary1Max", "Rotary 1 max"),
            new("Rotary2Min", "Rotary 2 min"),
            new("Rotary2Max", "Rotary 2 max"),
            new("SliderMin", "Slider min"),
            new("SliderMax", "Slider max"),
        };

        /// <summary>Everything a key mapping can use, in display order.</summary>
        public static readonly IReadOnlyList<ButtonInfo> Mappable = VJoyButtons.Concat(ExtraInputs).ToList();

        private static readonly Dictionary<string, string> LabelById =
            Mappable.ToDictionary(b => b.Id, b => b.Label);

        /// <summary>Friendly label for a stored button ID (falls back to the ID itself).</summary>
        public static string Label(string? id) =>
            id != null && LabelById.TryGetValue(id, out var label) ? label : (id ?? "");
    }
}
