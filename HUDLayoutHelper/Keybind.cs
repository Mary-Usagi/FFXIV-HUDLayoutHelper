using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HUDLayoutHelper;


internal class Keybind {
    internal struct Description {
        public string Name { get; set; }
        public string Text { get; set; }
        public string ShortText { get; set; }

        public Description(string name, string description, string shortDescription) {
            Name = name;
            Text = description;
            ShortText = shortDescription;
        }
    }
    internal struct Keys {
        public SeVirtualKey MainKey { get; set; }
        public bool ShiftPressed { get; set; }
        public KeyStateFlags State { get; set; }

        public Keys(SeVirtualKey mainKey, KeyStateFlags state, bool shiftPressed) {
            MainKey = mainKey;
            ShiftPressed = shiftPressed;
            State = state;
        }

        public override string ToString() {
            string extraModifier = ShiftPressed ? $" + Shift" : "";
            return $"Ctrl{extraModifier} + {MainKey}";
        }
    }

    public Description description { get; set; }
    public Keys keys { get; set; }
    public KeybindAction KeybindAction { get; set; }

    public Keybind((string name, string text, string shortText) description, (SeVirtualKey mainKey, KeyStateFlags state, bool shiftPressed) keys, KeybindAction action) {
        this.description = new Description(description.name, description.text, description.shortText);
        this.keys = new Keys(keys.mainKey, keys.state, keys.shiftPressed);
        this.KeybindAction = action;
    }
}
