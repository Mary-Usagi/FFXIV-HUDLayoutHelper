using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HUDLayoutHelper.KeyboardShortcuts;
internal enum KeybindAction { None, Copy, Paste, Undo, Redo, ToggleAlignmentOverlay }
internal class HudActions {
    //private List<HudAction> _allActions { get; } = new();

    //internal HudActions() {
    //    _allActions = new List<HudAction> {
    //        Copy,
    //        Paste,
    //        Undo,
    //        Redo,
    //        ToggleAlignmentOverlay
    //    };
    //}
    
    //internal List<HudAction> GetAll() {
    //    return _allActions;
    //}

    internal static HudAction Copy = new HudAction {
        Name = "Copy",
        Description = "Copy position of selected HUD element",
        PrimaryKeybind = new Keybind {
            MainKey = SeVirtualKey.C,
            KeyPressState = KeyStateFlags.Pressed,
            ModifierKeys = [SeVirtualKey.CONTROL]
        },
        Callback = new Action(() => { })
    };

    internal static HudAction Paste = new HudAction {
        Name = "Paste",
        Description = "Paste copied position to selected HUD element",
        PrimaryKeybind = new Keybind {
            MainKey = SeVirtualKey.V,
            KeyPressState = KeyStateFlags.Released,
            ModifierKeys = [SeVirtualKey.CONTROL]
        },
        Callback = new Action(() => { })
    };

    internal static HudAction Undo = new HudAction {
        Name = "Undo",
        Description = "Undo last action",
        PrimaryKeybind = new Keybind {
            MainKey = SeVirtualKey.Z,
            KeyPressState = KeyStateFlags.Pressed,
            ModifierKeys = [SeVirtualKey.CONTROL]
        },
        Callback = new Action(() => { })
    };

    internal static HudAction Redo = new HudAction {
        Name = "Redo",
        Description = "Redo last action",
        PrimaryKeybind = new Keybind {
            MainKey = SeVirtualKey.Y,
            KeyPressState = KeyStateFlags.Pressed,
            ModifierKeys = [SeVirtualKey.CONTROL]
        },
        AlternateKeybind = new Keybind {
            MainKey = SeVirtualKey.Z,
            KeyPressState = KeyStateFlags.Pressed,
            ModifierKeys = new List<SeVirtualKey>() {
                SeVirtualKey.CONTROL,
                SeVirtualKey.SHIFT,
            }
        },
        Callback = new Action(() => { })
    };

    internal static HudAction ToggleAlignmentOverlay = new HudAction {
        Name = "Toggle Alignment Helper Overlay",
        Description = "Toggle alignment helper overlay on/off",
        PrimaryKeybind = new Keybind {
            MainKey = SeVirtualKey.R,
            KeyPressState = KeyStateFlags.Pressed,
            ModifierKeys = [SeVirtualKey.CONTROL]
        },
        Callback = new Action(() => { })
    };
}
