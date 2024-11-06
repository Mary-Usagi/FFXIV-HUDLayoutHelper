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
/// 
/// TODO: add toggle for guidelines etc. to settings or keybind 
/// TODO: automatically open when HUD Layout editor is open 
/// TODO: add setting to always show selected element guidelines 
/// TODO: add setting to show guidelines for all elements? 
/// TODO: rename feature. "Alignment helper"? 
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


        var red_color = Color.FromArgb(255, 0, 0);
        var blue_color = Color.FromArgb(0, 0, 255);
        var green_color = Color.FromArgb(0, 255, 0);
        var black_color = Color.FromArgb(175, 0, 0, 0); // show corners of other elements 
        var black_color2 = Color.FromArgb(75, 0, 0, 0); 
        //var black_color = Color.FromArgb(0, 0, 0, 0); // don't show corners of other elements except matching ones

        ImDrawListPtr imDrawListPtr = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());

        // TODO: get current element data 
        AtkResNode* selectedResNode = Utils.GetCollisionNodeByIndex(Plugin.HudLayoutScreen, 0);
        if (selectedResNode == null) return;

        // Create a new HudElementData object with the data of the selected element
        HudElementData selectedNode = new HudElementData(selectedResNode);
        Vector2 selectedNodePos = new Vector2(selectedNode.PosX, selectedNode.PosY);
        Vector2 selectedNodeCenter = selectedNodePos + new Vector2((int)Math.Round(selectedNode.Width / 2f), (int)Math.Round(selectedNode.Height / 2f));
        Vector2[] selectedNodeCorners = {
            selectedNodePos,
            selectedNodePos + new Vector2(selectedNode.Width, 0),
            selectedNodePos + new Vector2(0, selectedNode.Height),
            selectedNodePos + new Vector2(selectedNode.Width, selectedNode.Height),
            selectedNodeCenter
        };

        Color selectedNodeCenterColor = red_color;
        Color[] selectedNodeCornerColors = { green_color, green_color, green_color, green_color, selectedNodeCenterColor };
        float[] selectedNodeMarkerSizes = { 2.5f, 2.5f, 2.5f, 2.5f, 2f };

        // TODO: use this to compare the selected element with all others
        var currentElements = Plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(Plugin)];

        // Guide lines are vectors that either go horizontally or vertically from opposite corners of the window through the matching corners/nodes
        List<Tuple<Vector2, Color>> guideLines = new List<Tuple<Vector2, Color>>();

        foreach (var elementData in currentElements) {
            if (!elementData.Value.IsVisible) continue;

            var elementValue = elementData.Value;
            Vector2 elementPos = new Vector2(elementValue.PosX, elementValue.PosY);
            Vector2 elementCenter = elementPos + new Vector2((int)Math.Round(elementValue.Width / 2f), (int)Math.Round(elementValue.Height / 2f));
            Vector2[] elementCorners = {
                elementPos,
                elementPos + new Vector2(elementValue.Width, 0),
                elementPos + new Vector2(0, elementValue.Height),
                elementPos + new Vector2(elementValue.Width, elementValue.Height),
                elementCenter
            };

            float[] elementMarkerSizes = { 1.5f, 1.5f, 1.5f, 1.5f, 1.5f };
            Color[] elementCornerColors = { black_color, black_color, black_color, black_color, black_color };

            // Use selectedNodeData if the element is the selected one
            if (elementData.Value.ElementId == selectedNode.ElementId) {
                elementCorners = selectedNodeCorners;
                elementMarkerSizes = selectedNodeMarkerSizes;
                elementCornerColors = selectedNodeCornerColors;
            } else {
                for (int i = 0; i < elementCorners.Length; i++) {
                    for (int j = 0; j < selectedNodeCorners.Length; j++) {
                        // TODO: remove && to enable dimming of near corners
                        if (Math.Abs(elementCorners[i].X - selectedNodeCorners[j].X) < 3) {

                            //var horizontalLine = new Vector2(elementCorners[i].X, 0); // dimmed line and real line have the same position 
                            var horizontalLine = new Vector2(selectedNodeCorners[j].X, 0); // dimmed line and real line do not have the same position

                            // If the X coordinate is only similar but not the same, dim the color
                            if (elementCorners[i].X == selectedNodeCorners[j].X) {
                                elementCornerColors[i] = selectedNodeCornerColors[j];
                                elementMarkerSizes[i] = selectedNodeMarkerSizes[j];
                                guideLines.Add(new Tuple<Vector2, Color>(horizontalLine, elementCornerColors[i]));
                            } else {
                                var dimmed_color = Color.FromArgb(100, selectedNodeCornerColors[j].R, selectedNodeCornerColors[j].G, selectedNodeCornerColors[j].B);
                                elementCornerColors[i] = Color.FromArgb(150, selectedNodeCornerColors[j].R, selectedNodeCornerColors[j].G, selectedNodeCornerColors[j].B);
                                //guideLines.Add(new Tuple<Vector2, Color>(horizontalLine, dimmed_color));
                                //guideLines.Add(new Tuple<Vector2, Color>(horizontalLine, black_color2));

                            }
                        }
                        if (Math.Abs(elementCorners[i].Y - selectedNodeCorners[j].Y) < 3) {

                            var verticalLine = new Vector2(0, selectedNodeCorners[j].Y);
                            // If the Y coordinate is only similar but not the same, dim the color
                            if (elementCorners[i].Y == selectedNodeCorners[j].Y) {
                                elementCornerColors[i] = selectedNodeCornerColors[j];
                                elementMarkerSizes[i] = selectedNodeMarkerSizes[j];
                                guideLines.Add(new Tuple<Vector2, Color>(verticalLine, elementCornerColors[i]));
                            } else {
                                var dimmed_color = Color.FromArgb(100, selectedNodeCornerColors[j].R, selectedNodeCornerColors[j].G, selectedNodeCornerColors[j].B);
                                elementCornerColors[i] = Color.FromArgb(150, selectedNodeCornerColors[j].R, selectedNodeCornerColors[j].G, selectedNodeCornerColors[j].B);
                                //guideLines.Add(new Tuple<Vector2, Color>(horizontalLine, dimmed_color));
                                //guideLines.Add(new Tuple<Vector2, Color>(verticalLine, black_color2));
                            }
                        }
                    }
                }
                //for (int i = 0; i < selectedNodeCorners.Length; i++) {
                //    var horizontalLine1 = new Vector2(selectedNodeCorners[i].X, 0);
                //    var verticalLine1 = new Vector2(0, selectedNodeCorners[i].Y);
                //    if (!guideLines.Exists(x => x.Item1.X == horizontalLine1.X && x.Item1.Y == horizontalLine1.Y)){
                //        guideLines.Add(new Tuple<Vector2, Color>(horizontalLine1, black_color2));
                //    }
                //    if (!guideLines.Exists(x => x.Item1.X == verticalLine1.X && x.Item1.Y == verticalLine1.Y)){
                //        guideLines.Add(new Tuple<Vector2, Color>(verticalLine1, black_color2));
                //    }
                //}
            }
            for (int i = 0; i < elementCorners.Length; i++) {
                imDrawListPtr.AddCircleFilled(elementCorners[i], elementMarkerSizes[i], ColorToUint(elementCornerColors[i]));
            }

        }

        // Filter out duplicate guide lines
        if (guideLines.Count > 0) {
            //Plugin.Log.Debug($"Guide lines: {guideLines.Count}");
            guideLines = guideLines.Distinct().ToList();
            //Plugin.Log.Debug($"Guide lines: {guideLines.Count}");
            foreach (var guideLine in guideLines) {
                var thickness = guideLine.Item2 == black_color2 ? 1.0f : 1.0f;
                if (guideLine.Item1.Y == 0) {
                    imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item1 + new Vector2(0, ImGui.GetIO().DisplaySize.Y), ColorToUint(guideLine.Item2), thickness);
                } else if (guideLine.Item1.X == 0) {
                    imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item1 + new Vector2(ImGui.GetIO().DisplaySize.X, 0), ColorToUint(guideLine.Item2), thickness);
                }
            }
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
