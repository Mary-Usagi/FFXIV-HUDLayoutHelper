using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace HUDLayoutHelper.Windows;

public class ShortcutHintsWindow : Window, IDisposable {
    private Plugin Plugin { get; }
    private Configuration Configuration { get; }
    public ShortcutHintsWindow(Plugin plugin) : base("HUD Layout Helper Shortcuts") {
        this.Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground;

        this.Plugin = plugin;
        this.SizeCondition = ImGuiCond.Always;
        this.PositionCondition = ImGuiCond.Always;
        //base.BgAlpha = 0.5f;
        this.ShowCloseButton = false;
        this.AllowClickthrough = true;
        this.RespectCloseHotkey = false;
        this.SizeConstraints = new WindowSizeConstraints {
            MinimumSize = new Vector2(260, 50),
        };
        this.Configuration = plugin.Configuration;
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
        this.SetWindowPosition();

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


            foreach (var keybind in this.Plugin.Keybindings) {
                ImGui.TableNextColumn();
                ImGui.Text(keybind.KeybindKeys.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(keybind.KeybindDescription.ShortText);
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
        if (this.Configuration.ShowShortcutHints == false) return;
        if (!this.Plugin.ClientState.IsLoggedIn) return;
        if (this.Plugin.ClientState is not { LocalPlayer.ClassJob.RowId: var classJobId }) return;
        if (this.Plugin.AgentHudLayout == null || this.Plugin.HudLayoutScreen == null) return;
        this.DrawHelpWindow();
    }
}
