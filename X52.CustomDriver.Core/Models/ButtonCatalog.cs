using System.Collections.Generic;
using System.Linq;

namespace X52.CustomDriver.Core.Models
{
    /// <summary>
    /// One place for button names. Uses the original repository's names (also what profiles store),
    /// shown the same way on the LIVE tab and in the mappings table.
    /// </summary>
    public static class ButtonCatalog
    {
        public record ButtonInfo(string Id, string Label);

        /// <summary>Buttons sent to vJoy, in vJoy button order (1..31 within a mode bank).</summary>
        public static readonly IReadOnlyList<ButtonInfo> VJoyButtons = new List<ButtonInfo>
        {
            new("Trigger", "Trigger"),
            new("ButtonFire", "ButtonFire"),
            new("ButtonA", "ButtonA"),
            new("ButtonB", "ButtonB"),
            new("ButtonC", "ButtonC"),
            new("Pinkie", "Pinkie"),
            new("ButtonD", "ButtonD"),
            new("ButtonE", "ButtonE"),
            new("T1", "T1"),
            new("T2", "T2"),
            new("T3", "T3"),
            new("T4", "T4"),
            new("T5", "T5"),
            new("T6", "T6"),
            new("TriggerStage2", "TriggerStage2"),
            new("Hat1Up", "Hat1Up"),
            new("Hat1Right", "Hat1Right"),
            new("Hat1Down", "Hat1Down"),
            new("Hat1Left", "Hat1Left"),
            new("HatRearUp", "HatRearUp"),
            new("HatRearRight", "HatRearRight"),
            new("HatRearDown", "HatRearDown"),
            new("HatRearLeft", "HatRearLeft"),
            new("ClutchButton", "ClutchButton"),
            new("MfdFunction", "MfdFunction"),
            new("MfdStartStop", "MfdStartStop"),
            new("MfdReset", "MfdReset"),
            new("MouseLeftClick", "MouseLeftClick"),
            new("MouseWheelClick", "MouseWheelClick"),
            new("MouseWheelDown", "MouseWheelDown"),
            new("MouseWheelUp", "MouseWheelUp"),
        };

        /// <summary>Extra inputs that can trigger key mappings but are not vJoy buttons.</summary>
        public static readonly IReadOnlyList<ButtonInfo> ExtraInputs = new List<ButtonInfo>
        {
            new("Hat2Up", "Hat2Up"),
            new("Hat2Right", "Hat2Right"),
            new("Hat2Down", "Hat2Down"),
            new("Hat2Left", "Hat2Left"),
            new("Rotary1Min", "Rotary1Min"),
            new("Rotary1Max", "Rotary1Max"),
            new("Rotary2Min", "Rotary2Min"),
            new("Rotary2Max", "Rotary2Max"),
            new("SliderMin", "SliderMin"),
            new("SliderMax", "SliderMax"),
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
