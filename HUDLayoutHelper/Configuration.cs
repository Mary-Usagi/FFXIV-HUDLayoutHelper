using Dalamud.Configuration;
using HUDLayoutHelper.Utilities;
using System;

namespace HUDLayoutHelper; 
[Serializable]
public class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 0;
    public bool DebugTabOpen { get; set; } = false;
    public bool ShowShortcutHints { get; set; } = false;
    public int MaxUndoHistorySize { get; set; } = 100;

    public HudHistoryManager.RedoStrategy RedoActionStrategy { get; set; } = HudHistoryManager.RedoStrategy.InsertOnAction;

    // the below exist just to make saving less cumbersome
    public void Save() {
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    internal string GetHash() {
        return $"{DebugTabOpen}{ShowShortcutHints}{MaxUndoHistorySize}{RedoActionStrategy}";
    }
}
