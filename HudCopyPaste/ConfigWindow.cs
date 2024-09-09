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

        // TODO: autoamtic min size? + resizable 
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
            if (ImGui.BeginTabItem("Settings")) {
                DrawSettings();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Keybinds")) {
                DrawKeybinds();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Debug Info")) {
                DrawDebugInfo();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    internal void DrawDebugInfo() {
        ImGui.Spacing();
        ImGui.Columns(2, "##Columns", true);
        ImGui.Text("Undo History");
        // Table representing the current state of the undo history
        ImGui.BeginTable("##Table2", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("##Column3", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn("##Column4", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn("##Column5", ImGuiTableColumnFlags.WidthFixed, 90f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");

        var undoHistory = Plugin.undoHistory;
        for (var i = 0; i < undoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(undoHistory[i].ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.Text($"({undoHistory[i].PosX}, {undoHistory[i].PosY})");
        }
        for (var i = undoHistory.Count; i < Configuration.MaxUndoHistorySize; i++) {
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
        ImGui.BeginTable("##Table3", 3, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn("##Column6", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn("##Column7", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn("##Column8", ImGuiTableColumnFlags.WidthFixed, 90f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");

        var redoHistory = Plugin.redoHistory;
        for (var i = 0; i < redoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(redoHistory[i].ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.Text($"({redoHistory[i].PosX}, {redoHistory[i].PosY})");
        }
        for (var i = redoHistory.Count; i < Configuration.MaxUndoHistorySize; i++) {
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
        // TODO: add save button
        // can't ref a property, so use a local copy
        var maxUndoHistorySize = Configuration.MaxUndoHistorySize;

        ImGui.Spacing();
        ImGui.Text("Max Undo History Size");
        if (ImGui.InputInt("", ref maxUndoHistorySize)) {
            Configuration.MaxUndoHistorySize = maxUndoHistorySize;
        }
        if (ImGui.Button("Save")) {
            Plugin.Log.Debug("Saving configuration");
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
