using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HUDLayoutHelper;


internal class Keybind {
    public string Name { get; set; }
    public string Text { get; set; }

    internal struct Combo {
        public SeVirtualKey MainKey { get; set; }
        public bool ShiftUsed { get; set; }
        public KeyStateFlags State { get; set; }

        public Combo(SeVirtualKey mainKey, KeyStateFlags state = KeyStateFlags.Pressed, bool shiftUsed = false) {
            MainKey = mainKey;
            ShiftUsed = shiftUsed;
            State = state;
        }

        public override string ToString() {
            string extraModifier = ShiftUsed ? $" + Shift" : "";
            return $"Ctrl{extraModifier} + {MainKey}";
        }
    }

    public List<Combo> Combos { get; set; } = new List<Combo>();

    public Keybind(string name, string text, List<Combo> combos) { 
        this.Name = name;
        this.Text = text;
        this.Combos = combos;
    }
}
