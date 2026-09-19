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
* **Snap Indicator on Floating Badge:**
  * When a line or element snaps, the badge updates dynamically with a magnetic lock icon and target name:
    * `🧲 X: +120 px (Snapped to 'Header')`
  * Badge border transitions from neutral gray (`#737380`) to the bright snap color (`#FF14E6` or `#00E5FF`).
* **Line Snap Color Flash / Pulse:**
  * The dragged guide line temporarily glows or pulses with the snap accent color the exact frame it snaps, giving instant tactile confirmation.
* **Alignment Projection Guides:**
  * When snapping to a UI element, extend subtle dashed projection rays from the element's edge to the canvas boundary to clearly visualize the alignment relationship.
* **Snap Impact Visual Pop:**
  * A momentary subtle pulse or expanding corner bracket animation when an element locks onto a grid line.

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

### 4. Modern In-Scene HUD Toolbar
* **Sleek Floating Panel:**
  * Replace the legacy gray OS window (`GUI.skin.window`) with a dark, rounded glassmorphic toolbar matching the Position Badge aesthetic (`#1F1F24` with drop shadow and border).
* **Interactive Quick-Action Buttons:**
  * Provide one-click Scene View toggles so users don't need to return to the Inspector window:
    * 🧲 **Snap to UI Elements** toggle
    * 📐 **Snap Elements to Grid** toggle
    * 🪞 **Mirror Mode** toggle
    * ➕ **Add H-Line / V-Line** quick-creation buttons
    * 🗑️ **Delete Selected** button (active when lines are selected)
    * ✕ **Exit Edit Mode** button
* **Collapse / Minimize State:**
  * A compact pill button to tuck the HUD away into a mini status badge when full scene visibility is needed.

---

### 5. Canvas Edge Rulers & Guide Pull-Tabs
* **Edge Pull-Tabs (Photoshop / Illustrator Style):**
  * Subtle draggable tabs or tick marks on the canvas borders.
  * In "Add Lines Mode", designers can drag directly from the canvas edge into the layout to pull out a new horizontal or vertical guide.
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
