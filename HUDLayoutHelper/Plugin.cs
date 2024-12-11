using System;
using System.Collections.Generic;
using System.Text.Json;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HUDLayoutHelper.Utilities;
using HUDLayoutHelper.Windows;
using ImGuiNET;

namespace HUDLayoutHelper;
public sealed class Plugin : IDalamudPlugin {
    private const bool DEBUG = false;

    public string Name => "HUDLayoutHelper";
    private const string CommandName = "/hudhelper";

    public readonly WindowSystem WindowSystem = new("HUDLayoutHelper");
    public Configuration Configuration { get; init; }
    private ConfigWindow ConfigWindow { get; init; }
    private AlignmentOverlayWindow AlignmentOverlayWindow { get; init; }
    private bool isOverlayVisible = false;

    private ShortcutHintsWindow ShortcutHintsWindow { get; init; }

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IAddonEventManager AddonEventManager { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    public static Debug Debug { get; private set; } = null!;


    // HUD Layout Addon Pointers
    internal unsafe AgentHUDLayout* AgentHudLayout = null;
    internal unsafe AddonHudLayoutScreen* HudLayoutScreen = null;
    internal unsafe AddonHudLayoutWindow* HudLayoutWindow = null;
    
    internal static HudHistoryManager HudHistoryManager { get; private set; } = null!;

    public Plugin() {
        Debug = new Debug(DEBUG);
        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        HudHistoryManager = new HudHistoryManager(this.Configuration.MaxUndoHistorySize, this.Configuration.RedoActionStrategy);

        this.ConfigWindow = new ConfigWindow(this);
        this.WindowSystem.AddWindow(this.ConfigWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand) {
            HelpMessage = "Toggle the config and debug window."
        });

        // Add the alignment overlay window
        this.AlignmentOverlayWindow = new AlignmentOverlayWindow(this);
        this.WindowSystem.AddWindow(this.AlignmentOverlayWindow);

        this.ShortcutHintsWindow = new ShortcutHintsWindow(this);
        this.WindowSystem.AddWindow(this.ShortcutHintsWindow);

        PluginInterface.UiBuilder.Draw += this.DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += this.ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += this.ToggleMainUI;

        if (GameGui.GetAddonByName("_HudLayoutScreen", 1) != IntPtr.Zero) {
            Debug.Log(Log.Debug, "HudLayoutScreen already loaded.");
            this.RegisterCallbacks();
        }

        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_HudLayoutScreen", (type, args) => {
            Debug.Log(Log.Debug, "HudLayoutScreen setup.");
            this.RegisterCallbacks();
        });

        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen", (type, args) => {
            Debug.Log(Log.Debug, "HudLayoutScreen finalize.");
            // Remove unsaved history if HUD is closed (instead of layout changed) with unsaved changes
            HudHistoryManager.RewindHistoryAndAddToRedo(this.currentHudLayoutIndex);
            this.RemoveCallbacks();
        });

        // TODO: Show numbers for distance of alignment lines? 
    }

    // SETUP START
    private bool callbackAdded = false;


    /// <summary>
    /// Initializes the HUD layout addons and sets the AgentHUDLayout and HudLayoutScreen pointers when the HUD layout is opened. 
    /// </summary>
    /// <returns></returns>
    private unsafe bool InitializeHudLayoutAddons() {
        if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, out AddonHudLayoutWindow* hudLayoutWindow, this)) {
            Log.Warning("HudLayoutScreen not ready.");
            return false;
        }

        this.AgentHudLayout = agentHudLayout;
        this.HudLayoutScreen = hudLayoutScreen;
        this.HudLayoutWindow = hudLayoutWindow;
        return true;
    }

    private unsafe void ClearHudLayoutAddons() {
        this.AgentHudLayout = null;
        this.HudLayoutScreen = null;
        this.HudLayoutWindow = null;
    }

    private void RegisterCallbacks() {
        if (this.callbackAdded) return;
        // Gets the needed UI Addons
        this.InitializeHudLayoutAddons();

        // Listen for mouse events to track manual element movements for undo/redo
        AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", this.HandleMouseDownEvent);
        AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutScreen", this.HandleMouseUpEvent);

        // For all other changes, track all element positions
        AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", this.HandleKeyboardMoveEvent);
        AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow", this.HandleControllerMoveEvent);

        // Add a check for keyboard shortcuts to the update loop
        Framework.Update += this.HandleKeyboardShortcuts;

        // Add a check for element changes to the update loop
        this.previousHudLayoutIndexElements.Clear();
        for (int i = 0; i < HudHistoryManager.HudLayoutCount; i++) {
            this.previousHudLayoutIndexElements.Add([]);
        }

        if (this.isOverlayVisible && !this.AlignmentOverlayWindow.IsOpen) this.AlignmentOverlayWindow.Toggle();
        if (!this.ShortcutHintsWindow.IsOpen) this.ShortcutHintsWindow.Toggle();
        this.UpdatePreviousElements();
        Framework.Update += this.PerformScheduledElementChangeCheck;
        Framework.Update += this.OnUpdate;

        this.callbackAdded = true;
    }
    private void RemoveCallbacks() {
        // Remove all event listeners and callbacks
        Framework.Update -= this.HandleKeyboardShortcuts;
        Framework.Update -= this.PerformScheduledElementChangeCheck;
        Framework.Update -= this.OnUpdate;

        AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen");
        AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutScreen");

        AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow");
        AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutWindow");

        if (this.AlignmentOverlayWindow.IsOpen) {
            this.AlignmentOverlayWindow.Toggle();
            this.isOverlayVisible = true;
        } else {
            this.isOverlayVisible = false;
        }
        this.AlignmentOverlayWindow.IsOpen = false;

        if (this.ShortcutHintsWindow.IsOpen) {
            this.ShortcutHintsWindow.Toggle();
        }
        this.ShortcutHintsWindow.IsOpen = false;

        this.previousHudLayoutIndexElements.Clear();

        this.ClearHudLayoutAddons();
        this.callbackAdded = false;
    }
    // SETUP END


    private HudElementData? mouseDownTarget = null;
    internal const AtkEventStateFlags CustomAtkFlag = (AtkEventStateFlags)16;

    private unsafe void HandleMouseDownEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseDown) return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        // Check if the event has the custom flag set, if so, filter it out
        AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;

        if ((atkEvent->State.StateFlags & CustomAtkFlag) == CustomAtkFlag) {
            atkEvent->State.StateFlags &= (AtkEventStateFlags)~CustomAtkFlag;
            return;
        }

        // Check if the layout editor is open, abort if not
        if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

        // Get the currently selected element, abort if none is selected
        int selectedNodeId = receiveEventArgs.EventParam;
        if (selectedNodeId < 0 || selectedNodeId >= this.HudLayoutScreen->CollisionNodeListCount) {
            Debug.Log(Log.Error, $"No valid element selected.");
        }

        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(this.HudLayoutScreen, selectedNodeId);
        if (selectedNode == null) {
            Log.Debug($"No element selected.");
            return;
        }

        // Temporarily save the current state of the selected element for undo operations
        HudElementData previousState = new HudElementData(selectedNode);
        if (previousState.ResNodeDisplayName != string.Empty && previousState.ResNodeDisplayName != "Unknown") {
            Debug.Log(Log.Debug, $"Selected Element by Mouse: {previousState}");
            this.mouseDownTarget = previousState;

        } else {
            Debug.Log(Log.Warning, $"Could not get ResNodeDisplayName for selected element.");
            this.mouseDownTarget = null;
        }
    }

    private unsafe void HandleMouseUpEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseUp) return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;


        // Check if the event has the custom flag set, if so, filter it out
        AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;
        if ((atkEvent->State.StateFlags & CustomAtkFlag) == CustomAtkFlag) {
            atkEvent->State.StateFlags &= (AtkEventStateFlags)~CustomAtkFlag;
            return;
        }

        // Check if the layout editor is open, abort if not
        if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

        // Get the currently selected element, abort if none is selected
        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(this.HudLayoutScreen, 0);
        if (selectedNode == null) {
            Log.Debug($"No element selected.");
            return;
        }

        // Check if the mouse target is the same as the selected element 
        if (this.mouseDownTarget == null) {
            Debug.Log(Log.Warning, $"No mouse target found.");
            return;
        }

        // Get the current state of the selected element
        HudElementData newState = new HudElementData(selectedNode);

        // Check if the currently selected element is the same as the last MouseDown target
        if (newState.ResNodeDisplayName != this.mouseDownTarget.ResNodeDisplayName) {
            Debug.Log(Log.Warning, $"Mouse target does not match selected element: {newState}");
            return;
        } else {
            Debug.Log(Log.Debug, $"Mouse target matches selected element.");
        }

        // check if the position has changed, if not, do not add to undo history
        if (this.mouseDownTarget.PosX == newState.PosX && this.mouseDownTarget.PosY == newState.PosY) {
            return;
        }

        // Save the current state of the selected element for undo operations
        Log.Debug($"User moved: {this.mouseDownTarget.PrettyPrint()} -> ({newState.PosX}, {newState.PosY})");
        HudHistoryManager.AddUndoAction(Utils.GetCurrentHudLayoutIndex(), this.mouseDownTarget, newState);

        // Update previousElements
        var previousElements = this.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex()];
        previousElements[this.mouseDownTarget.ElementId] = newState;

        this.mouseDownTarget = null;
    }


    private HudElementData? currentlyCopied = null;

    internal class Keybind {
        internal enum Action { None, Copy, Paste, Undo, Redo, ToggleAlignmentOverlay }
        internal struct Description(string name, string description, string shortDescription) {
            public string Name { get; set; } = name;
            public string Text { get; set; } = description;
            public string ShortText { get; set; } = shortDescription;
        }
        internal struct Keys(SeVirtualKey mainKey, KeyStateFlags state, bool shiftPressed) {
            public SeVirtualKey MainKey { get; set; } = mainKey;
            public bool ShiftPressed { get; set; } = shiftPressed;
            public KeyStateFlags State { get; set; } = state;

            public override string ToString() {
                string extraModifier = this.ShiftPressed ? $" + Shift" : "";
                return $"Ctrl{extraModifier} + {this.MainKey}";
            }
        }

        public Description KeybindDescription { get; set; }
        public Keys KeybindKeys { get; set; }
        public Action KeybindAction { get; set; }

        public Keybind((string name, string text, string shortText) description, (SeVirtualKey mainKey, KeyStateFlags state, bool shiftPressed) keys, Keybind.Action action) {
            this.KeybindDescription = new Description(description.name, description.text, description.shortText);
            this.KeybindKeys = new Keys(keys.mainKey, keys.state, keys.shiftPressed);
            this.KeybindAction = action;
        }
    }

    internal static List<Keybind> Keybindings { get; } = [
        new Keybind( action: Keybind.Action.Copy,
            description: (name: "Copy", text: "Copy position of selected HUD element", shortText: "Copy"),
            keys: ( mainKey: SeVirtualKey.C, state: KeyStateFlags.Pressed, shiftPressed: false)
        ),
        new Keybind( action: Keybind.Action.Paste,
            description: (name: "Paste", text: "Paste copied position to selected HUD element", shortText: "Paste"),
            keys: ( mainKey: SeVirtualKey.V, state: KeyStateFlags.Released, shiftPressed: false)
        ),
        new Keybind( action: Keybind.Action.Undo,
            description: (name: "Undo", text: "Undo last action", shortText: "Undo"),
            keys: ( mainKey: SeVirtualKey.Z, state: KeyStateFlags.Pressed,shiftPressed : false)
        ),
        new Keybind( action: Keybind.Action.Redo,
            description: (name: "Redo", text: "Redo last action", shortText: "Redo"),
            keys: ( mainKey: SeVirtualKey.Y, state: KeyStateFlags.Pressed, shiftPressed: false)
        ),
        new Keybind( action: Keybind.Action.Redo,
            description: (name: "Redo", text: "Redo last action", shortText: "Redo"),
            keys: ( mainKey: SeVirtualKey.Z, state: KeyStateFlags.Pressed, shiftPressed: true)
        ),
        new Keybind( action: Keybind.Action.ToggleAlignmentOverlay,
            description: (name: "Toggle Alignment Helper Overlay", text: "Toggle alignment helper overlay on/off", shortText: "Toggle Overlay"),
            keys: ( mainKey: SeVirtualKey.R, state: KeyStateFlags.Pressed, shiftPressed : false)
        )

    ];

    /// <summary>
    /// Handles keyboard shortcuts for copy, paste, undo, and redo actions.
    /// </summary>
    /// <param name="framework">The framework interface.</param>
    private unsafe void HandleKeyboardShortcuts(IFramework framework) {
        // Executes every frame
        if (!ClientState.IsLoggedIn) return;
        if (ClientState is not { LocalPlayer.ClassJob.RowId: var classJobId }) return;
        if (ImGui.GetIO().WantCaptureKeyboard) return; // TODO: Necessary? 

        // Get the state of the control key, abort if not pressed 
        KeyStateFlags ctrlKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.CONTROL);
        if (!ctrlKeystate.HasFlag(KeyStateFlags.Down)) return;

        // Set the keyboard action based on the key states
        Keybind.Action keyboardAction = Keybind.Action.None;
        foreach (var keybind in Keybindings) {
            KeyStateFlags keyState = UIInputData.Instance()->GetKeyState(keybind.KeybindKeys.MainKey);
            if (keybind.KeybindKeys.ShiftPressed && !UIInputData.Instance()->GetKeyState(SeVirtualKey.SHIFT).HasFlag(KeyStateFlags.Down)) continue;
            if (!keybind.KeybindKeys.ShiftPressed && UIInputData.Instance()->GetKeyState(SeVirtualKey.SHIFT).HasFlag(KeyStateFlags.Down)) continue;
            if (keyState.HasFlag(keybind.KeybindKeys.State)) {
                keyboardAction = keybind.KeybindAction;
                break;
            }
        }

        if (keyboardAction == Keybind.Action.None) return;
        Debug.Log(Log.Debug, $"Keybind.Action: {keyboardAction}");

        // Abort if a popup is open
        if (this.HudLayoutWindow->NumOpenPopups > 0) {
            Debug.Log(Log.Warning, "Popup open, not executing action.");
            return;
        }

        // Check if the layout editor is open, abort if not
        if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

        // Depending on the keyboard action, execute the corresponding operation
        HudElementData? changedElement = null;
        switch (keyboardAction) {
            case Keybind.Action.Copy:
                this.HandleCopyAction(this.HudLayoutScreen);
                break;
            case Keybind.Action.Paste:
                changedElement = this.HandlePasteAction(this.HudLayoutScreen, this.AgentHudLayout);
                break;
            case Keybind.Action.Undo:
                changedElement = this.HandleUndoAction(this.HudLayoutScreen, this.AgentHudLayout);
                break;
            case Keybind.Action.Redo:
                changedElement = this.HandleRedoAction(this.HudLayoutScreen, this.AgentHudLayout);
                break;
            case Keybind.Action.ToggleAlignmentOverlay:
                this.ToggleAlignmentOverlay();
                break;
        }

        // Update previousElements if a change was made
        if (changedElement != null) {
            Debug.Log(Log.Debug, $"Changed Element: {changedElement}");
            var previousElements = this.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex()];
            previousElements.TryGetValue(changedElement.ElementId, out HudElementData? changedPreviousElement);
            previousElements[changedElement.ElementId] = changedElement;
        }
    }
    /// <summary>
    /// Copy the position of the selected element to the clipboard. 
    /// </summary>
    /// <param name="hudLayoutScreen"></param>
    private unsafe void HandleCopyAction(AddonHudLayoutScreen* hudLayoutScreen) {
        // Get the currently selected element, abort if none is selected
        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(hudLayoutScreen, 0);
        if (selectedNode == null) {
            Log.Debug($"No element selected.");
            return;
        }

        // Create a new HudElementData object with the data of the selected element
        HudElementData selectedNodeData = new HudElementData(selectedNode);
        this.currentlyCopied = selectedNodeData;

        // Copy the data to the clipboard
        ImGui.SetClipboardText(selectedNodeData.ToString());
        Debug.Log(Log.Debug, $"Copied to Clipboard: {selectedNodeData}");
        Log.Debug($"Copied position to clipboard: {selectedNodeData.PrettyPrint()}");
    }

    /// <summary>
    /// Paste the position from the clipboard to the selected element 
    /// and simulate a mouse click on the element.
    /// </summary>
    /// <param name="hudLayoutScreen"></param>
    /// <param name="agentHudLayout"></param>
    private unsafe HudElementData? HandlePasteAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
        // Get the currently selected element, abort if none is selected
        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(hudLayoutScreen, 0);
        if (selectedNode == null) {
            Log.Debug($"No element selected.");
            return null;
        }

        // Get the clipboard text
        string clipboardText = ImGui.GetClipboardText();
        if (clipboardText == null) {
            Log.Debug($"Clipboard is empty.");
            return null;
        }

        // Parse the clipboard text to a HudElementData object
        HudElementData? parsedData = null;
        try {
            parsedData = JsonSerializer.Deserialize<HudElementData>(clipboardText);
        } catch {
            Log.Warning($"Clipboard data could not be parsed: '{clipboardText}'");
            return null;
        }
        if (parsedData == null) {
            Log.Warning($"Clipboard data could not be parsed. '{clipboardText}'");
            return null;
        }
        Debug.Log(Log.Debug, $"Parsed Clipboard: {parsedData}");

        // Save the current state of the selected element for undo operations
        HudElementData previousState = new HudElementData(selectedNode);


        // Set the position of the currently selected element to the parsed position
        selectedNode->ParentNode->SetPositionShort(parsedData.PosX, parsedData.PosY);

        // Add the previous state and the new state to the undo history
        int hudLayoutIndex = Utils.GetCurrentHudLayoutIndex();
        HudHistoryManager.AddUndoAction(hudLayoutIndex, previousState, parsedData);

        // Simulate Mouse Click
        Utils.SimulateMouseClickOnHudElement(selectedNode, 0, parsedData, hudLayoutScreen, CustomAtkFlag);

        // Send Event to HudLayout to inform about a change 
        Utils.SendChangeEvent(agentHudLayout);

        Log.Debug($"Pasted position to selected element: {previousState.ResNodeDisplayName} ({previousState.PosX}, {previousState.PosY}) -> ({parsedData.PosX}, {parsedData.PosY})");
        return parsedData;
    }

    /// <summary>
    /// Undo the last operation and simulate a mouse click on the element.
    /// </summary>
    /// <param name="hudLayoutScreen"></param>
    /// <param name="agentHudLayout"></param>
    private unsafe HudElementData? HandleUndoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
        // Get the last added action from the undo history
        (HudElementData? oldState, HudElementData? newState) = HudHistoryManager.PeekUndoAction(Utils.GetCurrentHudLayoutIndex());
        if (oldState == null || newState == null) {
            Log.Debug($"Nothing to undo.");
            return null;
        }

        // Find node with same name as oldState
        (nint undoNodePtr, uint undoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, oldState.ResNodeDisplayName);
        if (undoNodePtr == nint.Zero) {
            Log.Warning($"Could not find node with name '{oldState.ResNodeDisplayName}'");
            return null;
        }
        AtkResNode* undoNode = (AtkResNode*)undoNodePtr;

        HudElementData undoNodeState = new HudElementData(undoNode);

        // Set the position of the currently selected element to the parsed position
        undoNode->ParentNode->SetPositionShort(oldState.PosX, oldState.PosY);

        HudHistoryManager.PerformUndo(Utils.GetCurrentHudLayoutIndex(), undoNodeState);

        // Simulate Mouse Click
        Utils.SimulateMouseClickOnHudElement(undoNode, undoNodeId, oldState, hudLayoutScreen, CustomAtkFlag);

        // Send Event to HudLayout to inform about a change 
        Utils.SendChangeEvent(agentHudLayout);

        Log.Debug($"Undo: Moved '{undoNodeState.ResNodeDisplayName}' from ({undoNodeState.PosX}, {undoNodeState.PosY}) back to ({oldState.PosX}, {oldState.PosY})");

        return oldState;
    }

    /// <summary>
    /// Redo the last operation and simulate a mouse click on the element.
    /// </summary>
    /// <param name="hudLayoutScreen"></param>
    /// <param name="agentHudLayout"></param>
    private unsafe HudElementData? HandleRedoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
        // Get the last added action from the redo history
        (HudElementData? oldState, HudElementData? newState) = HudHistoryManager.PeekRedoAction(Utils.GetCurrentHudLayoutIndex());
        if (oldState == null || newState == null) {
            Log.Debug($"Nothing to redo.");
            return null;
        }

        // Find node with same name as new state
        (nint redoNodePtr, uint redoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, newState.ResNodeDisplayName);
        if (redoNodePtr == nint.Zero) {
            Log.Warning($"Could not find node with name '{newState.ResNodeDisplayName}'");
            return null;
        }
        AtkResNode* redoNode = (AtkResNode*)redoNodePtr;
        HudElementData redoNodeState = new HudElementData(redoNode);

        // Set the position of the currently selected element to the parsed position
        redoNode->ParentNode->SetPositionShort(newState.PosX, newState.PosY);

        HudHistoryManager.PerformRedo(Utils.GetCurrentHudLayoutIndex(), redoNodeState);

        // Simulate Mouse Click
        Utils.SimulateMouseClickOnHudElement(redoNode, redoNodeId, newState, hudLayoutScreen, CustomAtkFlag);

        // Send Event to HudLayout to inform about a change 
        Utils.SendChangeEvent(agentHudLayout);

        Log.Debug($"Redo: Moved '{redoNodeState.ResNodeDisplayName}' again from ({redoNodeState.PosX}, {redoNodeState.PosY}) to ({newState.PosX}, {newState.PosY})");

        return newState;
    }

    public void Dispose() {
        this.RemoveCallbacks();
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "_HudLayoutScreen");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen");
        Debug.Dispose();

        this.WindowSystem.RemoveAllWindows();
        this.ConfigWindow.Dispose();
        this.AlignmentOverlayWindow.Dispose();
        this.ShortcutHintsWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) {
        this.ConfigWindow.Toggle();
    }

    private void DrawUI() => this.WindowSystem.Draw();

    public void ToggleConfigUI() {
        this.ConfigWindow.SetSelectedTab(this.ConfigWindow.AllTabs.Settings);
        if (!this.ConfigWindow.IsOpen)
            this.ConfigWindow.Toggle();
        this.ConfigWindow.BringToFront();
    }
    public void ToggleMainUI() {
        this.ConfigWindow.SetSelectedTab(this.ConfigWindow.AllTabs.Keybinds);
        if (!this.ConfigWindow.IsOpen)
            this.ConfigWindow.Toggle();
        this.ConfigWindow.BringToFront();
    }

    public void ToggleAlignmentOverlay() {
        this.AlignmentOverlayWindow.Toggle();
        Log.Info($"Toggled Alignment Overlay {(this.AlignmentOverlayWindow.IsOpen ? "on" : "off")}");
    }


    // ==== Logic for periodically checking for changes
    private int currentHudLayoutIndex = -1;
    private bool currentNeedToSave = false;
    private unsafe void OnUpdate(IFramework framework) {
        if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

        bool hudLayoutIndex_change = false;
        bool needToSave_change = false;

        int currentHudLayoutIndex_backup = this.currentHudLayoutIndex;
        bool needToSave_backup = this.currentNeedToSave;

        // Check if HUD Layout index has changed
        int hudLayoutIndex = Utils.GetCurrentHudLayoutIndex(false);
        if (hudLayoutIndex != this.currentHudLayoutIndex) {
            hudLayoutIndex_change = true;
            Debug.Log(Log.Debug, $"HUD Layout Index changed: {hudLayoutIndex}");
            this.currentHudLayoutIndex = hudLayoutIndex;
            this.UpdatePreviousElements();
        }

        // Check flag if HUD Layout needs to be saved
        bool needToSave = this.AgentHudLayout->NeedToSave;
        if (needToSave != this.currentNeedToSave) {
            needToSave_change = true;
            Debug.Log(Log.Debug, $"HUD Layout needs to be saved changed to: {needToSave}");
            this.currentNeedToSave = needToSave;
        }

        // Reset undo and redo history if HUD Layout was closed with unsaved changes
        if (needToSave_change && !needToSave && hudLayoutIndex_change) {
            Debug.Log(Log.Debug, $"HUD Layout changed without saving.");
            HudHistoryManager.RewindHistoryAndAddToRedo(currentHudLayoutIndex_backup);
        }

        // Mark history as saved when HUD Layout is saved
        if (needToSave_change && !needToSave && !hudLayoutIndex_change) {
            Debug.Log(Log.Debug, $"HUD Layout was saved.");
            HudHistoryManager.MarkHistoryAsSaved(currentHudLayoutIndex_backup);
        }
    }

    private int lastKeyboardEvent = 0;
    private int lastChangeCheck = 0;
    private int lastChangeCHeckHudLayoutIndex = -1;
    private const int ChangeCheckInterval = 200; // ms

    private unsafe void HandleKeyboardMoveEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != 13) // && (AtkEventType)receiveEventArgs.AtkEventType != AtkEventType.InputReceived) 
            return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        if (this.lastChangeCHeckHudLayoutIndex != Utils.GetCurrentHudLayoutIndex(false)) {
            this.UpdatePreviousElements();
            this.lastChangeCHeckHudLayoutIndex = Utils.GetCurrentHudLayoutIndex();
        }

        this.lastKeyboardEvent = Environment.TickCount;
    }

    private unsafe void HandleControllerMoveEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != 15) // && (AtkEventType)receiveEventArgs.AtkEventType != AtkEventType.InputReceived) 
            return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        if (this.lastChangeCHeckHudLayoutIndex != Utils.GetCurrentHudLayoutIndex(false)) {
            this.UpdatePreviousElements();
            this.lastChangeCHeckHudLayoutIndex = Utils.GetCurrentHudLayoutIndex();
        }

        this.lastKeyboardEvent = Environment.TickCount;
    }


    private unsafe void PerformScheduledElementChangeCheck(IFramework framework) {
        if (this.lastKeyboardEvent > this.lastChangeCheck && Environment.TickCount - this.lastKeyboardEvent > ChangeCheckInterval) {
            Debug.Log(Log.Debug, "Keyboard event detected, checking for element changes.");
            this.PerformElementChangeCheck();
            this.lastChangeCheck = Environment.TickCount;
        }
    }


    internal List<Dictionary<int, HudElementData>> previousHudLayoutIndexElements = [];

    private unsafe void PerformElementChangeCheck() {
        if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;
        Debug.Log(Log.Debug, "Checking for element changes.");

        var previousElements = this.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(false)];

        var currentElements = this.GetCurrentElements();

        var changedElements = new List<HudElementData>();
        foreach (var elementData in currentElements) {
            if (previousElements.TryGetValue(elementData.Key, out var previousData)) {
                if (HasPositionChanged(previousData, elementData.Value)) {
                    HudHistoryManager.AddUndoAction(Utils.GetCurrentHudLayoutIndex(false), previousData, elementData.Value);
                    changedElements.Add(elementData.Value);
                    Log.Debug($"User moved: {previousData.PrettyPrint()} -> ({elementData.Value.PosX}, {elementData.Value.PosY})");
                }
            }
            previousElements[elementData.Key] = elementData.Value;
        }
        if (changedElements.Count > 0)
            Debug.PrettyPrintList(changedElements, "Changed Elements");
    }

    internal unsafe void UpdatePreviousElements() {
        Debug.Log(Log.Debug, "Updating previous elements.");
        var currentElements = this.GetCurrentElements();
        var previousElements = this.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(false)];
        foreach (var elementData in currentElements) {
            previousElements[elementData.Key] = elementData.Value;
        }
    }

    internal unsafe Dictionary<int, HudElementData> GetCurrentElements() {
        var elements = new Dictionary<int, HudElementData>();

        for (int i = 0; i < this.HudLayoutScreen->CollisionNodeListCount; i++) {
            var resNode = this.HudLayoutScreen->CollisionNodeList[i];
            var elementData = new HudElementData(resNode);
            if (!elementData.IsVisible) continue; // TODO: works? 
            elements[elementData.ElementId] = elementData;
        }

        return elements;
    }

    private static unsafe bool HasPositionChanged(HudElementData previousData, HudElementData currentData) {
        return previousData.PosX != currentData.PosX || previousData.PosY != currentData.PosY;
    }
}
