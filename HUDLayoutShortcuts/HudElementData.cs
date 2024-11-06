using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using FFXIVClientStructs.FFXIV.Client.Graphics;

namespace HUDLayoutShortcuts {
    /// <summary>
    /// Represents data for a HUD element.
    /// </summary>
    public class HudElementData {
        public int ElementId { get; set; } = -1;
        public string ResNodeDisplayName { get; set; } = string.Empty;
        public string AddonName { get; set; } = string.Empty;
        public short PosX { get; set; } = 0;
        public short PosY { get; set; } = 0;
        public ushort Width { get; set; } = 0;
        public ushort Height { get; set; } = 0;

        // Stores the visibility state of the HUD element (but not if it is disabled or not).
        public bool IsVisible { get; set; } = false;

        public bool IsEnabled { get; set; } = false;
        public float Scale { get; set; } = 1.0f;

        public override string ToString() => JsonSerializer.Serialize(this);
        public string PrettyPrint() => $"{ResNodeDisplayName} ({PosX}, {PosY})";

        public HudElementData() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="HudElementData"/> class from an AtkResNode.
        /// </summary>
        /// <param name="resNode">The AtkResNode pointer.</param>
        internal unsafe HudElementData(AtkResNode* resNode) {
            if (resNode->ParentNode == null) return;
            try {
                ResNodeDisplayName = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->NodeText.ToString();
            } catch (NullReferenceException) {
                ResNodeDisplayName = "Unknown";
            }

            PosX = resNode->ParentNode->GetXShort();
            PosY = resNode->ParentNode->GetYShort();

            Width = resNode->ParentNode->GetWidth();
            Height = resNode->ParentNode->GetHeight();

            IsVisible = resNode->ParentNode->NodeFlags.HasFlag(NodeFlags.Visible);

            try {
                // The text color of the node indicates if it is enabled or not. Purple-ish is disabled, gray/white is enabled.
                var color = resNode->ParentNode->GetComponent()->GetTextNodeById(4)->GetAsAtkTextNode()->TextColor;

                switch (color.RGBA) {
                    case 0xFF888888: // Enabled and not selected
                        IsEnabled = true;
                        break;
                    case 0xFF996666: // Disabled and not selected
                        IsEnabled = false;
                        break;
                    case 0xFFEEAAAA: // Disabled and selected
                        IsEnabled = false;
                        break;
                    case 0xFFEEEEEE: // Enabled and selected
                        IsEnabled = true;
                        break;
                }
            } catch (NullReferenceException) {}
            //IsEnabled = resNode->NodeFlags.HasFlag(NodeFlags.Visible);

            // TODO: Maybe get corresponding addon?
            ElementId = ResNodeDisplayName.GetHashCode();
            AddonName = "";
            Scale = -1;
        }
    }
}
