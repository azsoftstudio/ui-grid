# UI Furnace - Grid Guide
## Visual Feedback & Roadmap Specification

This document outlines the complete plan for **Visual Feedback enhancements** (Current Phase), along with the overarching **Product Roadmap** for upcoming phases.

---

## 🗺️ Product Roadmap Overview

```
┌────────────────────────────────────────────────────────┐
│  PHASE 1: Visual Feedback & Tactile Response (Current) │
│  - Snapping lock cues, smart spacing, HUD toolbar      │
├────────────────────────────────────────────────────────┤
│  PHASE 2: User-Friendliness & Workflow Retouch         │
│  - Frictionless canvas setup, intuitive UX, presets   │
├────────────────────────────────────────────────────────┤
│  PHASE 3: Time-Saving Utilities & Power Tools          │
│  - Auto-layout generators, quick distribution, export  │
└────────────────────────────────────────────────────────┘
```

---

## 🎨 Phase 1: Visual Feedback Improvements

### 1. Smart Snapping & Magnetic "Lock-On" Feedback
* **Adaptive Complementary Color [IMPLEMENTED]:**
  * Real-time sampling of the snapped UI element's `Graphic` color.
  * Dynamically computes 180° optical hue complement with perceptual luminance inversion:
    * E.g. Magenta/pink element → Electric Emerald/Cyan highlight.
    * E.g. Navy/blue element → Radiant Amber-Gold highlight.
    * Achromatic light elements (white/gray) → Luminous Electric Magenta (`#FF0D8C`).
    * Achromatic dark elements (black/charcoal) → Radiant Electric Cyan (`#00F2FF`).
* **Stroke Prominence Over Unity RectTool [IMPLEMENTED]:**
  * L-brackets boosted to an ultra-bold **3.5px thickness** with a **6.0px solid dark backing** (`alpha = 0.55f`) for commanding visual dominance over Unity's 1px RectTool wireframe.
  * Centre crosshair kept sleek at **1.75px** with a **3.5px shadow pass**.
* **Decoupled Grid Line vs. UI Element Snap Highlight [IMPLEMENTED]:**
  * **UI Element Brackets:** Dynamically computed using the 180° optical complement of the UI element's `Graphic` color.
  * **Grid Line Highlight:** Dynamically computed using the 180° optical complement of the grid's own `GridColor`, ensuring clean visual harmony along the grid line independent of the UI element.

---

### 2. Smart Spacing & Dimension Indicators (Figma/Sketch Style)
* **Neighboring Gap Indicators:**
  * While dragging or hovering a line, render subtle dimension arrows and pixel badges showing distances to adjacent lines:
    * `|← 48px →| [Active Guide] |← 48px →|`
* **Equidistant Spacing Detection:**
  * If a dragged line reaches an exact midpoint between two guides, or matches the spacing of existing columns, turn the spacing badges vibrant green (`#00E676`) to signal rhythm alignment.
* **Canvas Margin Indicators:**
  * Display distances to the nearest canvas edges (Left/Right for vertical guides, Top/Bottom for horizontal guides) so margins can be verified instantly without manual calculation.

---

### 3. Selection & Handle Visuals
* **Dedicated Selection State:**
  * Decouple selected lines from hovered lines:
    * **Hovered Line:** Complementary hue shift (existing responsive system).
    * **Selected Lines:** Distinct bold accent color (e.g., vibrant Unity Blue `#2196F3` or Amber Gold `#FFB300`).
* **Canvas Edge Grab Handles (Pill / Dot Nodes):**
  * Draw small anchor handles at the endpoints of selected guides where they intersect the canvas boundary.
  * Clicking/dragging these edge handles prevents accidental mis-selection of dense UI components positioned directly behind grid lines.
* **Multi-Selection Bounding Zone:**
  * When multi-selecting lines (`Ctrl`/`Cmd` + Click), render a soft translucent highlight over the spanned zone to visualize the selected column/row block.

---

### 4. Modern In-Scene HUD Toolbar [IMPLEMENTED]
* **Native Dual-Bar Floating Overlay:**
  * Replaced the legacy gray OS window (`GUI.skin.window`) with a native Unity-styled dark/light adaptive dual-bar floating panel (`EditorStyles.toolbarButton` and theme-aware styling).
  * **Bar 1 (Header):** Drag grip (`⠿`), active status LED (`●`), title, minimize toggle (`—`/`▸`), and quick-exit (`✕`).
  * **Bar 2 (Controls Toolbar):**
    * 🧲 **Snap** toggle (magnetic snapping to child UI elements)
    * 🪞 **Mirror** toggle (symmetric guide placement)
    * ➕ **+H / +V** buttons (one-click center guide insertion)
    * 🗑️ **Delete Selected** button with dynamic selection count badge `🗑️ (N)`
* **Collapse / Minimize State:**
  * One-click toggle between full toolbar and a compact 24px micro-pill `[ ⠿ ● UI Furnace ▸ ✕ ]` to ensure zero scene clutter.
* **Persistent Screen Placement:**
  * Clamped to SceneView bounds and persisted across sessions via `EditorPrefs`.

---

### 5. Canvas Edge Rulers & Guide Pull-Tabs
* **Detailed Specification:** See [Canvas_Edge_Ruler_Specification.md](Canvas_Edge_Ruler_Specification.md) for full commercial analysis, feature hooks, and rendering architecture.
* **Edge Pull Rails (Photoshop / Illustrator Style) [IMPLEMENTED]:**
  * Sleek border rails on the 4 canvas boundaries with hover highlights, native diamond notch, and dynamic cursor switching.
  * In "Pull Guides from Edges", designers click and drag directly from the canvas edge into the layout to pull out a new horizontal or vertical guide.
* **Canvas Origin Crosshair:**
  * Clearly demarcate the (0,0) canvas origin with subtle tick marks and coordinate readouts.

---

### 6. Uniform Grid Visual Enhancements
* **Grid Spec Overlay Tag:**
  * Display a subtle floating tag at the top-left or bottom-left of the canvas summarizing the active grid:
    * `Grid: Fixed 32 × 32 px` or `Columns: 12 | Gutter: 20 px | Margin: 40 px`
* **Column / Row Index Numbering (Optional Toggle):**
  * Subtle column numbers (`1`, `2`, `3` ... `12`) above columns in stretch mode to allow instant visual reference during UI assembly.
* **Hover Cell Highlight:**
  * Lightly highlight the grid cell currently under the mouse cursor to assist in placing buttons and cards precisely.

---

## 🚀 Upcoming Phases (Preview)

### Phase 2: User-Friendliness & Workflow Retouch
* Automatic Canvas profile assignment with seamless zero-friction onboarding.
* Smart undo/redo support for all guide operations with descriptive history names.
* Context-sensitive right-click menus directly in the Scene View.
* Preset library: Standard mobile/desktop layout templates (12-column Bootstrap, 8pt Grid, Golden Ratio, Safe Area guides).

### Phase 3: Time-Saving Utilities & Power Tools
* **Auto-Distribute Guides:** Evenly space selected lines across canvas width or between two bounds.
* **Wrap UI Elements:** One-click generation of custom guides tightly wrapping selected RectTransforms with configurable padding.
* **Preset Import/Export:** Share grid configurations across team members and projects via JSON/asset files.
* **Quick Alignment Shortcuts:** Align selected RectTransforms to the nearest grid line via hotkey.
