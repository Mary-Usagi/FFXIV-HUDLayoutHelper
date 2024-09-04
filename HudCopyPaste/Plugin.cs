using Dalamud.Game;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using HudCopyPaste.Windows;
using System;
using System.Collections.Generic;
using System.IO;
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

        // you might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(MainWindow);

        //CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) {
        //    HelpMessage = "A useful message to display in /xlhelp"
        //});

        PluginInterface.UiBuilder.Draw += DrawUI;

        // Adds another button that is doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;


        this.Framework.Update += OnUpdate;

        // For debugging purposes
        this.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", (type, args) => {
            unsafe {
                if (args is not AddonReceiveEventArgs receiveEventArgs) return;
                var handledTypeList = new List<AtkEventType> {
                    //AtkEventType.MouseMove,
                    //AtkEventType.MouseOut,
                    //AtkEventType.MouseOver,
                    //AtkEventType.MouseDown,
                    //AtkEventType.MouseUp
                };
                if (!handledTypeList.Contains((AtkEventType) receiveEventArgs.AtkEventType)) return;

                // FINDINGS:
                // - MouseDown AtkEvent:
                //   - Param: current index in collisionNodeList
                //   - eventid: 0
                //   - NextEvent: selected resnode (collisionnode) -> atkeventmanager->atkevent->nextevent
                //   - listener: selected resnode (collisionnode) -> atkeventmanager->atkevent->listener
                //   - target: Pointer to selected resnode (collisionnode) == CollisionNodeList[Param]
                // - MouseUp AtkEvent:
                //   - eventid: 99
                //   - Target: AtkStage.Instance()

                this.Log.Debug("=====================================");
                this.Log.Debug("AtkEventType: " + (AtkEventType) receiveEventArgs.AtkEventType);

                this.Log.Debug("AddonArgsType: " + receiveEventArgs.Type);
                this.Log.Debug($"AtkEvent nint: {receiveEventArgs.AtkEvent:X}");
                if (receiveEventArgs.AtkEvent != nint.Zero) {
                    AtkEvent* atkEvent = (AtkEvent*) receiveEventArgs.AtkEvent;
                    this.Log.Debug("---------- AtkEvent ----------");
                    this.Log.Debug($"AtkEvent: {atkEvent->ToString()}");
                    PrintAtkEvent(atkEvent);
                    this.Log.Debug("---------- AtkEvent End ----------");
                }
                this.Log.Debug("AddonName: " + receiveEventArgs.AddonName);
                this.Log.Debug("EventId int: " + receiveEventArgs.EventParam);
                this.Log.Debug($"Data Ptr: {receiveEventArgs.Data:X}");
                if (receiveEventArgs.Data != nint.Zero) {
                    AtkEventData* eventData = (AtkEventData*) receiveEventArgs.Data;
                    this.Log.Debug($"EventData: {eventData->ToString()}");
                    this.Log.Debug($"ListItemData: {eventData->ListItemData}");
                    this.Log.Debug($"SelectedIndex: {eventData->ListItemData.SelectedIndex}");

                    byte* bytePtr = (byte*) receiveEventArgs.Data;
                    int structSize = sizeof(AtkEventData);

                    // ==> First 2 bytes: Mouse X, Second 2 bytes: Mouse Y
                    PrintAtkEventData(eventData);

                    if (eventData->ListItemData.ListItemRenderer != null) {
                    }

                    bytePtr = (byte*) receiveEventArgs.Data;
                    structSize = sizeof(AtkEventData);

                    // Interpret the first 8 bytes as mouse position values 
                    uint[] mousePositions = new uint[4];
                    for (int i = 0; i < 4; i++) {
                        mousePositions[i] = BitConverter.ToUInt16(new byte[] { bytePtr[i * 2], bytePtr[i * 2 + 1] }, 0);
                    }
                    this.Log.Debug($"Mouse Position: X={mousePositions[0]}, Y={mousePositions[1]}, Z={mousePositions[2]}, W={mousePositions[3]}");


                    // Print the rest of the bytes in groups of 8
                    for (int i = 8; i < structSize; i += 8) {
                        string byteGroup = string.Empty;
                        for (int j = 0; j < 8 && i + j < structSize; j++) {
                            byteGroup += $"{bytePtr[i + j]} ";
                        }
                        this.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
                    }
                }
            }
        });
        return;
    }

    public class HudElementData
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


    private unsafe void PrintAtkEventData(AtkEventData* atkEventData) {
        if (atkEventData == null) {
            this.Log.Debug("AtkEventData is null");
            return;
        }

        // Print the byte values of the AtkEventData struct, 8 bytes per line
        byte* bytePtr = (byte*) atkEventData;
        int structSize = sizeof(AtkEventData);
        for (int i = 0; i < structSize; i += 8) {
            string byteGroup = string.Empty;
            for (int j = 0; j < 8 && i + j < structSize; j++) {
                byteGroup += $"{bytePtr[i + j]} ";
            }
            this.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
        }
    }

    /*
     * Simulate a mouse click on a HUD element
     */
    private unsafe void SimulateMouseClickOnHudElement(AtkResNode* resNode, HudElementData hudElementData, AddonHudLayoutScreen* hudLayoutScreen) {
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
        mouseDownEvent.Param = 0;

        //printAtkEventData(&eventData);
        // Set the event data with the x and y position of the mouse click
        AtkEventData* mouseDownEventData = &eventData;

        // Call the mouse down event on the HUD layout
        this.Log.Debug("Calling MouseDown event");
        hudLayoutScreen->ReceiveEvent(AtkEventType.MouseDown, 0, &mouseDownEvent, mouseDownEventData);

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

    /*
     * Send a change event to the HUD layout
     */
    private unsafe void SendChangeEvent(AgentHUDLayout* agentHudLayout) {
        AtkValue* result = stackalloc AtkValue[1];
        AtkValue* command = stackalloc AtkValue[2];
        command[0].SetInt(22);
        command[1].SetInt(0);
        agentHudLayout->ReceiveEvent(result, command, 1, 0);
    }

    private unsafe void PrintAtkEvent(AtkEvent* atkEvent) {
        if (atkEvent == null) {
            this.Log.Debug("AtkEvent is null");
            return;
        }
        this.Log.Debug($"-------- AtkEvent --------");
        this.Log.Debug($"AtkEvent Flags: {atkEvent->Flags}");
        this.Log.Debug($"AtkEvent Param: {atkEvent->Param}");
        this.Log.Debug($"AtkEvent Listener: {(uint) atkEvent->Listener:X}");
        this.Log.Debug($"AtkEvent Node: {(uint) atkEvent->Node:X}");
        this.Log.Debug($"AtkEvent Unk29: {atkEvent->Unk29}");
        this.Log.Debug($"AtkEvent NextEvent: {(uint) atkEvent->NextEvent:X}");
        // ===> MouseUp Target: 1892F212190 ---> AddonInventory -> AddonControl -> AtkStage (!) (AtkStage.Instance()?)
        this.Log.Debug($"(AtkStage): {(uint) AtkStage.Instance():X}");
        this.Log.Debug($"AtkEvent Target: {(uint) atkEvent->Target:X}");
        try {
            //AtkCollisionNode* targetCollisionNode = (AtkCollisionNode*) atkEvent->Target;
            //if (targetCollisionNode == null) {
            //    this.Log.Debug("AtkEvent Target is null");
            //} else {
            //    this.Log.Debug($"-> Target Str: {targetCollisionNode->ToString()}");
            //    this.Log.Debug($"-> Target ScreenX/ScreenY: {targetCollisionNode->ScreenX}, {targetCollisionNode->ScreenY}");
            //    this.Log.Debug($"-> Target width/height: {targetCollisionNode->Width}, {targetCollisionNode->Height}");
            //    this.Log.Debug($"-> Target LinkedComponent: {(uint) targetCollisionNode->LinkedComponent}");
            //    this.Log.Debug($"-> Target NodeId: {(uint) targetCollisionNode->NodeId}");
            //    this.Log.Debug($"-> Target NodeId X: {(uint) targetCollisionNode->NodeId:X}");
            //    this.Log.Debug($"-> Target NodeFlags: {targetCollisionNode->NodeFlags}");
            //    this.Log.Debug($"-> Target ChildCount: {targetCollisionNode->ChildCount}");
            //    this.Log.Debug($"-> Target Parent: {(uint) targetCollisionNode->ParentNode:X}");
            //}
        }
        catch (Exception e) {
            this.Log.Debug($"AtkEvent Target: {e.Message}");
        }
        this.Log.Debug($"-------- AtkEvent End --------");
        this.Log.Debug($"AtkEvent Type: {atkEvent->Type}");
    }

    private unsafe AtkResNode* FindHudResnodeByName(AddonHudLayoutScreen* hudLayoutScreen, string searchName) {
        AtkResNode** resNodes = hudLayoutScreen->CollisionNodeList;
        uint resNodeCount = hudLayoutScreen->CollisionNodeListCount;
        for (int i = 0; i < resNodeCount; i++) {
            AtkResNode* resNode = resNodes[i];
            if (resNode == null) continue;
            try {
                Utf8String resNodeName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText;
                if (resNodeName.ToString() == searchName) {
                    return resNode;
                }
            }
            catch (NullReferenceException) {
                continue;
            }
        }
        return null;
    }

    private unsafe void OnUpdate(IFramework framework) {
        // Executes every frame
        if (!ClientState.IsLoggedIn) return;
        if (ClientState.IsPvP) return;
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
                SimulateMouseClickOnHudElement(selectedNode, parsedData, hudLayoutScreen);

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
             * TODO: Implement for standard element movements? 
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
                    AtkResNode* lastNode = FindHudResnodeByName(hudLayoutScreen, lastState.resNodeDisplayName);

                    HudElementData redoState = new HudElementData(lastNode);
                    redoHistory.Add(redoState);

                    // Set the position of the currently selected element to the parsed position
                    lastNode->ParentNode->SetPositionShort(lastState.posX, lastState.posY);

                    // ======= Simulate Mouse Click =======
                    SimulateMouseClickOnHudElement(lastNode, lastState, hudLayoutScreen);

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
                    AtkResNode* redoNode = FindHudResnodeByName(hudLayoutScreen, redoState.resNodeDisplayName);

                    HudElementData undoState = new HudElementData(redoNode);
                    undoHistory.Add(undoState);

                    // Set the position of the currently selected element to the parsed position
                    redoNode->ParentNode->SetPositionShort(redoState.posX, redoState.posY);

                    // ======= Simulate Mouse Click =======
                    SimulateMouseClickOnHudElement(redoNode, redoState, hudLayoutScreen);

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
        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();

        //CommandManager.RemoveHandler(CommandName);
    }

    //private void OnCommand(string command, string args) {
    //    // in response to the slash command, just toggle the display status of our main ui
    //    ToggleMainUI();
    //}

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();
}
