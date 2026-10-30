# UI Furnace: Layout Designer
## Canvas Edge Ruler System & Guide Pull Architecture
### Commercial-Grade Specification, Anchoring Engine & 8K Implementation Plan
### **REVISION 3 — Designer-Grade, Lean & Viewport-Adaptive Edition**

---

## 1. Executive Summary & Vision

The **Canvas Edge Ruler System** brings the ubiquitous design workflow of Figma, Adobe Photoshop, and Illustrator natively into the Unity Scene View.

In standard Unity development, designers assembling uGUI interfaces face a critical productivity barrier: **Unity provides no native pixel-ruler reference, and no direct visual bridge between Scene View manipulation and `RectTransform` anchor mathematics.** Designers are forced to manually perform mental arithmetic, toggle between the Inspector and Scene View to verify margins, and guess anchor placements.

By delivering an adaptive, zero-lag, pixel-accurate canvas ruler with **viewport-clamped visibility (never scrolls off-screen)**, **drag-to-create and drag-back-to-delete guide workflows**, **instant `Shift+R` toggling**, and **deep uGUI anchor point visualization**, UI Furnace transforms Unity into a precision layout studio.

```
+------------------------------------------------------------------------+
| TOP RULER (Sticky Viewport-Clamped): [ 0,0 Box ] |--|--|-- [Anchor] --| |
+----------+-------------------------------------------------------------+
| L RULER: |  CANVAS WORKSPACE (480p / 1080p / 4K / 8K / mobile)        |
| (Sticky) |                                                             |
|          |   +-------------------------------------------+             |
|          |   | Active UI Element                         |             |
| [Anchor] |   | (Anchors visualized on ruler in real-time)|             |
|          |   +-------------------------------------------+             |
| Pull ----+-----------> [Interactive Guide Pull]                        |
| <--- Drag Guide back into Ruler to Delete it (Figma UX)                |
+----------+-------------------------------------------------------------+
```

---

## 2. Target Market & Core Designer Needs

### The 4 Core Jobs-to-be-Done
1. **Coordinate Awareness ("Where am I?"):** Real-time pixel feedback along X and Y with an instant `[X, Y]` readout in the corner box and cursor hairlines tracking the mouse.
2. **Guide Instantiation ("Drop a guide at X=64px"):** Click and pull directly from the ruler track into the canvas with numerical tick precision and magnetic snapping.
3. **Muscle-Memory Guide Deletion:** Dragging an existing guide back onto the ruler track deletes it immediately (Photoshop/Figma standard).
4. **Anchor & Spacing Reference:** Instant projection of selected UI element anchor points (`anchorMin`, `anchorMax`, `pivot`) directly onto ruler tracks.

### Competitive Advantages
- **Viewport Edge Clamping (Never Disappears):** When zooming into an element (e.g. 400% zoom into a button at the bottom-right), the ruler sticks to the SceneView screen edge so it remains 100% visible and usable during close-up editing.
- **Universal `Shift + R` Hotkey:** Instant toggling of rulers without opening menus or overlays.
- **True Canvas Pixel Context:** 1 unit on the ruler = 1 local uGUI pixel across all `CanvasScaler` modes (`Scale With Screen Size`, `Constant Pixel Size`, `Expand`, `Shrink`).
- **3 Coordinate Origins:** Center (Unity standard), Top-Left (Figma/Web standard), and Bottom-Left (Raw uGUI standard).
- **0-Byte Garbage Collection:** On-demand small string cache and batched `Handles.DrawLines` eliminate GC spikes.

---

## 3. Mathematical Foundations & Viewport-Adaptive Engine

### 3.1. Canvas Scaler & Local Design Units

Unity's uGUI operates on local canvas coordinates (`RectTransform.rect`), scaled by `CanvasScaler` to match target displays.

The Ruler System operates strictly in **Canvas Local Units (True Design Pixels)**. Regardless of whether the Game View is 1080p, 4K, or 8K, 1 unit on the ruler equals exactly 1 local uGUI pixel.

#### CanvasScaler Mode Handling

| CanvasScaler Mode | Ruler Response |
| :--- | :--- |
| **Scale With Screen Size** | Read `canvasRect.rect` directly — Unity sets this to Reference Resolution. Ruler always reflects design pixels. |
| **Constant Pixel Size** | Re-query `canvasRect.rect.width/height` fresh each frame. Ruler updates dynamically on editor resize. |
| **Expand / Shrink** | Read `canvasRect.rect` directly (Unity automatically handles the scaled aspect). |

> **Bug Defense C1:** Never cache `localWidth`/`localHeight` across frames. Always read fresh from `canvasRect.rect.width` and `canvasRect.rect.height` at the top of `OnSceneGUI`.

---

### 3.2. Dynamic Zoom & Adaptive Subdivision Algorithm

The tick subdivision dynamically adapts based on **Screen Pixels per Canvas Unit ($S_{px}$)**:

$$S_{px} = \frac{\text{Distance in SceneView GUI Pixels between Canvas Corners}}{\text{Canvas Local Unit Dimension}}$$

```csharp
// Stable calculation using canvas midpoint to guard against perspective distortions:
Vector2 topMidGUI = HandleUtility.WorldToGUIPoint((TL + TR) * 0.5f);
Vector2 botMidGUI = HandleUtility.WorldToGUIPoint((BL + BR) * 0.5f);
float Spx_Y = Mathf.Abs(botMidGUI.y - topMidGUI.y) / Mathf.Max(localHeight, 0.001f);
```

| Screen Scale ($S_{px}$) | Major Tick Step | Minor Tick Step | Visual Context |
| :--- | :--- | :--- | :--- |
| $S_{px} < 0.05$ | 2000 px | 500 px | Bird's-eye 8K view |
| $0.05 \le S_{px} < 0.15$ | 1000 px | 200 px | Far out 4K view |
| $0.15 \le S_{px} < 0.40$ | 500 px | 100 px | Zoomed out view |
| $0.40 \le S_{px} < 1.20$ | 100 px | 20 px | **1:1 Normal editing scale** |
| $1.20 \le S_{px} < 3.50$ | 50 px | 10 px | Component close-up |
| $3.50 \le S_{px} < 8.00$ | 10 px | 2 px | Pixel-level alignment |
| $S_{px} \ge 8.00$ | 5 px | 1 px | Sub-pixel inspection |

> **Bug Defense Z1:** Divide-by-zero guard. Clamp local canvas dimension to `Mathf.Max(dim, 0.001f)`.
>
> **Bug Defense Z2:** Label overlap prevention. Before rendering each label, verify the screen distance to the previous drawn label is $\ge 28 \text{px} \times ppp$. Skip minor label collisions cleanly.

---

### 3.3. Viewport Edge Clamping ("Never-Disappearing Ruler")

#### The Core Problem Solved
If rulers are rendered strictly at the canvas boundary, zooming into an element at the center or bottom-right causes the top and left canvas edges to scroll hundreds of pixels off-screen. **The ruler would disappear when the designer needs it most.**

#### The Clamping Solution
The ruler strips sit along the canvas edges when visible, but **clamp to the SceneView viewport margins** when the canvas scrolls out of view:

```csharp
float ppp = EditorGUIUtility.pixelsPerPoint;
float rulerThickness = 18f * ppp;

// Get canvas corner screen coordinates
Vector2 tlGUI = HandleUtility.WorldToGUIPoint(TL);
Vector2 trGUI = HandleUtility.WorldToGUIPoint(TR);
Vector2 blGUI = HandleUtility.WorldToGUIPoint(BL);

// Viewport visible boundaries (accounting for SceneView toolbar at top if needed)
float viewportMinX = 0f;
float viewportMaxX = SceneView.currentDrawingSceneView.position.width;
float viewportMinY = 0f;
float viewportMaxY = SceneView.currentDrawingSceneView.position.height;

// Top ruler clamped position: sits above canvas top edge, clamped to viewport top
float topRulerY = Mathf.Clamp(tlGUI.y - rulerThickness, viewportMinY, viewportMaxY - rulerThickness);

// Left ruler clamped position: sits left of canvas left edge, clamped to viewport left
float leftRulerX = Mathf.Clamp(tlGUI.x - rulerThickness, viewportMinX, viewportMaxX - rulerThickness);

// Active horizontal ruler rect spanning visible canvas width (clamped to screen)
float topRulerLeft  = Mathf.Max(tlGUI.x, leftRulerX + rulerThickness);
float topRulerRight = Mathf.Min(trGUI.x, viewportMaxX);
Rect topRulerRect   = new Rect(topRulerLeft, topRulerY, Mathf.Max(0, topRulerRight - topRulerLeft), rulerThickness);

// Active vertical ruler rect spanning visible canvas height (clamped to screen)
float leftRulerTop    = Mathf.Max(tlGUI.y, topRulerY + rulerThickness);
float leftRulerBottom = Mathf.Min(blGUI.y, viewportMaxY);
Rect leftRulerRect    = new Rect(leftRulerX, leftRulerTop, rulerThickness, Mathf.Max(0, leftRulerBottom - leftRulerTop));

// Corner origin box at the intersection
Rect cornerRect = new Rect(leftRulerX, topRulerY, rulerThickness, rulerThickness);
```

**Benefits:**
1. Zoomed out: Rulers frame the canvas naturally.
2. Zoomed in: Rulers smoothly stick to the top and left edges of the SceneView window.
3. Completely off-screen: If the canvas is 100% off-screen, ruler rendering gracefully early-outs.

---

### 3.4. Direct Screen-Space Frustum Culling (No 3D Plane Raycasting)

Instead of complex 4-corner 3D plane raycasting, tick generation iterates **directly across the visible screen span**:

```csharp
// For Top Ruler:
// 1. Convert visible ruler start and end GUI pixels to canvas local coordinates:
float visibleStartLocalX = ScreenToCanvasLocalX(topRulerRect.xMin);
float visibleEndLocalX   = ScreenToCanvasLocalX(topRulerRect.xMax);

// 2. Snap to nearest step:
int startTick = Mathf.FloorToInt(Mathf.Min(visibleStartLocalX, visibleEndLocalX) / minorStep) * minorStep;
int endTick   = Mathf.CeilToInt(Mathf.Max(visibleStartLocalX, visibleEndLocalX) / minorStep) * minorStep;

// 3. Loop is naturally clamped to at most 30-60 iterations:
for (int tick = startTick; tick <= endTick; tick += minorStep)
{
    float screenX = CanvasLocalToScreenX(tick);
    // Draw tick line...
}
```
**Result:** 100% frustum culled by definition, zero raycast allocations, perfectly stable at 8K and any camera angle.

---

### 3.5. DPI & High-DPI Scaling

All ruler geometry scales with `EditorGUIUtility.pixelsPerPoint` (PPP):

| Property | Formula |
| :--- | :--- |
| Ruler strip thickness | `18 * ppp` |
| Corner box size | `18 * ppp` |
| Major tick height | `8 * ppp` |
| Minor tick height | `4 * ppp` |
| Cursor hairline thickness | `1 * ppp` |
| Label font size | `Mathf.RoundToInt(9 * ppp)` |
| Grab hitbox thickness | `20 * ppp` |

---

## 4. Deep Unity uGUI Anchoring Engine

### 4.1. Coordinate Origin Switcher

Designers toggle origins by clicking the Ruler Corner Box:

| Origin Mode | X Range | Y Range | Notes |
| :--- | :--- | :--- | :--- |
| **Canvas Center (Default)** | `[-halfW, +halfW]` | `[-halfH, +halfH]` | Matches Unity `RectTransform` center math |
| **Top-Left (Figma/Web)** | `[0, width]` | `[0, height]` (Y down) | Familiar Figma/Web designer muscle memory |
| **Bottom-Left (uGUI)** | `[0, width]` | `[0, height]` (Y up) | Matches raw uGUI `anchorMin=(0,0)` space |

```csharp
// Label display conversion (stored values are always center-relative):
float ToDisplayX(float localPx, float halfW, RulerCoordinateOrigin origin) =>
    origin == RulerCoordinateOrigin.CanvasCenter ? localPx : localPx + halfW;

float ToDisplayY(float localPx, float halfH, RulerCoordinateOrigin origin) =>
    origin == RulerCoordinateOrigin.CanvasCenter ? localPx :
    origin == RulerCoordinateOrigin.TopLeft      ? (halfH - localPx) : // Invert for Figma top-down Y
    (localPx + halfH);
```

---

### 4.2. Clean Anchor Projection (Eliminating Visual Soup)

To keep the 18px ruler track clean and readable, anchor visualization is streamlined:
- **Anchor Notch Pips:** Subtle amber-gold (`#FFB300`) 3px triangular notch pips drawn along the **inner edge** of the ruler track (facing the canvas).
- **Single vs Stretched:** 
  - If `anchorMin == anchorMax`: a single crisp notch pip.
  - If `anchorMin != anchorMax`: two notch pips with a subtle 1px connecting hairline along the inner edge.
- **Pivot:** A tiny purple diamond pip (`#C084FC`) on the inner edge.
- **The Ruler Body Remains Clean:** Numbers, ticks, and cursor hairlines remain 100% unobstructed.

```csharp
// Compute canvas-local anchor positions for selected RectTransform:
RectTransform parentRT = selectedRT.parent as RectTransform ?? canvasRect;
float parentW = parentRT.rect.width;
float parentH = parentRT.rect.height;

Vector3[] parentCorners = new Vector3[4];
parentRT.GetWorldCorners(parentCorners);
Vector3 parentBL_local = canvasRect.InverseTransformPoint(parentCorners[0]);
float parentOffsetX = parentBL_local.x + parentW * 0.5f;
float parentOffsetY = parentBL_local.y + parentH * 0.5f;

float anchorMinX = (selectedRT.anchorMin.x * parentW) - (parentW * 0.5f) + parentOffsetX;
float anchorMaxX = (selectedRT.anchorMax.x * parentW) - (parentW * 0.5f) + parentOffsetX;
float anchorMinY = (selectedRT.anchorMin.y * parentH) - (parentH * 0.5f) + parentOffsetY;
float anchorMaxY = (selectedRT.anchorMax.y * parentH) - (parentH * 0.5f) + parentOffsetY;
```

---

## 5. Guide Interactions: Pull, Delete & Hotkeys

### 5.1. Pull to Create Guide
1. Clicking down on the **Top Ruler** track initiates dragging a **Horizontal Guide**.
2. Clicking down on the **Left Ruler** track initiates dragging a **Vertical Guide**.
3. Clicking down on the **Corner Box** initiates dragging **Both (H + V) Guides** simultaneously.
4. Integrates directly into UI Furnace's existing `s_IsAxisDragging` pipeline, ensuring identical snapping, mirror mode symmetry, and undo/redo registration.

### 5.2. Drag Back to Ruler to Delete (Figma/Photoshop Muscle Memory)
When dragging an existing guide (or a freshly pulled guide):
- If the cursor moves over either ruler track (or out of canvas bounds), the guide preview tints red with a subtle `✕` badge.
- Releasing the mouse while over the ruler removes the guide and commits the change via `Undo.RecordObject`.

### 5.3. Universal Hotkey (`Shift + R`)
- Pressing `Shift + R` in the Scene View instantly toggles `GridSettings.ShowRulers`.
- Registered via `Event.current.type == EventType.KeyDown && Event.current.shift && Event.current.keyCode == KeyCode.R`.

### 5.4. Cursor Coordinate Readout HUD
- Inside the **Corner Origin Box**, a clean micro-readout displays the current mouse position in active display coordinates:
  ```
  +--------------+
  | X:  450      |
  | Y: -120  [C] |
  +--------------+
  ```
- Clicking `[C]` toggles between **Center (C)**, **Top-Left (TL)**, and **Bottom-Left (BL)**.

### 5.5. Magnetic Snapping
Guides pulled from rulers automatically snap to:
1. **Canvas Center (0px / 50%)** — Highest priority.
2. **Selected Element Anchors & Bounds:** Snaps to `anchorMin`, `anchorMax`, and element rect edges.
3. **Responsive Percentages:** 25%, 33.3%, 50%, 66.7%, 75%.
4. **Existing Custom Guides.**

---

## 6. Visual Styling & Lean Zero-GC Engine

### 6.1. Color Palette Tokens

| Element | Hex (Alpha) | RGBA Float | Purpose |
| :--- | :--- | :--- | :--- |
| **Ruler Background** | #18181C (94%) | `(0.094, 0.094, 0.110, 0.94)` | Studio dark track |
| **Ruler Border** | #2C2C35 (100%) | `(0.173, 0.173, 0.208, 1.00)` | Crisp 1px separation hairline |
| **Major Ticks & Labels** | #E0E0E6 (100%) | `(0.878, 0.878, 0.902, 1.00)` | Crisp white markers |
| **Minor Ticks** | #6E6E7A (80%) | `(0.431, 0.431, 0.478, 0.80)` | Muted subdividers |
| **Cursor Hairline** | #38BDF8 (100%) | `(0.220, 0.741, 0.973, 1.00)` | Cyan position cursor |
| **Anchor Notches** | #FFB300 (100%) | `(1.000, 0.702, 0.000, 1.00)` | Amber inner edge pips |
| **Pivot Marker** | #C084FC (100%) | `(0.753, 0.518, 0.988, 1.00)` | Purple inner edge diamond |
| **Delete Zone Tint** | #EF4444 (30%) | `(0.937, 0.267, 0.267, 0.30)` | Red overlay when hovering ruler during drag |

### 6.2. Lean Zero-GC String Cache
Instead of pre-populating 20,000 strings, an on-demand string dictionary caches only the actual multiples rendered:

```csharp
private static readonly Dictionary<int, string> s_StringCache = new Dictionary<int, string>(128);

public static string GetCachedIntString(int value)
{
    if (!s_StringCache.TryGetValue(value, out string str))
    {
        str = value.ToString();
        s_StringCache[value] = str;
    }
    return str;
}
```
**GC Impact:** Exactly 0 bytes during steady-state rendering. Memory footprint: < 5 KB.

---

## 7. Architecture & Code Integration

```
Assets/AZSoftStudio/UI Furnace - Layout Designer/
+-- Editor/
    +-- GridRenderer.cs                  <- Hook DrawCanvasRulers & HandleRulerEvents in OnSceneGUI
    +-- GridRenderer.Ruler.cs            <- NEW: Ruler rendering, viewport clamping, tick math, HUD
    +-- GridRenderer.RulerInteraction.cs <- NEW: Ruler hover, drag-pull, drag-to-delete, Shift+R
    +-- GridSettings.cs                  <- ADD: Ruler settings & RulerCoordinateOrigin enum
    +-- UIFurnaceOverlay.cs              <- ADD: Rulers toggle button
```

### 7.1. GridSettings.cs Settings
```csharp
public enum RulerCoordinateOrigin
{
    CanvasCenter = 0,
    TopLeft      = 1,
    BottomLeft   = 2
}

public static bool ShowRulers
{
    get => EditorPrefs.GetBool("UIFurnace_ShowRulers", true);
    set => EditorPrefs.SetBool("UIFurnace_ShowRulers", value);
}

public static RulerCoordinateOrigin RulerOrigin
{
    get => (RulerCoordinateOrigin)EditorPrefs.GetInt("UIFurnace_RulerOrigin", 0);
    set => EditorPrefs.SetInt("UIFurnace_RulerOrigin", (int)value);
}
```

---

## 8. Complete Bug-Defense Matrix

| ID | Issue | Root Cause | Defense |
| :--- | :--- | :--- | :--- |
| **V1** | Ruler disappears when zoomed in | Anchored strictly to canvas edge | Clamp ruler screen rects to SceneView viewport margins |
| **I1** | Alt+Click camera orbit intercepted | Ruler captures mouse without checking Alt | Guard with `if (e.alt) return;` |
| **I2** | Middle-click pan intercepted | Ruler captures buttons other than left | Guard with `if (e.button != 0) return;` |
| **I3** | Guide drag doesn't delete on ruler | Missing drag-back check | Check mouse over ruler during drag; trigger delete |
| **I4** | Control ID collision | Duplicate hash | Use unique hash `"UIFurnaceRuler"` |
| **K1** | Shift+R conflicts with Unity tools | KeyDown processed while typing in TextField | Check `EditorGUIUtility.editingTextField` before hotkey |
| **C1** | Stale canvas size on window resize | Cached canvas dimensions | Read `canvasRect.rect` fresh every `OnSceneGUI` |
| **Z1** | Divide-by-zero on zero-size canvas | Degenerate canvas size | Clamp divisor to `Mathf.Max(val, 0.001f)` |
| **Z2** | Label overlap at scale boundaries | Steps too close on screen | Skip label if screen distance to previous < 28px * ppp |
| **D1** | Blurry on Retina / 4K displays | Missing pixelsPerPoint scaling | Multiply all dimensions, thicknesses, and fonts by `ppp` |
| **A1** | Anchor brackets cluttering ruler numbers | Overlapping visual elements | Anchor markers rendered strictly as inner edge notch pips |
| **A2** | NullReference on selection clear | `Selection.activeGameObject` null | Null-check active RectTransform before anchor math |
| **G1** | GC spikes during tick rendering | String concatenation / ToString | On-demand cached string dictionary |
| **G2** | Draw call overhead | Handles.DrawLine in a loop | Batch lines into pre-allocated buffer with `Handles.DrawLines` |
| **H1** | Event order inversion | Render before handle events | Always call `HandleRulerEvents` before `DrawCanvasRulers` |

---

## 9. Implementation Checklist (Ready for Execution)

### Step 1: Settings & Enums (`GridSettings.cs`)
- [ ] Add `RulerCoordinateOrigin` enum (CanvasCenter, TopLeft, BottomLeft).
- [ ] Add `ShowRulers` and `RulerOrigin` EditorPrefs properties.
- [ ] Verify project compiles with 0 errors.

### Step 2: Ruler Core Implementation (`GridRenderer.Ruler.cs`)
- [ ] Implement `DrawCanvasRulers` entry point with viewport edge clamping.
- [ ] Implement `GetCachedIntString` zero-GC lookup.
- [ ] Implement adaptive tick subdivision & batched line drawing (`Handles.DrawLines`).
- [ ] Implement corner origin box with coordinate readout HUD and origin click-switcher.
- [ ] Implement anchor notch pips along the inner ruler border.

### Step 3: Ruler Interaction & Shortcuts (`GridRenderer.RulerInteraction.cs`)
- [ ] Implement `Shift + R` SceneView hotkey.
- [ ] Implement edge-pull guide creation from top, left, and corner tracks.
- [ ] Implement drag-back-to-ruler to delete guides (Figma/Photoshop standard).
- [ ] Wire state reset to existing `ResetInteractionState()`.

### Step 4: Overlay & Editor Polish (`UIFurnaceOverlay.cs` & `GridRenderer.cs`)
- [ ] Add "Rulers" toggle button to the SceneView overlay.
- [ ] Hook `HandleRulerEvents` and `DrawCanvasRulers` into `GridRenderer.OnSceneGUI`.
- [ ] Perform full verification across 1080p, 4K, 8K, extreme zoom-in, and origin switching.
