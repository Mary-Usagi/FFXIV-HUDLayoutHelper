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
public class AlignmentOverlayWindow : Window, IDisposable {
    private Plugin Plugin;
    private Configuration Configuration;

    public AlignmentOverlayWindow(Plugin plugin) : base("Overlay"){
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

    private class HudOverlayNode {
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

        internal SortedDictionary<string, Anchor> AnchorMap { get; }

        internal Anchor TopLeft => AnchorMap["TopLeft"];
        internal Anchor TopRight => AnchorMap["TopRight"];
        internal Anchor BottomLeft => AnchorMap["BottomLeft"];
        internal Anchor BottomRight => AnchorMap["BottomRight"];
        internal Anchor Center => AnchorMap["Center"];

        internal HudOverlayNode(
            short left, short top, 
            int width, int height, 
            Color? centerColor = null, Color? cornerColor = null, 
            float centerSize = 1.5f, float cornerSize = 1.5f
        ) {
            Vector2 topLeft = new Vector2(left, top);
            Vector2 center = topLeft + new Vector2(MathF.Round(width / 2f, MidpointRounding.AwayFromZero), MathF.Round(height / 2f, MidpointRounding.AwayFromZero));

            AnchorMap = new SortedDictionary<string, Anchor> {
                { "TopLeft", new Anchor(topLeft, cornerColor ?? blackColor, cornerSize) },
                { "TopRight", new Anchor(topLeft + new Vector2(width, 0), cornerColor ?? blackColor, cornerSize) },
                { "BottomLeft", new Anchor(topLeft + new Vector2(0, height), cornerColor ?? blackColor, cornerSize) },
                { "BottomRight", new Anchor(topLeft + new Vector2(width, height), cornerColor ?? blackColor, cornerSize) },
                { "Center", new Anchor(center, centerColor ?? blackColor, centerSize) }
            };
        }
    }

    static readonly Color redColor = Color.FromArgb(255, 0, 0);
    static readonly Color blueColor = Color.FromArgb(0, 0, 255);
    static readonly Color greenColor = Color.FromArgb(0, 255, 0);
    static readonly Color blackColor = Color.FromArgb(175, 0, 0, 0); 

    const int MAX_ANCHOR_DIFF = 10;
    const int guideLinePadding = 25;

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
        var hudLayoutElements = Plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(Plugin)];

        HudElementData selectedHudElement = new HudElementData(selectedResNode);
        HudOverlayNode selectedHudOverlayNode = new HudOverlayNode(
            selectedHudElement.PosX, selectedHudElement.PosY,
            selectedHudElement.Width-1, selectedHudElement.Height-1,
            redColor, greenColor,
            2.5f, 2f
        );

        // Create guide nodes for all elements except the selected one
        List<HudOverlayNode> otherHudOverlayNodes = new List<HudOverlayNode>();
        foreach (var element in hudLayoutElements) {
            if (!element.Value.IsVisible) continue;
            if (element.Value.ElementId == selectedHudElement.ElementId) continue;
            otherHudOverlayNodes.Add(new HudOverlayNode(
                element.Value.PosX, element.Value.PosY,
                element.Value.Width-1, element.Value.Height-1
            ));
        }
        HudOverlayNode fullScreenHudOverlayNode = new HudOverlayNode(
            -1, -1, 
            (int)ImGui.GetIO().DisplaySize.X, (int)ImGui.GetIO().DisplaySize.Y
        );
        otherHudOverlayNodes.Add(fullScreenHudOverlayNode);

        var alignedAnchors = (
            from selectedAnchor in selectedHudOverlayNode.AnchorMap
                let dimmedColor = Color.FromArgb(100, selectedAnchor.Value.color.R, selectedAnchor.Value.color.G, selectedAnchor.Value.color.B)
            from otherNode in otherHudOverlayNodes
                from otherAnchor in otherNode.AnchorMap
                    let diff = Vector2.Abs(otherAnchor.Value.position - selectedAnchor.Value.position)
                    let isHorizontal = diff.Y < MAX_ANCHOR_DIFF
                    let isVertical = diff.X < MAX_ANCHOR_DIFF
            where isHorizontal || isVertical
            select new { otherNode, otherAnchor=otherAnchor.Value, otherAnchorName=otherAnchor, selectedAnchor=selectedAnchor.Value, selectedAnchorName=selectedAnchor, diff, dimmedColor, isHorizontal, isVertical }
        ).ToList();

        // Set dimmed anchor colors
        alignedAnchors.ForEach(x => {
            x.otherAnchor.color = x.dimmedColor;
            x.otherAnchor.size = x.selectedAnchor.size;
        });

        // Set non-dimmed anchor colors
        alignedAnchors.Where(x => (int)x.diff.X == 0 || (int)x.diff.Y == 0).ToList().ForEach(x => {
            x.otherAnchor.color = x.selectedAnchor.color;
            x.otherAnchor.size = x.selectedAnchor.size + 1;
        });

        // Create guide lines
        List<(Vector2, Vector2, Color)> overlayGuideLines = new List<(Vector2, Vector2, Color)>();
        foreach (var group in alignedAnchors.GroupBy(x => (x.otherAnchor, x.selectedAnchor))) {
            var otherNode = group.First().otherNode;
            var selectedAnchor = group.First().selectedAnchor;
            var dimmedColor = group.First().dimmedColor;

            List<HudOverlayNode.Anchor> referenceAnchorPoints = [
                otherNode.TopLeft,
                otherNode.BottomRight,
                selectedHudOverlayNode.TopLeft,
                selectedHudOverlayNode.BottomRight
            ];

            var horizontalAlignments = group.Where(x => x.isHorizontal).ToList();
            var verticalAlignments = group.Where(x => x.isVertical).ToList();

            if (horizontalAlignments.Count > 0) {
                var horizontalLine = new {
                    start = new Vector2(referenceAnchorPoints.Min(x => x.position.X) - guideLinePadding, selectedAnchor.position.Y),
                    end = new Vector2(referenceAnchorPoints.Max(x => x.position.X) + guideLinePadding, selectedAnchor.position.Y),
                    color = horizontalAlignments.First().otherAnchor.color
                };
                //Plugin.Log.Debug($"Horizontal line: {horizontalLine.start} -> {horizontalLine.end}");
                overlayGuideLines.Add((horizontalLine.start, horizontalLine.end, horizontalLine.color));
            }
            if (verticalAlignments.Count > 0) {
                var verticalLine = new {
                    start = new Vector2(selectedAnchor.position.X, referenceAnchorPoints.Min(x => x.position.Y) - guideLinePadding),
                    end = new Vector2(selectedAnchor.position.X, referenceAnchorPoints.Max(x => x.position.Y) + guideLinePadding),
                    color = verticalAlignments.First().otherAnchor.color
                };
                //Plugin.Log.Debug($"Vertical line: {verticalLine.start} -> {verticalLine.end}");
                overlayGuideLines.Add((verticalLine.start, verticalLine.end, verticalLine.color));
            }
        }

        // Draw all anchors of the guide nodes
        otherHudOverlayNodes.Add(selectedHudOverlayNode);
        foreach (var otherNode in otherHudOverlayNodes) {
            foreach (var otherAnchor in otherNode.AnchorMap.Values) {
                imDrawListPtr.AddCircleFilled(otherAnchor.position, otherAnchor.size, ColorToUint(otherAnchor.color));
            }
        }

        if (overlayGuideLines.Count > 0) {
            overlayGuideLines = overlayGuideLines.Distinct().ToList();
            //Plugin.Log.Debug($"Guide lines: {guideLines.Count}");
            //foreach (var guideLine in guideLines) {
            //    Plugin.Log.Debug($"Guide line: {guideLine.Item1} -> {guideLine.Item2} ({guideLine.Item3})");
            //}
            foreach (var guideLine in overlayGuideLines) {
                imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item2, ColorToUint(guideLine.Item3), 1.0f);
            }
        }
    }
}
