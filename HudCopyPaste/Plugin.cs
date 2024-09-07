using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HudCopyPaste;

public sealed class Plugin : IDalamudPlugin
{
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

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;

        this.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "_HudLayoutScreen", (type, args) => {
            this.Log.Debug("HudLayoutScreen setup.");
            this.Framework.Update += HandleKeyboardShortcuts;
        });

        this.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen", (type, args) => {
            this.Log.Debug("HudLayoutScreen finalize.");
            this.Framework.Update -= HandleKeyboardShortcuts;
        });
    }

    /// <summary>
    /// Represents data for a HUD element.
    /// </summary>
    private class HudElementData
    {
        public int elementId { get; set; }
        public string resNodeDisplayName { get; set; }
        public string addonName { get; set; }
        public short posX { get; set; }
        public short posY { get; set; }
        public float scale { get; set; }
        public override string ToString() {
            return JsonSerializer.Serialize(this);
        }

        public HudElementData() {
            this.elementId = -1;
            this.resNodeDisplayName = string.Empty;
            this.addonName = string.Empty;
            this.posX = 0;
            this.posY = 0;
            this.scale = 1.0f;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HudElementData"/> class from an AtkResNode.
        /// </summary>
        /// <param name="resNode">The AtkResNode pointer.</param>
        public unsafe HudElementData(AtkResNode* resNode) {
            try {
                this.resNodeDisplayName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText.ToString();
            }
            catch (NullReferenceException) {
                this.resNodeDisplayName = "Unknown";
            }
            this.posX = resNode->ParentNode->GetXShort();
            this.posY = resNode->ParentNode->GetYShort();

            // TODO : Maybe get corresponding addon?
            this.elementId = -1;
            this.addonName = "";
            this.scale = -1;
        }
    }

    private List<HudElementData> undoHistory = new List<HudElementData>();
    private List<HudElementData> redoHistory = new List<HudElementData>();

    /// <summary>
    /// Pretty prints a list of <see cref="HudElementData"/> to the debug log.
    /// </summary>
    /// <param name="list">The list of HUD element data.</param>
    /// <param name="title">The title for the log entry.</param>
    private void PrettyPrintList(List<HudElementData> list, string title) {
        this.Log.Debug($"'{title}' count: {list.Count}");
        foreach (var element in list) {
            this.Log.Debug($"\t{element.resNodeDisplayName} ({element.posX}, {element.posY})");
        }
    }

    private HudElementData? currentlyCopied = null;

    private enum KeyboardAction
    {
        None,
        Copy,
        Paste,
        Undo,
        Redo
    }

    /// <summary>
    /// Simulates a mouse click on a HUD element.
    /// </summary>
    /// <param name="resNode">The AtkResNode pointer.</param>
    /// <param name="resNodeID">The resource node ID.</param>
    /// <param name="hudElementData">The HUD element data.</param>
    /// <param name="hudLayoutScreen">The HUD layout screen pointer.</param>
    private unsafe void SimulateMouseClickOnHudElement(AtkResNode* resNode, uint resNodeID, HudElementData hudElementData, AddonHudLayoutScreen* hudLayoutScreen) {
        if (resNode == null) {
            this.Log.Warning("ResNode is null");
            return;
        }
        // Create the event data by converting the parsed x and y values as mouse position to a byte array
        int size = sizeof(AtkEventData);
        byte[] eventDataBytes = new byte[size];
        byte[] xBytes = BitConverter.GetBytes(hudElementData.posX + 10);
        byte[] yBytes = BitConverter.GetBytes(hudElementData.posY + 10);
        Array.Copy(xBytes, 0, eventDataBytes, 0, 2);
        Array.Copy(yBytes, 0, eventDataBytes, 2, 2);

        // Create the event data struct from the byte array
        AtkEventData eventData = new AtkEventData();
        fixed (byte* p = eventDataBytes) {
            eventData = *(AtkEventData*) p;
        }

        // ----> MouseDown Event
        // Create the mouse down event from the selected node's event
        AtkEvent mouseDownEvent = *resNode->AtkEventManager.Event;
        mouseDownEvent.Type = AtkEventType.MouseDown;
        mouseDownEvent.Flags = 4;
        mouseDownEvent.Param = resNodeID;

        //printAtkEventData(&eventData);
        // Set the event data with the x and y position of the mouse click
        AtkEventData* mouseDownEventData = &eventData;

        // Call the mouse down event on the HUD layout
        this.Log.Debug("Calling MouseDown event");
        hudLayoutScreen->ReceiveEvent(AtkEventType.MouseDown, (int) mouseDownEvent.Param, &mouseDownEvent, mouseDownEventData);

        // ----> MouseUp Event
        // Create the mouse up event from the selected node's event
        uint atkStagePtr = (uint) AtkStage.Instance();
        AtkEvent mouseUpEvent = *resNode->AtkEventManager.Event;
        mouseUpEvent.Type = AtkEventType.MouseUp;
        mouseUpEvent.Flags = 4;
        mouseUpEvent.Param = 99;
        mouseUpEvent.NextEvent = null;
        mouseUpEvent.Target = (AtkEventTarget*) atkStagePtr;
        mouseUpEvent.Unk29 = 0;

        //printAtkEventData(&eventData);
        //printAtkEvent(&mouseUpEvent);

        // Set the event data with the x and y position of the mouse click
        this.Log.Debug("Calling MouseUp event");
        AtkEventData* mouseUpEventData = &eventData;

        // Call the mouse up event on the HUD layout
        hudLayoutScreen->ReceiveEvent(AtkEventType.MouseUp, 99, &mouseUpEvent, mouseUpEventData);

        // ----> Reset mouse cursor to default image (Arrow) 
        AddonEventManager.ResetCursor();
    }

    /// <summary>
    /// Sends a change event to the HUD layout.
    /// </summary>
    /// <param name="agentHudLayout">The agent HUD layout pointer.</param>
    private unsafe void SendChangeEvent(AgentHUDLayout* agentHudLayout) {
        AtkValue* result = stackalloc AtkValue[1];
        AtkValue* command = stackalloc AtkValue[2];
        command[0].SetInt(22);
        command[1].SetInt(0);
        agentHudLayout->ReceiveEvent(result, command, 1, 0);
    }

    /// <summary>
    /// Finds a HUD resource node by name.
    /// </summary>
    /// <param name="hudLayoutScreen">The HUD layout screen pointer.</param>
    /// <param name="searchName">The name to search for.</param>
    /// <returns>A tuple containing the node pointer and its ID.</returns>
    private unsafe (nint, uint) FindHudResnodeByName(AddonHudLayoutScreen* hudLayoutScreen, string searchName) {
        AtkResNode** resNodes = hudLayoutScreen->CollisionNodeList;
        uint resNodeCount = hudLayoutScreen->CollisionNodeListCount;
        for (int i = 0; i < resNodeCount; i++) {
            AtkResNode* resNode = resNodes[i];
            if (resNode == null) continue;
            try {
                Utf8String resNodeName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText;
                if (resNodeName.ToString() == searchName) {
                    return ((nint) resNode, (uint) i);
                }
            }
            catch (NullReferenceException) {
                continue;
            }
        }
        return (nint.Zero, 0);
    }

    /// <summary>
    /// Handles keyboard shortcuts for copy, paste, undo, and redo actions.
    /// </summary>
    /// <param name="framework">The framework interface.</param>
    private unsafe void HandleKeyboardShortcuts(IFramework framework) {
        // Executes every frame
        if (!ClientState.IsLoggedIn) return;
        if (ClientState is not { LocalPlayer.ClassJob.Id: var classJobId }) return;

        // Get the state of the control key
        KeyStateFlags ctrlKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.CONTROL);

        // Set the keyboard action based on the key states
        KeyboardAction keyboardAction = KeyboardAction.None;
        if (ctrlKeystate.HasFlag(KeyStateFlags.Down)) {
            // Get the state of the C, V, Z and Y keys
            KeyStateFlags cKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.C);
            KeyStateFlags vKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.V);
            KeyStateFlags zKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.Z);
            KeyStateFlags yKeystate = UIInputData.Instance()->GetKeyState(SeVirtualKey.Y);
            if (cKeystate.HasFlag(KeyStateFlags.Pressed)) keyboardAction = KeyboardAction.Copy;
            if (vKeystate.HasFlag(KeyStateFlags.Released)) keyboardAction = KeyboardAction.Paste;
            if (zKeystate.HasFlag(KeyStateFlags.Pressed)) keyboardAction = KeyboardAction.Undo;
            if (yKeystate.HasFlag(KeyStateFlags.Pressed)) keyboardAction = KeyboardAction.Redo;
        } else {
            return;
        }

        if (keyboardAction == KeyboardAction.None) return;
        this.Log.Debug($"KeyboardAction: {keyboardAction}");

        // Check for open popups
        AddonHudLayoutWindow* hudLayoutWindow = (AddonHudLayoutWindow*) GameGui.GetAddonByName("_HudLayoutWindow", 1);
        if (hudLayoutWindow == null) return;
        if (hudLayoutWindow->NumOpenPopups > 0) {
            this.Log.Debug("Popup open, not executing action.");
            return;
        }

        // Get the HudLayout agent, abort if not found
        AgentHUDLayout* agentHudLayout = (AgentHUDLayout*) GameGui.FindAgentInterface("HudLayout");
        if (agentHudLayout == null) return;

        // Get the HudLayoutScreen, abort if not found
        nint addonHudLayoutScreenPtr = GameGui.GetAddonByName("_HudLayoutScreen", 1);
        if (addonHudLayoutScreenPtr == nint.Zero) return;
        AddonHudLayoutScreen* hudLayoutScreen = (AddonHudLayoutScreen*) addonHudLayoutScreenPtr;

        // Get the currently selected element, abort if none is selected
        AtkResNode* selectedNode = hudLayoutScreen->CollisionNodeList[0];
        if (selectedNode == null) {
            this.Log.Debug("No element selected.");
            return;
        }
        if (selectedNode->ParentNode == null) return;

        // Depending on the keyboard action, execute the corresponding operation
        switch (keyboardAction) {
            /*
             * Copy the position of the selected element to the clipboard
             */
            case KeyboardAction.Copy:
                this.Log.Debug("======= COPY =======");
                // Create a new HudElementData object with the data of the selected element
                var selectedNodeData = new HudElementData(selectedNode);

                currentlyCopied = selectedNodeData;

                // Copy the data to the clipboard
                ImGui.SetClipboardText(selectedNodeData.ToString());
                this.Log.Debug($"Copied to Clipboard: {selectedNodeData}");

                // Print a chat message 
                Dalamud.Game.Text.XivChatEntry chatEntry = new Dalamud.Game.Text.XivChatEntry();
                chatEntry.Message = "Copied position to clipboard: " + selectedNodeData.resNodeDisplayName;
                //this.ChatGui.Print(chatEntry);

                PrettyPrintList(undoHistory, "Undo");
                PrettyPrintList(redoHistory, "Redo");
                break;
            /*
             * Paste the position of the selected element from the clipboard to the selected element
             * And simulate a mouse click event on the element
             */
            case KeyboardAction.Paste:
                this.Log.Debug("======= PASTE =======");
                // Get the clipboard text
                string clipboardText = ImGui.GetClipboardText();
                if (clipboardText == null) {
                    this.Log.Warning("Clipboard is empty.");
                    return;
                }

                // Parse the clipboard text to a HudElementData object
                HudElementData? parsedData = null;
                try {
                    parsedData = JsonSerializer.Deserialize<HudElementData>(clipboardText);
                }
                catch {
                    this.Log.Warning($"Clipboard data could not be parsed: '{clipboardText}'");
                    return;
                }
                if (parsedData == null) {
                    this.Log.Warning($"Clipboard data could not be parsed. '{clipboardText}'");
                    return;
                }
                this.Log.Debug($"Parsed Clipboard: {parsedData}");

                // Save the current state of the selected element for undo operations
                try {
                    HudElementData previousState = new HudElementData(selectedNode);
                    undoHistory.Add(previousState);
                    if (undoHistory.Count > 50) {
                        undoHistory.RemoveAt(0);
                    }
                }
                catch {
                    this.Log.Error("Could not save state of selected element for undo operation.");
                }

                // Set the position of the currently selected element to the parsed position
                selectedNode->ParentNode->SetPositionShort(parsedData.posX, parsedData.posY);

                // ======= Simulate Mouse Click =======
                SimulateMouseClickOnHudElement(selectedNode, 0, parsedData, hudLayoutScreen);

                // ======= Send Event to HudLayout to inform about a change ======= 
                SendChangeEvent(agentHudLayout);

                // Print a chat message
                Dalamud.Game.Text.XivChatEntry chatPasteEntry = new Dalamud.Game.Text.XivChatEntry();
                chatPasteEntry.Message = "Pasted position from clipboard to: " + parsedData.resNodeDisplayName;
                //this.ChatGui.Print(chatPasteEntry);

                PrettyPrintList(undoHistory, "Undo");
                PrettyPrintList(redoHistory, "Redo");
                // maybe todo: Set the position of the element with the same name as the copied element
                break;
            /*
             * Undo the last operation
             */
            case KeyboardAction.Undo:
                this.Log.Debug("======= UNDO =======");
                this.Log.Debug("Before Undo: ");
                PrettyPrintList(undoHistory, "Undo");
                PrettyPrintList(redoHistory, "Redo");
                if (undoHistory.Count > 0) {
                    HudElementData lastState = undoHistory[undoHistory.Count - 1];
                    undoHistory.RemoveAt(undoHistory.Count - 1);
                    this.Log.Debug($"Undoing last operation: {lastState}");

                    // Find node with same name as last state
                    (nint lastNodePtr, uint lastNodeId) = FindHudResnodeByName(hudLayoutScreen, lastState.resNodeDisplayName);
                    if (lastNodePtr == nint.Zero) {
                        this.Log.Warning($"Could not find node with name '{lastState.resNodeDisplayName}'");
                        return;
                    }
                    AtkResNode* lastNode = (AtkResNode*) lastNodePtr;

                    HudElementData redoState = new HudElementData(lastNode);
                    redoHistory.Add(redoState);

                    // Set the position of the currently selected element to the parsed position
                    lastNode->ParentNode->SetPositionShort(lastState.posX, lastState.posY);

                    // ======= Simulate Mouse Click =======
                    SimulateMouseClickOnHudElement(lastNode, lastNodeId, lastState, hudLayoutScreen);

                    // ======= Send Event to HudLayout to inform about a change ======= 
                    SendChangeEvent(agentHudLayout);

                    this.Log.Debug("After Undo: ");
                    PrettyPrintList(undoHistory, "Undo");
                    PrettyPrintList(redoHistory, "Redo");
                } else {
                    this.Log.Debug("No undo history available.");
                }
                break;
            /*
             * Redo the last operation
             */
            case KeyboardAction.Redo:
                this.Log.Debug("======= REDO =======");
                this.Log.Debug("Before Redo: ");
                PrettyPrintList(undoHistory, "Undo");
                PrettyPrintList(redoHistory, "Redo");
                if (redoHistory.Count > 0) {
                    HudElementData redoState = redoHistory[redoHistory.Count - 1];
                    redoHistory.RemoveAt(redoHistory.Count - 1);
                    this.Log.Debug($"Redoing last operation: {redoState}");

                    // Find node with same name as last state
                    (nint redoNodePtr, uint redoNodeId) = FindHudResnodeByName(hudLayoutScreen, redoState.resNodeDisplayName);
                    if (redoNodePtr == nint.Zero) {
                        this.Log.Warning($"Could not find node with name '{redoState.resNodeDisplayName}'");
                        return;
                    }
                    AtkResNode* redoNode = (AtkResNode*) redoNodePtr;
                    HudElementData undoState = new HudElementData(redoNode);
                    undoHistory.Add(undoState);

                    // Set the position of the currently selected element to the parsed position
                    redoNode->ParentNode->SetPositionShort(redoState.posX, redoState.posY);

                    // ======= Simulate Mouse Click =======
                    SimulateMouseClickOnHudElement(redoNode, redoNodeId, redoState, hudLayoutScreen);

                    // ======= Send Event to HudLayout to inform about a change ======= 
                    SendChangeEvent(agentHudLayout);

                    this.Log.Debug("After Redo: ");
                    PrettyPrintList(undoHistory, "Undo");
                    PrettyPrintList(redoHistory, "Redo");
                } else {
                    this.Log.Debug("No redo history available.");
                }
                break;
        }
    }

    public void Dispose() {
        this.Framework.Update -= HandleKeyboardShortcuts;
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, "_HudLayoutScreen");
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_HudLayoutScreen");
        
        this.Debug.Dispose();

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();
}
