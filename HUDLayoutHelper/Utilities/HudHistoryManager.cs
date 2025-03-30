using System;
using System.Collections.Generic;
using System.Linq;

namespace HUDLayoutHelper.Utilities {
    internal class HudElementAction {
        public HudElementData PreviousState { get; }
        public HudElementData NewState { get; }

        public bool Saved { get; private set; } = false;

        public HudElementAction(HudElementData previousState, HudElementData newState, bool saved = false) {
            PreviousState = previousState;
            NewState = newState;
            Saved = saved;
        }

        public void SaveAction() {
            Saved = true;
        }

        public void UnsaveAction() {
            Saved = false;
        }
    }

    public class HudHistoryManager {
        private Plugin _plugin { get; }

        // The history of undo actions for each element for each HUD Layout
        internal readonly List<List<HudElementAction>> undoHistory = [];

        // The history of redo actions for each element for each HUD Layout
        internal readonly List<List<HudElementAction>> redoHistory = [];

        internal int HudLayoutCount { get; } = 4;
        internal int MaxHistorySize { get; private set; }

        // How to handle the redo history when a new action is added
        [Serializable]
        public enum RedoStrategy {
            ClearOnAction,
            //ClearSameElementOnAction,
            InsertOnAction
        }
        internal static Dictionary<RedoStrategy, string> RedoStrategyDescriptions = new() {
            { RedoStrategy.ClearOnAction, "Clears the Redo History when any element is moved after an Undo" },
            //{ RedoStrategy.ClearSameElementOnAction, "Clears only the Redo History for the element that was moved after an Undo" },
            { RedoStrategy.InsertOnAction, "Inserts the new action into the History without clearing anything" }
        };

        internal RedoStrategy RedoActionStrategy { get; private set; } = RedoStrategy.InsertOnAction;

        internal void SetRedoStrategy(RedoStrategy strategy) {
            RedoActionStrategy = strategy;
            Plugin.Log.Debug($"Redo Strategy set to {strategy}");
        }

        public HudHistoryManager(Plugin plugin, int HistorySize, RedoStrategy RedoStrategy) {
            _plugin = plugin;
            MaxHistorySize = HistorySize;
            RedoActionStrategy = RedoStrategy;

            // Initialize the undo and redo history lists
            for (int i = 0; i < HudLayoutCount; i++) {
                undoHistory.Add([]);
                redoHistory.Add([]);
            }
        }

        public bool SetHistorySize(int size) {
            if (size < 1) return false;
            MaxHistorySize = size;
            Plugin.Log.Debug($"History size set to {size}");


            // Trim the undo history
            for (int i = 0; i < HudLayoutCount; i++) {
                if (undoHistory[i].Count > size) {
                    _plugin.Debug.Log(Plugin.Log.Warning, $"Removing {undoHistory[i].Count - size} elements from undo history on HUD Layout {i}");
                    undoHistory[i].RemoveRange(0, undoHistory[i].Count - size);
                }
            }

            // Trim the redo history
            for (int i = 0; i < HudLayoutCount; i++) {
                if (redoHistory[i].Count > size) {
                    _plugin.Debug.Log(Plugin.Log.Debug, $"Removing {redoHistory[i].Count - size} elements from redo history on HUD Layout {i}");
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
                //case RedoStrategy.ClearSameElementOnAction:
                //    redoHistory[hudLayoutIndex].RemoveAll(a => a.NewState.ResNodeDisplayName == action.NewState.ResNodeDisplayName);
                //    break;
                case RedoStrategy.InsertOnAction:
                    // Do nothing
                    break;
            }

            // Add the action to the history
            undoHistory[hudLayoutIndex].Add(action);

            // Trim the history if it exceeds the maximum size
            if (undoHistory[hudLayoutIndex].Count > MaxHistorySize) {
                undoHistory[hudLayoutIndex].RemoveRange(0, undoHistory[hudLayoutIndex].Count - MaxHistorySize);
            }
        }

        public void AddUndoAction(int hudLayoutIndex, HudElementData previousState, HudElementData newState) {
            AddUndoAction(hudLayoutIndex, new HudElementAction(previousState, newState));
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
            action = new HudElementAction(action.PreviousState, currentState, saved: action.Saved);
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
            action = new HudElementAction(currentState, action.NewState, saved: action.Saved);
            undoHistory[hudLayoutIndex].Add(action);
            return true;
        }

        public void MarkHistoryAsSaved(int hudLayoutIndex) {
            if (!HudLayoutExists(hudLayoutIndex)) return;
            foreach (HudElementAction action in undoHistory[hudLayoutIndex]) {
                action.SaveAction();
            }
            foreach (HudElementAction action in redoHistory[hudLayoutIndex]) {
                action.UnsaveAction();
            }
        }

        private void RewindHistory(int hudLayoutIndex) {
            if (!HudLayoutExists(hudLayoutIndex)) return;
            for (int i = redoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
                if (!redoHistory[hudLayoutIndex][i].Saved) break;
                PerformRedo(hudLayoutIndex, redoHistory[hudLayoutIndex][i].NewState);
            }
        }

        /// <summary>
        /// Rewinds the history and clears all unsaved actions from the redo and undo history.
        /// </summary>
        /// <param name="hudLayoutIndex">The index of the HUD layout.</param>
        public void RewindAndClearHistory(int hudLayoutIndex) {
            if (!HudLayoutExists(hudLayoutIndex)) return;
            RewindHistory(hudLayoutIndex);
            redoHistory[hudLayoutIndex].Clear();

            // Remove all unsaved actions from the undo history
            for (int i = undoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
                if (undoHistory[hudLayoutIndex][i].Saved) break;
                undoHistory[hudLayoutIndex].RemoveAt(i);
            }
        }

        /// <summary>
        /// Rewinds the history and moves all unsaved actions to the redo history.
        /// </summary>
        /// <param name="hudLayoutIndex">The index of the HUD layout.</param>
        public void RewindHistoryAndAddToRedo(int hudLayoutIndex) {
            // Different to RewindAndClearHistory, this makes sure all unsaved actions are moved to the redo history
            if (!HudLayoutExists(hudLayoutIndex)) return;
            RewindHistory(hudLayoutIndex);

            // Move all unsaved actions to the redo history
            for (int i = undoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
                if (undoHistory[hudLayoutIndex][i].Saved) break;
                redoHistory[hudLayoutIndex].Add(undoHistory[hudLayoutIndex][i]);
                undoHistory[hudLayoutIndex].RemoveAt(i);
            }
        }

        private bool HudLayoutExists(int hudLayoutIndex) {
            if (hudLayoutIndex < 0 || hudLayoutIndex >= HudLayoutCount) {
                Plugin.Log.Warning($"Invalid HUD Layout index: {hudLayoutIndex}");
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
