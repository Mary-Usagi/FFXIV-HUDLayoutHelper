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



    private class HudElementNode {
        internal class Anchor {
            internal Vector2 position;
            internal Color color;
            internal float size;

            internal Anchor(Vector2 position, Color color, float size) {
                this.position = position;
                this.color = color;
                this.size = size;
            }
        }

        internal Anchor TopLeft { get; }
        internal Anchor TopRight { get; }
        internal Anchor BottomLeft { get; }
        internal Anchor BottomRight { get; }
        internal Anchor Center { get; }

        internal Anchor[] Anchors { get; }

        internal HudElementNode(
            short left, short top, 
            int width, int height, 
            Color? centerColor = null, Color? cornerColor = null, 
            float centerSize = 1.5f, float cornerSize = 1.5f
        ) {
            Vector2 topLeft = new Vector2(left, top);
            Vector2 center = topLeft + new Vector2(MathF.Round(width / 2f, MidpointRounding.AwayFromZero), MathF.Round(height / 2f, MidpointRounding.AwayFromZero));

            TopLeft = new Anchor(topLeft, cornerColor ?? black_color, cornerSize);
            TopRight = new Anchor(topLeft + new Vector2(width, 0), cornerColor ?? black_color, cornerSize);
            BottomLeft = new Anchor(topLeft + new Vector2(0, height), cornerColor ?? black_color, cornerSize);
            BottomRight = new Anchor(topLeft + new Vector2(width, height), cornerColor ?? black_color, cornerSize);
            Center = new Anchor(center, centerColor ?? black_color, centerSize);

            Anchors = new Anchor[] { TopLeft, TopRight, BottomLeft, BottomRight, Center };
        }
    }

    static Color red_color = Color.FromArgb(255, 0, 0);
    static Color blue_color = Color.FromArgb(0, 0, 255);
    static Color green_color = Color.FromArgb(0, 255, 0);
    static Color black_color = Color.FromArgb(175, 0, 0, 0); 

    const int MAX_DIFF = 10;

    /// <summary>
    ///  TODO
    /// </summary>
    public unsafe override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        //ImDrawList* draw_list = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        if (!Plugin.ClientState.IsLoggedIn) return;
        if (Plugin.ClientState is not { LocalPlayer.ClassJob.Id: var classJobId }) return;
        if (Plugin.AgentHudLayout == null || Plugin.HudLayoutScreen == null) return;

        ImDrawListPtr imDrawListPtr = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());

        // get current element data 
        AtkResNode* selectedResNode = Utils.GetCollisionNodeByIndex(Plugin.HudLayoutScreen, 0);
        if (selectedResNode == null) return;

        // Create a new HudElementData object with the data of the selected element
        var currentElements = Plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(Plugin)];

        HudElementData selectedHudElement = new HudElementData(selectedResNode);
        HudElementNode selectedNode = new HudElementNode(
            selectedHudElement.PosX, selectedHudElement.PosY,
            selectedHudElement.Width-1, selectedHudElement.Height-1,
            red_color, green_color,
            2.5f, 2f
        );

        // Create guide nodes for all elements except the selected one
        List<HudElementNode> guideNodes = new List<HudElementNode>();
        foreach (var element in currentElements) {
            if (!element.Value.IsVisible) continue;
            if (element.Value.ElementId == selectedHudElement.ElementId) continue;
            guideNodes.Add(new HudElementNode(
                element.Value.PosX, element.Value.PosY,
                element.Value.Width-1, element.Value.Height-1
            ));
        }
        HudElementNode fullScreenNode = new HudElementNode(
            -1, -1, 
            (int)ImGui.GetIO().DisplaySize.X, (int)ImGui.GetIO().DisplaySize.Y
        );
        guideNodes.Add(fullScreenNode);

        // Guide lines are vectors that either go horizontally or vertically from opposite corners of the window through the matching corners/nodes
        // full length lines that go through the whole window
        List<Tuple<Vector2, Color>> guideLines = new List<Tuple<Vector2, Color>>(); 

        // Iterate over all guide nodes and create guideLines and modify anchors when close to anchors of the selected node
        foreach (var currentNode in guideNodes) {
            foreach (var anchor in currentNode.Anchors) {
                foreach (var selectedAnchor in selectedNode.Anchors) {
                    //Vector2 diff = Vector2.Abs(anchor.position - selectedAnchor.position);
                    int diffX = (int)Math.Abs(anchor.position.X - selectedAnchor.position.X);
                    int diffY = (int)Math.Abs(anchor.position.Y - selectedAnchor.position.Y);

                    if (diffX >= MAX_DIFF && diffY >= MAX_DIFF) continue;

                    Color dimmedColor = Color.FromArgb(100, selectedAnchor.color.R, selectedAnchor.color.G, selectedAnchor.color.B);
                    anchor.color = dimmedColor;
                    anchor.size = selectedAnchor.size;

                    if (diffX == 0 || diffY == 0) {
                        anchor.color = selectedAnchor.color;
                        anchor.size = selectedAnchor.size + 1;
                    }

                    if (diffX < MAX_DIFF) {
                        Vector2 horizontalLine = new Vector2(selectedAnchor.position.X, 0);
                        guideLines.Add(new Tuple<Vector2, Color>(horizontalLine, anchor.color));
                    }

                    if (diffY < MAX_DIFF) {
                        Vector2 verticalLine = new Vector2(0, selectedAnchor.position.Y);
                        guideLines.Add(new Tuple<Vector2, Color>(verticalLine, anchor.color));
                    }
                }
            }
        }
        // Draw all anchors of the guide nodes
        guideNodes.Add(selectedNode);
        foreach (var currentNode in guideNodes) {
            foreach (var anchor in currentNode.Anchors) {
                imDrawListPtr.AddCircleFilled(anchor.position, anchor.size, ColorToUint(anchor.color));
            }
        }

        // Draw all guide lines
        if (guideLines.Count > 0) {
            // Filter out duplicate guide lines
            //Plugin.Log.Debug($"Guide lines: {guideLines.Count}");
            guideLines = guideLines.Distinct().ToList();
            //Plugin.Log.Debug($"Guide lines: {guideLines.Count}");
            foreach (var guideLine in guideLines) {
                if (guideLine.Item1.Y == 0) {
                    imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item1 + new Vector2(0, ImGui.GetIO().DisplaySize.Y), ColorToUint(guideLine.Item2), 1.0f);
                } else if (guideLine.Item1.X == 0) {
                    imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item1 + new Vector2(ImGui.GetIO().DisplaySize.X, 0), ColorToUint(guideLine.Item2), 1.0f);
                }
            }
        }
    }
}
