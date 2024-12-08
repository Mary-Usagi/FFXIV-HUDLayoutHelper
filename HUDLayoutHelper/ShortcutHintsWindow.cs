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
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HUDLayoutHelper;

public class ShortcutHintsWindow : Window, IDisposable {
    private Plugin Plugin;
    private Configuration Configuration;
    public ShortcutHintsWindow(Plugin plugin) : base("HUD Layout Helper Shortcuts"){
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;

        Plugin = plugin;
        SizeCondition = ImGuiCond.Always;
        PositionCondition = ImGuiCond.Always;
        //base.BgAlpha = 0.5f;
        base.ShowCloseButton = false;
        base.AllowClickthrough = true;
        base.RespectCloseHotkey = false;
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(260, 50),
        };
        Configuration = plugin.Configuration;
    }

    public unsafe void SetWindowPosition() {
        if (this.Plugin.HudLayoutWindow == null) return;
        // Get the position of the HUD Layout window
        short x = 0, y = 0;
        this.Plugin.HudLayoutWindow->GetPosition(&x, &y);
        float height = this.Plugin.HudLayoutWindow->GetScaledHeight(true);
        this.Position = new Vector2(x + 2, y + height - 15);
    }

    public unsafe void DrawHelpWindow() {
        if (this.Plugin.HudLayoutWindow == null) return;
        SetWindowPosition();

        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.5f, 0, 0, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0, 0, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 0, 0, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
        if (ImGui.CollapsingHeader("HUD Layout Helper - Shortcut List", ImGuiTreeNodeFlags.DefaultOpen)) {
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 1));

            ImGui.PushStyleColor(ImGuiCol.TableBorderStrong, new Vector4(1, 1, 1, 0.2f));
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, new Vector4(1, 1, 1, 0.2f));

            ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));
            ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(0.0f, 0.0f, 0.0f, 0.5f));

            ImGui.BeginTable("##Table1", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.PadOuterX | ImGuiTableFlags.BordersInner | ImGuiTableFlags.RowBg);
            ImGui.TableSetupColumn("##Column1", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("##Column2", ImGuiTableColumnFlags.WidthStretch);


            foreach (var keybind in Plugin.Keybindings) {
                ImGui.TableNextColumn();
                ImGui.Text(keybind.keys.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(keybind.description.ShortText);
            }
            ImGui.EndTable();

            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    public void Dispose() { }

    public unsafe override void PreDraw() { }

    public unsafe override void Draw() {
        if (Configuration.ShowShortcutHints == false) return;
        if (!Plugin.ClientState.IsLoggedIn) return;
        if (Plugin.ClientState is not { LocalPlayer.ClassJob.RowId: var classJobId }) return;
        if (Plugin.AgentHudLayout == null || Plugin.HudLayoutScreen == null) return;
        DrawHelpWindow();
    }

}
