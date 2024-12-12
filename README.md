# HUDLayoutHelper

A Dalamud Plugin for FFXIV that adds QoL features to the native HUD Layout editor. 

- Copy and paste HUD element positions. 
- Undo and redo movement of HUD elements. 
- Show an overlay that displays lines between the corners and centers of HUD elements to help align them. 

## How To Use
1. Open the HUD Layout Editor.
2. Use the following keybinds:
	- **Ctrl + C**: Copy position (top-left corner) of selected HUD element. 
	- **Ctrl + V**: Paste copied HUD element position.
	- **Ctrl + Z**: Undo last action.
	- **Ctrl + Y**: Redo last action.
	- **Ctrl + R**: Toggle alignment helper overlay.

### Alignment Helper Overlay
When the alignment helper overlay is enabled, lines will be drawn between the corners and centers of the selected HUD element and other HUD elements to help align them. 

There are different colors and line styles for different types of alignment: 
- **Solid line**: the selected HUD element has at least one point (corner or center) that is fully aligned with another HUD element.  
- Faded line: the selected HUD element has one point that is close to being aligned with another HUD element.
- ![#f03c15](https://placehold.co/15x15/f03c15/f03c15.png) Red lines and dots: highlighting alignment with the **centers** of HUD elements.
- ![#2bf015](https://placehold.co/15x15/2bf015/2bf015.png) Green lines and dots: highlighting alignment with the **corners** of HUD elements. 

## Screenshots
#### Alignment Overlay
![Alignment Overlay](./images/alignment_lines.png)

#### Window with Shortcut Hints
This window can be toggled on and off in the settings and displays the keybinds for the plugin in the HUD Layout Editor. 

![Shortcut Hints](./images/shortcut_hints.png)


<details>
  <summary>GIF</summary>

  ![HUDLayoutHelper](https://github.com/user-attachments/assets/854e77d8-2313-4c49-a277-983a1719c63c)

</details> 
