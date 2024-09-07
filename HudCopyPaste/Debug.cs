using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;

namespace HudCopyPaste
{
    public sealed class Debug
    {
        private Plugin Plugin { get; }
        public Debug(Plugin plugin, bool enabled) {
            Plugin = plugin;

            if (enabled) {
                // For debugging purposes
                Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, "_HudLayoutScreen", (type, args) => {
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

                        Plugin.Log.Debug("=====================================");
                        Plugin.Log.Debug("AtkEventType: " + (AtkEventType) receiveEventArgs.AtkEventType);

                        Plugin.Log.Debug("AddonArgsType: " + receiveEventArgs.Type);
                        Plugin.Log.Debug($"AtkEvent nint: {receiveEventArgs.AtkEvent:X}");
                        if (receiveEventArgs.AtkEvent != nint.Zero) {
                            AtkEvent* atkEvent = (AtkEvent*) receiveEventArgs.AtkEvent;
                            Plugin.Log.Debug("---------- AtkEvent ----------");
                            Plugin.Log.Debug($"AtkEvent: {atkEvent->ToString()}");
                            PrintAtkEvent(atkEvent);
                            Plugin.Log.Debug("---------- AtkEvent End ----------");
                        }
                        Plugin.Log.Debug("AddonName: " + receiveEventArgs.AddonName);
                        Plugin.Log.Debug("EventId int: " + receiveEventArgs.EventParam);
                        Plugin.Log.Debug($"Data Ptr: {receiveEventArgs.Data:X}");
                        if (receiveEventArgs.Data != nint.Zero) {
                            AtkEventData* eventData = (AtkEventData*) receiveEventArgs.Data;
                            Plugin.Log.Debug($"EventData: {eventData->ToString()}");
                            Plugin.Log.Debug($"ListItemData: {eventData->ListItemData}");
                            Plugin.Log.Debug($"SelectedIndex: {eventData->ListItemData.SelectedIndex}");

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
                            Plugin.Log.Debug($"Mouse Position: X={mousePositions[0]}, Y={mousePositions[1]}, Z={mousePositions[2]}, W={mousePositions[3]}");


                            // Print the rest of the bytes in groups of 8
                            for (int i = 8; i < structSize; i += 8) {
                                string byteGroup = string.Empty;
                                for (int j = 0; j < 8 && i + j < structSize; j++) {
                                    byteGroup += $"{bytePtr[i + j]} ";
                                }
                                Plugin.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
                            }
                        }
                    }
                });
            }
        }

        private unsafe void PrintAtkEvent(AtkEvent* atkEvent) {
            if (atkEvent == null) {
                Plugin.Log.Debug("AtkEvent is null");
                return;
            }
            Plugin.Log.Debug($"-------- AtkEvent --------");
            Plugin.Log.Debug($"AtkEvent Flags: {atkEvent->Flags}");
            Plugin.Log.Debug($"AtkEvent Param: {atkEvent->Param}");
            Plugin.Log.Debug($"AtkEvent Listener: {(uint) atkEvent->Listener:X}");
            Plugin.Log.Debug($"AtkEvent Node: {(uint) atkEvent->Node:X}");
            Plugin.Log.Debug($"AtkEvent Unk29: {atkEvent->Unk29}");
            Plugin.Log.Debug($"AtkEvent NextEvent: {(uint) atkEvent->NextEvent:X}");
            // ===> MouseUp Target: 1892F212190 ---> AddonInventory -> AddonControl -> AtkStage (!) (AtkStage.Instance()?)
            Plugin.Log.Debug($"(AtkStage): {(uint) AtkStage.Instance():X}");
            Plugin.Log.Debug($"AtkEvent Target: {(uint) atkEvent->Target:X}");
            try {
                //AtkCollisionNode* targetCollisionNode = (AtkCollisionNode*) atkEvent->Target;
                //if (targetCollisionNode == null) {
                //    Plugin.Log.Debug("AtkEvent Target is null");
                //} else {
                //    Plugin.Log.Debug($"-> Target Str: {targetCollisionNode->ToString()}");
                //    Plugin.Log.Debug($"-> Target ScreenX/ScreenY: {targetCollisionNode->ScreenX}, {targetCollisionNode->ScreenY}");
                //    Plugin.Log.Debug($"-> Target width/height: {targetCollisionNode->Width}, {targetCollisionNode->Height}");
                //    Plugin.Log.Debug($"-> Target LinkedComponent: {(uint) targetCollisionNode->LinkedComponent}");
                //    Plugin.Log.Debug($"-> Target NodeId: {(uint) targetCollisionNode->NodeId}");
                //    Plugin.Log.Debug($"-> Target NodeId X: {(uint) targetCollisionNode->NodeId:X}");
                //    Plugin.Log.Debug($"-> Target NodeFlags: {targetCollisionNode->NodeFlags}");
                //    Plugin.Log.Debug($"-> Target ChildCount: {targetCollisionNode->ChildCount}");
                //    Plugin.Log.Debug($"-> Target Parent: {(uint) targetCollisionNode->ParentNode:X}");
                //}
            }
            catch (Exception e) {
                Plugin.Log.Debug($"AtkEvent Target: {e.Message}");
            }
            Plugin.Log.Debug($"-------- AtkEvent End --------");
            Plugin.Log.Debug($"AtkEvent Type: {atkEvent->Type}");
        }

        private unsafe void PrintAtkEventData(AtkEventData* atkEventData) {
            if (atkEventData == null) {
                Plugin.Log.Debug("AtkEventData is null");
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
                Plugin.Log.Debug($"Bytes {i}-{i + 7}: {byteGroup.Trim()}");
            }
        }
    }
}
