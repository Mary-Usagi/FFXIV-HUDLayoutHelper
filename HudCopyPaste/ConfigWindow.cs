using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace HudCopyPaste;

public class ConfigWindow : Window, IDisposable {
    private Plugin Plugin;
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) : base("Hud Copy Paste Controls"){
        Flags = ImGuiWindowFlags.AlwaysUseWindowPadding;

        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(290, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    private string[][] keybindDescriptions = [
        ["Ctrl + C", "Copy selected HUD element"],
        ["Ctrl + V", "Paste copied HUD element"],
        ["Ctrl + Z", "Undo last action"],
        ["Ctrl + Y", "Redo last action"]
    ];

    public override void PreDraw() {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (Configuration.IsConfigWindowMovable) {
            Flags &= ~ImGuiWindowFlags.NoMove;
        } else {
            Flags |= ImGuiWindowFlags.NoMove;
        }
    }

    public override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        if (ImGui.BeginTabBar("##TabBar")) {
            if (ImGui.BeginTabItem("Settings##TabItem1")) {
                DrawSettings();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Keybinds##TabItem2")) {
                DrawKeybinds();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug Info##TabItem3")) { 
                if (ImGui.BeginTabBar("##TabBarHudLayouts")) {
                    for (var i = 0; i < Plugin.HudHistoryManager.HudLayoutCount; i++) {
                        if (ImGui.BeginTabItem($"HUD {i+1}##TabItemHudLayout{i}")) {
                            ImGui.BeginChild($"##Child {i}", new Vector2(0, 0), false);
                            DrawDebugInfo(i);
                            ImGui.EndChild();
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    internal void DrawDebugInfo(int hudLayout) {
        ImGui.Spacing();
        ImGui.Columns(2, $"##Columns {hudLayout}", true);
        ImGui.Text("Undo History");
        // Table representing the current state of the undo history
        ImGui.BeginTable($"##Table2 {hudLayout}", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn($"##Column3 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn($"##Column4 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn($"##Column5 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 190f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");

        var undoHistory = Plugin.HudHistoryManager.undoHistory[hudLayout];
        for (var i = 0; i < undoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(undoHistory[i].PreviousState.ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.Text($"({undoHistory[i].PreviousState.PosX}, {undoHistory[i].PreviousState.PosY}) -> ({undoHistory[i].NewState.PosX}, {undoHistory[i].NewState.PosY})");
        }
        for (var i = undoHistory.Count; i < Plugin.HudHistoryManager.MaxHistorySize; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text("");
            ImGui.TableNextColumn();
            ImGui.Text("");
        }
        ImGui.EndTable();
        ImGui.Spacing();

        ImGui.NextColumn();
        ImGui.Text("Redo History");
        // Table representing the current state of the redo history
        ImGui.BeginTable($"##Table3 {hudLayout}", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn($"##Column6 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn($"##Column7 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn($"##Column8 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 190f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");

        var redoHistory = Plugin.HudHistoryManager.redoHistory[hudLayout];
        for (var i = 0; i < redoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(redoHistory[i].NewState.ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.Text($"({redoHistory[i].PreviousState.PosX}, {redoHistory[i].PreviousState.PosY}) -> ({redoHistory[i].NewState.PosX}, {redoHistory[i].NewState.PosY})");
        }
        for (var i = redoHistory.Count; i < Plugin.HudHistoryManager.MaxHistorySize; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text("");
            ImGui.TableNextColumn();
            ImGui.Text("");
        }
        ImGui.EndTable();
        ImGui.Spacing();
    }


    internal void DrawSettings() {
        // can't ref a property, so use a local copy
        int maxUndoHistorySize = Configuration.MaxUndoHistorySize;
        HudHistoryManager.RedoStrategy redoActionStrategy = Configuration.RedoActionStrategy;

        ImGui.Spacing();
        // Max Undo History Size
        ImGui.Text("Max Size of Undo History:");
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("The maximum number of actions that can be undone");
        }
        if (ImGui.InputInt("", ref maxUndoHistorySize)) {
            if (maxUndoHistorySize < 1) {
                maxUndoHistorySize = 1;
            }
            Configuration.MaxUndoHistorySize = maxUndoHistorySize;
        }
        ImGui.Spacing();
        //ImGui.MenuItem("Strategy to apply when an action is performed after undoing");
        ImGui.Text("Redo Strategy on Action:");
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Strategy to apply when an action is performed after undoing");
        }

        // Redo Strategy
        HudHistoryManager.RedoStrategy[] redoStrategies = (HudHistoryManager.RedoStrategy[])Enum.GetValues(typeof(HudHistoryManager.RedoStrategy));
        int itemSelectedIndex = Array.IndexOf(redoStrategies, redoActionStrategy);
        string combo_preview_value = Configuration.RedoActionStrategy.ToString();

        if (ImGui.BeginCombo("##RedoStrategy", combo_preview_value)) {
            for (int n = 0; n < redoStrategies.Length; n++) {
                bool is_selected = itemSelectedIndex == n;
                if (ImGui.Selectable(redoStrategies[n].ToString(), is_selected)) {
                    // TODO: not showing tooltips yet
                    redoActionStrategy = redoStrategies[n];
                    Configuration.RedoActionStrategy = redoActionStrategy;
                    itemSelectedIndex = n;
                }
                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip(HudHistoryManager.RedoStrategyDescriptions[redoStrategies[n]]);
                }
                // Set the initial focus when opening the combo (scrolling + keyboard navigation focus)
                if (is_selected) {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip(HudHistoryManager.RedoStrategyDescriptions[redoStrategies[itemSelectedIndex]]);
        }


        if (ImGui.Button("Save")) {
            Plugin.Log.Debug("Saving configuration");
            if (!Plugin.HudHistoryManager.SetHistorySize(maxUndoHistorySize)) {
                Configuration.MaxUndoHistorySize = 50;
                Plugin.Log.Warning("Failed to set history size");
            }
            Plugin.HudHistoryManager.SetRedoStrategy(redoActionStrategy);
            Configuration.Save();
        }

    }

    internal void DrawKeybinds() {
        ImGui.Spacing();

        ImGui.Text(" (Only when in HUD Layout Editor)");
        ImGui.Spacing();

        ImGui.BeginTable("##Table1", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX);
        ImGui.TableSetupColumn("##Column1", ImGuiTableColumnFlags.WidthFixed, 75f);
        ImGui.TableSetupColumn("##Column2", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextColumn();
        ImGui.TableHeader("Keybinds");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Description");

        foreach (var keybind in keybindDescriptions) {
            for (var i = 0; i < keybind.Length; i++) {
                ImGui.TableNextColumn();
                ImGui.Text(keybind[i]);
            }
        }
        ImGui.EndTable();
    }
}
