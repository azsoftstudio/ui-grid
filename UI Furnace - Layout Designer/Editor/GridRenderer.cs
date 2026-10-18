#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Handles grid rendering in the Scene View.
	/// </summary>
	[InitializeOnLoad]
	public static partial class GridRenderer
	{
		// Reusable array to avoid allocations
		private static readonly Vector3[] s_CornersCache = new Vector3[4];

		// Component cache to avoid expensive GetComponent calls every frame
		private static GameObject   s_CachedGameObject;
		private static RectTransform s_CachedRectTransform;
		private static Canvas        s_CachedCanvas;
		private static RectTransform s_CachedCanvasRect;
		private static RenderMode    s_CachedRenderMode;
		private static int           s_CachedInstanceID = -1;

		/// <summary>
		/// True when the selected object lives under a WorldSpace canvas (grid not supported there).
		/// Used by LayoutDesignerWindow to show a contextual warning.
		/// </summary>
		public static bool IsWorldSpaceCanvas { get; private set; }

		static GridRenderer()
		{
			SceneView.duringSceneGui  -= OnSceneGUI;
			SceneView.duringSceneGui  += OnSceneGUI;
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.update += OnEditorUpdate;
			Selection.selectionChanged -= OnSelectionChanged;
			Selection.selectionChanged += OnSelectionChanged;
			EditorApplication.hierarchyChanged += () => s_SnapHierarchyIsDirty = true;

			// P0 Safety: Reset all interaction state when entering/exiting play mode
			// or after a domain reload to prevent phantom stuck states.
			EditorApplication.playModeStateChanged += _ => ResetInteractionState();
			AssemblyReloadEvents.afterAssemblyReload += ResetInteractionState;

			GridSettings.LoadCurrentSettings();
		}

		/// <summary>
		/// Resets all transient interaction state (drag, hover, add-line, axis-drag).
		/// Called on play mode transitions, assembly reloads, and scene changes
		/// to prevent the tool from getting stuck in a phantom mode.
		/// </summary>
		private static void ResetInteractionState()
		{
			s_IsAddingLine = false;
			s_IsAxisDragging = false;
			s_IsDragging = false;
			s_DragIndexX = -1;
			s_DragIndexY = -1;
			s_MirrorDragIndexX = -1;
			s_MirrorDragIndexY = -1;
			s_HoverIndexX = -1;
			s_HoverIndexY = -1;
			s_HoverEdgeH = false;
			s_HoverEdgeV = false;
			s_HoverEdgeTop = false;
			s_HoverEdgeLeft = false;
			s_CurrentDragMode = DragMode.None;
			s_HUDInitialized = false;
			s_SpecHUDInitialized = false;
			s_LastDraggedID = -1;
			SelectedIndicesX.Clear();
			SelectedIndicesY.Clear();
			s_SnapHighlightedRects.Clear();
			s_ElementSnapHighlightedRects.Clear();
			s_HighlightOffsetsX.Clear();
			s_HighlightOffsetsY.Clear();
			InvalidateCache();
		}

		private static void OnSelectionChanged()
		{
			InvalidateCache();

			var canvases = new List<Canvas>();
			var seen = new HashSet<Canvas>();

			foreach (var go in Selection.gameObjects)
			{
				if (go == null) continue;
				var rt = go.GetComponent<RectTransform>();
				if (rt != null)
				{
					var can = rt.GetComponentInParent<Canvas>();
					if (can != null && can.renderMode != RenderMode.WorldSpace && seen.Add(can))
					{
						canvases.Add(can);
					}
				}
			}

			if (canvases.Count == 0)
			{
				GridSettings.SyncCanvasSelection(null);
			}
			else if (canvases.Count == 1)
			{
				GridSettings.SyncCanvasSelection(canvases[0]);
			}
			else
			{
				GridSettings.SyncMultipleCanvasesSelection(canvases);
			}
		}

		private static void OnEditorUpdate()
		{
			if (!GridSettings.ShowGrid) return;
			ProcessElementSnapping();
		}
		/// <summary>Invalidate component cache (called when selection changes).</summary>
		private static void InvalidateCache()
		{
			s_CachedGameObject    = null;
			s_CachedRectTransform = null;
			s_CachedCanvas        = null;
			s_CachedCanvasRect    = null;
			s_CachedRenderMode    = (RenderMode)(-1);
			s_CachedInstanceID    = -1;
			IsWorldSpaceCanvas    = false;
			s_CachedSnapHierarchy = null;
			s_CachedSnapMatrices  = null;
			s_CachedSnapRects     = null;
		}

		private static bool IsCacheValid()
		{
			if (Selection.activeGameObject == null)    return false;
			if (s_CachedGameObject == null)            return false;
			if (s_CachedRectTransform == null || s_CachedCanvas == null || s_CachedCanvasRect == null) return false;
			if (s_CachedGameObject.GetInstanceID() != s_CachedInstanceID) return false;

			var currentCanvas = s_CachedRectTransform.GetComponentInParent<Canvas>();
			if (currentCanvas == null || currentCanvas != s_CachedCanvas || currentCanvas.renderMode != s_CachedRenderMode) return false;

			return true;
		}

		private static bool TryGetCachedComponents(out RectTransform rectTransform, out Canvas canvas, out RectTransform canvasRect)
		{
			if (GridSettings.HasMultipleCanvasesSelected || GridSettings.ActiveCanvas == null)
			{
				rectTransform = null; canvas = null; canvasRect = null;
				return false;
			}

			if (IsCacheValid())
			{
				rectTransform = s_CachedRectTransform;
				canvas        = s_CachedCanvas;
				canvasRect    = s_CachedCanvasRect;
				return true;
			}

			rectTransform = null; canvas = null; canvasRect = null;

			var activeGO = Selection.activeGameObject;
			if (activeGO == null) return false;

			var rt = activeGO.GetComponent<RectTransform>();
			if (rt == null) return false;

			var can = rt.GetComponentInParent<Canvas>();
			if (can == null) return false;

			// Suggestion 4: detect WorldSpace and expose flag for the window warning
			if (can.renderMode == RenderMode.WorldSpace)
			{
				IsWorldSpaceCanvas = true;
				return false;
			}

			IsWorldSpaceCanvas = false;

			if (can.renderMode != RenderMode.ScreenSpaceOverlay &&
				can.renderMode != RenderMode.ScreenSpaceCamera)
				return false;

			var crt = can.GetComponent<RectTransform>();
			if (crt == null) return false;

			s_CachedGameObject    = activeGO;
			s_CachedRectTransform = rt;
			s_CachedCanvas        = can;
			s_CachedCanvasRect    = crt;
			s_CachedRenderMode    = can.renderMode;
			s_CachedInstanceID    = activeGO.GetInstanceID();

			if (GridSettings.ActiveCanvas != can)
			{
				GridSettings.SyncCanvasSelection(can);
			}

			rectTransform = rt; canvas = can; canvasRect = crt;
			return true;
		}

		/// <summary>Returns the local dimensions of the current canvas, needed for Dynamic mode conversion.
		/// Returns Vector2.zero when no canvas is selected Ã¢â‚¬â€ callers must guard against this.</summary>
		public static Vector2 GetCanvasSize()
		{
			if (s_CachedCanvasRect != null)
				return new Vector2(s_CachedCanvasRect.rect.width, s_CachedCanvasRect.rect.height);
			// BUG 3 fix: return zero so callers can detect the no-selection case
			// instead of silently generating lines based on a fake 1000x1000 canvas.
			return Vector2.zero;
		}
		/// <summary>Main rendering callback for Scene View.</summary>
		private static void OnSceneGUI(SceneView sceneView)
		{
			if (Event.current != null)
			{
				// Reserved scaffolding: modifier-key state for planned features
				// (Shift = constrain drag to one axis, Alt = duplicate line on drop)
				s_IsShiftDown = Event.current.shift;
				s_IsAltDown   = Event.current.alt;
			}

			s_SnapHighlightedRects.Clear();
			s_ElementSnapHighlightedRects.Clear();
			s_HighlightOffsetsX.Clear();
			s_HighlightOffsetsY.Clear();
			s_PendingBadgeText = null;

			if (!GridSettings.ShowGrid) return;

			// Prefab mode is allowed Ã¢â‚¬â€ grid is useful when editing UI prefabs.
			// PrefabStageUtility.GetCurrentPrefabStage() is intentionally not cached here;
			// prefab mode works automatically since we operate on Selection.activeGameObject.

			if (!TryGetCachedComponents(out RectTransform rectTransform, out Canvas canvas, out RectTransform canvasRect))
				return;

			if (GridSettings.ActiveProfile == null)
			{
				s_ActiveWorldGridLinesX.Clear();
				s_ActiveWorldGridLinesY.Clear();
				return;
			}

			canvasRect.GetWorldCorners(s_CornersCache);
			Vector3 BL = s_CornersCache[0], TL = s_CornersCache[1], TR = s_CornersCache[2], BR = s_CornersCache[3];

			var originalZTest = Handles.zTest;
			try
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

				float localWidth = canvasRect.rect.width;
				float localHeight = canvasRect.rect.height;

				if (GridSettings.CurrentMode == GridMode.UniformGrid)
				{
					if (GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
						DrawFixedSpacingGrid(BL, TL, TR, BR, localWidth, localHeight);
					else
						DrawStretchGrid(BL, TL, TR, BR);
				}
				else if (GridSettings.CurrentMode == GridMode.CustomLines)
				{
					int controlID = GUIUtility.GetControlID("UIFurnaceGrid".GetHashCode(), FocusType.Passive);
					HandleDynamicGridEvents(Event.current, controlID, BL, TL, TR, BR, localWidth, localHeight);
					DrawDynamicGrid(BL, TL, TR, BR, localWidth, localHeight);
				}

				// Universal features apply to both
				Vector3 center     = (BL + TR) * 0.5f;
				Vector3 rightDir   = BR - BL;
				Vector3 upDir      = TL - BL;
				float canvasWidth  = rightDir.magnitude;
				float canvasHeight = upDir.magnitude;
				Vector3 right      = rightDir / canvasWidth;
				Vector3 up         = upDir / canvasHeight;

				float scaleX = localWidth  > 0.001f ? (canvasWidth  / localWidth)  : 1f;
				float scaleY = localHeight > 0.001f ? (canvasHeight / localHeight) : 1f;

				HandleElementSnapping(center, right, up, canvasWidth, canvasHeight, scaleX, scaleY);

				// Draw snap highlight for UI elements when a grid line snaps to them
				if (Event.current.type == EventType.Repaint && s_SnapHighlightedRects.Count > 0)
				{
					var snapCorners = new Vector3[4];
					foreach (var rect in s_SnapHighlightedRects)
					{
						if (rect != null)
						{
							rect.GetWorldCorners(snapCorners);
							DrawSnapHighlight(snapCorners, rect);
						}
					}
				}

				// Draw snap highlight for UI elements when they snap to grid lines while dragging the element
				if (Event.current.type == EventType.Repaint && s_ElementSnapHighlightedRects.Count > 0 && s_CurrentDragMode != DragMode.None)
				{
					var snapCorners = new Vector3[4];
					foreach (var rect in s_ElementSnapHighlightedRects)
					{
						if (rect != null)
						{
							rect.GetWorldCorners(snapCorners);
							DrawSnapHighlight(snapCorners, rect);
						}
					}
				}

				// Draw position badge on top of all grid lines, axes, and snap highlights
				DrawPendingPositionBadge();

#if !UNITY_2021_2_OR_NEWER
				// Fallback floating Canvas Spec HUD for Unity 2020 and earlier (Unity 2021.2+ uses native UIFurnaceOverlay)
				DrawMovableSpecHUD(localWidth, localHeight);

				// #7 â€” Scene View HUD: fallback for Unity 2020 and earlier (Unity 2021.2+ uses native UIFurnaceOverlay)
				if (GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive)
					DrawEditModeHUD();
#endif
			}
			finally
			{
				Handles.zTest = originalZTest;
			}
		}


		// â”€â”€â”€ Menu Items & Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		[MenuItem("Tools/UI Furnace/Toggle Grid &g", false, 1)]
		public static void ToggleGrid()
		{
			GridSettings.SaveVisibility(!GridSettings.ShowGrid);
			SceneView.RepaintAll();
			RefreshOpenWindows();
		}

		[MenuItem("Tools/UI Furnace/Toggle Snap Elements to Grid &#s", false, 2)]
		public static void ToggleSnapElementsToGrid()
		{
			GridSettings.SaveSnapSettings(GridSettings.SnapToElements, !GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
			SceneView.RepaintAll();
			RefreshOpenWindows();
		}

		[MenuItem("Tools/UI Furnace/Configure Shortcuts...", false, 20)]
		public static void OpenShortcutManager()
		{
			EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
		}

		public static void ToggleEditMode()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines) return;
			if (GridSettings.IsEditModeActive)
			{
				ExitEditMode();
			}
			else
			{
				GridSettings.IsEditModeActive = true;
				GridSettings.TriggerSettingsChanged();
				RefreshOpenWindows();
			}
		}

		public static void StartAddingHorizontalLine()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive) return;
			s_IsAddingLine = true;
			s_AddingLineIsVertical = false;
			
			if (TryGetCachedComponents(out RectTransform rt, out Canvas c, out RectTransform crt))
			{
				Vector3[] corners = new Vector3[4];
				crt.GetWorldCorners(corners);
				Vector3 center = (corners[0] + corners[2]) * 0.5f;
				Vector3 rightDir = corners[3] - corners[0];
				Vector3 upDir = corners[1] - corners[0];
				PrepareSnapLines(center, rightDir.normalized, upDir.normalized);
			}

			if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
		}

		public static void StartAddingVerticalLine()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive) return;
			s_IsAddingLine = true;
			s_AddingLineIsVertical = true;
			
			if (TryGetCachedComponents(out RectTransform rt, out Canvas c, out RectTransform crt))
			{
				Vector3[] corners = new Vector3[4];
				crt.GetWorldCorners(corners);
				Vector3 center = (corners[0] + corners[2]) * 0.5f;
				Vector3 rightDir = corners[3] - corners[0];
				Vector3 upDir = corners[1] - corners[0];
				PrepareSnapLines(center, rightDir.normalized, upDir.normalized);
			}

			if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
		}

		public static void DeleteSelectedLines()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive || s_IsAddingLine) return;
			
			if (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0)
			{
				GridSettings.RecordUndo("Delete Grid Lines");

				// Cancel active drag to prevent out of bounds exceptions
				s_IsDragging = false;
				s_DragIndexX = -1;
				s_DragIndexY = -1;
				GUIUtility.hotControl = 0;

				if (GridSettings.IsMirrorModeActive)
				{
					var mx = new List<int>();
					foreach (int idx in SelectedIndicesX)
					{
						var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
						if (idx >= 0 && idx < list.Count)
						{
							float val = list[idx];
							for (int i = 0; i < list.Count; i++) if (i != idx && IsMirroredValue(val, list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { if (!SelectedIndicesX.Contains(i) && !mx.Contains(i)) mx.Add(i); }
						}
					}
					SelectedIndicesX.AddRange(mx);
					
					var my = new List<int>();
					foreach (int idx in SelectedIndicesY)
					{
						var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;
						if (idx >= 0 && idx < list.Count)
						{
							float val = list[idx];
							for (int i = 0; i < list.Count; i++) if (i != idx && IsMirroredValue(val, list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { if (!SelectedIndicesY.Contains(i) && !my.Contains(i)) my.Add(i); }
						}
					}
					SelectedIndicesY.AddRange(my);
				}

				// Sort indices in descending order so removing early elements doesn't shift the indices of later ones
				SelectedIndicesX.Sort((a, b) => b.CompareTo(a));
				SelectedIndicesY.Sort((a, b) => b.CompareTo(a));

				foreach (int index in SelectedIndicesX)
				{
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (index >= 0 && index < GridSettings.DynamicFixedX.Count) GridSettings.DynamicFixedX.RemoveAt(index);
					}
					else
					{
						if (index >= 0 && index < GridSettings.DynamicStretchX.Count) GridSettings.DynamicStretchX.RemoveAt(index);
					}
				}

				foreach (int index in SelectedIndicesY)
				{
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (index >= 0 && index < GridSettings.DynamicFixedY.Count) GridSettings.DynamicFixedY.RemoveAt(index);
					}
					else
					{
						if (index >= 0 && index < GridSettings.DynamicStretchY.Count) GridSettings.DynamicStretchY.RemoveAt(index);
					}
				}
				
				GridSettings.SaveDynamicLists();
				SelectedIndicesX.Clear();
				SelectedIndicesY.Clear();
				s_HoverIndexX = -1;
				s_HoverIndexY = -1;
				RefreshOpenWindows();
				if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
			}
		}

		public static void ToggleMirrorMode()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive) return;
			GridSettings.IsMirrorModeActive = !GridSettings.IsMirrorModeActive;
			SceneView.RepaintAll();
			RefreshOpenWindows();
		}

		public static void ToggleSnapLinesToUI()
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive) return;
			GridSettings.SaveSnapSettings(!GridSettings.SnapToElements, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
			SceneView.RepaintAll();
			RefreshOpenWindows();
		}

		private static void RefreshOpenWindows()
		{
			foreach (var win in Resources.FindObjectsOfTypeAll<LayoutDesignerWindow>())
				win.CreateGUI();
		}

		public static void ExitEditMode()
		{
			GridSettings.IsEditModeActive   = false;
			GridSettings.IsMirrorModeActive = false; // #8 reset mirror on exit
			s_IsAddingLine   = false;
			IsAddLinesMode   = false;
			s_IsAxisDragging = false;
			s_HoverEdgeH = false;
			s_HoverEdgeV = false;
			s_HoverEdgeTop = false;
			s_HoverEdgeLeft = false;
			GUIUtility.hotControl = 0;
			SelectedIndicesX.Clear();
			SelectedIndicesY.Clear();
			
			// Force Repaint for all SceneViews and Manager Windows so the toggle switch visually turns off
			GridSettings.TriggerSettingsChanged();
			RefreshOpenWindows();
		}
	}
}
#endif
