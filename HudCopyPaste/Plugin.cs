using Dalamud.Game.Addon.Lifecycle;
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

            this.Debug = new Debug(this, true);
            MainWindow = new MainWindow(this);
            WindowSystem.AddWindow(MainWindow);
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

            this.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_HudLayoutScreen", (type, args) => {
                this.Debug.Log(this.Log.Debug, "HudLayoutScreen setup.");
                this.Framework.Update += HandleKeyboardShortcuts;
            });

            this.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen", (type, args) => {
                this.Debug.Log(this.Log.Debug, "HudLayoutScreen finalize.");
                this.Framework.Update -= HandleKeyboardShortcuts;
            });
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

            /// <summary>
            /// Initializes a new instance of the <see cref="HudElementData"/> class from an AtkResNode.
            /// </summary>
            /// <param name="resNode">The AtkResNode pointer.</param>
            public unsafe HudElementData(AtkResNode* resNode) {
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

        private HudElementData? currentlyCopied = null;

        private enum KeyboardAction {
            None,
            Copy,
            Paste,
            Undo,
            Redo
        }

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

        private List<HudElementData> undoHistory = new();
        private List<HudElementData> redoHistory = new();

        /// <summary>
        /// Handles keyboard shortcuts for copy, paste, undo, and redo actions.
        /// </summary>
        /// <param name="framework">The framework interface.</param>
        private unsafe void HandleKeyboardShortcuts(IFramework framework) {
            // Executes every frame
            if (!ClientState.IsLoggedIn) return;
            if (ClientState is not { LocalPlayer.ClassJob.Id: var classJobId }) return;

            // Get the state of the control key, abort if not pressed 
            KeyStateFlags ctrlKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.CONTROL);
            if (!ctrlKeystate.HasFlag(KeyStateFlags.Down)) return;

            // Set the keyboard action based on the key states
            KeyboardAction keyboardAction = KeyboardAction.None;
            List<(SeVirtualKey, KeyStateFlags, KeyboardAction)> keybinds = new() {
                (SeVirtualKey.C, KeyStateFlags.Pressed, KeyboardAction.Copy),
                (SeVirtualKey.V, KeyStateFlags.Released, KeyboardAction.Paste),
                (SeVirtualKey.Z, KeyStateFlags.Pressed, KeyboardAction.Undo),
                (SeVirtualKey.Y, KeyStateFlags.Pressed, KeyboardAction.Redo)
            };
            for (int i = 0; i < keybinds.Count; i++) {
                (SeVirtualKey key, KeyStateFlags state, KeyboardAction action) = keybinds[i];
                KeyStateFlags keyState = UIInputData.Instance()->GetKeyState(key);
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

            // Get the HudLayout agent, abort if not found
            AgentHUDLayout* agentHudLayout = (AgentHUDLayout*)GameGui.FindAgentInterface("HudLayout");
            if (agentHudLayout == null) return;

            // Get the HudLayoutScreen, abort if not found
            nint addonHudLayoutScreenPtr = GameGui.GetAddonByName("_HudLayoutScreen", 1);
            if (addonHudLayoutScreenPtr == nint.Zero) return;
            AddonHudLayoutScreen* hudLayoutScreen = (AddonHudLayoutScreen*)addonHudLayoutScreenPtr;

            // Get the currently selected element, abort if none is selected
            AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[0];
            if (selectedNode == null) {
                this.Log.Debug($"[{this.Name}] No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

            // Depending on the keyboard action, execute the corresponding operation
            // TODO: What should happen if a node in the undo/redo list is moved normally? 
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
                this.Log.Debug($"[{this.Name}] No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

            // Create a new HudElementData object with the data of the selected element
            var selectedNodeData = new HudElementData(selectedNode);
            currentlyCopied = selectedNodeData;

            // Copy the data to the clipboard
            ImGui.SetClipboardText(selectedNodeData.ToString());
            this.Debug.Log(this.Log.Debug, $"Copied to Clipboard: {selectedNodeData}");
            this.Log.Debug($"[{this.Name}] Copied position to clipboard: {selectedNodeData.PrettyPrint()}");
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
                this.Log.Debug($"[{this.Name}] No element selected.");
                return;
            }
            if (selectedNode->ParentNode == null) return;

            // Get the clipboard text
            string clipboardText = ImGui.GetClipboardText();
            if (clipboardText == null) {
                this.Log.Info($"[{this.Name}] Clipboard is empty.");
                return;
            }

            // Parse the clipboard text to a HudElementData object
            HudElementData? parsedData = null;
            try {
                parsedData = JsonSerializer.Deserialize<HudElementData>(clipboardText);
            } catch {
                this.Log.Warning($"[{this.Name}] Clipboard data could not be parsed: '{clipboardText}'");
                return;
            }
            if (parsedData == null) {
                this.Log.Warning($"[{this.Name}] Clipboard data could not be parsed. '{clipboardText}'");
                return;
            }
            this.Debug.Log(this.Log.Debug, $"Parsed Clipboard: {parsedData}");

            // Save the current state of the selected element for undo operations
            HudElementData previousState = new HudElementData(selectedNode);
            undoHistory.Add(previousState);
            if (undoHistory.Count > 50) {
                undoHistory.RemoveAt(0);
            }

            // Set the position of the currently selected element to the parsed position
            selectedNode->ParentNode->SetPositionShort(parsedData.PosX, parsedData.PosY);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(selectedNode, 0, parsedData, hudLayoutScreen, this);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"[{this.Name}] Pasted position of '{parsedData.ResNodeDisplayName}' to selected element: {parsedData.ResNodeDisplayName} ({previousState.PosX}, {previousState.PosY}) -> ({parsedData.PosX}, {parsedData.PosY})");
        }

        /// <summary>
        /// Undo the last operation and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandleUndoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            if (undoHistory.Count == 0) {
                this.Log.Debug($"[{this.Name}] Nothing to undo.");
                return;
            }

            HudElementData lastState = undoHistory[undoHistory.Count - 1];
            undoHistory.RemoveAt(undoHistory.Count - 1);

            // Find node with same name as last state
            (nint lastNodePtr, uint lastNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, lastState.ResNodeDisplayName);
            if (lastNodePtr == nint.Zero) {
                this.Log.Warning($"[{this.Name}] Could not find node with name '{lastState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* lastNode = (AtkResNode*)lastNodePtr;

            HudElementData redoState = new HudElementData(lastNode);
            redoHistory.Add(redoState);

            // Set the position of the currently selected element to the parsed position
            lastNode->ParentNode->SetPositionShort(lastState.PosX, lastState.PosY);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(lastNode, lastNodeId, lastState, hudLayoutScreen, this);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"[{this.Name}] Undone last operation: Moved '{redoState.ResNodeDisplayName}' from ({redoState.PosX}, {redoState.PosY}) back to ({lastState.PosX}, {lastState.PosY})");
        }

        /// <summary>
        /// Redo the last operation and simulate a mouse click on the element.
        /// </summary>
        /// <param name="hudLayoutScreen"></param>
        /// <param name="agentHudLayout"></param>
        private unsafe void HandleRedoAction(AddonHudLayoutScreen* hudLayoutScreen, AgentHUDLayout* agentHudLayout) {
            if (redoHistory.Count == 0) {
                this.Log.Debug($"[{this.Name}] Nothing to redo.");
                return;
            }

            HudElementData redoState = redoHistory[redoHistory.Count - 1];
            redoHistory.RemoveAt(redoHistory.Count - 1);

            // Find node with same name as last state
            (nint redoNodePtr, uint redoNodeId) = Utils.FindHudResnodeByName(hudLayoutScreen, redoState.ResNodeDisplayName);
            if (redoNodePtr == nint.Zero) {
                this.Log.Warning($"[{this.Name}] Could not find node with name '{redoState.ResNodeDisplayName}'");
                return;
            }
            AtkResNode* redoNode = (AtkResNode*)redoNodePtr;
            HudElementData undoState = new HudElementData(redoNode);
            undoHistory.Add(undoState);

            // Set the position of the currently selected element to the parsed position
            redoNode->ParentNode->SetPositionShort(redoState.PosX, redoState.PosY);

            // Simulate Mouse Click
            Utils.SimulateMouseClickOnHudElement(redoNode, redoNodeId, redoState, hudLayoutScreen, this);

            // Send Event to HudLayout to inform about a change 
            Utils.SendChangeEvent(agentHudLayout);

            this.Log.Debug($"[{this.Name}] Redone last operation: Moved '{redoState.ResNodeDisplayName}' again from ({undoState.PosX}, {undoState.PosY}) to ({redoState.PosX}, {redoState.PosY})");
        }

        public void Dispose() {
            this.Framework.Update -= HandleKeyboardShortcuts;
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
