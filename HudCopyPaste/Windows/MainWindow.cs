using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace HudCopyPaste.Windows;

public class MainWindow : Window, IDisposable
{
    private string GoatImagePath;
    private Plugin Plugin;

    public MainWindow(Plugin plugin)
        : base("Hud Copy Paste Controls", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(290, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Plugin = plugin;
    }

    public void Dispose() { }

    private string[][] keybindDescriptions = [
        ["Ctrl + C", "Copy selected HUD element"],
        ["Ctrl + V", "Paste copied HUD element"],
        ["Ctrl + Z", "Undo last action"],
        ["Ctrl + Y", "Redo last action"]
    ];

    public override void Draw()
    {
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

        foreach (var keybind in keybindDescriptions)
        {
            for (var i = 0; i < keybind.Length; i++)
            {
                ImGui.TableNextColumn();
                ImGui.Text(keybind[i]);
            }
        }
        ImGui.EndTable();
    }
}
