using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HudCopyPaste {
    public sealed class Plugin : IDalamudPlugin {
        public bool DEBUG = true;
        public string Name => "HudCopyPaste";
        private const string CommandName = "/hudcp";

        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

        public readonly WindowSystem WindowSystem = new("HudCopyPaste");
        private MainWindow MainWindow { get; init; }

        public IGameGui GameGui { get; init; }
        public IClientState ClientState { get; init; }
        public IPluginLog Log { get; init; }
        public IAddonEventManager AddonEventManager { get; init; }
        public IAddonLifecycle AddonLifecycle { get; init; }
        public IFramework Framework { get; init; }
        public IChatGui ChatGui { get; init; } = null!;
        public Debug Debug { get; private set; } = null!;

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
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(MainWindow);
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

            if(this.GameGui.GetAddonByName("_HudLayoutScreen", 1) != IntPtr.Zero) {
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

            // TODO: add custom debug UI that shows the undo/redo history 
            // TODO: turn elements of histories into tuples of before and after states
            // TODO: maybe find a better name for the Plugin, as its functionality is not only copy/paste anymore 
        }

        // TODO: handle mouse events
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
            if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, this)) return;

            // Get the currently selected element, abort if none is selected
            // atkEvent->Param == selectedNodeId in list 
            int selectedNodeId = receiveEventArgs.EventParam;
            if (selectedNodeId < 0 || selectedNodeId >= hudLayoutScreen->CollisionNodeListCount) {
                this.Debug.Log(this.Log.Error, $"No valid element selected.");
            }

            AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[selectedNodeId];
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

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
            if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, this)) return;

            // Get the currently selected element, abort if none is selected
            AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[0];
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

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
            AddToUndoHistory(mouseDownTarget);

        }

        // END TODO


        private bool callbackAdded = false;
        private void addOnUpdateCallback() {
            if (callbackAdded) return;
            this.Framework.Update += HandleKeyboardShortcuts;
            callbackAdded = true;
        }
        private void removeOnUpdateCallback() {
            this.Framework.Update -= HandleKeyboardShortcuts;
            callbackAdded = false;
        }

        /// <summary>
        /// Represents data for a HUD element.
        /// </summary>
        internal class HudElementData {
            public int ElementId { get; set; } = -1;
            public string ResNodeDisplayName { get; set; } = string.Empty;
            public string AddonName { get; set; } = string.Empty;
            public short PosX { get; set; } = 0;
            public short PosY { get; set; } = 0;
            public float Scale { get; set; } = 1.0f;

            public override string ToString() => JsonSerializer.Serialize(this);
            public string PrettyPrint() => $"{ResNodeDisplayName} ({PosX}, {PosY})";

            public HudElementData() { }

            /// <summary>
            /// Initializes a new instance of the <see cref="HudElementData"/> class from an AtkResNode.
            /// </summary>
            /// <param name="resNode">The AtkResNode pointer.</param>
            internal unsafe HudElementData(AtkResNode* resNode) {
                if (resNode->ParentNode == null) return;
                try {
                    ResNodeDisplayName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText.ToString();
                } catch (NullReferenceException) {
                    ResNodeDisplayName = "Unknown";
                }
                PosX = resNode->ParentNode->GetXShort();
                PosY = resNode->ParentNode->GetYShort();

                // TODO: Maybe get corresponding addon?
                ElementId = -1;
                AddonName = "";
                Scale = -1;
            }
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
        // TODO: Or add a history per element? 
        private List<HudElementData> undoHistory = new();
        private List<HudElementData> redoHistory = new();

        // TODO: Expose as variable in settings
        private int maxHistorySize = 50;
        private void AddToUndoHistory(HudElementData data, bool clearRedo = true) {
            undoHistory.Add(data);

            if (undoHistory.Count > maxHistorySize) {
                undoHistory.RemoveAt(0);
            }

            if (clearRedo) RemoveElementFromRedoHistory(data);
        }
        // TODO: Or completely clear redo history when a new move is made?
        private void RemoveElementFromRedoHistory(HudElementData data) {
            redoHistory.RemoveAll(x => x.ResNodeDisplayName == data.ResNodeDisplayName);
        }


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
            if (!Utils.IsHudLayoutReady(out AgentHUDLayout* agentHudLayout, out AddonHudLayoutScreen* hudLayoutScreen, this)) return;

            // Depending on the keyboard action, execute the corresponding operation
            switch (keyboardAction) {
                case KeyboardAction.Copy:
                    HandleCopyAction(hudLayoutScreen);
                    break;
                case KeyboardAction.Paste:
                    HandlePasteAction(hudLayoutScreen, agentHudLayout);
                    break;
                case KeyboardAction.Undo:
                    HandleUndoAction(hudLayoutScreen, agentHudLayout);
                    break;
                case KeyboardAction.Redo:
                    HandleRedoAction(hudLayoutScreen, agentHudLayout);
                    break;
            }
        }

        /// <summary>
        /// Copy the position of the selected element to the clipboard. 
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        private unsafe void HandleCopyAction(AddonHudLayoutScreen* hudLayoutScreen) {
            // Get the currently selected element, abort if none is selected
            AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[0];
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

            // Create a new HudElementData object with the data of the selected element
            var selectedNodeData = new HudElementData(selectedNode);
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
            AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[0];
            if (selectedNode == null) {
                this.Log.Debug($"No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

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
            this.AddToUndoHistory(previousState);

            // Set the position of the currently selected element to the parsed position
            selectedNode->ParentNode->SetPositionShort(parsedData.PosX, parsedData.PosY);

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
            if (undoHistory.Count == 0) {
                this.Log.Debug($"Nothing to undo.");
                return;
            }

            HudElementData lastState = undoHistory[undoHistory.Count - 1];
            undoHistory.RemoveAt(undoHistory.Count - 1);

            // Find node with same name as last state
            (nint lastNodePtr, uint lastNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, lastState.ResNodeDisplayName);
            if (lastNodePtr == nint.Zero) {
                this.Log.Warning($"Could not find node with name '{lastState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* lastNode = (AtkResNode*)lastNodePtr;

            HudElementData redoState = new HudElementData(lastNode);
            redoHistory.Add(redoState);

            // Set the position of the currently selected element to the parsed position
            lastNode->ParentNode->SetPositionShort(lastState.PosX, lastState.PosY);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(lastNode, lastNodeId, lastState, hudLayoutScreen, this, this.CUSTOM_FLAG);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"Undone last operation: Moved '{redoState.ResNodeDisplayName}' from ({redoState.PosX}, {redoState.PosY}) back to ({lastState.PosX}, {lastState.PosY})");
        }

        /// <summary>
        /// Redo the last operation and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandleRedoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            if (redoHistory.Count == 0) {
                this.Log.Debug($"Nothing to redo.");
                return;
            }

            HudElementData redoState = redoHistory[redoHistory.Count - 1];
            redoHistory.RemoveAt(redoHistory.Count - 1);

            // Find node with same name as last state
            (nint redoNodePtr, uint redoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, redoState.ResNodeDisplayName);
            if (redoNodePtr == nint.Zero) {
                this.Log.Warning($"Could not find node with name '{redoState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* redoNode = (AtkResNode*)redoNodePtr;
            HudElementData undoState = new HudElementData(redoNode);
            this.AddToUndoHistory(undoState, false);

            // Set the position of the currently selected element to the parsed position
            redoNode->ParentNode->SetPositionShort(redoState.PosX, redoState.PosY);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(redoNode, redoNodeId, redoState, hudLayoutScreen, this, this.CUSTOM_FLAG);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"Redone last operation: Moved '{redoState.ResNodeDisplayName}' again from ({undoState.PosX}, {undoState.PosY}) to ({redoState.PosX}, {redoState.PosY})");
        }

        public void Dispose() {
            this.removeOnUpdateCallback();
            this.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "_HudLayoutScreen");
            this.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen");
            this.Debug.Dispose();

            WindowSystem.RemoveAllWindows();
            MainWindow.Dispose();
        }

        private void DrawUI() => WindowSystem.Draw();

        public void ToggleMainUI() => MainWindow.Toggle();
    }
}
