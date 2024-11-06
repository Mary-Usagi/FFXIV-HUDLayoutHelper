using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static FFXIVClientStructs.FFXIV.Client.UI.UIInputData;
using YamlDotNet.Serialization;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System.Drawing;

namespace HUDLayoutShortcuts;

/// <summary>
/// https://www.reddit.com/r/imgui/comments/kknzqw/how_do_i_draw_triangle_in_imgui/
/// https://github.com/ocornut/imgui/issues/3541
/// https://github.com/ocornut/imgui/issues/2274
/// https://www.google.com/search?q=rgba+hex+color+picker&client=firefox-b-d&sca_esv=2ee6c9bbd1613675&biw=1920&bih=919&sxsrf=ADLYWIJksTLF2nIWdhZIH29fiqVjNxpHLQ%3A1730862043613&ei=29sqZ7-HJfyJ7NYPtoqIuAQ&ved=0ahUKEwj_4I7K28aJAxX8BNsEHTYFAkcQ4dUDCA8&uact=5&oq=rgba+hex+color+picker&gs_lp=Egxnd3Mtd2l6LXNlcnAiFXJnYmEgaGV4IGNvbG9yIHBpY2tlcjIIEAAYgAQYywEyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTILEAAYgAQYhgMYigUyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTIIEAAYgAQYogRI9QlQ4AVY_whwAXgBkAEAmAFeoAG9AqoBATS4AQPIAQD4AQGYAgSgAu4BwgIKEAAYsAMY1gQYR8ICDRAAGIAEGLADGEMYigXCAgYQABgHGB6YAwCIBgGQBgqSBwE0oAetGA&sclient=gws-wiz-serp
/// </summary>
public class OverlayWindow : Window, IDisposable {
    private Plugin Plugin;
    private Configuration Configuration;

    public OverlayWindow(Plugin plugin) : base("Overlay"){
        Flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = ImGui.GetIO().DisplaySize,
            MaximumSize = ImGui.GetIO().DisplaySize
        };
        Position = new Vector2(0, 0);

        Plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw() {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        Flags |= ImGuiWindowFlags.NoMove;
    }

    internal uint ColorToUint(Color color) {
        return (uint)(color.A << 24 | color.B << 16 | color.G << 8 | color.R);
    }

    /// <summary>
    ///  TODO
    /// </summary>
    public unsafe override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        //ImDrawList* draw_list = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        if (!Plugin.ClientState.IsLoggedIn) return;
        if (Plugin.ClientState is not { LocalPlayer.ClassJob.Id: var classJobId }) return;
        if (Plugin.AgentHudLayout == null || Plugin.HudLayoutScreen == null) return;


        var red_color = ColorToUint(Color.FromArgb(255, 0, 0));
        var blue_color = ColorToUint(Color.FromArgb(0, 0, 255));
        var green_color = ColorToUint(Color.FromArgb(0, 255, 0));
        var black_color = ColorToUint(Color.FromArgb(0, 0, 0));

        ImDrawListPtr imDrawListPtr = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());

        // TODO: get current element data 
        AtkResNode* selectedResNode = Utils.GetCollisionNodeByIndex(Plugin.HudLayoutScreen, 0);
        if (selectedResNode == null) return;

        // Create a new HudElementData object with the data of the selected element
        HudElementData selectedNode = new HudElementData(selectedResNode);
        Vector2 selectedNodePos = new Vector2(selectedNode.PosX, selectedNode.PosY);
        Vector2 selectedNodeCenter = selectedNodePos + new Vector2(selectedNode.Width / 2, selectedNode.Height / 2);
        Vector2[] selectedNodeCorners = new Vector2[] {
            selectedNodePos,
            selectedNodePos + new Vector2(selectedNode.Width, 0),
            selectedNodePos + new Vector2(0, selectedNode.Height),
            selectedNodePos + new Vector2(selectedNode.Width, selectedNode.Height)
        };

        uint selectedNodeCenterColor = red_color;
        uint[] selectedNodeCornerColors = new uint[] { green_color, green_color, green_color, green_color };

        // TODO: use this to compare the selected element with all others
        var currentElements = Plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(Plugin)];
        foreach (var elementData in currentElements) {
            if (!elementData.Value.IsVisible) continue;
            var elementValue = elementData.Value;
            float markerSize = 2;

            var color_center = black_color;
            uint[] color_corners = new uint[] { black_color, black_color, black_color, black_color };

            Vector2 elementPos = new Vector2(elementValue.PosX, elementValue.PosY);
            Vector2 elementCenter = elementPos + new Vector2(elementValue.Width / 2, elementValue.Height / 2);

            Vector2[] elementCorners = new Vector2[] {
                elementPos,
                elementPos + new Vector2(elementValue.Width, 0),
                elementPos + new Vector2(0, elementValue.Height),
                elementPos + new Vector2(elementValue.Width, elementValue.Height)
            };

            // Use selectedNodeData if the element is the selected one
            if (elementData.Value.ElementId == selectedNode.ElementId) {
                elementPos = selectedNodePos;
                elementCenter = selectedNodeCenter;
                elementCorners = selectedNodeCorners;
                markerSize = 2.5f;
                color_center = selectedNodeCenterColor;
                color_corners = selectedNodeCornerColors;
            } else {
                // Color the corners of the element if they have the same position the same as one of the selected node
                for (int i = 0; i < elementCorners.Length; i++) {
                    for (int j = 0; j < selectedNodeCorners.Length; j++) {
                        if (elementCorners[i].X == selectedNodeCorners[j].X || elementCorners[i].Y == selectedNodeCorners[j].Y) {
                            color_corners[i] = selectedNodeCornerColors[j];
                        }
                    }
                    if (elementCorners[i].X == selectedNodeCenter.X || elementCorners[i].Y == selectedNodeCenter.Y) {
                        color_corners[i] = selectedNodeCenterColor;
                    }
                }
                // Color the center of the element if it has the same position as one of the selected node
                if (elementCenter.X == selectedNodeCenter.X || elementCenter.Y == selectedNodeCenter.Y) {
                    color_center = selectedNodeCenterColor;
                }
                for (int i = 0; i < selectedNodeCorners.Length; i++) {
                    if (elementCenter.X == selectedNodeCorners[i].X || elementCenter.Y == selectedNodeCorners[i].Y) {
                        color_center = selectedNodeCornerColors[i];
                    }
                }


                //if (elementCenter.X == selectedNodeCenter.X || elementCenter.Y == selectedNodeCenter.Y) {
                //    color_center = selectedNodeCenterColor;
                //}
                //for (int j = 0; j < selectedNodeCorners.Length; j++) {
                //    if (elementCenter.X == selectedNodeCorners[j].X || elementCenter.Y == selectedNodeCorners[j].Y) {
                //        color_center = selectedNodeCornerColors[j];
                //    }
                //}
            }



            imDrawListPtr.AddCircleFilled(elementCenter, markerSize, color_center);

            for (int i = 0; i < elementCorners.Length; i++) {
                imDrawListPtr.AddCircleFilled(elementCorners[i], markerSize, color_corners[i]);
            }
            //for
        }

        //var currentElements = Plugin.GetCurrentElements();
        //foreach (var elementData in currentElements) {
        //    if (!elementData.Value.IsVisible) continue;

        //    Vector2 elementPos = new Vector2(elementData.Value.PosX, elementData.Value.PosY);
        //    Vector2 elementCenter = elementPos + new Vector2(elementData.Value.Width / 2, elementData.Value.Height / 2);

        //    imDrawListPtr.AddCircleFilled(elementCenter, 3, color_center);

        //    imDrawListPtr.AddCircleFilled(elementPos, 3, color_corners);
        //    imDrawListPtr.AddCircleFilled(elementPos + new Vector2(elementData.Value.Width, 0), 3, color_corners);
        //    imDrawListPtr.AddCircleFilled(elementPos + new Vector2(0, elementData.Value.Height), 3, color_corners);
        //    imDrawListPtr.AddCircleFilled(elementPos + new Vector2(elementData.Value.Width, elementData.Value.Height), 3, color_corners);
        //}

        //imDrawListPtr.AddLine(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), 0xFFFFFFFF, 1.0f);
        //draw_list->AddTriangleFilled(ImVec2(50, 100), ImVec2(100, 50), ImVec2(150, 100), ImColor(255, 0, 0));
    }
}
