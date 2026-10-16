# UI Furnace - Grid Guide
## Canvas Edge Ruler System & Guide Pull Architecture
### Commercial-Grade Specification & Market Strategy

---

## 📌 Executive Summary

The **Canvas Edge Ruler System** brings the ubiquitous design workflow of Figma, Adobe Photoshop, and Illustrator natively into the Unity Scene View. 

In standard Unity development, designers building uGUI interfaces struggle with a major pain point: **Unity's Scene View provides no native pixel-ruler reference**. Designers are forced to repeatedly switch focus between the Scene View and the Inspector's `RectTransform` properties to verify positions, margins, and paddings. 

By delivering an adaptive, zero-lag, pixel-accurate canvas ruler with edge-pull guide instantiation, **UI Furnace** transforms the Unity Scene View into a professional, desktop-grade layout studio.

---

## 🎯 Target Market & Customer Value

### Target Audience
1. **Unity UI/UX Designers & Technical Artists:** Professionals accustomed to Figma/Sketch/Photoshop who want identical muscle memory inside Unity.
2. **Indie Developers & Game Studios:** Developers looking to assemble clean, consistent game menus, HUDs, and dialogue boxes in minutes without manual math.
3. **Asset Store Buyers:** Users looking for high-production-value, trustworthy tools that adhere strictly to Unity's native UI look and feel.

### Competitive Advantages
* **Native Pixel Context:** Displays true uGUI canvas pixels rather than ambiguous 3D world units.
* **Direct Pull Interaction:** Click and pull guides directly from ruler notch tracks into the canvas without modal dialogs or clunky buttons.
* **Adaptive Zoom Subdivisions:** Dynamically scales tick marks (1000px → 500px → 100px → 50px → 10px → 1px) based on camera distance, ensuring zero visual clutter.
* **Zero Performance Tax:** GPU-batched rendering with frustum culling ensures 60+ FPS even with complex UI scenes.

---

## 📐 Core Feature Architecture

### 1. Visual Presentation & Dark Studio Aesthetic
* **Placement Modes:**
  * **Canvas-Border Mode (Primary):** Snaps directly to the active Canvas bounds in the Scene View. Pans, rotates, and zooms in 1:1 sync with the canvas.
  * **Viewport-Docked Mode (Secondary):** Fixed to the top and left edges of the Scene View window like traditional 2D desktop graphics suites.
* **Palette & Contrast:**
  * Background Track: Sleek studio charcoal (`#18181C` at 92% opacity).
  * Major Ticks & Numerals: Crisp off-white (`#E0E0E6`) with bold micro-typography (9pt).
  * Minor Ticks: Clean muted slate (`#6E6E7A`).
  * Hairline Cursor Tracker: High-contrast Accent Cyan (`#38BDF8`) that tracks mouse movements in real-time along both axes.

---

## 2. Ruler Subdivision Algorithm
The ruler dynamically determines subdivision density based on the on-screen pixel length of the canvas:

| Zoom Level (Screen Px per Canvas Unit) | Major Tick Step | Minor Tick Step | Label Frequency |
| :--- | :--- | :--- | :--- |
| **Far Out (< 0.25x)** | 500 px | 100 px | Every 500 px |
| **Standard (0.25x - 1.0x)** | 100 px | 20 px | Every 100 px |
| **Close Up (1.0x - 4.0x)** | 50 px | 10 px | Every 50 px |
| **Pixel Hunt (> 4.0x)** | 10 px | 1 px | Every 10 px |

---

## 3. Edge-Pull Guide Instantiation (Figma/Photoshop Style)
1. **Interactive Ruler Track:** The ruler strip functions as a dedicated guide generation rail.
2. **Hover Cue:** When hovering over the horizontal or vertical ruler, the cursor switches to `MouseCursor.ResizeVertical` or `MouseCursor.ResizeHorizontal`, and the hovered tick notch brightens.
3. **Pull Gesture:** Clicking and dragging anywhere along the ruler strip instantly extracts a new guide line that follows the cursor into the layout space.
4. **Smart Placement:**
   * Releasing inside the canvas commits the new guide to the active profile with full Undo support (`Undo.RecordObject`).
   * Dragging back onto the ruler strip and releasing cancels creation or deletes an existing guide (standard Adobe convention).

---

## 4. Coordinate Origins & Units
* **Origin Switcher:**
  * **Canvas Center (0, 0):** Default for game UI, matching standard symmetry and UI Furnace anchor mechanics.
  * **Top-Left (0, 0):** Figma / Web convention (X increases right, Y increases down).
  * **Bottom-Left (0, 0):** Native uGUI standard coordinate system.
* **Unit Modes:**
  * **Pixels (px):** Direct canvas local units.
  * **Percentages (%):** Normalized 0% to 100% ratios for responsive UI design.

---

## 🛠️ Technical Specifications

### Event Handling & Priority
* Must register `HandleUtility.AddControl(controlID, 0f)` during `EventType.Layout` when hovering over ruler zones to ensure complete priority over Unity's built-in `RectTool` manipulators.
* Hot control capture (`GUIUtility.hotControl = controlID`) ensures zero interference with scene camera panning or game object dragging.

### Rendering Pipeline
* Rendered via `Handles.DrawLines` using pre-allocated static coordinate buffers to ensure **0 bytes of Garbage Collection per frame**.
* Strict camera frustum culling: Only tick marks visible within the Scene View viewport are computed and drawn.
