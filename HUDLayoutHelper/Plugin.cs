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
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HUDLayoutHelper; 
public sealed class Plugin : IDalamudPlugin {
    public bool DEBUG = false;

    public string Name => "HUDLayoutHelper";
    private const string CommandName = "/hudhelper";

    public readonly WindowSystem WindowSystem = new("HUDLayoutHelper");
    public Configuration Configuration { get; init; }
    private ConfigWindow ConfigWindow { get; init; }
    private AlignmentOverlayWindow AlignmentOverlayWindow { get; init; }
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

    internal static Debug Debug { get; private set; } = null!;
    internal HudHistoryManager HudHistoryManager { get; private set; } = null!;


    // HUD Layout Addon Pointers
    internal static unsafe AgentHUDLayout* AgentHudLayout = null;
    internal static unsafe AddonHudLayoutScreen* HudLayoutScreen = null;
    internal static unsafe AddonHudLayoutWindow* HudLayoutWindow = null;

    internal KeybindManager KeybindManager { get; } = null!;

    public Plugin() {
        Debug = new Debug(this.DEBUG);

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();


        this.HudHistoryManager = new HudHistoryManager(Configuration.MaxUndoHistorySize, Configuration.RedoActionStrategy);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
            HelpMessage = "Toggle the config and debug window."
        });

        // Add the alignment overlay window
        AlignmentOverlayWindow = new AlignmentOverlayWindow(this);
        WindowSystem.AddWindow(AlignmentOverlayWindow);

        ShortcutHintsWindow = new ShortcutHintsWindow(this);
        WindowSystem.AddWindow(ShortcutHintsWindow);

        // ===== Initialize handlers ===== 
        this.KeybindManager = new KeybindManager(this);


        // ===== Register events =====
        PluginInterface.UiBuilder.Draw += DrawUI;

        // This adds a button to the plugin installer entry of this plugin which allows
        // to toggle the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        if (Plugin.GameGui.GetAddonByName("_HudLayoutScreen", 1) != IntPtr.Zero) {
            Debug.Log(Plugin.Log.Debug, "HudLayoutScreen already loaded.");
            this.RegisterCallbacks();
        }

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_HudLayoutScreen", (type, args) => {
            Debug.Log(Plugin.Log.Debug, "HudLayoutScreen setup.");
            this.RegisterCallbacks();
        });

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen", (type, args) => {
            Debug.Log(Plugin.Log.Debug, "HudLayoutScreen finalize.");
            // Remove unsaved history if HUD is closed (instead of layout changed) with unsaved changes
            this.HudHistoryManager.RewindHistoryAndAddToRedo(this.currentHudLayoutIndex);
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
        if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, out AddonHudLayoutWindow* hudLayoutWindow)) {
            Plugin.Log.Warning("HudLayoutScreen not ready.");
            return false;
        }

        AgentHudLayout = agentHudLayout;
        HudLayoutScreen = hudLayoutScreen;
        HudLayoutWindow = hudLayoutWindow;
        return true;
    }

    private unsafe void ClearHudLayoutAddons() {
        AgentHudLayout = null;
        HudLayoutScreen = null;
        HudLayoutWindow = null;
    }

    private void RegisterCallbacks() {
        if (callbackAdded) return;
        // Gets the needed UI Addons
        InitializeHudLayoutAddons();

        // Listen for mouse events to track manual element movements for undo/redo
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", HandleMouseDownEvent);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutScreen", HandleMouseUpEvent);

        // For all other changes, track all element positions
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", HandleKeyboardMoveEvent);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow", HandleControllerMoveEvent);

        // Add a check for keyboard shortcuts to the update loop
        Plugin.Framework.Update += this.KeybindManager.HandleKeyboardShortcuts;

        // Add a check for element changes to the update loop
        PreviousHudLayoutIndexElements.Clear();
        for (int i = 0; i < this.HudHistoryManager.HudLayoutCount; i++) {
            PreviousHudLayoutIndexElements.Add(new Dictionary<int, HudElementData>());
        }

        if (AlignmentOverlayWindow.ToggledOnByUser && !AlignmentOverlayWindow.IsOpen) AlignmentOverlayWindow.Toggle();
        if (Configuration.ShowShortcutHints && !ShortcutHintsWindow.IsOpen) ShortcutHintsWindow.Toggle();
        UpdatePreviousElements();
        Plugin.Framework.Update += PerformScheduledElementChangeCheck;
        Plugin.Framework.Update += OnUpdate;

        callbackAdded = true;
    }
    private void RemoveCallbacks() {
        // Remove all event listeners and callbacks
        Plugin.Framework.Update -= this.KeybindManager.HandleKeyboardShortcuts;
        Plugin.Framework.Update -= PerformScheduledElementChangeCheck;
        Plugin.Framework.Update -= OnUpdate;

        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen");
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutScreen");

        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow");
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutWindow");

        if (AlignmentOverlayWindow.IsOpen) {
            AlignmentOverlayWindow.Toggle();
        } 
        AlignmentOverlayWindow.IsOpen = false;

        if (ShortcutHintsWindow.IsOpen) {
            ShortcutHintsWindow.Toggle();
        }
        ShortcutHintsWindow.IsOpen = false;

        PreviousHudLayoutIndexElements.Clear();

        ClearHudLayoutAddons();
        callbackAdded = false;
    }
    // SETUP END

    internal AtkEventStateFlags CUSTOM_FLAG = (AtkEventStateFlags)16;
    private HudElementData? mouseDownTarget = null;

    private unsafe void HandleMouseDownEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseDown) return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        // Check if the event has the custom flag set, if so, filter it out
        AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;

        if ((atkEvent->State.StateFlags & CUSTOM_FLAG) == CUSTOM_FLAG) {
            atkEvent->State.StateFlags &= (AtkEventStateFlags)~CUSTOM_FLAG;
            return;
        }

        // Check if the layout editor is open, abort if not
        if (AgentHudLayout == null || HudLayoutScreen == null) return;

        // Get the currently selected element, abort if none is selected
        int selectedNodeId = receiveEventArgs.EventParam;
        if (selectedNodeId < 0 || selectedNodeId >= HudLayoutScreen->CollisionNodeListCount) {
            Debug.Log(Plugin.Log.Error, $"No valid element selected.");
        }

        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(HudLayoutScreen, selectedNodeId);
        if (selectedNode == null) {
            Plugin.Log.Debug($"No element selected.");
            return;
        }

        // Temporarily save the current state of the selected element for undo operations
        HudElementData previousState = new HudElementData(selectedNode);
        if (previousState.ResNodeDisplayName != string.Empty && previousState.ResNodeDisplayName != "Unknown") {
            Debug.Log(Plugin.Log.Debug, $"Selected Element by Mouse: {previousState}");
            mouseDownTarget = previousState;

        } else {
            Debug.Log(Plugin.Log.Warning, $"Could not get ResNodeDisplayName for selected element.");
            mouseDownTarget = null;
        }
    }

    private unsafe void HandleMouseUpEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseUp) return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;


        // Check if the event has the custom flag set, if so, filter it out
        AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;
        if ((atkEvent->State.StateFlags & CUSTOM_FLAG) == CUSTOM_FLAG) {
            atkEvent->State.StateFlags &= (AtkEventStateFlags)~CUSTOM_FLAG;
            return;
        }

        // Check if the layout editor is open, abort if not
        if (AgentHudLayout == null || HudLayoutScreen == null) return;

        // Get the currently selected element, abort if none is selected
        AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(HudLayoutScreen, 0);
        if (selectedNode == null) {
            Plugin.Log.Debug($"No element selected.");
            return;
        }

        // Check if the mouse target is the same as the selected element 
        if (mouseDownTarget == null) {
            Debug.Log(Plugin.Log.Warning, $"No mouse target found.");
            return;
        }

        // Get the current state of the selected element
        HudElementData newState = new HudElementData(selectedNode);

        // Check if the currently selected element is the same as the last MouseDown target
        if (newState.ResNodeDisplayName != mouseDownTarget.ResNodeDisplayName) {
            Debug.Log(Plugin.Log.Warning, $"Mouse target does not match selected element: {newState}");
            return;
        } else {
            Debug.Log(Plugin.Log.Debug, $"Mouse target matches selected element.");
        }

        // check if the position has changed, if not, do not add to undo history
        if (mouseDownTarget.PosX == newState.PosX && mouseDownTarget.PosY == newState.PosY) {
            return;
        }

        // Save the current state of the selected element for undo operations
        Plugin.Log.Debug($"User moved: {mouseDownTarget.PrettyPrint()} -> ({newState.PosX}, {newState.PosY})");
        this.HudHistoryManager.AddUndoAction(Utils.GetCurrentHudLayoutIndex(), mouseDownTarget, newState);

        // Update previousElements
        var previousElements = PreviousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex()];
        previousElements[mouseDownTarget.ElementId] = newState;

        mouseDownTarget = null;
    }


    public void Dispose() {
        this.RemoveCallbacks();
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "_HudLayoutScreen");
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen");
        Debug.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        AlignmentOverlayWindow.Dispose();
        ShortcutHintsWindow.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) {
        ConfigWindow.Toggle();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() {
        ConfigWindow.SetSelectedTab(ConfigWindow.windowTabs.Settings);
        if (!ConfigWindow.IsOpen)
            ConfigWindow.Toggle();
        ConfigWindow.BringToFront();
    }
    public void ToggleMainUI() {
        ConfigWindow.SetSelectedTab(ConfigWindow.windowTabs.Keybinds);
        if (!ConfigWindow.IsOpen)
            ConfigWindow.Toggle();
        ConfigWindow.BringToFront();
    }

    public void ToggleAlignmentOverlay() {
        if (AlignmentOverlayWindow.ToggledOnByUser) {
            AlignmentOverlayWindow.ToggledOnByUser = false;
        } else {
            AlignmentOverlayWindow.ToggledOnByUser = true;
        }
        Plugin.Log.Info($"Toggled Alignment Overlay {(AlignmentOverlayWindow.ToggledOnByUser ? "on" : "off")}");
    }


    // ==== Logic for periodically checking for changes
    private int currentHudLayoutIndex = -1;
    private bool currentNeedToSave = false;
    private unsafe void OnUpdate(IFramework framework) {
        if (AgentHudLayout == null || HudLayoutScreen == null) return;

        bool hudLayoutIndex_change = false;
        bool needToSave_change = false;

        int currentHudLayoutIndex_backup = this.currentHudLayoutIndex;
        bool needToSave_backup = this.currentNeedToSave;

        // Check if HUD Layout index has changed
        int hudLayoutIndex = Utils.GetCurrentHudLayoutIndex(false);
        if (hudLayoutIndex != this.currentHudLayoutIndex) {
            hudLayoutIndex_change = true;
            Debug.Log(Plugin.Log.Debug, $"HUD Layout Index changed: {hudLayoutIndex}");
            this.currentHudLayoutIndex = hudLayoutIndex;
            UpdatePreviousElements();
        }

        // Check flag if HUD Layout needs to be saved
        bool needToSave = AgentHudLayout->NeedToSave;
        if (needToSave != currentNeedToSave) {
            needToSave_change = true;
            Debug.Log(Plugin.Log.Debug, $"HUD Layout needs to be saved changed to: {needToSave}");
            this.currentNeedToSave = needToSave;
        }

        // Reset undo and redo history if HUD Layout was closed with unsaved changes
        if (needToSave_change && !needToSave && hudLayoutIndex_change) {
            Debug.Log(Plugin.Log.Debug, $"HUD Layout changed without saving.");
            this.HudHistoryManager.RewindHistoryAndAddToRedo(currentHudLayoutIndex_backup);
        }

        // Mark history as saved when HUD Layout is saved
        if (needToSave_change && !needToSave && !hudLayoutIndex_change) {
            Debug.Log(Plugin.Log.Debug, $"HUD Layout was saved.");
            this.HudHistoryManager.MarkHistoryAsSaved(currentHudLayoutIndex_backup);
        }
    }

    private int LastKeyboardEvent = 0;
    private int LastChangeCheck = 0;
    private int LastChangeCHeckHudLayoutIndex = -1;
    private const int ChangeCheckInterval = 200; // ms

    private unsafe void HandleKeyboardMoveEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != 13) // && (AtkEventType)receiveEventArgs.AtkEventType != AtkEventType.InputReceived) 
            return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        if (LastChangeCHeckHudLayoutIndex != Utils.GetCurrentHudLayoutIndex(false)) {
            UpdatePreviousElements();
            LastChangeCHeckHudLayoutIndex = Utils.GetCurrentHudLayoutIndex();
        }

        LastKeyboardEvent = Environment.TickCount;
    }

    private unsafe void HandleControllerMoveEvent(AddonEvent type, AddonArgs args) {
        if (args is not AddonReceiveEventArgs receiveEventArgs) return;
        if (receiveEventArgs.AtkEventType != 15) // && (AtkEventType)receiveEventArgs.AtkEventType != AtkEventType.InputReceived) 
            return;
        if (receiveEventArgs.AtkEvent == nint.Zero) return;

        if (LastChangeCHeckHudLayoutIndex != Utils.GetCurrentHudLayoutIndex(false)) {
            UpdatePreviousElements();
            LastChangeCHeckHudLayoutIndex = Utils.GetCurrentHudLayoutIndex();
        }

        LastKeyboardEvent = Environment.TickCount;
    }


    private unsafe void PerformScheduledElementChangeCheck(IFramework framework) {
        if (LastKeyboardEvent > LastChangeCheck && Environment.TickCount - LastKeyboardEvent > ChangeCheckInterval) {
            Debug.Log(Plugin.Log.Debug, "Keyboard event detected, checking for element changes.");
            PerformElementChangeCheck();
            LastChangeCheck = Environment.TickCount;
        }
    }


    internal List<Dictionary<int, HudElementData>> PreviousHudLayoutIndexElements = new();

    private unsafe void PerformElementChangeCheck() {
        if (AgentHudLayout == null || HudLayoutScreen == null) return;
        Debug.Log(Plugin.Log.Debug, "Checking for element changes.");

        var previousElements = PreviousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(false)];

        var currentElements = GetCurrentElements();

        var changedElements = new List<HudElementData>();
        foreach (var elementData in currentElements) {
            if (previousElements.TryGetValue(elementData.Key, out var previousData)) {
                if (HasPositionChanged(previousData, elementData.Value)) {
                    HudHistoryManager.AddUndoAction(Utils.GetCurrentHudLayoutIndex(false), previousData, elementData.Value);
                    changedElements.Add(elementData.Value);
                    Plugin.Log.Debug($"User moved: {previousData.PrettyPrint()} -> ({elementData.Value.PosX}, {elementData.Value.PosY})");
                }
            }
            previousElements[elementData.Key] = elementData.Value;
        }
        if (changedElements.Count > 0)
            Debug.PrettyPrintList(changedElements, "Changed Elements");
    }

    internal unsafe void UpdatePreviousElements() {
        Debug.Log(Plugin.Log.Debug, "Updating previous elements.");
        var currentElements = GetCurrentElements();
        var previousElements = PreviousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(false)];
        foreach (var elementData in currentElements) {
            previousElements[elementData.Key] = elementData.Value;
        }
    }

    internal unsafe Dictionary<int, HudElementData> GetCurrentElements() {
        var elements = new Dictionary<int, HudElementData>();

        for (int i = 0; i < HudLayoutScreen->CollisionNodeListCount; i++) {
            var resNode = HudLayoutScreen->CollisionNodeList[i];
            var elementData = new HudElementData(resNode);
            if (!elementData.IsVisible) continue; // TODO: works? 
            elements[elementData.ElementId] = elementData;
        }

        return elements;
    }

    private unsafe bool HasPositionChanged(HudElementData previousData, HudElementData currentData) {
        return previousData.PosX != currentData.PosX || previousData.PosY != currentData.PosY;
    }
}
