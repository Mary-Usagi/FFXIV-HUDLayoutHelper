using System;
using System.Collections.Generic;
using System.Linq;

namespace HUDLayoutHelper.Utilities;
internal class HudElementAction(HudElementData previousState, HudElementData newState, bool saved = false) {
    public HudElementData PreviousState { get; } = previousState;
    public HudElementData NewState { get; } = newState;

    public bool Saved { get; private set; } = saved;

    public void SaveAction() {
        this.Saved = true;
    }

    public void UnsaveAction() {
        this.Saved = false;
    }
}

public class HudHistoryManager {
    // The history of undo actions for each element for each HUD Layout
    internal List<List<HudElementAction>> UndoHistory { get; set; } = [];

    // The history of redo actions for each element for each HUD Layout
    internal List<List<HudElementAction>> RedoHistory { get; set; } = [];

    internal const int HudLayoutCount = 4;
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
        this.RedoActionStrategy = strategy;
        Plugin.Log.Debug($"Redo Strategy set to {strategy}");
    }

    public HudHistoryManager(int HistorySize, RedoStrategy RedoStrategy) {
        this.MaxHistorySize = HistorySize;
        this.RedoActionStrategy = RedoStrategy;

        // Initialize the undo and redo history lists
        for (int i = 0; i < HudLayoutCount; i++) {
            this.UndoHistory.Add([]);
            this.RedoHistory.Add([]);
        }
    }

    public bool SetHistorySize(int size) {
        if (size < 1) return false;
        this.MaxHistorySize = size;
        Plugin.Log.Debug($"History size set to {size}");


        // Trim the undo history
        for (int i = 0; i < HudLayoutCount; i++) {
            if (this.UndoHistory[i].Count > size) {
                Plugin.Debug.Log(Plugin.Log.Warning, $"Removing {this.UndoHistory[i].Count - size} elements from undo history on HUD Layout {i}");
                this.UndoHistory[i].RemoveRange(0, this.UndoHistory[i].Count - size);
            }
        }

        // Trim the redo history
        for (int i = 0; i < HudLayoutCount; i++) {
            if (this.RedoHistory[i].Count > size) {
                Plugin.Debug.Log(Plugin.Log.Debug, $"Removing {this.RedoHistory[i].Count - size} elements from redo history on HUD Layout {i}");
                this.RedoHistory[i].RemoveRange(0, this.RedoHistory[i].Count - size);
            }
        }

        return true;
    }

    private void AddUndoAction(int hudLayoutIndex, HudElementAction action) {
        ArgumentNullException.ThrowIfNull(action);
        if (!HudLayoutExists(hudLayoutIndex)) return;

        switch (this.RedoActionStrategy) {
            case RedoStrategy.ClearOnAction:
                this.RedoHistory[hudLayoutIndex].Clear();
                break;
            //case RedoStrategy.ClearSameElementOnAction:
            //    redoHistory[hudLayoutIndex].RemoveAll(a => a.NewState.ResNodeDisplayName == action.NewState.ResNodeDisplayName);
            //    break;
            case RedoStrategy.InsertOnAction:
                // Do nothing
                break;
        }

        // Add the action to the history
        this.UndoHistory[hudLayoutIndex].Add(action);

        // Trim the history if it exceeds the maximum size
        if (this.UndoHistory[hudLayoutIndex].Count > this.MaxHistorySize) {
            this.UndoHistory[hudLayoutIndex].RemoveRange(0, this.UndoHistory[hudLayoutIndex].Count - this.MaxHistorySize);
        }
    }

    public void AddUndoAction(int hudLayoutIndex, HudElementData previousState, HudElementData newState) {
        this.AddUndoAction(hudLayoutIndex, new HudElementAction(previousState, newState));
    }

    public (HudElementData?, HudElementData?) PeekUndoAction(int hudLayoutIndex) {
        if (!HudLayoutExists(hudLayoutIndex)) return (null, null);
        if (HistoryEmpty(hudLayoutIndex, this.UndoHistory)) return (null, null);

        HudElementAction action = this.UndoHistory[hudLayoutIndex].Last();
        if (action == null) return (null, null);

        HudElementData previousState = action.PreviousState;
        HudElementData newState = action.NewState;
        return (previousState, newState);
    }

    public bool PerformUndo(int hudLayoutIndex, HudElementData currentState) {
        HudElementAction action = this.UndoHistory[hudLayoutIndex].Last();
        if (action == null) return false;

        // Apply the undo action
        this.UndoHistory[hudLayoutIndex].RemoveAt(this.UndoHistory[hudLayoutIndex].Count - 1);

        // Update the current state 
        action = new HudElementAction(action.PreviousState, currentState, saved: action.Saved);
        this.RedoHistory[hudLayoutIndex].Add(action);
        return true;
    }

    public (HudElementData?, HudElementData?) PeekRedoAction(int hudLayoutIndex) {
        if (!HudLayoutExists(hudLayoutIndex)) return (null, null);
        if (HistoryEmpty(hudLayoutIndex, this.RedoHistory)) return (null, null);

        HudElementAction action = this.RedoHistory[hudLayoutIndex].Last();
        if (action == null) return (null, null);

        HudElementData previousState = action.PreviousState;
        HudElementData newState = action.NewState;
        return (previousState, newState);
    }

    public bool PerformRedo(int hudLayoutIndex, HudElementData currentState) {
        HudElementAction action = this.RedoHistory[hudLayoutIndex].Last();
        if (action == null) return false;

        // Apply the redo action
        this.RedoHistory[hudLayoutIndex].RemoveAt(this.RedoHistory[hudLayoutIndex].Count - 1);

        // Update the current state
        action = new HudElementAction(currentState, action.NewState, saved: action.Saved);
        this.UndoHistory[hudLayoutIndex].Add(action);
        return true;
    }

    public void MarkHistoryAsSaved(int hudLayoutIndex) {
        if (!HudLayoutExists(hudLayoutIndex)) return;
        foreach (HudElementAction action in this.UndoHistory[hudLayoutIndex]) {
            action.SaveAction();
        }
        foreach (HudElementAction action in this.RedoHistory[hudLayoutIndex]) {
            action.UnsaveAction();
        }
    }

    private void RewindHistory(int hudLayoutIndex) {
        if (!HudLayoutExists(hudLayoutIndex)) return;
        for (int i = this.RedoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
            if (!this.RedoHistory[hudLayoutIndex][i].Saved) break;
            this.PerformRedo(hudLayoutIndex, this.RedoHistory[hudLayoutIndex][i].NewState);
        }
    }

    /// <summary>
    /// Rewinds the history and clears all unsaved actions from the redo and undo history.
    /// </summary>
    /// <param name="hudLayoutIndex">The index of the HUD layout.</param>
    public void RewindAndClearHistory(int hudLayoutIndex) {
        if (!HudLayoutExists(hudLayoutIndex)) return;
        this.RewindHistory(hudLayoutIndex);
        this.RedoHistory[hudLayoutIndex].Clear();

        // Remove all unsaved actions from the undo history
        for (int i = this.UndoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
            if (this.UndoHistory[hudLayoutIndex][i].Saved) break;
            this.UndoHistory[hudLayoutIndex].RemoveAt(i);
        }
    }

    /// <summary>
    /// Rewinds the history and moves all unsaved actions to the redo history.
    /// </summary>
    /// <param name="hudLayoutIndex">The index of the HUD layout.</param>
    public void RewindHistoryAndAddToRedo(int hudLayoutIndex) {
        // Different to RewindAndClearHistory, this makes sure all unsaved actions are moved to the redo history
        if (!HudLayoutExists(hudLayoutIndex)) return;
        this.RewindHistory(hudLayoutIndex);

        // Move all unsaved actions to the redo history
        for (int i = this.UndoHistory[hudLayoutIndex].Count - 1; i >= 0; i--) {
            if (this.UndoHistory[hudLayoutIndex][i].Saved) break;
            this.RedoHistory[hudLayoutIndex].Add(this.UndoHistory[hudLayoutIndex][i]);
            this.UndoHistory[hudLayoutIndex].RemoveAt(i);
        }
    }

    private static bool HudLayoutExists(int hudLayoutIndex) {
        if (hudLayoutIndex is < 0 or >= HudLayoutCount) {
            //Plugin.Log.Warning($"Invalid HUD Layout index: {hudLayoutIndex}");
            throw new ArgumentOutOfRangeException(nameof(hudLayoutIndex));
        }
        return true;
    }

    private static bool HistoryEmpty(int hudLayoutIndex, List<List<HudElementAction>> history) {
        if (history[hudLayoutIndex].Count == 0) return true;
        return false;
    }
}
