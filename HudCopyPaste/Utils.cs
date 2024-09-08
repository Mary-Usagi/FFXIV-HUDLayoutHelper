using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using static HudCopyPaste.Plugin;

namespace HudCopyPaste {
    internal class Utils {
        /// <summary>
        /// Finds a HUD resource node by name.
        /// </summary>
        /// <param name="hudLayoutScreen">The HUD layout screen pointer.</param>
        /// <param name="searchName">The name to search for.</param>
        /// <returns>A tuple containing the node pointer and its ID.</returns>
        internal static unsafe (nint, uint) FindHudResnodeByName(AddonHudLayoutScreen* hudLayoutScreen, string searchName) {
            AtkResNode** resNodes = hudLayoutScreen->CollisionNodeList;
            uint resNodeCount = hudLayoutScreen->CollisionNodeListCount;
            for (int i = 0; i < resNodeCount; i++) {
                AtkResNode* resNode = resNodes[i];
                if (resNode == null) continue;
                try {
                    Utf8String resNodeName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText;
                    if (resNodeName.ToString() == searchName) {
                        return ((nint)resNode, (uint)i);
                    }
                } catch (NullReferenceException) {
                    continue;
                }
            }
            return (nint.Zero, 0);
        }

        /// <summary>
        /// Simulates a mouse click on a HUD element.
        /// </summary>
        /// <param name="resNode">The AtkResNode pointer.</param>
        /// <param name="resNodeID">The resource node ID.</param>
        /// <param name="hudElementData">The HUD element data.</param>
        /// <param name="hudLayoutScreen">The HUD layout screen pointer.</param>
        internal static unsafe void SimulateMouseClickOnHudElement(AtkResNode* resNode, uint resNodeID, HudElementData hudElementData, AddonHudLayoutScreen* hudLayoutScreen, Plugin plugin, byte customFlag) {
            if (resNode == null) {
                plugin.Log.Warning("ResNode is null");
                return;
            }

            // Create the event data struct with the x and y position of the mouse click
            MouseEventData mouseEventData = new MouseEventData(hudElementData.PosX, hudElementData.PosY);
            AtkEventData eventData = mouseEventData.ToAtkEventData();

            // ----> MouseDown Event
            // Create the mouse down event from the selected node's event
            AtkEvent mouseDownEvent = *resNode->AtkEventManager.Event;
            mouseDownEvent.Type = AtkEventType.MouseDown;
            mouseDownEvent.Param = resNodeID;
            mouseDownEvent.Flags = 4;
            // Add custom Flag to the event to identify it as a custom event
            mouseDownEvent.Flags |= customFlag;

            // Set the event data with the x and y position of the mouse click
            AtkEventData* mouseDownEventData = &eventData;

            // Call the mouse down event on the HUD layout
            plugin.Debug.Log(plugin.Log.Debug, "Calling MouseDown event");
            hudLayoutScreen->ReceiveEvent(AtkEventType.MouseDown, (int)mouseDownEvent.Param, &mouseDownEvent, mouseDownEventData);

            // ----> MouseUp Event
            // Create the mouse up event from the selected node's event
            uint atkStagePtr = (uint)AtkStage.Instance();
            AtkEvent mouseUpEvent = *resNode->AtkEventManager.Event;
            mouseUpEvent.Type = AtkEventType.MouseUp;
            mouseUpEvent.Param = 99;
            mouseUpEvent.NextEvent = null;
            mouseUpEvent.Target = (AtkEventTarget*)atkStagePtr;
            mouseUpEvent.Unk29 = 0;
            mouseUpEvent.Flags = 6;
            // Add custom Flag to the event to identify it as a custom event
            mouseUpEvent.Flags |= customFlag;

            // Set the event data with the x and y position of the mouse click
            plugin.Debug.Log(plugin.Log.Debug, "Calling MouseUp event");
            AtkEventData* mouseUpEventData = &eventData;

            // Call the mouse up event on the HUD layout
            hudLayoutScreen->ReceiveEvent(AtkEventType.MouseUp, 99, &mouseUpEvent, mouseUpEventData);

            // ----> Reset mouse cursor to default image (Arrow) 
            plugin.AddonEventManager.ResetCursor();
        }

        /// <summary>
        /// Sends a change event to the HUD layout.
        /// </summary>
        /// <param name="agentHudLayout">The agent HUD layout pointer.</param>
        internal static unsafe void SendChangeEvent(AgentHUDLayout* agentHudLayout) {
            AtkValue* result = stackalloc AtkValue[1];
            AtkValue* command = stackalloc AtkValue[2];
            command[0].SetInt(22);
            command[1].SetInt(0);
            agentHudLayout->ReceiveEvent(result, command, 1, 0);
        }
    }
}
