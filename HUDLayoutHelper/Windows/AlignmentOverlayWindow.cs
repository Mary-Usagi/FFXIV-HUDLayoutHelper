using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HUDLayoutHelper.Utilities;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;

namespace HUDLayoutHelper.Windows;

/// <summary>
/// https://www.reddit.com/r/imgui/comments/kknzqw/how_do_i_draw_triangle_in_imgui/
/// https://github.com/ocornut/imgui/issues/3541
/// https://github.com/ocornut/imgui/issues/2274
/// https://www.google.com/search?q=rgba+hex+color+picker&client=firefox-b-d&sca_esv=2ee6c9bbd1613675&biw=1920&bih=919&sxsrf=ADLYWIJksTLF2nIWdhZIH29fiqVjNxpHLQ%3A1730862043613&ei=29sqZ7-HJfyJ7NYPtoqIuAQ&ved=0ahUKEwj_4I7K28aJAxX8BNsEHTYFAkcQ4dUDCA8&uact=5&oq=rgba+hex+color+picker&gs_lp=Egxnd3Mtd2l6LXNlcnAiFXJnYmEgaGV4IGNvbG9yIHBpY2tlcjIIEAAYgAQYywEyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTILEAAYgAQYhgMYigUyCxAAGIAEGIYDGIoFMgsQABiABBiGAxiKBTIIEAAYgAQYogRI9QlQ4AVY_whwAXgBkAEAmAFeoAG9AqoBATS4AQPIAQD4AQGYAgSgAu4BwgIKEAAYsAMY1gQYR8ICDRAAGIAEGLADGEMYigXCAgYQABgHGB6YAwCIBgGQBgqSBwE0oAetGA&sclient=gws-wiz-serp
/// 
/// </summary>
public class AlignmentOverlayWindow : Window, IDisposable {
    private readonly Plugin _plugin;
    private readonly Configuration Configuration;
    internal bool ToggledOnByUser { get; set; } = false;
    public AlignmentOverlayWindow(Plugin plugin) : base("Overlay") {
        Flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs;
        //Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = ImGui.GetIO().DisplaySize,
            MaximumSize = ImGui.GetIO().DisplaySize
        };
        Position = new Vector2(0, 0);

        _plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        Configuration = plugin.Configuration;
    }

    public override void OnOpen() {
        _plugin.UpdatePreviousElements();
    }

    public void Dispose() { }

    public override void PreDraw() {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        Flags |= ImGuiWindowFlags.NoMove;
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

    public unsafe override void PreOpenCheck() {
        // If any of these conditions are not met, the window will not be opened
        this.IsOpen = this.ToggledOnByUser 
            && Plugin.ClientState.IsLoggedIn 
            && Plugin.PlayerState is { ClassJob.RowId: var classJobId } 
            && _plugin.AgentHudLayout != null && _plugin.HudLayoutScreen != null;
    }

    /// <summary>
    ///  TODO
    /// </summary>
    public unsafe override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        //ImDrawList* draw_list = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        ImDrawListPtr imDrawListPtr = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());

        // get current element data 
        AtkResNode* selectedResNode = Utils.GetCollisionNodeByIndex(_plugin.HudLayoutScreen, 0);
        if (selectedResNode == null) return;

        // Create a new HudElementData object with the data of the selected element
        var hudLayoutElements = _plugin.previousHudLayoutIndexElements[Utils.GetCurrentHudLayoutIndex(_plugin, false)];

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
            let isHorizontal = diff.Y < MAX_ANCHOR_DIFF
            let isVertical = diff.X < MAX_ANCHOR_DIFF
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
                    start = new Vector2(referenceAnchorPoints.Min(x => x.position.X) - guideLinePadding, selectedNodeAnchor.position.Y),
                    end = new Vector2(referenceAnchorPoints.Max(x => x.position.X) + guideLinePadding, selectedNodeAnchor.position.Y),
                    color = horizontalAlignments.Any(x => (int)x.diff.Y == 0) ? selectedNodeAnchor.color : dimmedColor
                };
                //Plugin.Log.Debug($"Horizontal line: {horizontalLine.start} -> {horizontalLine.end}");
                overlayGuideLines.Add((horizontalLine.start, horizontalLine.end, horizontalLine.color));
            }
            if (verticalAlignments.Count > 0) {
                var verticalLine = new {
                    start = new Vector2(selectedNodeAnchor.position.X, referenceAnchorPoints.Min(x => x.position.Y) - guideLinePadding),
                    end = new Vector2(selectedNodeAnchor.position.X, referenceAnchorPoints.Max(x => x.position.Y) + guideLinePadding),
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
            overlayGuideLines = [.. overlayGuideLines.Distinct()];
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
