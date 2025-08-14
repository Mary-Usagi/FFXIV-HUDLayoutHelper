using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;

namespace HUDLayoutHelper.Utilities {
    /// <summary>
    /// Provides debugging functionality for the HUDLayoutHelper plugin.
    /// </summary>
    public sealed class Debug : IDisposable {
        private bool Enabled { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Debug"/> class.
        /// </summary>
        /// <param name="enabled">Indicates whether debugging is enabled.</param>
        /// 
        public unsafe Debug(bool enabled) {
            Enabled = enabled;

            if (Enabled) {
                // Log the initialization of the Debug class
                Plugin.Log.Debug("Debug class initialized");

                // Register a listener for the PreReceiveEvent of the "_HudLayoutScreen" addon
                Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", HandleHudLayoutScreenEvent);

                Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow", HandleHudLayoutScreenEvent);
            }
        }

        /// <summary>
        /// Handles the PreReceiveEvent for the "_HudLayoutScreen" addon.
        /// </summary>
        /// <param name="type">The type of the addon event.</param>
        /// <param name="args">The arguments for the addon event.</param>
        private unsafe void HandleHudLayoutScreenEvent(AddonEvent type, AddonArgs args) {
            if (args is not AddonReceiveEventArgs receiveEventArgs) return;

            // List of AtkEventTypes to handle
            var handledTypeList = new List<AtkEventType> {
                // Uncomment the following lines to handle specific AtkEventTypes
                // AtkEventType.MouseMove,
                // AtkEventType.MouseOut,
                // AtkEventType.MouseOver,
                // AtkEventType.MouseDown,
                // AtkEventType.MouseUp,
                // AtkEventType.InputReceived
            };

            var handledByteEventList = new List<byte> {
                // Uncomment the following lines to handle specific byte events
                //13,
                //15
            };

            // Check if the event type is in the handled list
            if (!handledTypeList.Contains((AtkEventType)receiveEventArgs.AtkEventType) && !handledByteEventList.Contains(receiveEventArgs.AtkEventType)) {
                return;
            }

            if (receiveEventArgs.AtkEventType == 15) {
                if (receiveEventArgs.Data != nint.Zero) {
                    AtkEventData* eventDataTemp = (AtkEventData*)receiveEventArgs.Data;
                    if (eventDataTemp->ListItemData.SelectedIndex == 256) {
                        //Plugin.Log.Debug("@@@@@@ Window action");
                        return;
                    } else {
                        Plugin.Log.Debug("@@@@@@ HudElement action");
                    }
                } else {
                    return;
                }
            }

            // FINDINGS:
            // _HudLayoutWindow: 
            // - 15 AtkEvent:
            //   - Sent when right controller stick is used to move something 
            //   - EventData: ControllerInput (up/down/left/right)
            //      - bytes 0-7: all 0 on stop moving
            //      - SelectedIndex:
            //          - 256: window action
            //          - 0: HudElement action
            // 
            // _HudLayoutScreen:
            // - InputReceived AtkEvent:
            //   - ALL keyboardevents that are pressed, if they do something or not
            //      - byte nr. 1: indicates which key was pressed 
            //      - byte nr. 5:
            //          - 0: start pressing
            //          - 1: press once
            //          - 2: press continuous
            //      - byte nr. 6: indicates modifier keys: 1 = ctrl, 2 = alt, 3 = ctrl + alt, 4 = shift
            // - '13' AtkEvent:
            //   - Only sent when something is moved by keyboard or controller arrows, though not with the controller stick
            //   - EventData: 
            //     - byte 1: indicates which key was pressed
            // - 15 AtkEvent:
            //   - Is sent when the left controller stick is used to move something, after clicking on it
            // - MouseDown AtkEvent:
            //   - Param: current index in collisionNodeList
            //   - eventid: 0
            //   - NextEvent: selected resnode (collisionnode) -> atkeventmanager->atkevent->nextevent
            //   - listener: selected resnode (collisionnode) -> atkeventmanager->atkevent->listener
            //   - target: Pointer to selected resnode (collisionnode) == CollisionNodeList[Param]
            // - MouseUp AtkEvent:
            //   - eventid: 99
            //   - Target: AtkStage.Instance()

            // Log the event details for debugging purposes
            Plugin.Log.Debug("=====================================");
            Plugin.Log.Debug($"AddonEvent: {type}");
            Plugin.Log.Debug($"AtkEventType: {(AtkEventType)receiveEventArgs.AtkEventType}");
            Plugin.Log.Debug($"AddonArgsType: {receiveEventArgs.Type}");
            Plugin.Log.Debug($"AtkEvent nint: {receiveEventArgs.AtkEvent:X}");

            if (receiveEventArgs.AtkEvent != nint.Zero) {
                AtkEvent* atkEvent = (AtkEvent*)receiveEventArgs.AtkEvent;
                Plugin.Log.Debug("---------- AtkEvent ----------");
                Plugin.Log.Debug($"AtkEvent: {atkEvent->ToString()}");
                PrintAtkEvent(atkEvent);
                Plugin.Log.Debug("---------- AtkEvent End ----------");
            }

            Plugin.Log.Debug($"AddonName: {receiveEventArgs.AddonName}");
            Plugin.Log.Debug($"EventId int: {receiveEventArgs.EventParam}");
            Plugin.Log.Debug($"Data Ptr: {receiveEventArgs.Data:X}");

            if (receiveEventArgs.Data != nint.Zero) {
                AtkEventData* eventData = (AtkEventData*)receiveEventArgs.Data;
                Plugin.Log.Debug($"EventData: {eventData->ToString()}");
                Plugin.Log.Debug($"ListItemData: {eventData->ListItemData}");
                Plugin.Log.Debug($"SelectedIndex: {eventData->ListItemData.SelectedIndex}");

                PrintAtkEventData(eventData);

                if (eventData->ListItemData.ListItemRenderer != null) {
                    // Additional processing if ListItemRenderer is not null
                }

                // Interpret the first 8 bytes as mouse position values
                byte* bytePtr = (byte*)receiveEventArgs.Data;
                uint[] mousePositions = new uint[4];
                for (int i = 0; i < 4; i++) {
                    mousePositions[i] = BitConverter.ToUInt16([bytePtr[i * 2], bytePtr[i * 2 + 1]], 0);
                }
                Plugin.Log.Debug($"Mouse Position: X={mousePositions[0]}, Y={mousePositions[1]}, Z={mousePositions[2]}, W={mousePositions[3]}");

                // Print the rest of the bytes in groups of 8
                int structSize = sizeof(AtkEventData);
                for (int i = 8; i < structSize; i += 8) {
                    string byteGroup = string.Empty;
                    for (int j = 0; j < 8 && i + j < structSize; j++) {
                        byteGroup += $"{bytePtr[i + j]} ";
                    }
                    Plugin.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
                }
            }
        }

        /// <summary>
        /// Prints detailed information about the specified <see cref="AtkEvent"/>.
        /// </summary>
        /// <param name="atkEvent">The <see cref="AtkEvent"/> to print.</param>
        private unsafe void PrintAtkEvent(AtkEvent* atkEvent) {
            if (atkEvent == null) {
                Plugin.Log.Debug("AtkEvent is null");
                return;
            }

            Plugin.Log.Debug("-------- AtkEvent --------");
            Plugin.Log.Debug($"AtkEvent Flags: {atkEvent->State.StateFlags}");
            Plugin.Log.Debug($"AtkEvent Param: {atkEvent->Param}");
            Plugin.Log.Debug($"AtkEvent Listener: {(uint)atkEvent->Listener:X}");
            Plugin.Log.Debug($"AtkEvent Node: {(uint)atkEvent->Node:X}");
            Plugin.Log.Debug($"AtkEvent Unk29: {atkEvent->State.ReturnFlags}");
            Plugin.Log.Debug($"AtkEvent NextEvent: {(uint)atkEvent->NextEvent:X}");
            Plugin.Log.Debug($"(AtkStage): {(uint)AtkStage.Instance():X}");
            Plugin.Log.Debug($"AtkEvent Target: {(uint)atkEvent->Target:X}");

            try {
                // Uncomment the following lines to print detailed information about the target collision node
                // AtkCollisionNode* targetCollisionNode = (AtkCollisionNode*)atkEvent->Target;
                // if (targetCollisionNode == null)
                // {
                //     Plugin.Log.Debug("AtkEvent Target is null");
                // }
                // else
                // {
                //     Plugin.Log.Debug($"-> Target Str: {targetCollisionNode->ToString()}");
                //     Plugin.Log.Debug($"-> Target ScreenX/ScreenY: {targetCollisionNode->ScreenX}, {targetCollisionNode->ScreenY}");
                //     Plugin.Log.Debug($"-> Target width/height: {targetCollisionNode->Width}, {targetCollisionNode->Height}");
                //     Plugin.Log.Debug($"-> Target LinkedComponent: {(uint)targetCollisionNode->LinkedComponent}");
                //     Plugin.Log.Debug($"-> Target NodeId: {(uint)targetCollisionNode->NodeId}");
                //     Plugin.Log.Debug($"-> Target NodeId X: {(uint)targetCollisionNode->NodeId:X}");
                //     Plugin.Log.Debug($"-> Target NodeFlags: {targetCollisionNode->NodeFlags}");
                //     Plugin.Log.Debug($"-> Target ChildCount: {targetCollisionNode->ChildCount}");
                //     Plugin.Log.Debug($"-> Target Parent: {(uint)targetCollisionNode->ParentNode:X}");
                // }
            } catch (Exception e) {
                Plugin.Log.Debug($"AtkEvent Target: {e.Message}");
            }

            Plugin.Log.Debug("-------- AtkEvent End --------");
            Plugin.Log.Debug($"AtkEvent Type: {atkEvent->State.EventType}");
        }

        /// <summary>
        /// Prints the byte values of the specified <see cref="AtkEventData"/> struct, 8 bytes per line.
        /// </summary>
        /// <param name="atkEventData">The <see cref="AtkEventData"/> to print.</param>
        private unsafe void PrintAtkEventData(AtkEventData* atkEventData) {
            if (atkEventData == null) {
                Plugin.Log.Debug("AtkEventData is null");
                return;
            }

            // Print the byte values of the AtkEventData struct, 8 bytes per line
            byte* bytePtr = (byte*)atkEventData;
            int structSize = sizeof(AtkEventData);
            for (int i = 0; i < structSize; i += 8) {
                string byteGroup = string.Empty;
                for (int j = 0; j < 8 && i + j < structSize; j++) {
                    byteGroup += $"{bytePtr[i + j]} ";
                }
                Plugin.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
            }
        }

        /// <summary>
        /// Pretty prints a list of <see cref="HudElementData"/> to the debug log.
        /// </summary>
        /// <param name="list">The list of HUD element data.</param>
        /// <param name="title">The title for the log entry.</param>
        internal void PrettyPrintList(List<HudElementData> list, string title) {
            if (!Enabled) return;
            Plugin.Log.Debug($"'{title}' count: {list.Count}");
            foreach (var element in list) {
                Plugin.Log.Debug($"\t{element.ResNodeDisplayName} ({element.PosX}, {element.PosY})");
            }
        }

        /// <summary>
        /// Logs a message to the provided log function if debugging is enabled.
        /// </summary>
        /// <param name="logFunction"></param>
        /// <param name="message"></param>
        public unsafe void Log(Action<string, object[]> logFunction, string message) {
            if (Enabled) {
                logFunction(message, []);
            }
        }

        public void Dispose() {
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", HandleHudLayoutScreenEvent);
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutWindow", HandleHudLayoutScreenEvent);
        }
    }
}
