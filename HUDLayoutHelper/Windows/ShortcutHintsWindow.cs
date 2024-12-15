using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace HUDLayoutHelper.Windows;

public class ShortcutHintsWindow : Window, IDisposable {
    private Plugin _plugin;
    private Configuration Configuration;
    public ShortcutHintsWindow(Plugin plugin) : base("HUD Layout Helper - Shortcut List") {
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

        _plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        BgAlpha = 0.8f;
        ShowCloseButton = false;
        AllowClickthrough = true;
        RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(290, 50),
        };
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public unsafe override void PreDraw() { }

    public unsafe override void PreOpenCheck() {
        // If any of these conditions are not met, the window will not be opened
        this.IsOpen = Configuration.ShowShortcutHints
            && Plugin.ClientState.IsLoggedIn
            && Plugin.ClientState is { LocalPlayer.ClassJob.RowId: var classJobId }
            && Plugin.AgentHudLayout != null && Plugin.HudLayoutScreen != null;
    }

    public unsafe override void Draw() {
        ImGui.Spacing();
        ImGui.BeginTable("##Table1", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg);
        ImGui.TableSetupColumn("##Column1", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##Column2", ImGuiTableColumnFlags.WidthStretch);

        foreach (var keybind in this._plugin.KeybindHandler.KeybindList) {
            ImGui.TableNextColumn();
            ImGui.Text(keybind.keys.ToString());
            ImGui.TableNextColumn();
            ImGui.Text(keybind.description.ShortText);
        }
        ImGui.EndTable();
        ImGui.Spacing();
    }

}
