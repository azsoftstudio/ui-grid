#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Handles grid rendering in the Scene View.
	/// </summary>
	[InitializeOnLoad]
	public static class GridRenderer
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
		/// Used by GridManagerWindow to show a contextual warning.
		/// </summary>
		public static bool IsWorldSpaceCanvas { get; private set; }

		// â”€â”€â”€ Drag State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static int s_DragIndexX = -1;
		private static int s_DragIndexY = -1;
		private enum DragMode { None, Move, Resize }
		private static DragMode s_CurrentDragMode = DragMode.None;
		private static int s_MirrorDragIndexX = -1;
		private static int s_MirrorDragIndexY = -1;
		private static bool s_IsDragging = false;
		private static bool s_IsShiftDown = false;
		private static bool s_IsAltDown = false;

		private static bool IsMirroredValue(float val1, float val2, bool isFixed)
		{
			float target = isFixed ? -val1 : (1f - val1);
			return Mathf.Abs(val2 - target) < 0.001f;
		}
		private static int s_HoverIndexX = -1;
		private static int s_HoverIndexY = -1;
		
		private static bool s_IsAddingLine = false;
		private static bool s_AddingLineIsVertical = false;

		/// <summary>True when currently in interactive guide placement mode.</summary>
		public static bool IsAddingLine => s_IsAddingLine;
		/// <summary>True if currently placing a vertical guide, false if horizontal.</summary>
		public static bool AddingLineIsVertical => s_AddingLineIsVertical;

		/// <summary>Cancels interactive guide placement mode safely.</summary>
		public static void CancelAddingLine()
		{
			s_IsAddingLine = false;
			if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
		}

		// â”€â”€â”€ Add Lines Mode (Drag from Axis) State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		/// <summary>When true, users can drag from the canvas edges to pull out a new horizontal/vertical guide.</summary>
		public static bool IsAddLinesMode { get; set; } = false;
		private static bool s_IsAxisDragging = false;     // True while mid-drag creating a new line
		private static bool s_AxisDragIsVertical = false; // Which guide orientation is being pulled (true = vertical, false = horizontal)
		private static bool s_HoverEdgeH = false;         // Mouse hovering near horizontal pull edges (Top or Bottom)
		private static bool s_HoverEdgeV = false;         // Mouse hovering near vertical pull edges (Left or Right)
		private static bool s_HoverEdgeTop = false;       // Specifically top edge
		private static bool s_HoverEdgeLeft = false;      // Specifically left edge

		// â”€â”€â”€ Selection State (Transient) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		public static readonly List<int> SelectedIndicesX = new List<int>();
		public static readonly List<int> SelectedIndicesY = new List<int>();

		// â”€â”€â”€ Snap Caching State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static readonly List<float> s_SnapWorldLinesX = new List<float>();
		private static readonly List<float> s_SnapWorldLinesY = new List<float>();
		// Maps each snap line entry index to the source RectTransform â€” same order as snap lists above
		private static readonly List<RectTransform> s_SnapSourcesX = new List<RectTransform>();
		private static readonly List<RectTransform> s_SnapSourcesY = new List<RectTransform>();
		// The elements whose borders are currently being snapped to (for highlight drawing when dragging grid lines)
		private static readonly List<RectTransform> s_SnapHighlightedRects = new List<RectTransform>();
		// Elements snapped while dragging UI elements with Unity's Rect Tool
		private static readonly List<RectTransform> s_ElementSnapHighlightedRects = new List<RectTransform>();
		// The specific grid line offsets that should be highlighted (relative to canvas center)
		private static readonly HashSet<float> s_HighlightOffsetsX = new HashSet<float>();
		private static readonly HashSet<float> s_HighlightOffsetsY = new HashSet<float>();

		private static readonly List<float> s_ActiveWorldGridLinesX = new List<float>();
		private static readonly List<float> s_ActiveWorldGridLinesY = new List<float>();

		// Cache for optimizing the O(N) lookup of elements during drag loops
		private static RectTransform[] s_CachedSnapHierarchy = null;
		private static Matrix4x4[] s_CachedSnapMatrices = null;
		private static Rect[] s_CachedSnapRects = null;
		private static bool s_SnapHierarchyIsDirty = true;
		
		private static readonly List<RectTransform> s_HierarchyListBuffer = new List<RectTransform>();
		private static readonly HashSet<GameObject> s_SelectionBuffer = new HashSet<GameObject>();

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

		private static Vector3 s_LastDraggedPos;
		private static Vector2 s_LastSizeDelta;
		private static float s_LastMinX, s_LastMaxX, s_LastMinY, s_LastMaxY;
		private static int s_LastDraggedID = -1;

		private static void GetRectWorldProjections(RectTransform rt, out float minX, out float maxX, out float minY, out float maxY, out Vector3 center, out Vector3 right, out Vector3 up)
		{
			minX = maxX = minY = maxY = 0;
			center = right = up = Vector3.zero;

			if (s_CachedCanvasRect == null) return;

			s_CachedCanvasRect.GetWorldCorners(s_CornersCache);
			Vector3 BL = s_CornersCache[0], TL = s_CornersCache[1], TR = s_CornersCache[2], BR = s_CornersCache[3];
			center = (BL + TR) * 0.5f;
			Vector3 rightDir = BR - BL;
			Vector3 upDir = TL - BL;
			float canvasWidth = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			right = rightDir / canvasWidth;
			up = upDir / canvasHeight;

			rt.GetWorldCorners(s_CornersCache);
			minX = float.MaxValue; maxX = float.MinValue;
			minY = float.MaxValue; maxY = float.MinValue;

			for (int i = 0; i < 4; i++)
			{
				float projX = Vector3.Dot(s_CornersCache[i] - center, right);
				float projY = Vector3.Dot(s_CornersCache[i] - center, up);

				if (projX < minX) minX = projX;
				if (projX > maxX) maxX = projX;
				if (projY < minY) minY = projY;
				if (projY > maxY) maxY = projY;
			}
		}

		private static void ProcessElementSnapping()
		{
			if (s_IsDragging || s_IsAxisDragging || s_IsAddingLine) return;
			if (GUIUtility.hotControl == 0)
			{
				s_CurrentDragMode = DragMode.None;
			}
			if (!GridSettings.SnapElementsToGrid) return;
			if (GridSettings.ActiveProfile == null) return;
			if (Tools.current != Tool.Rect) return;
			
			var validRTs = new List<RectTransform>();
			foreach (var transform in Selection.transforms)
			{
				RectTransform rt = transform as RectTransform;
				if (rt != null && rt != s_CachedCanvasRect)
				{
					validRTs.Add(rt);
				}
			}
			if (validRTs.Count == 0) return;

			RectTransform firstRT = validRTs[0];
			int id = firstRT.GetInstanceID();
			
			if (id != s_LastDraggedID)
			{
				s_LastDraggedID = id;
				s_LastDraggedPos = firstRT.position;
				s_LastSizeDelta = firstRT.sizeDelta;
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			bool posChanged = Vector3.Distance(firstRT.position, s_LastDraggedPos) > 0.0001f;
			bool sizeChanged = Vector2.Distance(firstRT.sizeDelta, s_LastSizeDelta) > 0.0001f;

			if (!posChanged && !sizeChanged) 
			{
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			if (validRTs.Count > 1 && sizeChanged)
			{
				s_LastDraggedPos = firstRT.position;
				s_LastSizeDelta = firstRT.sizeDelta;
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			if (sizeChanged)
			{
				s_CurrentDragMode = DragMode.Resize;
				ApplyResizeSnapCorrection(firstRT);
			}
			else
			{
				s_CurrentDragMode = DragMode.Move;
				ApplySnapCorrection(validRTs);
			}

			s_LastDraggedPos = firstRT.position;
			s_LastSizeDelta = firstRT.sizeDelta;
			
			if (s_CachedCanvasRect != null)
			{
				GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
			}
		}

		private static void ApplyResizeSnapCorrection(RectTransform rt)
		{
			if (s_IsShiftDown || s_IsAltDown) return;

			// Resize snap math assumes the element's local X/Y axes are aligned with the Canvas axes.
			// If the element is rotated at all (even 90Â°/180Â°/270Â°), the mapping between world-space
			// drag deltas and sizeDelta axes breaks down â€” 90Â°/270Â° swap the axes entirely.
			// Only at 0Â° (Â±0.5Â° tolerance) is the math guaranteed to be correct.
			// Move snapping (world-space) still works correctly for any rotation.
			float zAngle = rt.localEulerAngles.z % 360f;
			if (zAngle > 0.5f && zAngle < 359.5f) return;
			
			if (s_CachedCanvasRect == null || (s_ActiveWorldGridLinesX.Count == 0 && s_ActiveWorldGridLinesY.Count == 0)) return;

			GetRectWorldProjections(rt, out float minX, out float maxX, out float minY, out float maxY, out Vector3 center, out Vector3 right, out Vector3 up);

			float worldSnapDist = GridSettings.ElementSnapDistance * s_CachedCanvasRect.lossyScale.x;

			float diffMinX = minX - s_LastMinX;
			float diffMaxX = maxX - s_LastMaxX;
			float diffMinY = minY - s_LastMinY;
			float diffMaxY = maxY - s_LastMaxY;

			bool snapLeft = false;
			bool snapRight = false;
			bool snapBottom = false;
			bool snapTop = false;

			if (Mathf.Abs(diffMinX) > Mathf.Abs(diffMaxX) && Mathf.Abs(diffMinX) > 0.0001f) snapLeft = true;
			else if (Mathf.Abs(diffMaxX) > 0.0001f) snapRight = true;

			if (Mathf.Abs(diffMinY) > Mathf.Abs(diffMaxY) && Mathf.Abs(diffMinY) > 0.0001f) snapBottom = true;
			else if (Mathf.Abs(diffMaxY) > 0.0001f) snapTop = true;

			float scaleX = rt.lossyScale.x != 0 ? rt.lossyScale.x : 1f;
			float scaleY = rt.lossyScale.y != 0 ? rt.lossyScale.y : 1f;
			float localScaleX = rt.localScale.x;
			float localScaleY = rt.localScale.y;

			if (snapLeft)
			{
				float bestDist = float.MaxValue;
				float snapOffsetX = 0f;
				foreach (float gridX in s_ActiveWorldGridLinesX)
				{
					float dist = Mathf.Abs(gridX - minX);
					if (dist < worldSnapDist && dist < bestDist)
					{
						bestDist = dist;
						snapOffsetX = gridX - minX;
					}
				}
				if (bestDist < float.MaxValue)
				{
					float localDeltaX = snapOffsetX / scaleX;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x - localDeltaX, rt.sizeDelta.y);
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + (1f - rt.pivot.x) * localDeltaX * localScaleX, rt.anchoredPosition.y);
				}
			}
			else if (snapRight)
			{
				float bestDist = float.MaxValue;
				float snapOffsetX = 0f;
				foreach (float gridX in s_ActiveWorldGridLinesX)
				{
					float dist = Mathf.Abs(gridX - maxX);
					if (dist < worldSnapDist && dist < bestDist)
					{
						bestDist = dist;
						snapOffsetX = gridX - maxX;
					}
				}
				if (bestDist < float.MaxValue)
				{
					float localDeltaX = snapOffsetX / scaleX;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x + localDeltaX, rt.sizeDelta.y);
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x + rt.pivot.x * localDeltaX * localScaleX, rt.anchoredPosition.y);
				}
			}

			if (snapBottom)
			{
				float bestDist = float.MaxValue;
				float snapOffsetY = 0f;
				foreach (float gridY in s_ActiveWorldGridLinesY)
				{
					float dist = Mathf.Abs(gridY - minY);
					if (dist < worldSnapDist && dist < bestDist)
					{
						bestDist = dist;
						snapOffsetY = gridY - minY;
					}
				}
				if (bestDist < float.MaxValue)
				{
					float localDeltaY = snapOffsetY / scaleY;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y - localDeltaY);
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y + (1f - rt.pivot.y) * localDeltaY * localScaleY);
				}
			}
			else if (snapTop)
			{
				float bestDist = float.MaxValue;
				float snapOffsetY = 0f;
				foreach (float gridY in s_ActiveWorldGridLinesY)
				{
					float dist = Mathf.Abs(gridY - maxY);
					if (dist < worldSnapDist && dist < bestDist)
					{
						bestDist = dist;
						snapOffsetY = gridY - maxY;
					}
				}
				if (bestDist < float.MaxValue)
				{
					float localDeltaY = snapOffsetY / scaleY;
					rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y + localDeltaY);
					rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rt.anchoredPosition.y + rt.pivot.y * localDeltaY * localScaleY);
				}
			}
		}

		private static void ApplySnapCorrection(List<RectTransform> validRTs)
		{
			if (s_CachedCanvasRect == null || (s_ActiveWorldGridLinesX.Count == 0 && s_ActiveWorldGridLinesY.Count == 0)) return;

			s_CachedCanvasRect.GetWorldCorners(s_CornersCache);
			Vector3 BL = s_CornersCache[0], TL = s_CornersCache[1], TR = s_CornersCache[2], BR = s_CornersCache[3];
			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			float worldSnapDist = GridSettings.ElementSnapDistance * s_CachedCanvasRect.lossyScale.x;

			float globalBestDistX = float.MaxValue; float globalSnapOffsetX = 0f; bool globalSnappedX = false;
			float globalBestDistY = float.MaxValue; float globalSnapOffsetY = 0f; bool globalSnappedY = false;

			foreach (var rt in validRTs)
			{
				rt.GetWorldCorners(s_CornersCache);
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;

				for (int i = 0; i < 4; i++)
				{
					float projX = Vector3.Dot(s_CornersCache[i] - center, right);
					float projY = Vector3.Dot(s_CornersCache[i] - center, up);

					if (projX < minX) minX = projX;
					if (projX > maxX) maxX = projX;
					if (projY < minY) minY = projY;
					if (projY > maxY) maxY = projY;
				}

				float[] ptsX = { minX, (minX + maxX) * 0.5f, maxX };
				float[] ptsY = { minY, (minY + maxY) * 0.5f, maxY };

				foreach (float gridX in s_ActiveWorldGridLinesX)
				{
					foreach (float ptX in ptsX)
					{
						float dist = Mathf.Abs(gridX - ptX);
						if (dist < worldSnapDist && dist < globalBestDistX)
						{
							globalBestDistX = dist;
							globalSnapOffsetX = gridX - ptX;
							globalSnappedX = true;
						}
					}
				}

				foreach (float gridY in s_ActiveWorldGridLinesY)
				{
					foreach (float ptY in ptsY)
					{
						float dist = Mathf.Abs(gridY - ptY);
						if (dist < worldSnapDist && dist < globalBestDistY)
						{
							globalBestDistY = dist;
							globalSnapOffsetY = gridY - ptY;
							globalSnappedY = true;
						}
					}
				}
			}

			if (globalSnappedX || globalSnappedY)
			{
				foreach (var rt in validRTs)
				{
					Vector3 newPos = rt.position;
					if (globalSnappedX) newPos += right * globalSnapOffsetX;
					if (globalSnappedY) newPos += up * globalSnapOffsetY;
					rt.position = newPos;
				}
			}
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
		/// Returns Vector2.zero when no canvas is selected â€” callers must guard against this.</summary>
		public static Vector2 GetCanvasSize()
		{
			if (s_CachedCanvasRect != null)
				return new Vector2(s_CachedCanvasRect.rect.width, s_CachedCanvasRect.rect.height);
			// BUG 3 fix: return zero so callers can detect the no-selection case
			// instead of silently generating lines based on a fake 1000x1000 canvas.
			return Vector2.zero;
		}

		private static void PrepareSnapLines(Vector3 center, Vector3 right, Vector3 up)
		{
			if (!GridSettings.SnapToElements || s_CachedCanvasRect == null) return;

			// Step 1: Check if the cache needs rebuilding entirely (hierarchy changed or null)
			if (s_SnapHierarchyIsDirty || s_CachedSnapHierarchy == null)
			{
				s_HierarchyListBuffer.Clear();
				s_CachedCanvasRect.GetComponentsInChildren<RectTransform>(false, s_HierarchyListBuffer);
				
				var validRTs = new List<RectTransform>(s_HierarchyListBuffer.Count);
				for (int i = 0; i < s_HierarchyListBuffer.Count; i++)
				{
					if (s_HierarchyListBuffer[i] != null)
					{
						validRTs.Add(s_HierarchyListBuffer[i]);
					}
				}

				s_CachedSnapHierarchy = validRTs.ToArray();
				s_CachedSnapMatrices = new Matrix4x4[s_CachedSnapHierarchy.Length];
				s_CachedSnapRects = new Rect[s_CachedSnapHierarchy.Length];
				s_SnapHierarchyIsDirty = false;
				
				// Force a full rebuild of the bounds since the array changed
				RebuildSnapBounds(center, right, up);
				return;
			}

			// Step 2: Ultra-fast dirty check on Transforms to see if ANY element moved, rotated or scaled
			bool needsRebuild = false;
			for (int i = 0; i < s_CachedSnapHierarchy.Length; i++)
			{
				var rt = s_CachedSnapHierarchy[i];
				if (rt == null) 
				{
					// An element was destroyed, hierarchy is fundamentally broken until next EditorApplication callback
					s_SnapHierarchyIsDirty = true;
					return;
				}
				
				if (rt.localToWorldMatrix != s_CachedSnapMatrices[i] || rt.rect != s_CachedSnapRects[i])
				{
					needsRebuild = true;
					break; // Break early since we don't need to clear hasChanged anymore
				}
			}

			if (needsRebuild)
			{
				RebuildSnapBounds(center, right, up);
			}
		}

		private static void RebuildSnapBounds(Vector3 center, Vector3 right, Vector3 up)
		{
			s_SnapWorldLinesX.Clear();
			s_SnapWorldLinesY.Clear();
			s_SnapSourcesX.Clear();
			s_SnapSourcesY.Clear();
			s_SnapHighlightedRects.Clear();

			for (int j = 0; j < s_CachedSnapHierarchy.Length; j++)
			{
				var rt = s_CachedSnapHierarchy[j];
				if (rt == null) continue;

				s_CachedSnapMatrices[j] = rt.localToWorldMatrix; // Cache the current matrix
				s_CachedSnapRects[j] = rt.rect; // Cache the current rect
				
				// Process all elements including the canvas itself to provide border snapping
				rt.GetWorldCorners(s_CornersCache);

				// Extract minimum and maximum projection on the canvas axes
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;

				for (int i = 0; i < 4; i++)
				{
					float projX = Vector3.Dot(s_CornersCache[i] - center, right);
					float projY = Vector3.Dot(s_CornersCache[i] - center, up);

					if (projX < minX) minX = projX;
					if (projX > maxX) maxX = projX;
					if (projY < minY) minY = projY;
					if (projY > maxY) maxY = projY;
				}

				// Cache Left, Right, Center for X â€” each paired with source RT
				s_SnapWorldLinesX.Add(minX); s_SnapSourcesX.Add(rt);
				s_SnapWorldLinesX.Add(maxX); s_SnapSourcesX.Add(rt);
				s_SnapWorldLinesX.Add((minX + maxX) * 0.5f); s_SnapSourcesX.Add(rt);

				// Cache Bottom, Top, Center for Y â€” each paired with source RT
				s_SnapWorldLinesY.Add(minY); s_SnapSourcesY.Add(rt);
				s_SnapWorldLinesY.Add(maxY); s_SnapSourcesY.Add(rt);
				s_SnapWorldLinesY.Add((minY + maxY) * 0.5f); s_SnapSourcesY.Add(rt);
			}
		}
		

		/// <summary>Takes a raw distance along an axis and magnetically snaps it to nearby UI boundaries if within SnapDistance.
		/// If a snap occurs, updates s_SnapHighlightedRects with all matching source elements.</summary>
		private static float ApplySnap(float rawProj, System.Collections.Generic.List<float> snapLines, System.Collections.Generic.List<RectTransform> snapSources)
		{
			if (!GridSettings.SnapToElements || s_CachedCanvasRect == null || snapLines.Count == 0) return rawProj;

			// The User specifies SnapDistance in local canvas units via the UI
			// We have to convert this raw value mathematically because canvas transforms scale
			float worldSnapDist = GridSettings.SnapDistance * s_CachedCanvasRect.lossyScale.x; 
			float tolerance = worldSnapDist * 0.05f;

			float bestSnap = rawProj;
			float minDistance = float.MaxValue;
			
			for (int i = 0; i < snapLines.Count; i++)
			{
				float dist = Mathf.Abs(snapLines[i] - rawProj);
				if (dist < worldSnapDist)
				{
					if (dist < minDistance - 0.001f)
					{
						minDistance = dist;
						bestSnap = snapLines[i];
					}
				}
			}

			// Second pass: Highlight ALL elements that align with our snapped position
			if (minDistance <= worldSnapDist)
			{
				float highlightTolerance = Mathf.Max(tolerance, 0.001f);
				for (int i = 0; i < snapLines.Count; i++)
				{
					if (Mathf.Abs(snapLines[i] - bestSnap) <= highlightTolerance)
					{
						if (snapSources[i] != null && !s_SnapHighlightedRects.Contains(snapSources[i]))
							s_SnapHighlightedRects.Add(snapSources[i]);
					}
				}
			}

			return bestSnap;
		}

		/// <summary>
		/// Snaps a 3D world coordinate to the nearest exact physical pixel on the monitor.
		/// This prevents sub-pixel aliasing/crawling when the camera pans smoothly.
		/// </summary>
		private static Vector3 PixelSnap(Vector3 point)
		{
			if (Camera.current == null) return point;
			
			// 1. Where does this point land on the 2D screen?
			Vector2 guiPoint = HandleUtility.WorldToGUIPoint(point);
			
			// 2. Lock it to a whole number pixel
			guiPoint.x = Mathf.Round(guiPoint.x);
			guiPoint.y = Mathf.Round(guiPoint.y);
			
			// 3. Fire a ray from that pixel back into the 3D world
			Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
			
			// 4. Create a plane at the exact same depth as our original point, facing the camera
			Plane plane = new Plane(-Camera.current.transform.forward, point);
			
			// 5. Find exactly where our pixel-perfect ray hits that plane
			if (plane.Raycast(ray, out float enter))
				return ray.GetPoint(enter);
				
			return point;
		}

		/// <summary>
		/// Low-level screen-space line draw. screenThickness is always in physical screen pixels.
		/// Used by both grid lines and UI preview overlays (hover, snap highlight, crosshairs).
		/// </summary>
		private static void DrawCrispLineScreenSpace(float screenThickness, Vector3 p1, Vector3 p2)
		{
			float t = Mathf.Max(1f, Mathf.Round(screenThickness));

			if (t <= 1f)
			{
				Handles.DrawLine(p1, p2);
			}
			else
			{
#if UNITY_2020_2_OR_NEWER
				Handles.DrawLine(p1, p2, t);
#else
				Handles.DrawAAPolyLine(t, p1, p2);
#endif
			}
		}

		/// <summary>
		/// Draws a grid line at a CONSTANT screen-space thickness regardless of zoom level.
		/// <para>
		/// screenThickness is the desired width in physical screen pixels â€” "1" is always one
		/// pixel on the monitor no matter how far in or out the user has zoomed. This matches
		/// the behaviour of Unity's own UI gizmos (RectTransform borders, anchor handles).
		/// </para>
		/// PixelSnap is still applied to the line ENDPOINTS so that the position of each line
		/// stays locked to whole pixels as the camera pans, preventing sub-pixel crawl.
		/// </summary>
		private static void DrawCrispLine(float screenThickness, Vector3 p1, Vector3 p2)
		{
			// Snap endpoints to whole screen pixels so lines don't crawl when panning.
			if (Camera.current != null && Camera.current.orthographic)
			{
				p1 = PixelSnap(p1);
				p2 = PixelSnap(p2);
			}

			DrawCrispLineScreenSpace(screenThickness, p1, p2);
		}

		/// <summary>Main rendering callback for Scene View.</summary>
		private static void OnSceneGUI(SceneView sceneView)
		{
			if (Event.current != null)
			{
				s_IsShiftDown = Event.current.shift;
				s_IsAltDown = Event.current.alt;
			}

			s_SnapHighlightedRects.Clear();
			s_ElementSnapHighlightedRects.Clear();
			s_HighlightOffsetsX.Clear();
			s_HighlightOffsetsY.Clear();
			s_PendingBadgeText = null;

			if (!GridSettings.ShowGrid) return;

			// Prefab mode is allowed â€” grid is useful when editing UI prefabs.
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

				// #7 — Scene View HUD: fallback for Unity 2020 and earlier (Unity 2021.2+ uses native UIFurnaceOverlay)
				if (GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive)
					DrawEditModeHUD();
#endif
			}
			finally
			{
				Handles.zTest = originalZTest;
			}
		}

		// ─── Drawing ──────────────────────────────────────────────────────────
		/// <summary>Apply global opacity (Suggestion 6) to a color.</summary>
		private static Color WithOpacity(Color color)
		{
			color.a *= GridSettings.GridOpacity;
			return color;
		}

		private static Rect s_HUDRect = new Rect(20, 20, 310, 52);
		private static bool s_HUDInitialized = false;
		private static bool s_HUDCollapsed = false;
		private static GUIStyle s_HUDWindowStyle;
		private static GUIStyle HUDWindowStyle
		{
			get
			{
				if (s_HUDWindowStyle == null)
				{
					s_HUDWindowStyle = new GUIStyle();
					s_HUDWindowStyle.padding = new RectOffset(6, 6, 4, 6);
					s_HUDWindowStyle.border = new RectOffset(0, 0, 0, 0);
				}
				return s_HUDWindowStyle;
			}
		}

		// #7 — Modern In-Scene Dual-Bar HUD
		private static void DrawEditModeHUD()
		{
			SceneView sv = SceneView.currentDrawingSceneView;
			if (sv == null) return;

			Handles.BeginGUI();

			if (!s_HUDInitialized)
			{
				s_HUDCollapsed = EditorPrefs.GetBool("UIFurnace_HUD_Collapsed", false);
				float defaultX = EditorPrefs.GetFloat("UIFurnace_HUD_X", 20f);
				float defaultY = EditorPrefs.GetFloat("UIFurnace_HUD_Y", sv.position.height - 85f);

				s_HUDRect.x = defaultX;
				s_HUDRect.y = defaultY;
				s_HUDInitialized = true;
			}

			float targetWidth = s_HUDCollapsed ? 180f : 325f;
			float targetHeight = s_HUDCollapsed ? 26f : 54f;
			s_HUDRect.width = targetWidth;
			s_HUDRect.height = targetHeight;

			// Keep window on screen
			s_HUDRect.x = Mathf.Clamp(s_HUDRect.x, 0, Mathf.Max(0, sv.position.width - s_HUDRect.width));
			s_HUDRect.y = Mathf.Clamp(s_HUDRect.y, 0, Mathf.Max(0, sv.position.height - s_HUDRect.height));

			float preHUDX = s_HUDRect.x;
			float preHUDY = s_HUDRect.y;
			int controlID = GUIUtility.GetControlID("UIFurnaceHUD".GetHashCode(), FocusType.Passive);
			s_HUDRect = GUILayout.Window(controlID, s_HUDRect, HUDWindowContent, GUIContent.none, HUDWindowStyle);

			if (Event.current.type == EventType.MouseUp && (Mathf.Abs(s_HUDRect.x - preHUDX) > 0.5f || Mathf.Abs(s_HUDRect.y - preHUDY) > 0.5f))
			{
				EditorPrefs.SetFloat("UIFurnace_HUD_X", s_HUDRect.x);
				EditorPrefs.SetFloat("UIFurnace_HUD_Y", s_HUDRect.y);
			}

			Handles.EndGUI();
		}

		private static void HUDWindowContent(int id)
		{
			Rect totalRect = new Rect(0, 0, s_HUDRect.width, s_HUDRect.height);

			// 1. Drop shadow for depth
			EditorGUI.DrawRect(new Rect(totalRect.x + 1f, totalRect.y + 1f, totalRect.width, totalRect.height), new Color(0f, 0f, 0f, 0.45f));

			// 2. Solid native panel background (adapts to Unity dark/light theme)
			Color panelBg = EditorGUIUtility.isProSkin
				? new Color(0.18f, 0.18f, 0.21f, 0.96f)
				: new Color(0.85f, 0.85f, 0.88f, 0.96f);
			EditorGUI.DrawRect(totalRect, panelBg);

			// 3. Crisp border
			Color borderColor = EditorGUIUtility.isProSkin
				? new Color(0.12f, 0.12f, 0.14f, 0.9f)
				: new Color(0.55f, 0.55f, 0.6f, 0.9f);
			DrawBadgeBorder(totalRect, 1f, borderColor);

			// --- BAR 1: Header / Title Bar ---
			GUILayout.BeginHorizontal();

			// Grip icon
			GUILayout.Label(new GUIContent("⠿", "Drag to reposition toolbar"), EditorStyles.miniLabel, GUILayout.Width(14));

			// Green LED Status Dot
			var defColor = GUI.color;
			GUI.color = new Color(0.2f, 0.85f, 0.4f, 1f);
			GUILayout.Label("●", EditorStyles.boldLabel, GUILayout.Width(12));
			GUI.color = defColor;

			// Title Label
			GUILayout.Label(s_HUDCollapsed ? "UI Furnace" : "UI Furnace (Edit)", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();

			// Collapse / Expand Button
			string collapseIcon = s_HUDCollapsed ? "▸" : "—";
			string collapseTooltip = s_HUDCollapsed ? "Expand Toolbar" : "Minimize Toolbar";
			if (GUILayout.Button(new GUIContent(collapseIcon, collapseTooltip), EditorStyles.toolbarButton, GUILayout.Width(22), GUILayout.Height(18)))
			{
				s_HUDCollapsed = !s_HUDCollapsed;
				EditorPrefs.SetBool("UIFurnace_HUD_Collapsed", s_HUDCollapsed);
				if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
			}

			// Explicit Exit Button
			if (GUILayout.Button(new GUIContent("Exit", "Exit Edit Mode"), EditorStyles.toolbarButton, GUILayout.Width(34), GUILayout.Height(18)))
			{
				ExitEditMode();
			}

			GUILayout.EndHorizontal();

			// Drag region for Bar 1 (leaves minimize and exit buttons responsive)
			GUI.DragWindow(new Rect(0, 0, totalRect.width - 62f, 22f));

			if (s_HUDCollapsed) return;

			// --- Divider ---
			GUILayout.Space(2);
			Rect divRect = GUILayoutUtility.GetRect(totalRect.width - 12f, 1f);
			EditorGUI.DrawRect(divRect, new Color(0.5f, 0.5f, 0.5f, 0.25f));
			GUILayout.Space(2);

			// --- BAR 2: Toolbar Controls ---
			GUILayout.BeginHorizontal();

			Color defaultHUDControlBg = GUI.backgroundColor;

			// 1. Snap Toggle
			bool curSnap = GridSettings.SnapToElements;
			if (curSnap) GUI.backgroundColor = new Color(0.22f, 0.58f, 0.94f, 1f);
			bool newSnap = GUILayout.Toggle(curSnap, new GUIContent("Snap (Lines)", "Snap grid line to UI"), EditorStyles.toolbarButton, GUILayout.Height(20));
			GUI.backgroundColor = defaultHUDControlBg;
			if (newSnap != curSnap)
			{
				GridSettings.SaveSnapSettings(newSnap, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
				SceneView.RepaintAll();
			}

			// 2. Mirror Toggle
			bool curMirror = GridSettings.IsMirrorModeActive;
			if (curMirror) GUI.backgroundColor = new Color(0.22f, 0.58f, 0.94f, 1f);
			bool newMirror = GUILayout.Toggle(curMirror, new GUIContent("Mirror", "Toggle symmetric guide mirroring"), EditorStyles.toolbarButton, GUILayout.Height(20));
			GUI.backgroundColor = defaultHUDControlBg;
			if (newMirror != curMirror)
			{
				GridSettings.IsMirrorModeActive = newMirror;
				SceneView.RepaintAll();
			}

			// 3. Add Horizontal Guide
			bool isAdding = s_IsAddingLine;
			bool isAddingV = s_AddingLineIsVertical;
			if (isAdding && !isAddingV) GUI.backgroundColor = new Color(0.98f, 0.65f, 0.15f, 1f);
			string hLabel = (isAdding && !isAddingV) ? "Placing H..." : "+ H Line";
			if (GUILayout.Button(new GUIContent(hLabel, "Place a horizontal guide (or click to cancel)"), EditorStyles.toolbarButton, GUILayout.Height(20)))
			{
				if (isAdding && !isAddingV) CancelAddingLine();
				else StartAddingHorizontalLine();
			}
			GUI.backgroundColor = defaultHUDControlBg;

			// 4. Add Vertical Guide
			if (isAdding && isAddingV) GUI.backgroundColor = new Color(0.98f, 0.65f, 0.15f, 1f);
			string vLabel = (isAdding && isAddingV) ? "Placing V..." : "+ V Line";
			if (GUILayout.Button(new GUIContent(vLabel, "Place a vertical guide (or click to cancel)"), EditorStyles.toolbarButton, GUILayout.Height(20)))
			{
				if (isAdding && isAddingV) CancelAddingLine();
				else StartAddingVerticalLine();
			}
			GUI.backgroundColor = defaultHUDControlBg;

			// 5. Delete Selected Guides
			int selCount = SelectedIndicesX.Count + SelectedIndicesY.Count;
			bool hasSelection = selCount > 0;
			GUI.enabled = hasSelection;
			if (hasSelection) GUI.backgroundColor = new Color(0.92f, 0.28f, 0.28f, 1f);
			string delLabel = hasSelection ? $"Delete ({selCount})" : "Delete";
			if (GUILayout.Button(new GUIContent(delLabel, "Delete Selected Guides (Del)"), EditorStyles.toolbarButton, GUILayout.Height(20)))
			{
				DeleteSelectedLines();
			}
			GUI.backgroundColor = defaultHUDControlBg;
			GUI.enabled = true;

			GUILayout.EndHorizontal();
		}

		private static void DrawFixedSpacingGrid(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR, float localWidth, float localHeight)
		{
			float spacingX     = GridSettings.GridSpacingX;
			float spacingY     = GridSettings.GridSpacingY;
			float gridThickness = GridSettings.GridThickness;
			float axisThickness = GridSettings.AxisThickness;
			Color gridColor    = WithOpacity(GridSettings.GridColor);
			Color xAxisColor   = WithOpacity(GridSettings.XAxisColor);
			Color yAxisColor   = WithOpacity(GridSettings.YAxisColor);

			spacingX = spacingX < 0.1f ? 0.1f : spacingX;
			spacingY = spacingY < 0.1f ? 0.1f : spacingY;

			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			float worldSpacingX = spacingX * (localWidth > 0.001f ? canvasWidth / localWidth : 1f);
			float worldSpacingY = spacingY * (localHeight > 0.001f ? canvasHeight / localHeight : 1f);
			if (worldSpacingX < 0.0001f) worldSpacingX = 0.0001f;
			if (worldSpacingY < 0.0001f) worldSpacingY = 0.0001f;

			float halfWidth    = canvasWidth  * 0.5f;
			float halfHeight   = canvasHeight * 0.5f;

			Handles.color = gridColor;
			for (float o = worldSpacingX; o <= halfWidth; o += worldSpacingX)
			{
				Vector3 pos = center + right * o;
				DrawCrispLine(gridThickness, pos - up * halfHeight, pos + up * halfHeight);
			}
			for (float o = -worldSpacingX; o >= -halfWidth; o -= worldSpacingX)
			{
				Vector3 pos = center + right * o;
				DrawCrispLine(gridThickness, pos - up * halfHeight, pos + up * halfHeight);
			}
			for (float o = worldSpacingY; o <= halfHeight; o += worldSpacingY)
			{
				Vector3 pos = center + up * o;
				DrawCrispLine(gridThickness, pos - right * halfWidth, pos + right * halfWidth);
			}
			for (float o = -worldSpacingY; o >= -halfHeight; o -= worldSpacingY)
			{
				Vector3 pos = center + up * o;
				DrawCrispLine(gridThickness, pos - right * halfWidth, pos + right * halfWidth);
			}

			Handles.color = xAxisColor;
			DrawCrispLine(axisThickness, center - right * halfWidth, center + right * halfWidth);
			Handles.color = yAxisColor;
			DrawCrispLine(axisThickness, center - up * halfHeight, center + up * halfHeight);
		}

		/// <summary>Draw grid with percentage-based stretching.</summary>
		private static void DrawStretchGrid(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR)
		{
			int   cols          = GridSettings.GridColumns;
			int   rows          = GridSettings.GridRows;
			float gridThickness = GridSettings.GridThickness;
			float axisThickness = GridSettings.AxisThickness;
			Color gridColor     = WithOpacity(GridSettings.GridColor);
			Color xAxisColor    = WithOpacity(GridSettings.XAxisColor);
			Color yAxisColor    = WithOpacity(GridSettings.YAxisColor);

			cols = cols < 1 ? 1 : cols;
			rows = rows < 1 ? 1 : rows;

			Handles.color = gridColor;
			for (int i = 0; i <= rows; i++)
			{
				float t = (float)i / rows;
				DrawCrispLine(gridThickness, Vector3.Lerp(BL, TL, t), Vector3.Lerp(BR, TR, t));
			}
			for (int j = 0; j <= cols; j++)
			{
				float t = (float)j / cols;
				DrawCrispLine(gridThickness, Vector3.Lerp(BL, BR, t), Vector3.Lerp(TL, TR, t));
			}

			// Center axes are always drawn at the canvas geometric midpoint (t = 0.5).
			// They represent the main X/Y axes of the canvas and must not shift with column/row count.
			// X axis = horizontal line at vertical canvas centre
			Handles.color = xAxisColor;
			DrawCrispLine(axisThickness, Vector3.Lerp(BL, TL, 0.5f), Vector3.Lerp(BR, TR, 0.5f));

			// Y axis = vertical line at horizontal canvas centre
			Handles.color = yAxisColor;
			DrawCrispLine(axisThickness, Vector3.Lerp(BL, BR, 0.5f), Vector3.Lerp(TL, TR, 0.5f));
		}

		// â”€â”€â”€ Interactive Element Snapping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static void HandleElementSnapping(Vector3 center, Vector3 right, Vector3 up, float canvasWidth, float canvasHeight, float scaleX, float scaleY)
		{
			if (!GridSettings.SnapElementsToGrid) return;
			// If user is currently dragging, axis-dragging, or adding a grid line, element-to-grid snapping must NOT run!
			if (s_IsDragging || s_IsAxisDragging || s_IsAddingLine) return;
			// Ensure user is actually using the Rect Tool
			if (Tools.current != Tool.Rect) return;

			// Check if we are actively dragging something in the scene
			int hotID = GUIUtility.hotControl;
			if (hotID == 0) 
			{
				s_CurrentDragMode = DragMode.None;
				return;
			}

			// Must be an active element drag/resize (not stationary or another tool's hotControl)
			if (s_CurrentDragMode == DragMode.None) return;

			if (s_CurrentDragMode == DragMode.Resize && (s_IsShiftDown || s_IsAltDown))
				return;

			// We need to fetch the dragged item. Selection.transforms gets the Active selected objects being dragged.
			if (Selection.transforms.Length == 0) return;

			float worldSnapDist = GridSettings.ElementSnapDistance * s_CachedCanvasRect.lossyScale.x;
			// Tolerance for visual highlighting (matching the snapping precision)
			float tolerance = worldSnapDist * 0.05f;

			// Generate an array of all current active Grid line positions in World Space
			s_ActiveWorldGridLinesX.Clear();
			s_ActiveWorldGridLinesY.Clear();

			if (GridSettings.CurrentMode == GridMode.UniformGrid)
			{
				if (GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
				{
					float localWidth = s_CachedCanvasRect.rect.width;
					float localHeight = s_CachedCanvasRect.rect.height;
					float worldSpacingX = GridSettings.GridSpacingX * (localWidth > 0.001f ? canvasWidth / localWidth : 1f);
					float worldSpacingY = GridSettings.GridSpacingY * (localHeight > 0.001f ? canvasHeight / localHeight : 1f);
					if (worldSpacingX < 0.0001f) worldSpacingX = 0.0001f;
					if (worldSpacingY < 0.0001f) worldSpacingY = 0.0001f;

					float halfWidth = canvasWidth * 0.5f;
					float halfHeight = canvasHeight * 0.5f;

					// Add center
					s_ActiveWorldGridLinesX.Add(0f);
					s_ActiveWorldGridLinesY.Add(0f);

					for (float o = worldSpacingX; o <= halfWidth; o += worldSpacingX) { s_ActiveWorldGridLinesX.Add(o); s_ActiveWorldGridLinesX.Add(-o); }
					for (float o = worldSpacingY; o <= halfHeight; o += worldSpacingY) { s_ActiveWorldGridLinesY.Add(o); s_ActiveWorldGridLinesY.Add(-o); }
				}
				else // Symmetry Stretch
				{
					int cols = GridSettings.GridColumns < 1 ? 1 : GridSettings.GridColumns;
					int rows = GridSettings.GridRows < 1 ? 1 : GridSettings.GridRows;
					
					for (int i = 0; i <= cols; i++)
					{
						float t = (float)i / cols;
						s_ActiveWorldGridLinesX.Add((t - 0.5f) * canvasWidth);
					}
					for (int j = 0; j <= rows; j++)
					{
						float t = (float)j / rows;
						s_ActiveWorldGridLinesY.Add((t - 0.5f) * canvasHeight);
					}
				}
			}
			else // Dynamic Mode
			{
				s_ActiveWorldGridLinesX.Add(0);
				s_ActiveWorldGridLinesY.Add(0);

				if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
				{
					foreach (float val in GridSettings.DynamicFixedX) s_ActiveWorldGridLinesX.Add(val * scaleX);
					foreach (float val in GridSettings.DynamicFixedY) s_ActiveWorldGridLinesY.Add(val * scaleY);
				}
				else // Dynamic Stretch
				{
					foreach (float val in GridSettings.DynamicStretchX) s_ActiveWorldGridLinesX.Add((val - 0.5f) * canvasWidth);
					foreach (float val in GridSettings.DynamicStretchY) s_ActiveWorldGridLinesY.Add((val - 0.5f) * canvasHeight);
				}
			}

			// Snapping Logic
			var validRTs = new List<RectTransform>();
			foreach (var transform in Selection.transforms)
			{
				RectTransform rt = transform as RectTransform;
				if (rt != null && rt != s_CachedCanvasRect)
				{
					validRTs.Add(rt);
				}
			}
			if (validRTs.Count == 0) return;

			if (validRTs.Count > 1 && s_CurrentDragMode == DragMode.Resize) return;
			
			float globalBestDistX = float.MaxValue;
			float globalBestDistY = float.MaxValue;
			float globalSnapOffsetX = 0f;
			float globalSnapOffsetY = 0f;
			bool globalSnappedX = false;
			bool globalSnappedY = false;

			// Pass 1: Find global best distances
			foreach (var rt in validRTs)
			{
				if (s_CurrentDragMode == DragMode.Resize)
				{
					float zRot = rt.localEulerAngles.z % 360f;
					if (zRot > 0.5f && zRot < 359.5f) continue;
				}

				rt.GetWorldCorners(s_CornersCache);
				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;

				for (int i = 0; i < 4; i++)
				{
					float projX = Vector3.Dot(s_CornersCache[i] - center, right);
					float projY = Vector3.Dot(s_CornersCache[i] - center, up);
					if (projX < minX) minX = projX;
					if (projX > maxX) maxX = projX;
					if (projY < minY) minY = projY;
					if (projY > maxY) maxY = projY;
				}

				float[] ptsX = s_CurrentDragMode == DragMode.Resize ? new float[] { minX, maxX } : new float[] { minX, (minX + maxX) * 0.5f, maxX };
				float[] ptsY = s_CurrentDragMode == DragMode.Resize ? new float[] { minY, maxY } : new float[] { minY, (minY + maxY) * 0.5f, maxY };

				foreach (float gridX in s_ActiveWorldGridLinesX)
				{
					foreach (float ptX in ptsX)
					{
						float dist = Mathf.Abs(gridX - ptX);
						if (dist < worldSnapDist && dist < globalBestDistX)
						{
							globalBestDistX = dist;
							globalSnapOffsetX = gridX - ptX;
							globalSnappedX = true;
						}
					}
				}

				foreach (float gridY in s_ActiveWorldGridLinesY)
				{
					foreach (float ptY in ptsY)
					{
						float dist = Mathf.Abs(gridY - ptY);
						if (dist < worldSnapDist && dist < globalBestDistY)
						{
							globalBestDistY = dist;
							globalSnapOffsetY = gridY - ptY;
							globalSnappedY = true;
						}
					}
				}
			}

			// Pass 2: Collect highlights using the global offsets
			s_HighlightOffsetsX.Clear();
			s_HighlightOffsetsY.Clear();
			s_ElementSnapHighlightedRects.Clear();

			if (globalSnappedX || globalSnappedY)
			{
				foreach (var rt in validRTs)
				{
					if (s_CurrentDragMode == DragMode.Resize)
					{
						float zRot = rt.localEulerAngles.z % 360f;
						if (zRot > 0.5f && zRot < 359.5f) continue;
					}

					rt.GetWorldCorners(s_CornersCache);
					float minX = float.MaxValue, maxX = float.MinValue;
					float minY = float.MaxValue, maxY = float.MinValue;

					for (int i = 0; i < 4; i++)
					{
						float projX = Vector3.Dot(s_CornersCache[i] - center, right);
						float projY = Vector3.Dot(s_CornersCache[i] - center, up);
						if (projX < minX) minX = projX;
						if (projX > maxX) maxX = projX;
						if (projY < minY) minY = projY;
						if (projY > maxY) maxY = projY;
					}

					float[] ptsX = s_CurrentDragMode == DragMode.Resize ? new float[] { minX, maxX } : new float[] { minX, (minX + maxX) * 0.5f, maxX };
					float[] ptsY = s_CurrentDragMode == DragMode.Resize ? new float[] { minY, maxY } : new float[] { minY, (minY + maxY) * 0.5f, maxY };

					bool matchedForThisRect = false;

					if (globalSnappedX)
					{
						foreach (float gridX in s_ActiveWorldGridLinesX)
						{
							foreach (float ptX in ptsX)
							{
								if (Mathf.Abs(gridX - (ptX + globalSnapOffsetX)) < tolerance)
								{
									s_HighlightOffsetsX.Add(gridX);
									matchedForThisRect = true;
								}
							}
						}
					}
					if (globalSnappedY)
					{
						foreach (float gridY in s_ActiveWorldGridLinesY)
						{
							foreach (float ptY in ptsY)
							{
								if (Mathf.Abs(gridY - (ptY + globalSnapOffsetY)) < tolerance)
								{
									s_HighlightOffsetsY.Add(gridY);
									matchedForThisRect = true;
								}
							}
						}
					}

					if (matchedForThisRect)
					{
						s_ElementSnapHighlightedRects.Add(rt);
					}
				}
			}

			// Pass 3: Draw all unique snapped lines once
			if ((s_HighlightOffsetsX.Count > 0 || s_HighlightOffsetsY.Count > 0) && Event.current.type == EventType.Repaint)
			{
				Color snapLineColor = GetGridComplementColor();
				snapLineColor.a = 0.95f;
				Handles.color = snapLineColor;
				float snapLineThickness = Mathf.Max(2.5f, GridSettings.GridThickness + 1.5f);
				float halfW = canvasWidth * 0.5f;
				float halfH = canvasHeight * 0.5f;

				foreach (float gridX in s_HighlightOffsetsX)
					DrawCrispLine(snapLineThickness, center + right * gridX - up * halfH, center + right * gridX + up * halfH);
				
				foreach (float gridY in s_HighlightOffsetsY)
					DrawCrispLine(snapLineThickness, center + up * gridY - right * halfW, center + up * gridY + right * halfW);

				if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
			}
		}
		// â”€â”€â”€ Interactive Dynamic Mode Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static void HandleDynamicGridEvents(Event e, int controlID, Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR, float localWidth, float localHeight)
		{
			if (GridSettings.CurrentMode != GridMode.CustomLines || !GridSettings.IsEditModeActive) return;

			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			float scaleX = localWidth > 0.001f ? (canvasWidth / localWidth) : 1f;
			float scaleY = localHeight > 0.001f ? (canvasHeight / localHeight) : 1f;

			float halfWidth    = canvasWidth  * 0.5f;
			float halfHeight   = canvasHeight * 0.5f;

			// Handle Raycasting to find closest line
			Ray mouseRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
			float hitDist;
			// BUG 4 fix: check Plane.Raycast return value â€” if the ray is parallel to
			// the canvas plane (e.g. edge-on scene view) hitDist is 0 / garbage;
			// proceeding would snap all guides to the ray origin.
			if (!new Plane(Vector3.Cross(right, up), center).Raycast(mouseRay, out hitDist)) return;
			Vector3 mouseWorldPos = mouseRay.GetPoint(hitDist);

			Vector3 labelOffset = Vector3.zero;
			if (Camera.current != null)
				labelOffset = (Camera.current.transform.right - Camera.current.transform.up) * HandleUtility.GetHandleSize(mouseWorldPos) * 0.2f;

			// HandleUtility.DistanceToLine returns distance in GUI pixels. 
			// We must use a constant pixel threshold instead of world space size to prevent precision loss when zooming out.
			float grabThreshold = 10f;

			controlID = GUIUtility.GetControlID("GridLineDragger".GetHashCode(), FocusType.Passive);
			
			// Layout event: Register our control distance with Unity so it knows if we want the mouse click
			// or if it should pass the click through to standard UI Selection behind our grid.
			if (e.type == EventType.Layout)
			{
				float closestDistLayoutX = float.MaxValue;
				float closestDistLayoutY = float.MaxValue;
				
				if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
				{
					for (int i = 0; i < GridSettings.DynamicFixedX.Count; i++)
					{
						Vector3 linePos = center + right * (GridSettings.DynamicFixedX[i] * scaleX);
						float dist = HandleUtility.DistanceToLine(linePos - up * halfHeight, linePos + up * halfHeight);
						if (dist < closestDistLayoutX) closestDistLayoutX = dist;
					}
					for (int i = 0; i < GridSettings.DynamicFixedY.Count; i++)
					{
						Vector3 linePos = center + up * (GridSettings.DynamicFixedY[i] * scaleY);
						float dist = HandleUtility.DistanceToLine(linePos - right * halfWidth, linePos + right * halfWidth);
						if (dist < closestDistLayoutY) closestDistLayoutY = dist;
					}
				}
				else // Stretch Model
				{
					for (int i = 0; i < GridSettings.DynamicStretchX.Count; i++)
					{
						float t = GridSettings.DynamicStretchX[i];
						Vector3 p1 = Vector3.Lerp(BL, BR, t); Vector3 p2 = Vector3.Lerp(TL, TR, t);
						float dist = HandleUtility.DistanceToLine(p1, p2);
						if (dist < closestDistLayoutX) closestDistLayoutX = dist;
					}
					for (int i = 0; i < GridSettings.DynamicStretchY.Count; i++)
					{
						float t = GridSettings.DynamicStretchY[i];
						Vector3 p1 = Vector3.Lerp(BL, TL, t); Vector3 p2 = Vector3.Lerp(BR, TR, t);
						float dist = HandleUtility.DistanceToLine(p1, p2);
						if (dist < closestDistLayoutY) closestDistLayoutY = dist;
					}
				}
				
				float closestLineDist = Mathf.Min(closestDistLayoutX, closestDistLayoutY);
				if (closestLineDist < grabThreshold)
				{
					HandleUtility.AddControl(controlID, closestLineDist);
				}
			}

			int hoverIndexX = -1;
			int hoverIndexY = -1;

			// Right-click context menu processing for the Scene View
			if (e.type == EventType.ContextClick || (e.type == EventType.MouseUp && e.button == 1))
			{
				GenericMenu menu = new GenericMenu();
				menu.AddItem(new GUIContent("Add Horizontal Line"), false, StartAddingHorizontalLine);
				menu.AddItem(new GUIContent("Add Vertical Line"), false, StartAddingVerticalLine);
				menu.AddSeparator("");
				if (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0)
					menu.AddItem(new GUIContent("Delete Selected Lines\tDel"), false, DeleteSelectedLines);
				else
					menu.AddDisabledItem(new GUIContent("Delete Selected Lines\tDel"));
				
				menu.AddSeparator("");
				menu.AddItem(new GUIContent("Exit Edit Mode"), false, ExitEditMode);
				
				menu.ShowAsContext();
				e.Use();
			}

			// Key events in Scene View for grid editing: Delete / Backspace removes selected lines
			if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
			{
				if (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0)
				{
					DeleteSelectedLines();
					e.Use();
					return;
				}
			}

			// Add Line Preview Mode override
			if (s_IsAddingLine)
			{
				if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
				{
					s_IsAddingLine = false;
					e.Use();
				}
				
				if (e.type == EventType.Repaint)
				{
					// Match the actual grid line's color when dragging/hovering
					Color baseColor = WithOpacity(GridSettings.GridColor);
					float h, s, v; Color.RGBToHSV(baseColor, out h, out s, out v);
					float dragHue = (h + 0.25f) % 1f;
					Color dragColor = Color.HSVToRGB(dragHue, Mathf.Max(0.5f, s), Mathf.Max(0.6f, v));
					dragColor.a = baseColor.a;

					EditorGUIUtility.AddCursorRect(new Rect(0,0,10000,10000), MouseCursor.ArrowPlus, controlID);
					Handles.color = dragColor;

					// Visually lock the preview line to the actual grid axis just like a real grid line
					if (s_SnapWorldLinesX.Count == 0 && s_SnapWorldLinesY.Count == 0)
						PrepareSnapLines(center, right, up);

					if (s_AddingLineIsVertical)
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, right);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesX, s_SnapSourcesX);
						float t = snappedProj / canvasWidth;
						Vector3 lockedPos = center + right * (t * canvasWidth);
						DrawCrispLine(GridSettings.GridThickness, lockedPos - up * halfHeight, lockedPos + up * halfHeight);
						// #8 â€” Ghost mirror preview
						if (GridSettings.IsMirrorModeActive)
						{
							Color ghost = dragColor; ghost.a *= 0.4f;
							Handles.color = ghost;
							Vector3 mirrorPos = center + right * (-t * canvasWidth);
							DrawCrispLine(GridSettings.GridThickness, mirrorPos - up * halfHeight, mirrorPos + up * halfHeight);
							Handles.color = dragColor;
						}
						// #10 — Offset label. t = snappedProj / canvasWidth, ranges [-0.5, 0.5]
						float pxOffX = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localWidth > 0.001f ? canvasWidth / localWidth : 1f)
							: t * localWidth;
						DrawPositionBadge(mouseWorldPos, $"X: {(pxOffX >= 0 ? "+" : "")}{pxOffX:F0} px");
					}
					else
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, up);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesY, s_SnapSourcesY);
						float t = snappedProj / canvasHeight;
						Vector3 lockedPos = center + up * (t * canvasHeight);
						DrawCrispLine(GridSettings.GridThickness, lockedPos - right * halfWidth, lockedPos + right * halfWidth);
						// #8 — Ghost mirror preview
						if (GridSettings.IsMirrorModeActive)
						{
							Color ghost = dragColor; ghost.a *= 0.4f;
							Handles.color = ghost;
							Vector3 mirrorPos = center + up * (-t * canvasHeight);
							DrawCrispLine(GridSettings.GridThickness, mirrorPos - right * halfWidth, mirrorPos + right * halfWidth);
							Handles.color = dragColor;
						}
						// #10 — Offset label. t = snappedProj / canvasHeight, ranges [-0.5, 0.5]
						float pxOffY = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localHeight > 0.001f ? canvasHeight / localHeight : 1f)
							: t * localHeight;
						DrawPositionBadge(mouseWorldPos, $"Y: {(pxOffY >= 0 ? "+" : "")}{pxOffY:F0} px");
					}

					// Snap highlight is drawn after DrawDynamicGrid in OnSceneGUI to ensure it's always on top.
					// Nothing to draw here.
				}
				else if (e.type == EventType.MouseDown && e.button == 0)
				{
					Undo.RecordObject(GridSettingsAsset.instance, "Add Grid Line");
					
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (s_AddingLineIsVertical)
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) / scaleX;
							GridSettings.DynamicFixedX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicFixedX.Add(-val);
						}
						else
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) / scaleY;
							GridSettings.DynamicFixedY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicFixedY.Add(-val);
						}
					}
					else
					{
						if (s_AddingLineIsVertical)
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) + halfWidth) / canvasWidth;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchX.Add(Mathf.Clamp01(1f - val));
						}
						else
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchY.Add(Mathf.Clamp01(1f - val));
						}
					}
					
					GridSettings.SaveDynamicLists();
					s_IsAddingLine = false;
					e.Use();
				}
				
				if (e.type == EventType.MouseMove)
				{
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					e.Use();
				}
				
				return; // Skip normal hover/drag logic while adding a line
			} // end s_IsAddingLine

			// â”€â”€â”€ Add Lines Mode: Drag from Axis to Create New Line â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
			if (IsAddLinesMode && !s_IsAxisDragging)
			{
				float distTop    = HandleUtility.DistanceToLine(TL, TR);
				float distBottom = HandleUtility.DistanceToLine(BL, BR);
				float distLeft   = HandleUtility.DistanceToLine(BL, TL);
				float distRight  = HandleUtility.DistanceToLine(BR, TR);

				float minDistH = Mathf.Min(distTop, distBottom);
				float minDistV = Mathf.Min(distLeft, distRight);

				float edgeThreshold = grabThreshold * 1.6f;

				bool prevHoverH = s_HoverEdgeH;
				bool prevHoverV = s_HoverEdgeV;

				if (minDistH < edgeThreshold && minDistH <= minDistV)
				{
					s_HoverEdgeH = true;
					s_HoverEdgeV = false;
					s_HoverEdgeTop = distTop <= distBottom;
				}
				else if (minDistV < edgeThreshold && minDistV < minDistH)
				{
					s_HoverEdgeV = true;
					s_HoverEdgeH = false;
					s_HoverEdgeLeft = distLeft <= distRight;
				}
				else
				{
					s_HoverEdgeH = false;
					s_HoverEdgeV = false;
				}

				if (prevHoverH != s_HoverEdgeH || prevHoverV != s_HoverEdgeV)
				{
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
				}

				// CRITICAL FIX FOR CANVAS RESIZE: Register 0f distance during Layout so Unity RectTool
				// never intercepts edge clicks or resizes the Canvas RectTransform
				if (e.type == EventType.Layout)
				{
					if (s_HoverEdgeH || s_HoverEdgeV)
					{
						HandleUtility.AddControl(controlID, 0f);
					}
				}

				if (e.type == EventType.Repaint)
				{
					DrawEdgePullTabs(BL, TL, TR, BR, localWidth, localHeight, right, up, controlID, mouseWorldPos);
				}

				if (e.type == EventType.MouseDown && e.button == 0 && (s_HoverEdgeH || s_HoverEdgeV))
				{
					s_IsAxisDragging = true;
					s_AxisDragIsVertical = s_HoverEdgeV; // Dragging Left/Right border creates vertical guide; Top/Bottom creates horizontal guide
					GUIUtility.hotControl = controlID;
					PrepareSnapLines(center, right, up);
					e.Use();
				}
			}

			if (s_IsAxisDragging)
			{
				if (e.type == EventType.Layout)
				{
					HandleUtility.AddDefaultControl(controlID);
				}

				if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
				{
					s_IsAxisDragging = false;
					GUIUtility.hotControl = 0;
					e.Use();
				}

				if (e.type == EventType.Repaint)
				{
					Color baseColor = WithOpacity(GridSettings.GridColor);
					float bh, bs, bv; Color.RGBToHSV(baseColor, out bh, out bs, out bv);
					float dragHue = (bh + 0.25f) % 1f;
					Color dragColor = Color.HSVToRGB(dragHue, Mathf.Max(0.5f, bs), Mathf.Max(0.6f, bv));
					dragColor.a = baseColor.a;

					EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), s_AxisDragIsVertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical, controlID);
					Handles.color = dragColor;

					if (s_SnapWorldLinesX.Count == 0 && s_SnapWorldLinesY.Count == 0)
						PrepareSnapLines(center, right, up);

					if (s_AxisDragIsVertical)
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, right);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesX, s_SnapSourcesX);
						Vector3 lockedPos = center + right * snappedProj;
						DrawCrispLine(GridSettings.GridThickness, lockedPos - up * halfHeight, lockedPos + up * halfHeight);
						// #8 â€” Ghost mirror preview for axis drag
						if (GridSettings.IsMirrorModeActive)
						{
							Color ghost = dragColor; ghost.a *= 0.4f;
							Handles.color = ghost;
							Vector3 mirrorPos = center + right * (-snappedProj);
							DrawCrispLine(GridSettings.GridThickness, mirrorPos - up * halfHeight, mirrorPos + up * halfHeight);
							Handles.color = dragColor;
						}
						float pxX = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localWidth > 0.001f ? canvasWidth / localWidth : 1f)
							: (snappedProj / canvasWidth) * (s_CachedCanvasRect != null ? s_CachedCanvasRect.rect.width : localWidth);
						DrawPositionBadge(mouseWorldPos, $"X: {(pxX >= 0 ? "+" : "")}{pxX:F0} px");
					}
					else
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, up);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesY, s_SnapSourcesY);
						Vector3 lockedPos = center + up * snappedProj;
						DrawCrispLine(GridSettings.GridThickness, lockedPos - right * halfWidth, lockedPos + right * halfWidth);
						// #8 — Ghost mirror preview for axis drag
						if (GridSettings.IsMirrorModeActive)
						{
							Color ghost = dragColor; ghost.a *= 0.4f;
							Handles.color = ghost;
							Vector3 mirrorPos = center + up * (-snappedProj);
							DrawCrispLine(GridSettings.GridThickness, mirrorPos - right * halfWidth, mirrorPos + right * halfWidth);
							Handles.color = dragColor;
						}
						float pxY = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localHeight > 0.001f ? canvasHeight / localHeight : 1f)
							: (snappedProj / canvasHeight) * (s_CachedCanvasRect != null ? s_CachedCanvasRect.rect.height : localHeight);
						DrawPositionBadge(mouseWorldPos, $"Y: {(pxY >= 0 ? "+" : "")}{pxY:F0} px");
					}
				}

				if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
				{
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					if (e.type == EventType.MouseDrag) e.Use();
				}

				if (e.type == EventType.MouseUp && e.button == 0)
				{
					GridSettings.RecordUndo("Add Grid Line");

					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (s_AxisDragIsVertical)
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) / scaleX;
							GridSettings.DynamicFixedX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicFixedX.Add(-val);
						}
						else
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) / scaleY;
							GridSettings.DynamicFixedY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicFixedY.Add(-val);
						}
					}
					else
					{
						if (s_AxisDragIsVertical)
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) + halfWidth) / canvasWidth;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchX.Add(Mathf.Clamp01(1f - val));
						}
						else
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchY.Add(Mathf.Clamp01(1f - val));
						}
					}

					GridSettings.SaveDynamicLists();
					s_IsAxisDragging = false;
					GUIUtility.hotControl = 0;
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					e.Use();
				}

				if (!s_IsAxisDragging || e.isMouse) return; // Skip normal hover while axis-dragging
			}

			// Logic for picking / hovering
			if (!s_IsDragging)
			{
				float closestDistX = float.MaxValue;
				float closestDistY = float.MaxValue;

				if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
				{
					for (int i = 0; i < GridSettings.DynamicFixedX.Count; i++)
					{
						Vector3 linePos = center + right * (GridSettings.DynamicFixedX[i] * scaleX);
						float dist = HandleUtility.DistanceToLine(linePos - up * halfHeight, linePos + up * halfHeight);
						if (dist < closestDistX && dist < grabThreshold) { closestDistX = dist; hoverIndexX = i; }
					}
					for (int i = 0; i < GridSettings.DynamicFixedY.Count; i++)
					{
						Vector3 linePos = center + up * (GridSettings.DynamicFixedY[i] * scaleY);
						float dist = HandleUtility.DistanceToLine(linePos - right * halfWidth, linePos + right * halfWidth);
						if (dist < closestDistY && dist < grabThreshold) { closestDistY = dist; hoverIndexY = i; }
					}
				}
				else // Stretch Model
				{
					for (int i = 0; i < GridSettings.DynamicStretchX.Count; i++)
					{
						float t = GridSettings.DynamicStretchX[i];
						Vector3 p1 = Vector3.Lerp(BL, BR, t); Vector3 p2 = Vector3.Lerp(TL, TR, t);
						float dist = HandleUtility.DistanceToLine(p1, p2);
						if (dist < closestDistX && dist < grabThreshold) { closestDistX = dist; hoverIndexX = i; }
					}
					for (int i = 0; i < GridSettings.DynamicStretchY.Count; i++)
					{
						float t = GridSettings.DynamicStretchY[i];
						Vector3 p1 = Vector3.Lerp(BL, TL, t); Vector3 p2 = Vector3.Lerp(BR, TR, t);
						float dist = HandleUtility.DistanceToLine(p1, p2);
						if (dist < closestDistY && dist < grabThreshold) { closestDistY = dist; hoverIndexY = i; }
					}
				}

				if (hoverIndexX != -1 && hoverIndexY != -1)
				{
					// Apply hysteresis (stickiness) to prevent jitter when distances are almost identical
					float distanceDiff = Mathf.Abs(closestDistX - closestDistY);
					float hysteresisThreshold = 1f; // pixels

					if (distanceDiff < hysteresisThreshold)
					{
						// Stick to whatever we were already hovering in the previous frame
						if (s_HoverIndexX != -1)
							hoverIndexY = -1;
						else if (s_HoverIndexY != -1)
							hoverIndexX = -1;
						else
							hoverIndexY = -1; // Default to X if approaching from empty space
					}
					else
					{
						if (closestDistX <= closestDistY)
							hoverIndexY = -1; // X is significantly closer, discard Y
						else
							hoverIndexX = -1; // Y is significantly closer, discard X
					}
				}

				bool hoverChanged = (s_HoverIndexX != hoverIndexX || s_HoverIndexY != hoverIndexY);
				s_HoverIndexX = hoverIndexX;
				s_HoverIndexY = hoverIndexY;

				// Force Repaint on MouseMove for instant hover & cursor responsiveness
				if (e.type == EventType.MouseMove || hoverChanged)
				{
					if (SceneView.currentDrawingSceneView != null)
						SceneView.currentDrawingSceneView.Repaint();
				}

				if (e.type == EventType.MouseDown && e.button == 0)
				{
					// Selection Logic
					bool isMultiSelect = e.control || e.command;
					bool clickedSelection = false;

					if (s_HoverIndexX != -1)
					{
						if (isMultiSelect)
						{
							if (SelectedIndicesX.Contains(s_HoverIndexX)) SelectedIndicesX.Remove(s_HoverIndexX);
							else SelectedIndicesX.Add(s_HoverIndexX);
						}
						else
						{
							if (!SelectedIndicesX.Contains(s_HoverIndexX))
							{
								SelectedIndicesX.Clear();
								SelectedIndicesY.Clear();
								SelectedIndicesX.Add(s_HoverIndexX);
							}
						}
						clickedSelection = true;
					}
					else if (s_HoverIndexY != -1)
					{
						if (isMultiSelect)
						{
							if (SelectedIndicesY.Contains(s_HoverIndexY)) SelectedIndicesY.Remove(s_HoverIndexY);
							else SelectedIndicesY.Add(s_HoverIndexY);
						}
						else
						{
							if (!SelectedIndicesY.Contains(s_HoverIndexY))
							{
								SelectedIndicesX.Clear();
								SelectedIndicesY.Clear();
								SelectedIndicesY.Add(s_HoverIndexY);
							}
						}
						clickedSelection = true;
					}
					else if (!isMultiSelect)
					{
						// Clicked empty space
						SelectedIndicesX.Clear();
						SelectedIndicesY.Clear();
					}

					if (s_HoverIndexX != -1 || s_HoverIndexY != -1)
					{
						// Dragging with multi-select active implies dragging ALL selected lines.
						// (Phase 4 multi-drag implementation placeholder - currently dragging single grabbed line)
						s_IsDragging = true;
						s_DragIndexX = s_HoverIndexX;
						s_DragIndexY = s_HoverIndexY;
						s_MirrorDragIndexX = -1;
						s_MirrorDragIndexY = -1;

						GridSettings.RecordUndo("Move Grid Line");

						if (GridSettings.IsMirrorModeActive)
						{
							if (s_DragIndexX != -1)
							{
								var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
								if (s_DragIndexX >= 0 && s_DragIndexX < list.Count)
								{
									for (int i = 0; i < list.Count; i++) if (i != s_DragIndexX && IsMirroredValue(list[s_DragIndexX], list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { s_MirrorDragIndexX = i; break; }
									
									if (s_MirrorDragIndexX == -1)
									{
										float val = list[s_DragIndexX];
										float mirrorVal = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? -val : 1f - val;
										list.Add(mirrorVal);
										s_MirrorDragIndexX = list.Count - 1;
										if (GridSettings.ActiveProfile != null)
										{
											var pList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.ActiveProfile.DynamicFixedX : GridSettings.ActiveProfile.DynamicStretchX;
											if (pList != null) pList.Add(mirrorVal);
										}
										var aList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettingsAsset.instance?.DynamicFixedX : GridSettingsAsset.instance?.DynamicStretchX;
										if (aList != null) aList.Add(mirrorVal);
									}
								}
							}
							if (s_DragIndexY != -1)
							{
								var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;
								if (s_DragIndexY >= 0 && s_DragIndexY < list.Count)
								{
									for (int i = 0; i < list.Count; i++) if (i != s_DragIndexY && IsMirroredValue(list[s_DragIndexY], list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { s_MirrorDragIndexY = i; break; }
									
									if (s_MirrorDragIndexY == -1)
									{
										float val = list[s_DragIndexY];
										float mirrorVal = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? -val : 1f - val;
										list.Add(mirrorVal);
										s_MirrorDragIndexY = list.Count - 1;
										if (GridSettings.ActiveProfile != null)
										{
											var pList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.ActiveProfile.DynamicFixedY : GridSettings.ActiveProfile.DynamicStretchY;
											if (pList != null) pList.Add(mirrorVal);
										}
										var aList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettingsAsset.instance?.DynamicFixedY : GridSettingsAsset.instance?.DynamicStretchY;
										if (aList != null) aList.Add(mirrorVal);
									}
								}
							}
						}
						GUIUtility.hotControl = controlID;

						PrepareSnapLines(center, right, up);

						// Prevent default scene view stuff (e.g. dragging the view)
						e.Use();
					}
					else if (clickedSelection) // Just handling selection change
					{
						e.Use();
					}
				}
			}

			// Add instant cursors on Repaint
			if (e.type == EventType.Repaint)
			{
				Rect cursorRect = new Rect(0, 0, 10000, 10000); // Cover whole scene view
				if (s_IsDragging)
				{
					if (s_SnapWorldLinesX.Count == 0 && s_SnapWorldLinesY.Count == 0)
						PrepareSnapLines(center, right, up);

					if (s_DragIndexX != -1) 
					{
						EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeHorizontal, controlID);
						ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX);
					}
					if (s_DragIndexY != -1) 
					{
						EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeVertical, controlID);
						ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY);
					}
				}
				else
				{
					if (s_HoverIndexX != -1) EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeHorizontal, controlID);
					if (s_HoverIndexY != -1) EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeVertical, controlID);
				}

				// Snap highlight is drawn after DrawDynamicGrid in OnSceneGUI to ensure it's always on top.
				// Nothing to draw here.
			}

			// Processing the Drag event
			if (s_IsDragging)
			{
				if (e.type == EventType.MouseDrag)
				{
					if (s_SnapWorldLinesX.Count == 0 && s_SnapWorldLinesY.Count == 0)
						PrepareSnapLines(center, right, up);
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicFixedX.Count) 
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) / scaleX;
							GridSettings.DynamicFixedX[s_DragIndexX] = val;
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicFixedX.Count)
								GridSettings.DynamicFixedX[s_MirrorDragIndexX] = -val;
						}
						if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicFixedY.Count) 
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) / scaleY;
							GridSettings.DynamicFixedY[s_DragIndexY] = val;
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicFixedY.Count)
								GridSettings.DynamicFixedY[s_MirrorDragIndexY] = -val;
						}
					}
					else // Stretch Model
					{
						if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicStretchX.Count) 
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) + halfWidth) / canvasWidth;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchX[s_DragIndexX] = val;
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicStretchX.Count)
								GridSettings.DynamicStretchX[s_MirrorDragIndexX] = Mathf.Clamp01(1f - val);
						}
						if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicStretchY.Count) 
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							val = Mathf.Clamp01(val);
							GridSettings.DynamicStretchY[s_DragIndexY] = val;
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicStretchY.Count)
								GridSettings.DynamicStretchY[s_MirrorDragIndexY] = Mathf.Clamp01(1f - val);
						}
					}

					// Safely update the active profile and global asset's RAM representation without touching disk or Undo
					var activeProf = GridSettings.ActiveProfile;
					var globalAsset = GridSettingsAsset.instance;

					void SafeSet(List<float> targetList, int index, float val)
					{
						if (targetList != null && index >= 0 && index < targetList.Count)
							targetList[index] = val;
					}

					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (activeProf != null)
						{
							if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicFixedX.Count) SafeSet(activeProf.DynamicFixedX, s_DragIndexX, GridSettings.DynamicFixedX[s_DragIndexX]);
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicFixedX.Count) SafeSet(activeProf.DynamicFixedX, s_MirrorDragIndexX, GridSettings.DynamicFixedX[s_MirrorDragIndexX]);
							if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicFixedY.Count) SafeSet(activeProf.DynamicFixedY, s_DragIndexY, GridSettings.DynamicFixedY[s_DragIndexY]);
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicFixedY.Count) SafeSet(activeProf.DynamicFixedY, s_MirrorDragIndexY, GridSettings.DynamicFixedY[s_MirrorDragIndexY]);
						}
						if (globalAsset != null)
						{
							if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicFixedX.Count) SafeSet(globalAsset.DynamicFixedX, s_DragIndexX, GridSettings.DynamicFixedX[s_DragIndexX]);
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicFixedX.Count) SafeSet(globalAsset.DynamicFixedX, s_MirrorDragIndexX, GridSettings.DynamicFixedX[s_MirrorDragIndexX]);
							if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicFixedY.Count) SafeSet(globalAsset.DynamicFixedY, s_DragIndexY, GridSettings.DynamicFixedY[s_DragIndexY]);
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicFixedY.Count) SafeSet(globalAsset.DynamicFixedY, s_MirrorDragIndexY, GridSettings.DynamicFixedY[s_MirrorDragIndexY]);
						}
					}
					else
					{
						if (activeProf != null)
						{
							if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicStretchX.Count) SafeSet(activeProf.DynamicStretchX, s_DragIndexX, GridSettings.DynamicStretchX[s_DragIndexX]);
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicStretchX.Count) SafeSet(activeProf.DynamicStretchX, s_MirrorDragIndexX, GridSettings.DynamicStretchX[s_MirrorDragIndexX]);
							if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicStretchY.Count) SafeSet(activeProf.DynamicStretchY, s_DragIndexY, GridSettings.DynamicStretchY[s_DragIndexY]);
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicStretchY.Count) SafeSet(activeProf.DynamicStretchY, s_MirrorDragIndexY, GridSettings.DynamicStretchY[s_MirrorDragIndexY]);
						}
						if (globalAsset != null)
						{
							if (s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicStretchX.Count) SafeSet(globalAsset.DynamicStretchX, s_DragIndexX, GridSettings.DynamicStretchX[s_DragIndexX]);
							if (s_MirrorDragIndexX >= 0 && s_MirrorDragIndexX < GridSettings.DynamicStretchX.Count) SafeSet(globalAsset.DynamicStretchX, s_MirrorDragIndexX, GridSettings.DynamicStretchX[s_MirrorDragIndexX]);
							if (s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicStretchY.Count) SafeSet(globalAsset.DynamicStretchY, s_DragIndexY, GridSettings.DynamicStretchY[s_DragIndexY]);
							if (s_MirrorDragIndexY >= 0 && s_MirrorDragIndexY < GridSettings.DynamicStretchY.Count) SafeSet(globalAsset.DynamicStretchY, s_MirrorDragIndexY, GridSettings.DynamicStretchY[s_MirrorDragIndexY]);
						}
					}

					e.Use();
				}
				else if (e.type == EventType.MouseUp && e.button == 0)
				{
					GridSettings.SaveDynamicLists();

					// Clear the arrays cleanly on deselect
					s_IsDragging = false;
					s_MirrorDragIndexX = -1;
					s_MirrorDragIndexY = -1;
					s_DragIndexX = -1;
					s_DragIndexY = -1;
					s_HoverIndexX = -1;
					s_HoverIndexY = -1;
					// Flag hierarchy dirty so next drag is guaranteed to cleanly read fresh world matrix data
					s_SnapHierarchyIsDirty = true; 
					
					GUIUtility.hotControl = 0;
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					e.Use();
				}
			}
		}

		private static GUIStyle s_PositionBadgeStyle;
		private static GUIStyle PositionBadgeStyle
		{
			get
			{
				if (s_PositionBadgeStyle == null)
				{
					s_PositionBadgeStyle = new GUIStyle(EditorStyles.boldLabel)
					{
						fontSize = 11,
						alignment = TextAnchor.MiddleCenter,
						normal = { textColor = Color.white }
					};
				}
				return s_PositionBadgeStyle;
			}
		}

		private static void DrawBadgeBorder(Rect r, float thickness, Color color)
		{
			EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
			EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
			EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
			EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
		}

		private static string s_PendingBadgeText = null;
		private static Vector3 s_PendingBadgePos;

		private static void DrawPositionBadge(Vector3 worldPos, string text)
		{
			s_PendingBadgePos = worldPos;
			s_PendingBadgeText = text;
		}

		private static void DrawPendingPositionBadge()
		{
			if (string.IsNullOrEmpty(s_PendingBadgeText)) return;

			if (Camera.current != null && !Camera.current.orthographic)
			{
				if (Vector3.Dot(Camera.current.transform.forward, s_PendingBadgePos - Camera.current.transform.position) <= 0f)
				{
					s_PendingBadgeText = null;
					return;
				}
			}

			Vector2 screenPos = HandleUtility.WorldToGUIPoint(s_PendingBadgePos);

			Handles.BeginGUI();
			GUIContent content = new GUIContent(s_PendingBadgeText);
			Vector2 size = PositionBadgeStyle.CalcSize(content);

			float padX = 7f;
			float padY = 3f;
			float badgeWidth = size.x + padX * 2f;
			float badgeHeight = size.y + padY * 2f;

			// Position badge above and to the right of the line/cursor
			float targetX = screenPos.x + 12f;
			float targetY = screenPos.y - badgeHeight - 6f;

			if (SceneView.currentDrawingSceneView != null)
			{
				Rect svRect = SceneView.currentDrawingSceneView.position;
				// Flip below if too close to top edge
				if (targetY < 4f)
					targetY = screenPos.y + 14f;
				// Flip left if too close to right edge
				if (targetX + badgeWidth > svRect.width - 4f)
					targetX = screenPos.x - badgeWidth - 12f;

				targetX = Mathf.Clamp(targetX, 4f, svRect.width - badgeWidth - 4f);
				targetY = Mathf.Clamp(targetY, 4f, svRect.height - badgeHeight - 4f);
			}

			Rect badgeRect = new Rect(targetX, targetY, badgeWidth, badgeHeight);

			// 1. Subtle drop shadow for depth on bright/white backgrounds
			Rect shadowRect = new Rect(badgeRect.x + 1f, badgeRect.y + 1f, badgeRect.width, badgeRect.height);
			EditorGUI.DrawRect(shadowRect, new Color(0f, 0f, 0f, 0.45f));

			// 2. Solid dark pill background — guaranteed high contrast on white backgrounds
			EditorGUI.DrawRect(badgeRect, new Color(0.12f, 0.12f, 0.14f, 0.94f));

			// 3. Crisp border — ensures visibility against dark backgrounds
			DrawBadgeBorder(badgeRect, 1f, new Color(0.45f, 0.45f, 0.5f, 0.7f));

			// 4. Sharp, legible white text
			GUI.Label(badgeRect, content, PositionBadgeStyle);

			Handles.EndGUI();
			s_PendingBadgeText = null;
		}

		private static Rect s_SpecHUDRect = new Rect(20, 20, 240, 24);
		private static bool s_SpecHUDInitialized = false;
		private static GUIStyle s_SpecHUDWindowStyle;
		private static GUIStyle SpecHUDWindowStyle
		{
			get
			{
				if (s_SpecHUDWindowStyle == null)
				{
					s_SpecHUDWindowStyle = new GUIStyle();
					s_SpecHUDWindowStyle.padding = new RectOffset(4, 4, 3, 3);
					s_SpecHUDWindowStyle.border = new RectOffset(0, 0, 0, 0);
				}
				return s_SpecHUDWindowStyle;
			}
		}

		private static GUIStyle s_SpecHUDTextStyle;
		private static GUIStyle SpecHUDTextStyle
		{
			get
			{
				if (s_SpecHUDTextStyle == null)
				{
					s_SpecHUDTextStyle = new GUIStyle(EditorStyles.miniBoldLabel)
					{
						fontSize = 10,
						alignment = TextAnchor.MiddleLeft
					};
				}
				return s_SpecHUDTextStyle;
			}
		}

		/// <summary>
		/// Commercial visual enhancement: Movable floating in-scene HUD card summarizing
		/// active canvas resolution and grid parameters. Stays accessible regardless of zoom.
		/// </summary>
		private static void DrawMovableSpecHUD(float localWidth, float localHeight)
		{
			if (!GridSettings.ShowCanvasSpecHUD) return;

			SceneView sv = SceneView.currentDrawingSceneView;
			if (sv == null) return;

			Handles.BeginGUI();

			if (!s_SpecHUDInitialized)
			{
				float defaultX = EditorPrefs.GetFloat("UIFurnace_SpecHUD_X", 16f);
				float defaultY = EditorPrefs.GetFloat("UIFurnace_SpecHUD_Y", 16f);
				s_SpecHUDRect.x = defaultX;
				s_SpecHUDRect.y = defaultY;
				s_SpecHUDInitialized = true;
			}

			// Format reading text
			string specText;
			if (GridSettings.CurrentMode == GridMode.UniformGrid)
			{
				if (GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
					specText = $"Grid: {GridSettings.GridSpacingX:0.#} × {GridSettings.GridSpacingY:0.#} px";
				else
					specText = $"Layout: {GridSettings.GridColumns} Col × {GridSettings.GridRows} Row";
			}
			else
			{
				if (GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas)
					specText = $"Guides: {GridSettings.DynamicStretchX.Count}V | {GridSettings.DynamicStretchY.Count}H (Stretch)";
				else
					specText = $"Guides: {GridSettings.DynamicFixedX.Count}V | {GridSettings.DynamicFixedY.Count}H (Fixed)";
			}

			string fullText = $"{localWidth:0} × {localHeight:0} px  •  {specText}";
			GUIContent textContent = new GUIContent(fullText);

			Vector2 textSize = SpecHUDTextStyle.CalcSize(textContent);
			float targetWidth = textSize.x + 50f;
			float targetHeight = 24f;

			s_SpecHUDRect.width = targetWidth;
			s_SpecHUDRect.height = targetHeight;

			// Keep window within SceneView bounds
			s_SpecHUDRect.x = Mathf.Clamp(s_SpecHUDRect.x, 0, Mathf.Max(0, sv.position.width - s_SpecHUDRect.width));
			s_SpecHUDRect.y = Mathf.Clamp(s_SpecHUDRect.y, 0, Mathf.Max(0, sv.position.height - s_SpecHUDRect.height));

			float preSpecX = s_SpecHUDRect.x;
			float preSpecY = s_SpecHUDRect.y;
			int controlID = GUIUtility.GetControlID("UIFurnaceSpecHUD".GetHashCode(), FocusType.Passive);
			s_SpecHUDRect = GUILayout.Window(controlID, s_SpecHUDRect, id => SpecHUDWindowContent(id, textContent), GUIContent.none, SpecHUDWindowStyle);

			if (Event.current.type == EventType.MouseUp && (Mathf.Abs(s_SpecHUDRect.x - preSpecX) > 0.5f || Mathf.Abs(s_SpecHUDRect.y - preSpecY) > 0.5f))
			{
				EditorPrefs.SetFloat("UIFurnace_SpecHUD_X", s_SpecHUDRect.x);
				EditorPrefs.SetFloat("UIFurnace_SpecHUD_Y", s_SpecHUDRect.y);
			}

			Handles.EndGUI();
		}

		private static void SpecHUDWindowContent(int id, GUIContent textContent)
		{
			Rect totalRect = new Rect(0, 0, s_SpecHUDRect.width, s_SpecHUDRect.height);

			// 1. Subtle drop shadow
			EditorGUI.DrawRect(new Rect(totalRect.x + 1f, totalRect.y + 1f, totalRect.width, totalRect.height), new Color(0f, 0f, 0f, 0.4f));

			// 2. Studio background adapting to skin
			Color bg = EditorGUIUtility.isProSkin
				? new Color(0.14f, 0.14f, 0.16f, 0.94f)
				: new Color(0.85f, 0.85f, 0.88f, 0.94f);
			EditorGUI.DrawRect(totalRect, bg);

			// 3. Crisp border
			Color borderColor = EditorGUIUtility.isProSkin
				? new Color(0.35f, 0.35f, 0.38f, 0.7f)
				: new Color(0.55f, 0.55f, 0.6f, 0.7f);
			DrawBadgeBorder(totalRect, 1f, borderColor);

			// Content Row
			GUILayout.BeginHorizontal();

			// Grip
			GUILayout.Label(new GUIContent("⠿", "Drag to reposition Canvas Info"), EditorStyles.miniLabel, GUILayout.Width(12), GUILayout.Height(16));

			// Text
			SpecHUDTextStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.92f, 1f) : new Color(0.12f, 0.12f, 0.14f, 1f);
			GUILayout.Label(textContent, SpecHUDTextStyle, GUILayout.Height(16));

			GUILayout.FlexibleSpace();

			// Hide Button
			if (GUILayout.Button(new GUIContent("✕", "Hide Canvas Info"), EditorStyles.miniLabel, GUILayout.Width(14), GUILayout.Height(16)))
			{
				GridSettings.ShowCanvasSpecHUD = false;
				if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
			}

			GUILayout.EndHorizontal();

			// Drag window
			GUI.DragWindow(new Rect(0, 0, totalRect.width - 20f, totalRect.height));
		}

		/// <summary>
		/// #10 — Draw an offset label next to the hovered/dragged line so the user
		/// always knows its exact pixel position from center.
		/// </summary>
		private static void DrawLineOffsetLabel(Vector3 mouseWorldPos,
			float rawOffset, float canvasSize, float localSize, string axis)
		{
			float px = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
				? rawOffset / (localSize > 0.001f ? (canvasSize / localSize) : 1f)
				: (Mathf.Clamp01(rawOffset) - 0.5f) * localSize;   // stretch t -> pixels from center
			string sign = px >= 0 ? "+" : "";

			DrawPositionBadge(mouseWorldPos, $"{axis}: {sign}{px:F0} px");
		}

		private static void DrawEdgePullTabs(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight, Vector3 right, Vector3 up, int controlID, Vector3 mouseWorldPos)
		{
			// Subtle base color vs active hover color
			Color idleRailColor   = new Color(0.2f, 0.75f, 1f, 0.35f);
			Color hoverRailColor  = new Color(0.35f, 0.95f, 1f, 1f);
			Color draggingColor   = new Color(1f, 0.85f, 0.2f, 1f);

			bool isHoverTop    = s_HoverEdgeH && s_HoverEdgeTop;
			bool isHoverBottom = s_HoverEdgeH && !s_HoverEdgeTop;
			bool isHoverLeft   = s_HoverEdgeV && s_HoverEdgeLeft;
			bool isHoverRight  = s_HoverEdgeV && !s_HoverEdgeLeft;

			Color topColor    = (s_IsAxisDragging && !s_AxisDragIsVertical) ? draggingColor : (isHoverTop ? hoverRailColor : idleRailColor);
			Color bottomColor = (s_IsAxisDragging && !s_AxisDragIsVertical) ? draggingColor : (isHoverBottom ? hoverRailColor : idleRailColor);
			Color leftColor   = (s_IsAxisDragging && s_AxisDragIsVertical) ? draggingColor : (isHoverLeft ? hoverRailColor : idleRailColor);
			Color rightColor  = (s_IsAxisDragging && s_AxisDragIsVertical) ? draggingColor : (isHoverRight ? hoverRailColor : idleRailColor);

			float topThick    = (isHoverTop || (s_IsAxisDragging && !s_AxisDragIsVertical)) ? 2.5f : 1.2f;
			float bottomThick = (isHoverBottom || (s_IsAxisDragging && !s_AxisDragIsVertical)) ? 2.5f : 1.2f;
			float leftThick   = (isHoverLeft || (s_IsAxisDragging && s_AxisDragIsVertical)) ? 2.5f : 1.2f;
			float rightThick  = (isHoverRight || (s_IsAxisDragging && s_AxisDragIsVertical)) ? 2.5f : 1.2f;

			// Draw crisp highlighted edge rails along canvas boundary
			Handles.color = topColor;
			DrawCrispLine(topThick, TL, TR);

			Handles.color = bottomColor;
			DrawCrispLine(bottomThick, BL, BR);

			Handles.color = leftColor;
			DrawCrispLine(leftThick, BL, TL);

			Handles.color = rightColor;
			DrawCrispLine(rightThick, BR, TR);

			// Native Unity-style cursor feedback
			if (isHoverTop || isHoverBottom)
			{
				EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), MouseCursor.ResizeVertical, controlID);
			}
			else if (isHoverLeft || isHoverRight)
			{
				EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), MouseCursor.ResizeHorizontal, controlID);
			}

			// Subtle native handle notch at mouse projection along hovered edge
			if (isHoverTop || isHoverBottom || isHoverLeft || isHoverRight)
			{
				Vector3 edgePoint;
				if (isHoverTop) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, TL, TR);
				else if (isHoverBottom) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BL, BR);
				else if (isHoverLeft) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BL, TL);
				else edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BR, TR);

				DrawEdgeHandleNotch(edgePoint, right, up, isHoverTop || isHoverBottom);

				// Sleek native position badge near cursor showing clear action intent
				DrawPositionBadge(mouseWorldPos, (isHoverTop || isHoverBottom) ? "Pull Horizontal Guide" : "Pull Vertical Guide");
			}
		}

		private static void DrawEdgeHandleNotch(Vector3 center, Vector3 right, Vector3 up, bool isHorizontal)
		{
			float handleSize = HandleUtility.GetHandleSize(center);
			float halfAlong = handleSize * 0.08f;
			float halfAcross = handleSize * 0.04f;

			Color notchColor = new Color(0.35f, 0.95f, 1f, 1f);
			Color notchShadow = new Color(0f, 0f, 0f, 0.7f);

			Vector3 p1, p2, p3, p4;
			if (isHorizontal)
			{
				p1 = center - right * halfAlong;
				p2 = center + up * halfAcross;
				p3 = center + right * halfAlong;
				p4 = center - up * halfAcross;
			}
			else
			{
				p1 = center - up * halfAlong;
				p2 = center + right * halfAcross;
				p3 = center + up * halfAlong;
				p4 = center - right * halfAcross;
			}

			// Shadow pass
			Handles.color = notchShadow;
			Handles.DrawAAConvexPolygon(p1, p2, p3, p4);

			// Crisp border
			Handles.color = notchColor;
			Handles.DrawAAPolyLine(2f, p1, p2, p3, p4, p1);
		}

		/// <summary>Draw dynamically placed editable lines.</summary>
		private static void DrawDynamicGrid(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR, float localWidth, float localHeight)
		{
			float gridThickness = GridSettings.GridThickness;
			float axisThickness = GridSettings.AxisThickness;
			Color gridColor     = WithOpacity(GridSettings.GridColor);
			Color xAxisColor    = WithOpacity(GridSettings.XAxisColor);
			Color yAxisColor    = WithOpacity(GridSettings.YAxisColor);

			// Calculate High-Contrast Hover & Drag colors based on User's GridColor
			float h, s, v;
			Color.RGBToHSV(gridColor, out h, out s, out v);
			
			// Hover = Shift hue by 180 degrees (complementary) and ensure brightness
			Color hoverColor = Color.HSVToRGB((h + 0.5f) % 1f, Mathf.Max(s, 0.5f), Mathf.Max(v, 0.8f));
			hoverColor.a = 1f;

			// Drag = Shift hue by 90 degrees and crank saturation/brightness for high visibility
			Color dragColor = Color.HSVToRGB((h + 0.25f) % 1f, 1f, 1f);
			dragColor.a = 1f;

			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			float scaleX = localWidth > 0.001f ? (canvasWidth / localWidth) : 1f;
			float scaleY = localHeight > 0.001f ? (canvasHeight / localHeight) : 1f;

			float halfWidth    = canvasWidth  * 0.5f;
			float halfHeight   = canvasHeight * 0.5f;

			bool isEditing = GridSettings.IsEditModeActive;

			Vector3 mouseWorldPos = center;
			if (isEditing)
			{
				Ray mouseRay = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
				if (new Plane(Vector3.Cross(right, up), center).Raycast(mouseRay, out float hitDist))
					mouseWorldPos = mouseRay.GetPoint(hitDist);
			}

			if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
			{
				// Fixed X (Vertical lines)
				for (int i = 0; i < GridSettings.DynamicFixedX.Count; i++)
				{
					float xOffset = GridSettings.DynamicFixedX[i] * scaleX;
					Vector3 pos = center + right * xOffset;
					
					Color lineColor = gridColor;
					if (isEditing)
					{
						bool isSelected = SelectedIndicesX.Contains(i);
						bool isDraggedLine = s_IsDragging && s_DragIndexX == i;
						bool isHoveredLine = !s_IsDragging && s_HoverIndexX == i;
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicFixedX.Count && IsMirroredValue(GridSettings.DynamicFixedX[s_DragIndexX], GridSettings.DynamicFixedX[i], true);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexX >= 0 && s_HoverIndexX < GridSettings.DynamicFixedX.Count && IsMirroredValue(GridSettings.DynamicFixedX[s_HoverIndexX], GridSettings.DynamicFixedX[i], true);

						if (isDraggedLine || isMirrorDragged)
						{
							lineColor = dragColor;
							if (isDraggedLine) DrawLineOffsetLabel(mouseWorldPos, GridSettings.DynamicFixedX[i] * scaleX, canvasWidth, localWidth, "X");
						}
						else if (isHoveredLine || isMirrorHovered || isSelected)
						{
							lineColor = hoverColor;
							if (isHoveredLine) DrawLineOffsetLabel(mouseWorldPos, GridSettings.DynamicFixedX[i] * scaleX, canvasWidth, localWidth, "X");
						}
					}

					Handles.color = lineColor;
					DrawCrispLine(gridThickness, pos - up * halfHeight, pos + up * halfHeight);
				}

				// Fixed Y (Horizontal lines)
				for (int i = 0; i < GridSettings.DynamicFixedY.Count; i++)
				{
					float yOffset = GridSettings.DynamicFixedY[i] * scaleY;
					Vector3 pos = center + up * yOffset;
					
					Color lineColor = gridColor;
					if (isEditing)
					{
						bool isSelected = SelectedIndicesY.Contains(i);
						bool isDraggedLine = s_IsDragging && s_DragIndexY == i;
						bool isHoveredLine = !s_IsDragging && s_HoverIndexY == i;
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicFixedY.Count && IsMirroredValue(GridSettings.DynamicFixedY[s_DragIndexY], GridSettings.DynamicFixedY[i], true);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexY >= 0 && s_HoverIndexY < GridSettings.DynamicFixedY.Count && IsMirroredValue(GridSettings.DynamicFixedY[s_HoverIndexY], GridSettings.DynamicFixedY[i], true);

						if (isDraggedLine || isMirrorDragged)
						{
							lineColor = dragColor;
							if (isDraggedLine) DrawLineOffsetLabel(mouseWorldPos, GridSettings.DynamicFixedY[i] * scaleY, canvasHeight, localHeight, "Y");
						}
						else if (isHoveredLine || isMirrorHovered || isSelected)
						{
							lineColor = hoverColor;
							if (isHoveredLine) DrawLineOffsetLabel(mouseWorldPos, GridSettings.DynamicFixedY[i] * scaleY, canvasHeight, localHeight, "Y");
						}
					}

					Handles.color = lineColor;
					DrawCrispLine(gridThickness, pos - right * halfWidth, pos + right * halfWidth);
				}
			}
			else
			{
				// Stretch X (Vertical lines)
				for (int i = 0; i < GridSettings.DynamicStretchX.Count; i++)
				{
					float t = GridSettings.DynamicStretchX[i];
					Vector3 pBL = Vector3.Lerp(BL, BR, t);
					Vector3 pTL = Vector3.Lerp(TL, TR, t);

					Color lineColor = gridColor;
					if (isEditing)
					{
						bool isSelected = SelectedIndicesX.Contains(i);
						bool isDraggedLine = s_IsDragging && s_DragIndexX == i;
						bool isHoveredLine = !s_IsDragging && s_HoverIndexX == i;
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexX >= 0 && s_DragIndexX < GridSettings.DynamicStretchX.Count && IsMirroredValue(GridSettings.DynamicStretchX[s_DragIndexX], t, false);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexX >= 0 && s_HoverIndexX < GridSettings.DynamicStretchX.Count && IsMirroredValue(GridSettings.DynamicStretchX[s_HoverIndexX], t, false);

						if (isDraggedLine || isMirrorDragged)
						{
							lineColor = dragColor;
							if (isDraggedLine) DrawLineOffsetLabel(mouseWorldPos, t, canvasWidth, localWidth, "X");
						}
						else if (isHoveredLine || isMirrorHovered || isSelected)
						{
							lineColor = hoverColor;
							if (isHoveredLine) DrawLineOffsetLabel(mouseWorldPos, t, canvasWidth, localWidth, "X");
						}
					}

					Handles.color = lineColor;
					DrawCrispLine(gridThickness, pBL, pTL);
				}

				// Stretch Y (Horizontal lines)
				for (int i = 0; i < GridSettings.DynamicStretchY.Count; i++)
				{
					float t = GridSettings.DynamicStretchY[i];
					Vector3 pBL = Vector3.Lerp(BL, TL, t);
					Vector3 pBR = Vector3.Lerp(BR, TR, t);

					Color lineColor = gridColor;
					if (isEditing)
					{
						bool isSelected = SelectedIndicesY.Contains(i);
						bool isDraggedLine = s_IsDragging && s_DragIndexY == i;
						bool isHoveredLine = !s_IsDragging && s_HoverIndexY == i;
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexY >= 0 && s_DragIndexY < GridSettings.DynamicStretchY.Count && IsMirroredValue(GridSettings.DynamicStretchY[s_DragIndexY], t, false);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexY >= 0 && s_HoverIndexY < GridSettings.DynamicStretchY.Count && IsMirroredValue(GridSettings.DynamicStretchY[s_HoverIndexY], t, false);

						if (isDraggedLine || isMirrorDragged)
						{
							lineColor = dragColor;
							if (isDraggedLine) DrawLineOffsetLabel(mouseWorldPos, t, canvasHeight, localHeight, "Y");
						}
						else if (isHoveredLine || isMirrorHovered || isSelected)
						{
							lineColor = hoverColor;
							if (isHoveredLine) DrawLineOffsetLabel(mouseWorldPos, t, canvasHeight, localHeight, "Y");
						}
					}

					Handles.color = lineColor;
					DrawCrispLine(gridThickness, pBL, pBR);
				}
			}

			// Center axes
			Color finalXAxisColor = xAxisColor;
			Color finalYAxisColor = yAxisColor;
			float finalXAxisThickness = axisThickness;
			float finalYAxisThickness = axisThickness;

			Handles.color = finalXAxisColor;
			DrawCrispLine(finalXAxisThickness, Vector3.Lerp(BL, TL, 0.5f), Vector3.Lerp(BR, TR, 0.5f));

			Handles.color = finalYAxisColor;
			DrawCrispLine(finalYAxisThickness, Vector3.Lerp(BL, BR, 0.5f), Vector3.Lerp(TL, TR, 0.5f));
		}

		/// <summary>
		/// Calculates a high-contrast complementary snap highlight color in real time based on the target element's Graphic color.
		/// If low saturation or no graphic exists, gracefully selects radiant electric cyan or magenta for guaranteed contrast.
		/// </summary>
		private static Color GetAdaptiveSnapColor(RectTransform target)
		{
			Color baseColor = Color.clear;
			if (target != null)
			{
				var graphic = target.GetComponent<UnityEngine.UI.Graphic>();
				if (graphic != null && graphic.color.a > 0.05f)
				{
					baseColor = graphic.color;
				}
			}

			if (baseColor.a <= 0.05f)
			{
				baseColor = GridSettings.GridColor;
			}

			Color.RGBToHSV(baseColor, out float h, out float s, out float v);

			// For near-achromatic surfaces (pure white, grey, or black)
			if (s < 0.2f)
			{
				// On dark backgrounds/elements, emit radiant electric cyan
				if (v < 0.45f)
					return new Color(0f, 0.95f, 1f, 1f);
				// On light backgrounds/elements, emit hot electric magenta
				return new Color(1f, 0.05f, 0.55f, 1f);
			}

			// Shift hue by 180 degrees for true optical complement
			float compH = (h + 0.5f) % 1f;
			float compS = Mathf.Clamp(Mathf.Max(s, 0.85f), 0.75f, 1f);
			float compV = v < 0.5f ? 1.0f : 0.95f;

			Color compColor = Color.HSVToRGB(compH, compS, compV);
			compColor.a = 1f;
			return compColor;
		}

		/// <summary>
		/// Calculates a high-contrast complementary color derived strictly from the Grid's own GridColor.
		/// Used for highlighting snapped grid lines independently of whatever color UI elements have.
		/// </summary>
		private static Color GetGridComplementColor()
		{
			Color gridBase = GridSettings.GridColor;
			Color.RGBToHSV(gridBase, out float h, out float s, out float v);

			if (s < 0.2f)
			{
				return v < 0.45f ? new Color(0f, 0.95f, 1f, 1f) : new Color(1f, 0.05f, 0.55f, 1f);
			}

			float compH = (h + 0.5f) % 1f;
			float compS = Mathf.Clamp(Mathf.Max(s, 0.9f), 0.75f, 1f);
			float compV = v < 0.5f ? 1.0f : 0.95f;

			Color compColor = Color.HSVToRGB(compH, compS, compV);
			compColor.a = 1f;
			return compColor;
		}

		/// <summary>
		/// Draws professional snap feedback: 4 corner L-brackets + a tight centre crosshair.
		/// Uses a dark shadow pass then a bold real-time complementary pass so it's
		/// always visible over Unity's 1px RectTool gizmo on any background colour.
		/// corners[] must be GetWorldCorners order: 0=BL, 1=TL, 2=TR, 3=BR.
		/// </summary>
		private static void DrawSnapHighlight(Vector3[] c, RectTransform target = null)
		{
			Vector3 rightDir = c[3] - c[0];
			Vector3 upDir    = c[1] - c[0];
			float   width    = rightDir.magnitude;
			float   height   = upDir.magnitude;
			if (width < 0.0001f || height < 0.0001f) return;

			Vector3 right = rightDir / width;
			Vector3 up    = upDir    / height;

			// Bracket arm = 18% of shorter side. Cross arm = 10% of shorter side.
			float shorter  = Mathf.Min(width, height);
			float armLen   = shorter * 0.18f;
			float crossLen = shorter * 0.10f;
			Vector3 ctr    = (c[0] + c[2]) * 0.5f;

			Color shadow = new Color(0f, 0f, 0f, 0.55f);     // solid dark backing for razor-sharp edge contrast
			Color bright = GetAdaptiveSnapColor(target);      // real-time complementary color for the UI element

			// Boosted L-bracket thickness to give it strong visual dominance over Unity's 1px wireframe
			float bracketShadowThickness = 6.0f;
			float bracketBrightThickness = 3.5f; 
			float crossShadowThickness   = 3.5f;
			float crossBrightThickness   = 1.75f;

			// ─── Shadow pass ─────────────────────────────────────────────────────────
			Handles.color = shadow;
			// BL corner
			DrawCrispLineScreenSpace(bracketShadowThickness, c[0], c[0] + right * armLen);
			DrawCrispLineScreenSpace(bracketShadowThickness, c[0], c[0] + up    * armLen);
			// TL corner
			DrawCrispLineScreenSpace(bracketShadowThickness, c[1], c[1] + right * armLen);
			DrawCrispLineScreenSpace(bracketShadowThickness, c[1], c[1] - up    * armLen);
			// TR corner
			DrawCrispLineScreenSpace(bracketShadowThickness, c[2], c[2] - right * armLen);
			DrawCrispLineScreenSpace(bracketShadowThickness, c[2], c[2] - up    * armLen);
			// BR corner
			DrawCrispLineScreenSpace(bracketShadowThickness, c[3], c[3] - right * armLen);
			DrawCrispLineScreenSpace(bracketShadowThickness, c[3], c[3] + up    * armLen);
			// Centre crosshair
			DrawCrispLineScreenSpace(crossShadowThickness, ctr - right * crossLen, ctr + right * crossLen);
			DrawCrispLineScreenSpace(crossShadowThickness, ctr - up    * crossLen, ctr + up    * crossLen);

			// ─── Bright pass ─────────────────────────────────────────────────────────
			Handles.color = bright;
			// BL corner
			DrawCrispLineScreenSpace(bracketBrightThickness, c[0], c[0] + right * armLen);
			DrawCrispLineScreenSpace(bracketBrightThickness, c[0], c[0] + up    * armLen);
			// TL corner
			DrawCrispLineScreenSpace(bracketBrightThickness, c[1], c[1] + right * armLen);
			DrawCrispLineScreenSpace(bracketBrightThickness, c[1], c[1] - up    * armLen);
			// TR corner
			DrawCrispLineScreenSpace(bracketBrightThickness, c[2], c[2] - right * armLen);
			DrawCrispLineScreenSpace(bracketBrightThickness, c[2], c[2] - up    * armLen);
			// BR corner
			DrawCrispLineScreenSpace(bracketBrightThickness, c[3], c[3] - right * armLen);
			DrawCrispLineScreenSpace(bracketBrightThickness, c[3], c[3] + up    * armLen);
			// Centre crosshair
			DrawCrispLineScreenSpace(crossBrightThickness, ctr - right * crossLen, ctr + right * crossLen);
			DrawCrispLineScreenSpace(crossBrightThickness, ctr - up    * crossLen, ctr + up    * crossLen);
		}



		// ─── Menu Items & Commands ─────────────────────────────────────────────
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
			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
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
