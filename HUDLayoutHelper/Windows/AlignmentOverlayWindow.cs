using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HUDLayoutHelper.Utilities;
using ImGuiNET;

namespace HUDLayoutHelper.Windows;

/// <summary>
/// https://www.reddit.com/r/imgui/comments/kknzqw/how_do_i_draw_triangle_in_imgui/
/// https://github.com/ocornut/imgui/issues/3541
/// https://github.com/ocornut/imgui/issues/2274
/// https://www.google.com/search?q=rgba+hex+color+picker&client=firefox-b-d&sca_esv=2ee6c9bbd1613675&biw=1920&bih=919&sxsrf=ADLYWIJksTLF2nIWdhZIH29fiqVjNxpHLQ%3A1730862043613&ei=29sqZ7-HJfyJ7NYPtoqIuAQ&ved=0ahUKEwj_4I7K28aJAxX8BNsEHTYFAkcQ4dUDCA8&uact=5&oq=rgba+hex+color+picker&gs_lp=Egxnd3Mtd2l6LXNlcnAiFXJnYmEgaGV4IGNvbG9yIHBpY2tlcjIIEAAYgAQYywEyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTILEAAYgAQYhgMYigUyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTIIEAAYgAQYogRI9QlQ4AVY_whwAXgBkAEAmAFeoAG9AqoBATS4AQPIAQD4AQGYAgSgAu4BwgIKEAAYsAMY1gQYR8ICDRAAGIAEGLADGEMYigXCAgYQABgHGB6YAwCIBgGQBgqSBwE0oAetGA&sclient=gws-wiz-serp
/// 
/// </summary>
public class AlignmentOverlayWindow : Window, IDisposable {
    private Plugin Plugin { get; }
    private Configuration Configuration { get; }

    public AlignmentOverlayWindow(Plugin plugin) : base("Overlay") {
        this.Flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs;
        //Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs;

        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = ImGui.GetIO().DisplaySize,
            MaximumSize = ImGui.GetIO().DisplaySize
        };
        this.Position = new Vector2(0, 0);

        this.Plugin = plugin;
        this.SizeCondition = ImGuiCond.Always;
        this.PositionCondition = ImGuiCond.Always;
        this.Configuration = plugin.Configuration;
    }

    public override void OnOpen() {
        this.Plugin.UpdatePreviousElements();
    }

    public void Dispose() { }

    public override void PreDraw() {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        this.Flags |= ImGuiWindowFlags.NoMove;
    }

    internal static uint ColorToUint(Color color) {
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

        internal Anchor TopLeft => this.AnchorMap["TopLeft"];
        internal Anchor TopRight => this.AnchorMap["TopRight"];
        internal Anchor BottomLeft => this.AnchorMap["BottomLeft"];
        internal Anchor BottomRight => this.AnchorMap["BottomRight"];
        internal Anchor Center => this.AnchorMap["Center"];

        internal HudOverlayNode(
            short left, short top,
            int width, int height,
            Color? centerColor = null, Color? cornerColor = null,
            float centerSize = 1.5f, float cornerSize = 1.5f
        ) {
            Vector2 topLeft = new Vector2(left, top);
            Vector2 center = topLeft + new Vector2(MathF.Round(width / 2f, MidpointRounding.AwayFromZero), MathF.Round(height / 2f, MidpointRounding.AwayFromZero));

            this.AnchorMap = new SortedDictionary<string, Anchor> {
                { "TopLeft", new Anchor(topLeft, cornerColor ?? blackColor, cornerSize) },
                { "TopRight", new Anchor(topLeft + new Vector2(width, 0), cornerColor ?? blackColor, cornerSize) },
                { "BottomLeft", new Anchor(topLeft + new Vector2(0, height), cornerColor ?? blackColor, cornerSize) },
                { "BottomRight", new Anchor(topLeft + new Vector2(width, height), cornerColor ?? blackColor, cornerSize) },
                { "Center", new Anchor(center, centerColor ?? blackColor, centerSize) }
            };
        }
    }

    private static readonly Color redColor = Color.FromArgb(255, 0, 0);
    private static readonly Color blueColor = Color.FromArgb(0, 0, 255);
    private static readonly Color greenColor = Color.FromArgb(0, 255, 0);
    private static readonly Color blackColor = Color.FromArgb(175, 0, 0, 0);

    private const int MaxAnchorDist = 10;
    private const int GuideLinePadding = 25;

    /// <summary>
    ///  TODO
    /// </summary>
    public unsafe override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        //ImDrawList* draw_list = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        if (!this.Plugin.ClientState.IsLoggedIn) return;
        if (this.Plugin.ClientState is not { LocalPlayer.ClassJob.RowId: var classJobId }) return;
        if (this.Plugin.AgentHudLayout == null || this.Plugin.HudLayoutScreen == null) return;

        ImDrawListPtr imDrawListPtr = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());

        // get current element data 
        AtkResNode* selectedResNode = Utils.GetCollisionNodeByIndex(this.Plugin.HudLayoutScreen, 0);
        if (selectedResNode == null) return;

        // Create a new HudElementData object with the data of the selected element
        var hudLayoutElements = this.Plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(this.Plugin, false)];

        HudElementData selectedHudElement = new HudElementData(selectedResNode);
        HudOverlayNode selectedHudOverlayNode = new HudOverlayNode(
            selectedHudElement.PosX, selectedHudElement.PosY,
            selectedHudElement.Width - 1, selectedHudElement.Height - 1,
            redColor, greenColor,
            2.5f, 2f
        );
        //Plugin.Log.Debug($"hudLayoutElements: {hudLayoutElements.Count}");
        // Create guide nodes for all elements except the selected one
        List<HudOverlayNode> otherHudOverlayNodes = [];
        foreach (var element in hudLayoutElements) {
            if (!element.Value.IsVisible) continue;
            if (element.Value.ElementId == selectedHudElement.ElementId) continue;
            otherHudOverlayNodes.Add(new HudOverlayNode(
                element.Value.PosX, element.Value.PosY,
                element.Value.Width - 1, element.Value.Height - 1
            ));
        }
        HudOverlayNode fullScreenHudOverlayNode = new HudOverlayNode(
            -1, -1,
            (int)ImGui.GetIO().DisplaySize.X, (int)ImGui.GetIO().DisplaySize.Y
        );
        otherHudOverlayNodes.Add(fullScreenHudOverlayNode);
        //Plugin.Log.Debug($"Other nodes: {otherHudOverlayNodes.Count}");

        var alignedAnchors = (
            from selectedNodeAnchor in selectedHudOverlayNode.AnchorMap
            let dimmedColor = Color.FromArgb(100, selectedNodeAnchor.Value.color.R, selectedNodeAnchor.Value.color.G, selectedNodeAnchor.Value.color.B)
            from otherNode in otherHudOverlayNodes
            from otherNodeAnchor in otherNode.AnchorMap
            let diff = Vector2.Abs(otherNodeAnchor.Value.position - selectedNodeAnchor.Value.position)
            let isHorizontal = diff.Y < MaxAnchorDist
            let isVertical = diff.X < MaxAnchorDist
            where isHorizontal || isVertical
            select new { otherNode, otherNodeAnchor = otherNodeAnchor.Value, otherNodeAnchorName = otherNodeAnchor, selectedNodeAnchor = selectedNodeAnchor.Value, selectedNodeAnchorName = selectedNodeAnchor, diff, dimmedColor, isHorizontal, isVertical }
        ).ToList();

        //Plugin.Log.Debug($"Aligned anchors: {alignedAnchors.Count}");

        // Set dimmed anchor colors
        alignedAnchors.ForEach(x => {
            x.otherNodeAnchor.color = x.dimmedColor;
            x.otherNodeAnchor.size = x.selectedNodeAnchor.size;
        });

        // Set non-dimmed anchor colors
        alignedAnchors.Where(x => (int)x.diff.X == 0 || (int)x.diff.Y == 0).ToList().ForEach(x => {
            x.otherNodeAnchor.color = x.selectedNodeAnchor.color;
            x.otherNodeAnchor.size = x.selectedNodeAnchor.size + 1;
        });

        // Create guide lines
        List<(Vector2, Vector2, Color)> overlayGuideLines = [];

        foreach (var group in alignedAnchors.GroupBy(x => (x.otherNodeAnchor, x.selectedNodeAnchor))) {
            var otherNode = group.First().otherNode;
            var selectedNodeAnchor = group.First().selectedNodeAnchor;
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
                    start = new Vector2(referenceAnchorPoints.Min(x => x.position.X) - GuideLinePadding, selectedNodeAnchor.position.Y),
                    end = new Vector2(referenceAnchorPoints.Max(x => x.position.X) + GuideLinePadding, selectedNodeAnchor.position.Y),
                    color = horizontalAlignments.Any(x => (int)x.diff.Y == 0) ? selectedNodeAnchor.color : dimmedColor
                };
                //Plugin.Log.Debug($"Horizontal line: {horizontalLine.start} -> {horizontalLine.end}");
                overlayGuideLines.Add((horizontalLine.start, horizontalLine.end, horizontalLine.color));
            }
            if (verticalAlignments.Count > 0) {
                var verticalLine = new {
                    start = new Vector2(selectedNodeAnchor.position.X, referenceAnchorPoints.Min(x => x.position.Y) - GuideLinePadding),
                    end = new Vector2(selectedNodeAnchor.position.X, referenceAnchorPoints.Max(x => x.position.Y) + GuideLinePadding),
                    color = verticalAlignments.Any(x => (int)x.diff.X == 0) ? selectedNodeAnchor.color : dimmedColor
                };
                //Plugin.Log.Debug($"Vertical line: {verticalLine.start} -> {verticalLine.end}");
                overlayGuideLines.Add((verticalLine.start, verticalLine.end, verticalLine.color));
            }
        }

        // Draw all anchors of the guide nodes
        otherHudOverlayNodes.Add(selectedHudOverlayNode);
        foreach (var otherNode in otherHudOverlayNodes) {
            foreach (var otherNodeAnchor in otherNode.AnchorMap.Values) {
                imDrawListPtr.AddCircleFilled(otherNodeAnchor.position, otherNodeAnchor.size, ColorToUint(otherNodeAnchor.color));
            }
        }

        if (overlayGuideLines.Count > 0) {
            //Plugin.Log.Debug($"Guide lines: {overlayGuideLines.Count}");
            overlayGuideLines = overlayGuideLines.Distinct().ToList();
            //Plugin.Log.Debug($"Guide lines: {overlayGuideLines.Count}");
            //foreach (var guideLine in overlayGuideLines) {
            //    Plugin.Log.Debug($"Guide line: {guideLine.Item1} -> {guideLine.Item2} ({guideLine.Item3})");
            //}
            foreach (var guideLine in overlayGuideLines) {
                imDrawListPtr.AddLine(guideLine.Item1, guideLine.Item2, ColorToUint(guideLine.Item3), 1.0f);
            }
        }
    }
}
