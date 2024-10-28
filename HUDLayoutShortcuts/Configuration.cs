using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HUDLayoutShortcuts {
    [Serializable]
    public class Configuration : IPluginConfiguration {
        public int Version { get; set; } = 0;

        public bool IsConfigWindowMovable { get; set; } = true;
        public int MaxUndoHistorySize { get; set; } = 50;

        public HudHistoryManager.RedoStrategy RedoActionStrategy { get; set; } = HudHistoryManager.RedoStrategy.InsertOnAction;


        // the below exist just to make saving less cumbersome
        public void Save() {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
    }
}
