using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace HUDLayoutShortcuts;

public class ConfigWindow : Window, IDisposable {
    private Plugin Plugin;
    private Configuration Configuration;
    private List<Tuple<string, Action>> TabActions = new List<Tuple<string, Action>>();

    private string? OpenTab; 

    public ConfigWindow(Plugin plugin) : base("HUD Layout Shortcuts Settings"){
        // Initialize the tab actions
        TabActions.Add(new Tuple<string, Action>("About##TabItemAbout", DrawAbout));
        TabActions.Add(new Tuple<string, Action>("Keybinds##TabItemKeybinds", DrawKeybinds));
        TabActions.Add(new Tuple<string, Action>("Settings##TabItemSettings", DrawSettings));
        TabActions.Add(new Tuple<string, Action>("Debug Info##TabItemDebug", DrawDebugInfoTab));
        OpenTab = TabActions[0].Item1;

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

    private string[][] KeybindDescriptions = [
        ["Ctrl + C", "Copy position of selected HUD element"],
        ["Ctrl + V", "Paste copied position to selected HUD element"],
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


    public unsafe static bool BeginTabItem(string label, ImGuiTabItemFlags flags) {
        int num = 0;
        byte* ptr;
        if (label != null) {
            num = Encoding.UTF8.GetByteCount(label);
            Span<byte> span = (num <= 2048) ? stackalloc byte[num + 1] : new byte[num + 1];
            fixed (byte* spanPtr = span) {
                fixed (char* labelPtr = label) {
                    Encoding.UTF8.GetBytes(labelPtr, label.Length, spanPtr, num);
                }
                span[num] = 0;
                ptr = spanPtr;
            }
        } else {
            ptr = null;
        }

        byte* p_open2 = null;
        byte num2 = ImGuiNative.igBeginTabItem(ptr, p_open2, flags);
        if (num > 2048) {
            Marshal.FreeHGlobal((IntPtr)ptr);
        }

        return num2 != 0;
    }



    public unsafe override void Draw() {
        // See: https://github.com/ocornut/imgui/blob/master/imgui_demo.cpp
        if (ImGui.BeginTabBar("##TabBar", ImGuiTabBarFlags.None)) {
            foreach (var tab in TabActions) {
                var flags = ImGuiTabItemFlags.None;
                if (OpenTab == tab.Item1) {
                    flags |= ImGuiTabItemFlags.SetSelected;
                    OpenTab = null;
                }
                if (BeginTabItem(tab.Item1, flags)) { 
                    tab.Item2();
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }

    internal void DrawDebugInfoTab() {
        ImGui.TextWrapped($"Current HUD Layout Index: {Utils.GetCurrentHudLayoutIndex(this.Plugin, false)}");
        ImGui.Spacing();
        if (ImGui.BeginTabBar("##TabBarHudLayouts")) {
            for (var i = 0; i < Plugin.HudHistoryManager.HudLayoutCount; i++) {
                if (ImGui.BeginTabItem($"HUD {i + 1}##TabItemHudLayout{i}")) {
                    ImGui.BeginChild($"##Child {i}", new Vector2(0, 0), false);
                    DrawDebugInfo(i);
                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }

    internal void DrawDebugInfo(int hudLayout) {        
        ImGui.Spacing();
        ImGui.Columns(2, $"##Columns {hudLayout}", true);
        ImGui.TextWrapped("Undo History");
        // Table representing the current state of the undo history
        ImGui.BeginTable($"##Table2 {hudLayout}", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn($"##Column3 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn($"##Column4 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn($"##Column5 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableSetupColumn($"##Column5.1 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 50f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Saved");

        var undoHistory = Plugin.HudHistoryManager.undoHistory[hudLayout];
        for (var i = 0; i < undoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.TextWrapped(i.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(undoHistory[i].PreviousState.ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.TextWrapped($"({undoHistory[i].PreviousState.PosX}, {undoHistory[i].PreviousState.PosY}) -> ({undoHistory[i].NewState.PosX}, {undoHistory[i].NewState.PosY})");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(undoHistory[i].Saved ? "x" : "");
        }
        for (var i = undoHistory.Count; i < Plugin.HudHistoryManager.MaxHistorySize; i++) {
            ImGui.TableNextColumn();
            ImGui.TextWrapped(i.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
        }
        ImGui.EndTable();
        ImGui.Spacing();

        ImGui.NextColumn();
        ImGui.TextWrapped("Redo History");
        // Table representing the current state of the redo history
        ImGui.BeginTable($"##Table3 {hudLayout}", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV);
        ImGui.TableSetupColumn($"##Column6 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 25f);
        ImGui.TableSetupColumn($"##Column7 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 100f);
        ImGui.TableSetupColumn($"##Column8 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 190f);
        ImGui.TableSetupColumn($"##Column8.1 {hudLayout}", ImGuiTableColumnFlags.WidthFixed, 50f);

        ImGui.TableNextColumn();
        ImGui.TableHeader("i");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Name");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Position");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Saved");

        var redoHistory = Plugin.HudHistoryManager.redoHistory[hudLayout];
        for (var i = 0; i < redoHistory.Count; i++) {
            ImGui.TableNextColumn();
            ImGui.TextWrapped(i.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(redoHistory[i].NewState.ResNodeDisplayName);
            ImGui.TableNextColumn();
            ImGui.TextWrapped($"({redoHistory[i].PreviousState.PosX}, {redoHistory[i].PreviousState.PosY}) -> ({redoHistory[i].NewState.PosX}, {redoHistory[i].NewState.PosY})");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(redoHistory[i].Saved ? "x" : "");
        }
        for (var i = redoHistory.Count; i < Plugin.HudHistoryManager.MaxHistorySize; i++) {
            ImGui.TableNextColumn();
            ImGui.TextWrapped(i.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
            ImGui.TableNextColumn();
            ImGui.TextWrapped("");
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
        ImGui.TextWrapped("Max Size of Undo History:");
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
        ImGui.TextWrapped("Redo Strategy on Action:");
        if (ImGui.IsItemHovered()) {
            ImGui.SetTooltip("Strategy to apply when an action is performed after undoing");
        }

        // Redo Strategy
        HudHistoryManager.RedoStrategy[] redoStrategies = (HudHistoryManager.RedoStrategy[])Enum.GetValues(typeof(HudHistoryManager.RedoStrategy));
        int itemSelectedIndex = Array.IndexOf(redoStrategies, redoActionStrategy);
        string combo_preview_value = Configuration.RedoActionStrategy.ToString();

        for (int n = 0; n < redoStrategies.Length; n++) {
            bool is_selected = itemSelectedIndex == n;
            if (ImGui.RadioButton(redoStrategies[n].ToString(), is_selected)) {
                redoActionStrategy = redoStrategies[n];
                Configuration.RedoActionStrategy = redoActionStrategy;
                itemSelectedIndex = n;
            }
            if (ImGui.IsItemHovered()) {
                ImGui.SetTooltip(HudHistoryManager.RedoStrategyDescriptions[redoStrategies[n]]);
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Spacing();
        if (ImGui.Button("Save & Apply Settings")) {
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

        ImGui.TextWrapped("Keybinds that are available when in the HUD Layout Editor:");
        ImGui.Spacing();

        ImGui.BeginTable("##Table1", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX);
        ImGui.TableSetupColumn("##Column1", ImGuiTableColumnFlags.WidthFixed, 75f);
        ImGui.TableSetupColumn("##Column2", ImGuiTableColumnFlags.WidthStretch);

        ImGui.TableNextColumn();
        ImGui.TableHeader("Keybinds");
        ImGui.TableNextColumn();
        ImGui.TableHeader("Description");

        foreach (var keybind in KeybindDescriptions) {
            for (var i = 0; i < keybind.Length; i++) {
                ImGui.TableNextColumn();
                ImGui.TextWrapped(keybind[i]);
            }
        }
        ImGui.EndTable();
    }

    internal void DrawAbout() {
        ImGui.TextWrapped("About HUD Layout Shortcuts:");
        ImGui.Separator();
        ImGui.TextWrapped("This plugin helps manage and customize the HUD layout when being in the native HUD Layout editor of FFXIV. Use the provided keybinds to copy, paste, undo, and redo changes to HUD elements.");
        ImGui.Spacing();
        ImGui.TextWrapped("How to Use:");
        ImGui.BulletText("Open the native HUD Layout Editor.");

        ImGui.BulletText("Copy and Paste:");
        ImGui.Indent();
        ImGui.Bullet();
        ImGui.TextWrapped("Select the HUD element you want to copy and press the 'Copy' keybind, which saves the elements position to the clipboard.");
        ImGui.Bullet();
        ImGui.TextWrapped("Do anything you want, then select the same or another HUD element and press the 'Paste' keybind to apply the copied position.");
        ImGui.Unindent();

        ImGui.BulletText("Undo and Redo:");
        ImGui.Indent();
        ImGui.Bullet();
        ImGui.TextWrapped("Press the 'Undo' keybind to revert the last action, which includes copying, pasting, and moving HUD elements.");
        ImGui.Bullet();
        ImGui.TextWrapped("Press the 'Redo' keybind to reapply the last undone action.");
        ImGui.Bullet();
        ImGui.TextWrapped("Moving any HUD element after undoing will clear the redo history!");
        ImGui.Unindent();

        ImGui.Spacing();
        ImGui.TextWrapped("All undo and redo histories are saved per HUD layout. You can check the debug info tab to see the current state of the histories.");

        ImGui.Spacing();
    }

    /**
     * Set the tab that should be opened when the window is drawn
     */
    internal void SetOpenTab(string tabSubString) { 
        var tab = TabActions.FindIndex(t => t.Item1.Contains(tabSubString));
        if (tab != -1)
            OpenTab = TabActions[tab].Item1;
    }
}
