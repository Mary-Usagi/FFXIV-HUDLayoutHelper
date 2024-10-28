using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HUDLayoutShortcuts {
    public sealed class Plugin : IDalamudPlugin {
        public bool DEBUG = true;

        public string Name => "HUDLayoutShortcuts";
        private const string CommandName = "/hudshortcuts";

        public readonly WindowSystem WindowSystem = new("HUDLayoutShortcuts");
        public Configuration Configuration { get; init; }
        private ConfigWindow ConfigWindow { get; init; }

        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

        public IGameGui GameGui { get; init; }
        public IClientState ClientState { get; init; }
        public IPluginLog Log { get; init; }
        public IAddonEventManager AddonEventManager { get; init; }
        public IAddonLifecycle AddonLifecycle { get; init; }
        public IFramework Framework { get; init; }
        public IChatGui ChatGui { get; init; } = null!;
        public Debug Debug { get; private set; } = null!;
        internal HudHistoryManager HudHistoryManager { get; private set; } = null!;


        // HUD Layout Addon Pointers
        internal unsafe AgentHUDLayout* AgentHudLayout = null;
        internal unsafe AddonHudLayoutScreen* HudLayoutScreen = null;

        public Plugin(
            IGameGui gameGui,
            IClientState clientState,
            IPluginLog pluginLog,
            IAddonEventManager addonEventManager,
            IAddonLifecycle addonLifecycle,
            IFramework framework,
            IChatGui chatGui
        ) {
            this.Log = pluginLog;
            this.GameGui = gameGui;
            this.ClientState = clientState;
            this.AddonEventManager = addonEventManager;
            this.AddonLifecycle = addonLifecycle;
            this.Framework = framework;
            this.ChatGui = chatGui;

            this.Debug = new Debug(this, this.DEBUG);

            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();


            this.HudHistoryManager = new HudHistoryManager(this, Configuration.MaxUndoHistorySize, Configuration.RedoActionStrategy);

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
                HelpMessage = "Toggle the config and debug window."
            });

            PluginInterface.UiBuilder.Draw += DrawUI;

            // This adds a button to the plugin installer entry of this plugin which allows
            // to toggle the display status of the configuration ui
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;

            if (this.GameGui.GetAddonByName("_HudLayoutScreen", 1) != IntPtr.Zero) {
                this.Debug.Log(this.Log.Debug, "HudLayoutScreen already loaded.");
                this.addOnUpdateCallback();
            }

            this.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_HudLayoutScreen", (type, args) => {
                this.Debug.Log(this.Log.Debug, "HudLayoutScreen setup.");
                this.addOnUpdateCallback();
            });

            this.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen", (type, args) => {
                this.Debug.Log(this.Log.Debug, "HudLayoutScreen finalize.");
                this.removeOnUpdateCallback();
            });

            // Listen for mouse events to track manual element movements for undo/redo
            this.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", HandleMouseDownEvent);
            this.AddonLifecycle.RegisterListener(AddonEvent.PostReceiveEvent, "_HudLayoutScreen", HandleMouseUpEvent);

            // TODO: Fix history for other ways of moving elements, e.g. by using the arrow keys? 
            // TODO: Maybe save all newly moved elements every X seconds? 

            // TODO: What should happen when the Hud Layout was closed with unsaved changes? 
        }

        // SETUP START
        private bool callbackAdded = false;


        /// <summary>
        /// Initializes the HUD layout addons and sets the AgentHUDLayout and HudLayoutScreen pointers when the HUD layout is opened. 
        /// </summary>
        /// <returns></returns>
        private unsafe bool InitializeHudLayoutAddons() {
            if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, this)) {
                this.Log.Warning("HudLayoutScreen not ready.");
                return false;
            }

            AgentHudLayout = agentHudLayout;
            HudLayoutScreen = hudLayoutScreen;
            return true;
        }

        private unsafe void ClearHudLayoutAddons() {
            AgentHudLayout = null;
            HudLayoutScreen = null;
        }

        private void addOnUpdateCallback() {
            if (callbackAdded) return;
            this.Framework.Update += HandleKeyboardShortcuts;
            InitializeHudLayoutAddons();
            callbackAdded = true;
        }
        private void removeOnUpdateCallback() {
            this.Framework.Update -= HandleKeyboardShortcuts;
            ClearHudLayoutAddons();
            callbackAdded = false;
        }
        // SETUP END


        private byte CUSTOM_FLAG = 16;
        private HudElementData? mouseDownTarget = null;

        private unsafe void HandleMouseDownEvent(AddonEvent type, AddonArgs args) {
            if (args is not AddonReceiveEventArgs receiveEventArgs) return;
            if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseDown) return;
            if (receiveEventArgs.AtkEvent == nint.Zero) return;

            // Check if the event has the custom flag set, if so, filter it out
            AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;
            if ((atkEvent->Flags & CUSTOM_FLAG) == CUSTOM_FLAG) {
                atkEvent->Flags &= (byte)~CUSTOM_FLAG;
                return;
            }

            // Check if the layout editor is open, abort if not
            if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

            // Get the currently selected element, abort if none is selected
            int selectedNodeId = receiveEventArgs.EventParam;
            if (selectedNodeId < 0 || selectedNodeId >= this.HudLayoutScreen->CollisionNodeListCount) {
                this.Debug.Log(this.Log.Error, $"No valid element selected.");
            }

            AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(this.HudLayoutScreen, selectedNodeId);
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }

            // Temporarily save the current state of the selected element for undo operations
            HudElementData previousState = new HudElementData(selectedNode);
            if (previousState.ResNodeDisplayName != string.Empty && previousState.ResNodeDisplayName != "Unknown") {
                this.Debug.Log(this.Log.Debug, $"Selected Element by Mouse: {previousState}");
                mouseDownTarget = previousState; 
            } else {
                this.Debug.Log(this.Log.Warning, $"Could not get ResNodeDisplayName for selected element.");
                mouseDownTarget = null;
            }
        }

        private unsafe void HandleMouseUpEvent(AddonEvent type, AddonArgs args) {
            if (args is not AddonReceiveEventArgs receiveEventArgs) return;
            if (receiveEventArgs.AtkEventType != (uint)AtkEventType.MouseUp) return;
            if (receiveEventArgs.AtkEvent == nint.Zero) return;

            // Check if the event has the custom flag set, if so, filter it out
            AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;
            if ((atkEvent->Flags & CUSTOM_FLAG) == CUSTOM_FLAG) {
                atkEvent->Flags &= (byte)~CUSTOM_FLAG;
                return;
            }

            // Check if the layout editor is open, abort if not
            if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

            // Get the currently selected element, abort if none is selected
            AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(this.HudLayoutScreen, 0);
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }

            // Check if the mouse target is the same as the selected element 
            if (mouseDownTarget == null) { 
                this.Debug.Log(this.Log.Warning, $"No mouse target found.");
                return;
            }

            // Get the current state of the selected element
            HudElementData newState = new HudElementData(selectedNode);

            // Check if the currently selected element is the same as the last MouseDown target
            if (newState.ResNodeDisplayName != mouseDownTarget.ResNodeDisplayName) {
                this.Debug.Log(this.Log.Warning, $"Mouse target does not match selected element: {newState}");
                return;
            } else {
                this.Debug.Log(this.Log.Debug, $"Mouse target matches selected element.");
            }

            // check if the position has changed, if not, do not add to undo history
            if (mouseDownTarget.PosX == newState.PosX && mouseDownTarget.PosY == newState.PosY) {
                return;
            }

            // Save the current state of the selected element for undo operations
            this.Log.Debug($"User moved element: {mouseDownTarget.PrettyPrint()} -> ({newState.PosX}, {newState.PosY})");
            this.HudHistoryManager.AddUndoAction(Utils.GetCurrentHudLayoutIndex(this), mouseDownTarget, newState);

            mouseDownTarget = null;
        }

        private enum KeyboardAction { None, Copy, Paste, Undo, Redo }

        /// <summary>
        /// Represents the data for a mouse event. (AtkEventData) 
        /// </summary>
        public unsafe struct MouseEventData {
            public short PosX;
            public short PosY;

            public MouseEventData(short posX, short posY) {
                PosX = posX;
                PosY = posY;
            }

            /// <summary>
            /// Converts the mouse event data to an AtkEventData struct. 
            /// By placing the x and y values at the beginning of the byte array and padding the rest with 0. 
            /// </summary>
            public AtkEventData ToAtkEventData() {
                // Create a byte array with the size of the AtkEventData struct
                int size = sizeof(AtkEventData);
                byte[] eventDataBytes = new byte[size];

                // Convert the PosX and PosY values to byte arrays and add an offset of 10 
                byte[] xBytes = BitConverter.GetBytes(PosX + 10);
                byte[] yBytes = BitConverter.GetBytes(PosY + 10);

                // Copy xBytes to index 0 and yBytes to index 2 of the eventDataBytes array
                Array.Copy(xBytes, 0, eventDataBytes, 0, 2);
                Array.Copy(yBytes, 0, eventDataBytes, 2, 2);

                // Create the event data struct from the byte array
                AtkEventData eventData = new AtkEventData();

                // Use a fixed block to pin the byte array in memory and cast it to an AtkEventData pointer
                fixed (byte* p = eventDataBytes) {
                    eventData = *(AtkEventData*)p;
                }
                return eventData;
            }
        }

        private HudElementData? currentlyCopied = null;

        /// <summary>
        /// Handles keyboard shortcuts for copy, paste, undo, and redo actions.
        /// </summary>
        /// <param name="framework">The framework interface.</param>
        private unsafe void HandleKeyboardShortcuts(IFramework framework) {
            // Executes every frame
            if (!ClientState.IsLoggedIn) return;
            if (ClientState is not { LocalPlayer.ClassJob.Id: var classJobId }) return;
            if (ImGui.GetIO().WantCaptureKeyboard) return; // TODO: Necessary? 

            // Get the state of the control key, abort if not pressed 
            KeyStateFlags ctrlKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.CONTROL);
            if (!ctrlKeystate.HasFlag(KeyStateFlags.Down)) return;

            // Set the keyboard action based on the key states
            KeyboardAction keyboardAction = KeyboardAction.None;
            List<(SeVirtualKey, KeyStateFlags, KeyboardAction, SeVirtualKey?) > keybinds = new() {
                (SeVirtualKey.C, KeyStateFlags.Pressed, KeyboardAction.Copy,    null),
                (SeVirtualKey.V, KeyStateFlags.Released, KeyboardAction.Paste,  null),
                (SeVirtualKey.Z, KeyStateFlags.Pressed, KeyboardAction.Redo,    SeVirtualKey.SHIFT), 
                (SeVirtualKey.Z, KeyStateFlags.Pressed, KeyboardAction.Undo,    null),
                (SeVirtualKey.Y, KeyStateFlags.Pressed, KeyboardAction.Redo,    null),
            };
            for (int i = 0; i < keybinds.Count; i++) {
                (SeVirtualKey key, KeyStateFlags state, KeyboardAction action, SeVirtualKey? extraModifier) = keybinds[i];
                KeyStateFlags keyState = UIInputData.Instance()->GetKeyState(key);
                if (extraModifier != null && !UIInputData.Instance()->GetKeyState(extraModifier.Value).HasFlag(KeyStateFlags.Down)) continue;
                if (keyState.HasFlag(state)) {
                    keyboardAction = action;
                    break;
                }
            }

            if (keyboardAction == KeyboardAction.None) return;
            this.Debug.Log(this.Log.Debug, $"KeyboardAction: {keyboardAction}");

            // Abort if a popup is open
            AddonHudLayoutWindow* hudLayoutWindow = (AddonHudLayoutWindow*)GameGui.GetAddonByName("_HudLayoutWindow", 1);
            if (hudLayoutWindow == null) return;
            if (hudLayoutWindow->NumOpenPopups > 0) {
                this.Debug.Log(this.Log.Warning, "Popup open, not executing action.");
                return;
            }

            // Check if the layout editor is open, abort if not
            if (this.AgentHudLayout == null || this.HudLayoutScreen == null) return;

            // Depending on the keyboard action, execute the corresponding operation
            switch (keyboardAction) {
                case KeyboardAction.Copy:
                    HandleCopyAction(this.HudLayoutScreen);
                    break;
                case KeyboardAction.Paste:
                    HandlePasteAction(this.HudLayoutScreen, this.AgentHudLayout);
                    break;
                case KeyboardAction.Undo:
                    HandleUndoAction(this.HudLayoutScreen, this.AgentHudLayout);
                    break;
                case KeyboardAction.Redo:
                    HandleRedoAction(this.HudLayoutScreen, this.AgentHudLayout);
                    break;
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
                this.Log.Debug($"No element selected.");
                return;
            }

            // Create a new HudElementData object with the data of the selected element
            HudElementData selectedNodeData = new HudElementData(selectedNode);
            currentlyCopied = selectedNodeData;

            // Copy the data to the clipboard
            ImGui.SetClipboardText(selectedNodeData.ToString());
            this.Debug.Log(this.Log.Debug, $"Copied to Clipboard: {selectedNodeData}");
            this.Log.Debug($"Copied position to clipboard: {selectedNodeData.PrettyPrint()}");
        }

        /// <summary>
        /// Paste the position from the clipboard to the selected element 
        /// and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandlePasteAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            // Get the currently selected element, abort if none is selected
            AtkResNode* selectedNode = Utils.GetCollisionNodeByIndex(hudLayoutScreen, 0);
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }

            // Get the clipboard text
            string clipboardText = ImGui.GetClipboardText();
            if (clipboardText == null) {
                this.Log.Debug($"Clipboard is empty.");
                return;
            }

            // Parse the clipboard text to a HudElementData object
            HudElementData? parsedData = null;
            try {
                parsedData = JsonSerializer.Deserialize<HudElementData>(clipboardText);
            } catch (Exception e) {
                this.Log.Warning($"Clipboard data could not be parsed: '{clipboardText}'");
                return;
            }
            if (parsedData == null) {
                this.Log.Warning($"Clipboard data could not be parsed. '{clipboardText}'");
                return;
            }
            this.Debug.Log(this.Log.Debug, $"Parsed Clipboard: {parsedData}");

            // Save the current state of the selected element for undo operations
            HudElementData previousState = new HudElementData(selectedNode);


            // Set the position of the currently selected element to the parsed position
            selectedNode->ParentNode->SetPositionShort(parsedData.PosX, parsedData.PosY);

            // Add the previous state and the new state to the undo history
            int hudLayoutIndex = Utils.GetCurrentHudLayoutIndex(this);
            this.HudHistoryManager.AddUndoAction(hudLayoutIndex, previousState, parsedData); 

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(selectedNode, 0, parsedData, hudLayoutScreen, this, this.CUSTOM_FLAG);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"Pasted position to selected element: {previousState.ResNodeDisplayName} ({previousState.PosX}, {previousState.PosY}) -> ({parsedData.PosX}, {parsedData.PosY})");
        }

        /// <summary>
        /// Undo the last operation and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandleUndoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            // Get the last added action from the undo history
            (HudElementData? oldState, HudElementData? newState) = this.HudHistoryManager.PeekUndoAction(Utils.GetCurrentHudLayoutIndex(this));
            if (oldState == null || newState == null) {
                this.Log.Debug($"Nothing to undo.");
                return;
            }

            // TODO: Check if newState is the same as the current state of the element? 

            // Find node with same name as oldState
            (nint undoNodePtr, uint undoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, oldState.ResNodeDisplayName);
            if (undoNodePtr == nint.Zero) {
                this.Log.Warning($"Could not find node with name '{oldState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* undoNode = (AtkResNode*)undoNodePtr;

            HudElementData undoNodeState = new HudElementData(undoNode);

            // Set the position of the currently selected element to the parsed position
            undoNode->ParentNode->SetPositionShort(oldState.PosX, oldState.PosY);

            this.HudHistoryManager.PerformUndo(Utils.GetCurrentHudLayoutIndex(this), undoNodeState);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(undoNode, undoNodeId, oldState, hudLayoutScreen, this, this.CUSTOM_FLAG);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"Undone last operation: Moved '{undoNodeState.ResNodeDisplayName}' from ({undoNodeState.PosX}, {undoNodeState.PosY}) back to ({oldState.PosX}, {oldState.PosY})");
        }

        /// <summary>
        /// Redo the last operation and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandleRedoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            // Get the last added action from the redo history
            (HudElementData? oldState, HudElementData? newState) = this.HudHistoryManager.PeekRedoAction(Utils.GetCurrentHudLayoutIndex(this));  
            if (oldState == null || newState == null) {
                this.Log.Debug($"Nothing to redo.");
                return;
            }
            
            // TODO: Check if oldState is the same as the current state of the element? 

            // Find node with same name as new state
            (nint redoNodePtr, uint redoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, newState.ResNodeDisplayName);
            if (redoNodePtr == nint.Zero) {
                this.Log.Warning($"Could not find node with name '{newState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* redoNode = (AtkResNode*)redoNodePtr;
            HudElementData redoNodeState = new HudElementData(redoNode);

            // Set the position of the currently selected element to the parsed position
            redoNode->ParentNode->SetPositionShort(newState.PosX, newState.PosY);

            this.HudHistoryManager.PerformRedo(Utils.GetCurrentHudLayoutIndex(this), redoNodeState);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(redoNode, redoNodeId, newState, hudLayoutScreen, this, this.CUSTOM_FLAG);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"Redone last operation: Moved '{redoNodeState.ResNodeDisplayName}' again from ({redoNodeState.PosX}, {redoNodeState.PosY}) to ({newState.PosX}, {newState.PosY})");
        }

        public void Dispose() {
            this.removeOnUpdateCallback();
            this.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "_HudLayoutScreen");
            this.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen");
            this.Debug.Dispose();

            WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();
            CommandManager.RemoveHandler(CommandName);
        }

        private void OnCommand(string command, string args) {
            ToggleConfigUI();
        }

        private void DrawUI() => WindowSystem.Draw();

        public void ToggleConfigUI() => ConfigWindow.Toggle();
    }
}
