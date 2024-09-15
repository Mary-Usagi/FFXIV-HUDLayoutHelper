using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HudCopyPaste {
    internal class HudElementAction {
        public HudElementData PreviousState { get; }
        public HudElementData NewState { get; }

        //public HudElementAction(HudElementData previousState, HudElementData newState) {
        //    PreviousState = previousState;
        //    NewState = newState;
        //    Timestamp = Environment.TickCount;
        //}

        public HudElementAction(HudElementData previousState, HudElementData newState) {
            PreviousState = previousState;
            NewState = newState;
        }
    }

    public class HudHistoryManager {
        private Plugin Plugin { get; }

        // TODO: Or add a history per element? 
        // The history of undo actions for each element for each HUD Layout
        internal readonly List<List<HudElementAction>> undoHistory = new();

        // The history of redo actions for each element for each HUD Layout
        internal readonly List<List<HudElementAction>> redoHistory = new();

        internal int HudLayoutCount { get; } = 4;
        internal int MaxHistorySize { get; private set; } = 50;

        // How to handle the redo history when a new action is added
        [Serializable]
        public enum RedoStrategy {
            ClearOnAction,
            ClearSameElementOnAction,
            InsertOnAction
        }
        internal static Dictionary<RedoStrategy, string> RedoStrategyDescriptions = new() {
            { RedoStrategy.ClearOnAction, "Clears the Redo History when any element is moved after an Undo" },
            { RedoStrategy.ClearSameElementOnAction, "Clears only the Redo History for the element that was moved after an Undo" },
            { RedoStrategy.InsertOnAction, "Inserts the new action into the History without clearing anything" }
        };

        internal RedoStrategy RedoActionStrategy { get; private set; } = RedoStrategy.InsertOnAction;

        internal void SetRedoStrategy(RedoStrategy strategy) {
            RedoActionStrategy = strategy;
            Plugin.Log.Debug($"Redo Strategy set to {strategy}");
        }

        public HudHistoryManager(Plugin plugin, int HistorySize, RedoStrategy RedoStrategy) {
            Plugin = plugin;
            this.MaxHistorySize = HistorySize;
            this.RedoActionStrategy = RedoStrategy;

            // Initialize the undo and redo history lists
            for (int i = 0; i < HudLayoutCount; i++) {
                undoHistory.Add(new List<HudElementAction>());
                redoHistory.Add(new List<HudElementAction>());
            }
        }

        public bool SetHistorySize(int size) {
            if (size < 1) return false;
            this.MaxHistorySize = size;
            Plugin.Log.Debug($"History size set to {size}");


            // Trim the undo history
            for (int i = 0; i < HudLayoutCount; i++) {
                if (undoHistory[i].Count > size) {
                    Plugin.Debug.Log(Plugin.Log.Warning, $"Removing {undoHistory[i].Count - size} elements from undo history on HUD Layout {i}");
                    undoHistory[i].RemoveRange(0, undoHistory[i].Count - size);
                }
            }

            // Trim the redo history
            for (int i = 0; i < HudLayoutCount; i++) {
                if (redoHistory[i].Count > size) {
                    Plugin.Debug.Log(Plugin.Log.Debug, $"Removing {redoHistory[i].Count - size} elements from redo history on HUD Layout {i}");
                    redoHistory[i].RemoveRange(0, redoHistory[i].Count - size);
                }
            }

            return true;
        }

        private void AddUndoAction(int hudLayoutIndex, HudElementAction action) {
            if (action == null) {
                throw new ArgumentNullException(nameof(action));
            }
            if (!HudLayoutExists(hudLayoutIndex)) return;

            switch (RedoActionStrategy) {
                case RedoStrategy.ClearOnAction:
                    redoHistory[hudLayoutIndex].Clear();
                    break;
                case RedoStrategy.ClearSameElementOnAction:
                    redoHistory[hudLayoutIndex].RemoveAll(a => a.NewState.ResNodeDisplayName == action.NewState.ResNodeDisplayName);
                    break;
                case RedoStrategy.InsertOnAction:
                    // Do nothing
                    break;
            }

            // Add the action to the history
            undoHistory[hudLayoutIndex].Add(action);

            // Trim the history if it exceeds the maximum size
            if (undoHistory[hudLayoutIndex].Count > this.MaxHistorySize) {
                undoHistory[hudLayoutIndex].RemoveRange(0, undoHistory[hudLayoutIndex].Count - this.MaxHistorySize);
            }
        }

        public void AddUndoAction(int hudLayoutIndex, HudElementData previousState, HudElementData newState) {
            AddUndoAction(hudLayoutIndex, new HudElementAction(previousState, newState));
        }

        public bool UpdateUndoAction(int hudLayoutIndex, HudElementData newState) {
            if (!HudLayoutExists(hudLayoutIndex)) return false;
            if (HistoryEmpty(hudLayoutIndex, undoHistory)) return false;

            HudElementAction action = undoHistory[hudLayoutIndex].Last();
            if (action == null) return false;

            // Update the current state
            action = new HudElementAction(action.PreviousState, newState);
            undoHistory[hudLayoutIndex][undoHistory[hudLayoutIndex].Count - 1] = action;
            return true;
        }


        public (HudElementData?, HudElementData?) PeekUndoAction(int hudLayoutIndex) {
            if (!HudLayoutExists(hudLayoutIndex)) return (null, null);
            if (HistoryEmpty(hudLayoutIndex, undoHistory)) return (null, null);

            HudElementAction action = undoHistory[hudLayoutIndex].Last();
            if (action == null) return (null, null);

            HudElementData previousState = action.PreviousState;
            HudElementData newState = action.NewState;
            return (previousState, newState);
        }

        public bool PerformUndo(int hudLayoutIndex, HudElementData currentState) {
            HudElementAction action = undoHistory[hudLayoutIndex].Last();
            if (action == null) return false;

            // Apply the undo action
            undoHistory[hudLayoutIndex].RemoveAt(undoHistory[hudLayoutIndex].Count - 1);

            // Update the current state 
            currentState.Timestamp = action.NewState.Timestamp;
            action = new HudElementAction(action.PreviousState, currentState);
            redoHistory[hudLayoutIndex].Add(action);
            return true;
        }

        public (HudElementData?, HudElementData?) PeekRedoAction(int hudLayoutIndex) {
            if (!HudLayoutExists(hudLayoutIndex)) return (null, null);
            if (HistoryEmpty(hudLayoutIndex, redoHistory)) return (null, null);

            HudElementAction action = redoHistory[hudLayoutIndex].Last();
            if (action == null) return (null, null);

            HudElementData previousState = action.PreviousState;
            HudElementData newState = action.NewState;
            return (previousState, newState);
        }

        public bool PerformRedo(int hudLayoutIndex, HudElementData currentState) {
            HudElementAction action = redoHistory[hudLayoutIndex].Last();
            if (action == null) return false;

            // Apply the redo action
            redoHistory[hudLayoutIndex].RemoveAt(redoHistory[hudLayoutIndex].Count - 1);

            // Update the current state
            // TODO: don't create new one, just update old one (?)
            currentState.Timestamp = action.PreviousState.Timestamp;
            action = new HudElementAction(currentState, action.NewState);
            undoHistory[hudLayoutIndex].Add(action);
            return true;
        }

        private bool HudLayoutExists(int hudLayoutIndex) {
            if (hudLayoutIndex < 0 || hudLayoutIndex >= HudLayoutCount) {
                Plugin.Log.Warning("Invalid HUD Layout index.");
                throw new ArgumentOutOfRangeException(nameof(hudLayoutIndex));
            }
            return true;
        }

        private bool HistoryEmpty(int hudLayoutIndex, List<List<HudElementAction>> history) {
            if (history[hudLayoutIndex].Count == 0) return true;
            return false;
        }
    }
}
