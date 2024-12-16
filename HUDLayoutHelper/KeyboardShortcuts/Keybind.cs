using System.Collections.Generic;
using System;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HUDLayoutHelper.KeyboardShortcuts;

internal class Keybind {
    public required SeVirtualKey MainKey { get; set; }
    internal List<SeVirtualKey> ModifierKeys { get; set; } = [];
    internal required KeyStateFlags KeyPressState { get; set; }

    public override string ToString() {
        if (ModifierKeys.Count == 0) {
            return MainKey.ToString();
        } else {
            return $"{string.Join(" + ", ModifierKeys)} + {MainKey}";
        }
    }

    internal unsafe bool IsPressed() {
        foreach (var modKey in ModifierKeys) {
            if (!UIInputData.Instance()->IsKeyDown(modKey)) {
                return false;
            }
        }
        return UIInputData.Instance()->GetKeyState(MainKey).HasFlag(KeyPressState);
    }

    internal Keybind() { 
        ModifierKeys.Sort();
    }

}
