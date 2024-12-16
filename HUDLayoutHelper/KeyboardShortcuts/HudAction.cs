using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HUDLayoutHelper.KeyboardShortcuts;
internal class HudAction {
    internal required string Name { get; set; }
    internal required string Description { get; set; }
    
    internal required Keybind? PrimaryKeybind { 
        get { return Keybinds[0]; }
        set { SetKeybind(value, false); }
    }
    internal Keybind? AlternateKeybind {
        get { return Keybinds[1]; }
        set { SetKeybind(value, true); }
    }
    internal List<Keybind?> Keybinds { get; } = [null, null];

    private void SetKeybind(Keybind? keybind, bool alt = false) {
        if (keybind == null) return;
        int index = alt ? 1 : 0;
        Keybinds[index] = KeybindHandler.Register(this, keybind);
        if (Keybinds[index] == null) {
            Keybinds.All(k => KeybindHandler.Unregister(k));
            throw new Exception($"Failed to register Keybind {keybind}");
        }
    }

    internal HudAction() { 
        if (PrimaryKeybind == null && AlternateKeybind == null) {
            throw new Exception("HudAction must have at least one keybind");
        }
    }

    // TODO
    internal required Action Callback { get; set; }


    internal bool ChangeKeybind(Keybind keybind, bool alt = false) {
        var result = KeybindHandler.Register(this, keybind);
        if (result == null) {
            return false;
        }

        int index = alt ? 1 : 0;
        if (Keybinds[index] != null) {
            KeybindHandler.Unregister(Keybinds[index]);
        }
        Keybinds[index] = result;
        return true;
    }


    internal void SetCallback(Action callback) {
        Callback = callback;
    }


}
