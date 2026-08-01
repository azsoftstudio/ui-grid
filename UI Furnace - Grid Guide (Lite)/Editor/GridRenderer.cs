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

		// â”€â”€â”€ Add Lines Mode (Drag from Axis) State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		/// <summary>When true, users can drag from the X/Y axis to create a new grid line.</summary>
		public static bool IsAddLinesMode { get; set; } = false;
		private static bool s_IsAxisDragging = false;     // True while mid-drag creating a new line
		private static bool s_AxisDragIsVertical = false; // Which axis is being dragged
		private static bool s_HoverAxisX = false;         // Mouse hovering near X axis (horizontal)
		private static bool s_HoverAxisY = false;         // Mouse hovering near Y axis (vertical)

		// â”€â”€â”€ Selection State (Transient) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		public static readonly List<int> SelectedIndicesX = new List<int>();
		public static readonly List<int> SelectedIndicesY = new List<int>();

		// â”€â”€â”€ Snap Caching State â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static readonly List<float> s_SnapWorldLinesX = new List<float>();
		private static readonly List<float> s_SnapWorldLinesY = new List<float>();
		// Maps each snap line entry index to the source RectTransform â€” same order as snap lists above
		private static readonly List<RectTransform> s_SnapSourcesX = new List<RectTransform>();
		private static readonly List<RectTransform> s_SnapSourcesY = new List<RectTransform>();
		// The elements whose borders are currently being snapped to (for highlight drawing)
		private static readonly List<RectTransform> s_SnapHighlightedRects = new List<RectTransform>();
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
			Selection.selectionChanged -= InvalidateCache;
			Selection.selectionChanged += InvalidateCache;
			EditorApplication.hierarchyChanged += () => s_SnapHierarchyIsDirty = true;
			GridSettings.LoadFromAsset();
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
			if (!GridSettings.SnapElementsToGrid) return;
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
				
				s_SelectionBuffer.Clear();
				foreach (var go in Selection.gameObjects)
				{
					s_SelectionBuffer.Add(go);
				}
				
				var validRTs = new List<RectTransform>(s_HierarchyListBuffer.Count);
				for (int i = 0; i < s_HierarchyListBuffer.Count; i++)
				{
					if (s_HierarchyListBuffer[i] != null && !s_SelectionBuffer.Contains(s_HierarchyListBuffer[i].gameObject))
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
				for (int i = 0; i < snapLines.Count; i++)
				{
					if (Mathf.Abs(snapLines[i] - bestSnap) < tolerance)
					{
						if (!s_SnapHighlightedRects.Contains(snapSources[i]))
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
			s_HighlightOffsetsX.Clear();
			s_HighlightOffsetsY.Clear();

			if (!GridSettings.ShowGrid) return;

			// Prefab mode is allowed â€” grid is useful when editing UI prefabs.
			// PrefabStageUtility.GetCurrentPrefabStage() is intentionally not cached here;
			// prefab mode works automatically since we operate on Selection.activeGameObject.

			if (!TryGetCachedComponents(out RectTransform rectTransform, out Canvas canvas, out RectTransform canvasRect))
				return;

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

				// Draw snap highlight LAST so it always renders on top of all grid lines.
				if (Event.current.type == EventType.Repaint && s_SnapHighlightedRects.Count > 0)
				{
					var snapCorners = new Vector3[4];
					foreach (var rect in s_SnapHighlightedRects)
					{
						if (rect != null)
						{
							rect.GetWorldCorners(snapCorners);
							DrawSnapHighlight(snapCorners);
						}
					}
				}

				// #7 â€” Scene View HUD: movable GUI window over the scene
				if (GridSettings.IsEditModeActive)
					DrawEditModeHUD();
			}
			finally
			{
				Handles.zTest = originalZTest;
			}
		}

		// â”€â”€â”€ Shortcuts â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		/// <summary>
		/// Toggle grid visibility. Default: Alt+G.
		/// Rebind via Edit â†’ Shortcuts â†’ "UI Furnace/Toggle Grid".
		/// </summary>
		[Shortcut("UI Furnace/Toggle Grid", KeyCode.G, ShortcutModifiers.Alt)]
		private static void ToggleGridShortcut()
		{
			GridSettings.SaveVisibility(!GridSettings.ShowGrid);
			SceneView.RepaintAll();
		}

		[Shortcut("UI Furnace/Toggle Edit Mode", KeyCode.E, ShortcutModifiers.Alt)]
		private static void ToggleEditModeShortcut()
		{
			if (GridSettings.CurrentMode == GridMode.CustomLines)
			{
				GridSettings.IsEditModeActive = !GridSettings.IsEditModeActive;
				SceneView.RepaintAll();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}

		[Shortcut("UI Furnace/Toggle Snap to Elements", KeyCode.S, ShortcutModifiers.Alt)]
		private static void ToggleSnapShortcut()
		{
			if (GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive)
			{
				GridSettings.SaveSnapSettings(!GridSettings.SnapToElements, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
				SceneView.RepaintAll();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}

		// #8 â€” Mirror Mode shortcut
		[Shortcut("UI Furnace/Toggle Mirror Mode", KeyCode.M, ShortcutModifiers.Alt)]
		private static void ToggleMirrorModeShortcut()
		{
			if (GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive)
			{
				GridSettings.IsMirrorModeActive = !GridSettings.IsMirrorModeActive;
				SceneView.RepaintAll();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}

		[Shortcut("UI Furnace/Toggle Universal Snapping", KeyCode.S, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
		private static void ToggleUniversalSnapShortcut()
		{
			GridSettings.SaveSnapSettings(GridSettings.SnapToElements, !GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
			SceneView.RepaintAll();
			GridManagerWindow.Instance?.CreateGUI();
		}

		// â”€â”€â”€ Drawing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		/// <summary>Apply global opacity (Suggestion 6) to a color.</summary>
		private static Color WithOpacity(Color color)
		{
			color.a *= GridSettings.GridOpacity;
			return color;
		}

		private static Rect s_HUDRect = new Rect(20, 20, 220, 60);
		private static bool s_HUDInitialized = false;

		// #7 â€” Scene View HUD banner
		private static void DrawEditModeHUD()
		{
			SceneView sv = SceneView.currentDrawingSceneView;
			if (sv == null) return;

			Handles.BeginGUI();

			if (!s_HUDInitialized)
			{
				// Default to bottom-left corner
				s_HUDRect.y = sv.position.height - 110f;
				s_HUDRect.x = 20f;
				s_HUDInitialized = true;
			}

			// Keep window on screen
			s_HUDRect.x = Mathf.Clamp(s_HUDRect.x, 0, Mathf.Max(0, sv.position.width - s_HUDRect.width));
			s_HUDRect.y = Mathf.Clamp(s_HUDRect.y, 0, Mathf.Max(0, sv.position.height - s_HUDRect.height));

			int controlID = GUIUtility.GetControlID("UIFurnaceHUD".GetHashCode(), FocusType.Passive);
			s_HUDRect = GUILayout.Window(controlID, s_HUDRect, HUDWindowContent, "UI Furnace", GUI.skin.window);

			Handles.EndGUI();
		}

		private static void HUDWindowContent(int id)
		{
			var labelStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
			var boldStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
			var metaStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, padding = new RectOffset(0,0,2,0) };

			Color activeColor = new Color(0.2f, 0.75f, 0.35f, 1f); // Professional "Active" green

			GUILayout.BeginHorizontal();
			var defaultColor = GUI.color;
			GUI.color = activeColor;
			GUILayout.Label("â—", boldStyle, GUILayout.Width(14));
			GUI.color = defaultColor;
			GUILayout.Label("Grid Editing Active", boldStyle);
			GUILayout.FlexibleSpace();
			GUILayout.Label("(Alt+E)", metaStyle);
			GUILayout.EndHorizontal();

			if (GridSettings.IsMirrorModeActive)
			{
				GUILayout.Space(4);
				GUILayout.BeginHorizontal();
				GUI.color = activeColor;
				GUILayout.Label("â—", boldStyle, GUILayout.Width(14));
				GUI.color = defaultColor;
				GUILayout.Label("Mirror Mode", labelStyle);
				GUILayout.FlexibleSpace();
				GUILayout.Label("(Alt+M)", metaStyle);
				GUILayout.EndHorizontal();
			}

			// Make the entire window area draggable
			GUI.DragWindow(new Rect(0, 0, 10000, 10000));
		}

		/// <summary>Draw grid with fixed world-space spacing.</summary>
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
			// Ensure user is actually using the Rect Tool
			if (Tools.current != Tool.Rect) return;

			// Check if we are actively dragging something in the scene
			int hotID = GUIUtility.hotControl;
			if (hotID == 0) 
			{
				s_CurrentDragMode = DragMode.None;
				return;
			}

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
			s_SnapHighlightedRects.Clear();

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
						// Add dummy just for count check
						s_SnapHighlightedRects.Add(rt);
					}
				}
			}

			// Pass 3: Draw all unique snapped lines once
			if (s_SnapHighlightedRects.Count > 0 && Event.current.type == EventType.Repaint)
			{
				Handles.color = new Color(1f, 0.08f, 0.9f, 0.8f);
				float halfW = canvasWidth * 0.5f;
				float halfH = canvasHeight * 0.5f;

				foreach (float gridX in s_HighlightOffsetsX)
					DrawCrispLine(GridSettings.GridThickness, center + right * gridX - up * halfH, center + right * gridX + up * halfH);
				
				foreach (float gridY in s_HighlightOffsetsY)
					DrawCrispLine(GridSettings.GridThickness, center + up * gridY - right * halfW, center + up * gridY + right * halfW);

				if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
			}
		}
		// â”€â”€â”€ Interactive Dynamic Mode Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
		private static void HandleDynamicGridEvents(Event e, int controlID, Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR, float localWidth, float localHeight)
		{
			if (!GridSettings.IsEditModeActive) return;

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
				menu.AddItem(new GUIContent("[Grid] Add Horizontal Line\tAlt+H"), false, StartAddingHorizontalLine);
				menu.AddItem(new GUIContent("[Grid] Add Vertical Line\tAlt+V"), false, StartAddingVerticalLine);
				menu.AddSeparator("");
				if (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0)
					menu.AddItem(new GUIContent("[Grid] Delete Selected Lines\tAlt+Backspace"), false, DeleteSelectedLines);
				else
					menu.AddDisabledItem(new GUIContent("[Grid] Delete Selected Lines\tAlt+Backspace"));
				
				menu.AddSeparator("");
				menu.AddItem(new GUIContent("[Grid] Exit Edit Mode"), false, ExitEditMode);
				
				menu.ShowAsContext();
				e.Use();
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
						// #10 â€” Offset label. t = snappedProj / canvasWidth, ranges [-0.5, 0.5]
						float pxOffX = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localWidth > 0.001f ? canvasWidth / localWidth : 1f)
							: t * localWidth;
						Handles.Label(mouseWorldPos + labelOffset,
							$"X: {(pxOffX >= 0 ? "+" : "")}{pxOffX:F0} px");
					}
					else
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, up);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesY, s_SnapSourcesY);
						float t = snappedProj / canvasHeight;
						Vector3 lockedPos = center + up * (t * canvasHeight);
						DrawCrispLine(GridSettings.GridThickness, lockedPos - right * halfWidth, lockedPos + right * halfWidth);
						// #8 â€” Ghost mirror preview
						if (GridSettings.IsMirrorModeActive)
						{
							Color ghost = dragColor; ghost.a *= 0.4f;
							Handles.color = ghost;
							Vector3 mirrorPos = center + up * (-t * canvasHeight);
							DrawCrispLine(GridSettings.GridThickness, mirrorPos - right * halfWidth, mirrorPos + right * halfWidth);
							Handles.color = dragColor;
						}
						// #10 â€” Offset label. t = snappedProj / canvasHeight, ranges [-0.5, 0.5]
						float pxOffY = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
							? snappedProj / (localHeight > 0.001f ? canvasHeight / localHeight : 1f)
							: t * localHeight;
						Handles.Label(mouseWorldPos + labelOffset,
							$"Y: {(pxOffY >= 0 ? "+" : "")}{pxOffY:F0} px");
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
							GridSettings.DynamicStretchX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchX.Add(1f - val);
						}
						else
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							GridSettings.DynamicStretchY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchY.Add(1f - val);
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
				// Detect proximity to the X axis (horizontal line at center Y)
				float distToXAxis = HandleUtility.DistanceToLine(center - right * halfWidth, center + right * halfWidth);
				// Detect proximity to the Y axis (vertical line at center X)
				float distToYAxis = HandleUtility.DistanceToLine(center - up * halfHeight, center + up * halfHeight);

				float axisThreshold = grabThreshold * 1.5f; // Slightly wider grab zone for axes
				bool prevHoverAxisX = s_HoverAxisX;
				bool prevHoverAxisY = s_HoverAxisY;
				s_HoverAxisX = distToXAxis < axisThreshold;
				s_HoverAxisY = distToYAxis < axisThreshold && !s_HoverAxisX;

				if (prevHoverAxisX != s_HoverAxisX || prevHoverAxisY != s_HoverAxisY)
				{
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
				}

				if (e.type == EventType.Repaint)
				{
					Color highlightColor = new Color(0.3f, 0.9f, 1f, 1f);
					if (s_HoverAxisX)
					{
						EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), MouseCursor.ResizeVertical, controlID);
						Handles.color = highlightColor;
						DrawCrispLine(GridSettings.AxisThickness, center - right * halfWidth, center + right * halfWidth);
					}
					else if (s_HoverAxisY)
					{
						EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), MouseCursor.ResizeHorizontal, controlID);
						Handles.color = highlightColor;
						DrawCrispLine(GridSettings.AxisThickness, center - up * halfHeight, center + up * halfHeight);
					}
				}

				if (e.type == EventType.MouseDown && e.button == 0 && (s_HoverAxisX || s_HoverAxisY))
				{
					s_IsAxisDragging = true;
					s_AxisDragIsVertical = s_HoverAxisY; // Dragging from Y axis creates a vertical line
					GUIUtility.hotControl = controlID;
					PrepareSnapLines(center, right, up);
					e.Use();
				}
			}

			if (s_IsAxisDragging)
			{
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
						Handles.Label(mouseWorldPos + labelOffset,
							$"X: {(pxX >= 0 ? "+" : "")}{pxX:F0} px");
					}
					else
					{
						float rawProj = Vector3.Dot(mouseWorldPos - center, up);
						float snappedProj = ApplySnap(rawProj, s_SnapWorldLinesY, s_SnapSourcesY);
						Vector3 lockedPos = center + up * snappedProj;
						DrawCrispLine(GridSettings.GridThickness, lockedPos - right * halfWidth, lockedPos + right * halfWidth);
						// #8 â€” Ghost mirror preview for axis drag
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
						Handles.Label(mouseWorldPos + labelOffset,
							$"Y: {(pxY >= 0 ? "+" : "")}{pxY:F0} px");
					}
				}

				if (e.type == EventType.MouseDrag || e.type == EventType.MouseMove)
				{
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					if (e.type == EventType.MouseDrag) e.Use();
				}

				if (e.type == EventType.MouseUp && e.button == 0)
				{
					Undo.RecordObject(GridSettingsAsset.instance, "Add Grid Line");

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
							GridSettings.DynamicStretchX.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchX.Add(1f - val);
						}
						else
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							GridSettings.DynamicStretchY.Add(val);
							if (GridSettings.IsMirrorModeActive) GridSettings.DynamicStretchY.Add(1f - val);
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

						Undo.RecordObject(GridSettingsAsset.instance, "Move Grid Line");

						if (GridSettings.IsMirrorModeActive)
						{
							if (s_DragIndexX != -1)
							{
								var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
								for (int i = 0; i < list.Count; i++) if (i != s_DragIndexX && IsMirroredValue(list[s_DragIndexX], list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { s_MirrorDragIndexX = i; break; }
								
								if (s_MirrorDragIndexX == -1)
								{
									float val = list[s_DragIndexX];
									float mirrorVal = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? -val : 1f - val;
									list.Add(mirrorVal);
									s_MirrorDragIndexX = list.Count - 1;
									var aList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettingsAsset.instance.DynamicFixedX : GridSettingsAsset.instance.DynamicStretchX;
									aList.Add(mirrorVal);
								}
							}
							if (s_DragIndexY != -1)
							{
								var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;
								for (int i = 0; i < list.Count; i++) if (i != s_DragIndexY && IsMirroredValue(list[s_DragIndexY], list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { s_MirrorDragIndexY = i; break; }
								
								if (s_MirrorDragIndexY == -1)
								{
									float val = list[s_DragIndexY];
									float mirrorVal = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? -val : 1f - val;
									list.Add(mirrorVal);
									s_MirrorDragIndexY = list.Count - 1;
									var aList = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettingsAsset.instance.DynamicFixedY : GridSettingsAsset.instance.DynamicStretchY;
									aList.Add(mirrorVal);
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
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (s_DragIndexX != -1) 
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) / scaleX;
							GridSettings.DynamicFixedX[s_DragIndexX] = val;
							if (s_MirrorDragIndexX != -1) GridSettings.DynamicFixedX[s_MirrorDragIndexX] = -val;
						}
						if (s_DragIndexY != -1) 
						{
							float val = ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) / scaleY;
							GridSettings.DynamicFixedY[s_DragIndexY] = val;
							if (s_MirrorDragIndexY != -1) GridSettings.DynamicFixedY[s_MirrorDragIndexY] = -val;
						}
					}
					else // Stretch Model
					{
						if (s_DragIndexX != -1) 
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX) + halfWidth) / canvasWidth;
							GridSettings.DynamicStretchX[s_DragIndexX] = val;
							if (s_MirrorDragIndexX != -1) GridSettings.DynamicStretchX[s_MirrorDragIndexX] = 1f - val;
						}
						if (s_DragIndexY != -1) 
						{
							float val = (ApplySnap(Vector3.Dot(mouseWorldPos - center, up), s_SnapWorldLinesY, s_SnapSourcesY) + halfHeight) / canvasHeight;
							GridSettings.DynamicStretchY[s_DragIndexY] = val;
							if (s_MirrorDragIndexY != -1) GridSettings.DynamicStretchY[s_MirrorDragIndexY] = 1f - val;
						}
					}

					// Update the asset's RAM representation without touching disk or Undo so Editor GUI knows it's dirty
					var a = GridSettingsAsset.instance;
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
					{
						if (s_DragIndexX != -1) a.DynamicFixedX[s_DragIndexX] = GridSettings.DynamicFixedX[s_DragIndexX];
						if (s_MirrorDragIndexX != -1) a.DynamicFixedX[s_MirrorDragIndexX] = GridSettings.DynamicFixedX[s_MirrorDragIndexX];
						if (s_DragIndexY != -1) a.DynamicFixedY[s_DragIndexY] = GridSettings.DynamicFixedY[s_DragIndexY];
						if (s_MirrorDragIndexY != -1) a.DynamicFixedY[s_MirrorDragIndexY] = GridSettings.DynamicFixedY[s_MirrorDragIndexY];
					}
					else
					{
						if (s_DragIndexX != -1) a.DynamicStretchX[s_DragIndexX] = GridSettings.DynamicStretchX[s_DragIndexX];
						if (s_MirrorDragIndexX != -1) a.DynamicStretchX[s_MirrorDragIndexX] = GridSettings.DynamicStretchX[s_MirrorDragIndexX];
						if (s_DragIndexY != -1) a.DynamicStretchY[s_DragIndexY] = GridSettings.DynamicStretchY[s_DragIndexY];
						if (s_MirrorDragIndexY != -1) a.DynamicStretchY[s_MirrorDragIndexY] = GridSettings.DynamicStretchY[s_MirrorDragIndexY];
					}

					e.Use();
				}
				else if (e.type == EventType.MouseUp && e.button == 0)
				{
					if (s_MirrorDragIndexX != -1 || s_MirrorDragIndexY != -1)
					{
						GridSettings.SaveDynamicLists();
					}
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
					GridSettingsAsset.instance.SaveToDisk();
					if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
					e.Use();
				}
			}
		}

		/// <summary>
		/// #10 â€” Draw an offset label next to the hovered/dragged line so the user
		/// always knows its exact pixel position from center.
		/// </summary>
		private static void DrawLineOffsetLabel(Vector3 mouseWorldPos,
			float rawOffset, float canvasSize, float localSize, string axis)
		{
			float px = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition
				? rawOffset / (localSize > 0.001f ? (canvasSize / localSize) : 1f)
				: (rawOffset - 0.5f) * localSize;   // stretch t -> pixels from center
			string sign = px >= 0 ? "+" : "";

			Vector3 offset = Vector3.zero;
			if (Camera.current != null)
				offset = (Camera.current.transform.right - Camera.current.transform.up) * HandleUtility.GetHandleSize(mouseWorldPos) * 0.2f;

			Handles.Label(mouseWorldPos + offset, $"{axis}: {sign}{px:F0} px");
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
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexX != -1 && IsMirroredValue(GridSettings.DynamicFixedX[s_DragIndexX], GridSettings.DynamicFixedX[i], true);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexX != -1 && IsMirroredValue(GridSettings.DynamicFixedX[s_HoverIndexX], GridSettings.DynamicFixedX[i], true);

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
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexY != -1 && IsMirroredValue(GridSettings.DynamicFixedY[s_DragIndexY], GridSettings.DynamicFixedY[i], true);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexY != -1 && IsMirroredValue(GridSettings.DynamicFixedY[s_HoverIndexY], GridSettings.DynamicFixedY[i], true);

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
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexX != -1 && IsMirroredValue(GridSettings.DynamicStretchX[s_DragIndexX], t, false);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexX != -1 && IsMirroredValue(GridSettings.DynamicStretchX[s_HoverIndexX], t, false);

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
						bool isMirrorDragged = s_IsDragging && GridSettings.IsMirrorModeActive && s_DragIndexY != -1 && IsMirroredValue(GridSettings.DynamicStretchY[s_DragIndexY], t, false);
						bool isMirrorHovered = !s_IsDragging && GridSettings.IsMirrorModeActive && s_HoverIndexY != -1 && IsMirroredValue(GridSettings.DynamicStretchY[s_HoverIndexY], t, false);

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

			if (isEditing && IsAddLinesMode)
			{
				Color addLinesHighlight = new Color(0.3f, 0.9f, 1f, 1f); // Bright cyan
				finalXAxisColor = s_HoverAxisX ? addLinesHighlight : Color.Lerp(xAxisColor, addLinesHighlight, 0.5f);
				finalYAxisColor = s_HoverAxisY ? addLinesHighlight : Color.Lerp(yAxisColor, addLinesHighlight, 0.5f);
				
				if (s_HoverAxisX) finalXAxisThickness += 1f;
				if (s_HoverAxisY) finalYAxisThickness += 1f;
			}

			Handles.color = finalXAxisColor;
			DrawCrispLine(finalXAxisThickness, Vector3.Lerp(BL, TL, 0.5f), Vector3.Lerp(BR, TR, 0.5f));

			Handles.color = finalYAxisColor;
			DrawCrispLine(finalYAxisThickness, Vector3.Lerp(BL, BR, 0.5f), Vector3.Lerp(TL, TR, 0.5f));
		}

		/// <summary>
		/// Draws professional snap feedback: 4 corner L-brackets + a tight centre crosshair.
		/// Uses a dark shadow pass then a bright icy-cyan pass so it's always visible
		/// on any background colour (dark UI, bright UI, photos, etc.).
		/// corners[] must be GetWorldCorners order: 0=BL, 1=TL, 2=TR, 3=BR.
		/// </summary>
		private static void DrawSnapHighlight(Vector3[] c)
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

			Color shadow = new Color(0f, 0f, 0f, 0.2f);     // very subtle â€” don't drag brightness down
			Color bright = new Color(1f, 0.08f, 0.9f, 1f);  // blazing hot pink

			// â”€â”€ Shadow pass (4px) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
			Handles.color = shadow;
			// BL corner
			DrawCrispLineScreenSpace(2f, c[0], c[0] + right * armLen);
			DrawCrispLineScreenSpace(2f, c[0], c[0] + up    * armLen);
			// TL corner
			DrawCrispLineScreenSpace(2f, c[1], c[1] + right * armLen);
			DrawCrispLineScreenSpace(2f, c[1], c[1] - up    * armLen);
			// TR corner
			DrawCrispLineScreenSpace(2f, c[2], c[2] - right * armLen);
			DrawCrispLineScreenSpace(2f, c[2], c[2] - up    * armLen);
			// BR corner
			DrawCrispLineScreenSpace(2f, c[3], c[3] - right * armLen);
			DrawCrispLineScreenSpace(2f, c[3], c[3] + up    * armLen);
			// Centre crosshair
			DrawCrispLineScreenSpace(2f, ctr - right * crossLen, ctr + right * crossLen);
			DrawCrispLineScreenSpace(2f, ctr - up    * crossLen, ctr + up    * crossLen);

			// â”€â”€ Bright pass (2px) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
			Handles.color = bright;
			// BL corner
			DrawCrispLineScreenSpace(1f, c[0], c[0] + right * armLen);
			DrawCrispLineScreenSpace(1f, c[0], c[0] + up    * armLen);
			// TL corner
			DrawCrispLineScreenSpace(1f, c[1], c[1] + right * armLen);
			DrawCrispLineScreenSpace(1f, c[1], c[1] - up    * armLen);
			// TR corner
			DrawCrispLineScreenSpace(1f, c[2], c[2] - right * armLen);
			DrawCrispLineScreenSpace(1f, c[2], c[2] - up    * armLen);
			// BR corner
			DrawCrispLineScreenSpace(1f, c[3], c[3] - right * armLen);
			DrawCrispLineScreenSpace(1f, c[3], c[3] + up    * armLen);
			// Centre crosshair
			DrawCrispLineScreenSpace(1f, ctr - right * crossLen, ctr + right * crossLen);
			DrawCrispLineScreenSpace(1f, ctr - up    * crossLen, ctr + up    * crossLen);
		}



		[MenuItem("Tools/UI Furnace/[Grid] Add Horizontal Line &h", false, 11)]
		private static void StartAddingHorizontalLine()
		{
			if (!GridSettings.IsEditModeActive) return;
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

		[MenuItem("Tools/UI Furnace/[Grid] Add Horizontal Line &h", true, 11)]
		private static bool ValidateAddHorizontalLine() => GridSettings.IsEditModeActive;

		[MenuItem("Tools/UI Furnace/[Grid] Add Vertical Line &v", false, 12)]
		private static void StartAddingVerticalLine()
		{
			if (!GridSettings.IsEditModeActive) return;
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

		[MenuItem("Tools/UI Furnace/[Grid] Add Vertical Line &v", true, 12)]
		private static bool ValidateAddVerticalLine() => GridSettings.IsEditModeActive;

		[MenuItem("Tools/UI Furnace/[Grid] Delete Selected Lines &BACKSPACE", false, 13)]
		public static void DeleteSelectedLines()
		{
			if (!GridSettings.IsEditModeActive || s_IsAddingLine) return;
			
			if (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0)
			{
				Undo.RecordObject(GridSettingsAsset.instance, "Delete Grid Lines");

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
						float val = list[idx];
						for (int i = 0; i < list.Count; i++) if (i != idx && IsMirroredValue(val, list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { if (!SelectedIndicesX.Contains(i) && !mx.Contains(i)) mx.Add(i); }
					}
					SelectedIndicesX.AddRange(mx);
					
					var my = new List<int>();
					foreach (int idx in SelectedIndicesY)
					{
						var list = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;
						float val = list[idx];
						for (int i = 0; i < list.Count; i++) if (i != idx && IsMirroredValue(val, list[i], GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)) { if (!SelectedIndicesY.Contains(i) && !my.Contains(i)) my.Add(i); }
					}
					SelectedIndicesY.AddRange(my);
				}

				// Sort indices in descending order so removing early elements doesn't shift the indices of later ones
				SelectedIndicesX.Sort((a, b) => b.CompareTo(a));
				SelectedIndicesY.Sort((a, b) => b.CompareTo(a));

				foreach (int index in SelectedIndicesX)
				{
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition) GridSettings.DynamicFixedX.RemoveAt(index);
					else GridSettings.DynamicStretchX.RemoveAt(index);
				}

				foreach (int index in SelectedIndicesY)
				{
					if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition) GridSettings.DynamicFixedY.RemoveAt(index);
					else GridSettings.DynamicStretchY.RemoveAt(index);
				}
				
				GridSettings.SaveDynamicLists();
				SelectedIndicesX.Clear();
				SelectedIndicesY.Clear();
				s_HoverIndexX = -1;
				s_HoverIndexY = -1;
				if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Repaint();
			}
		}

		[MenuItem("Tools/UI Furnace/[Grid] Delete Selected Lines &BACKSPACE", true, 13)]
		private static bool ValidateDeleteSelectedLines() => GridSettings.IsEditModeActive && (SelectedIndicesX.Count > 0 || SelectedIndicesY.Count > 0);

		// #8 â€” Mirror Mode menu item (so it also appears in Tools > UI Furnace)
		[MenuItem("Tools/UI Furnace/[Grid] Toggle Mirror Mode &m", false, 14)]
		private static void ToggleMirrorModeMenuItem()
		{
			if (!GridSettings.IsEditModeActive) return;
			GridSettings.IsMirrorModeActive = !GridSettings.IsMirrorModeActive;
			SceneView.RepaintAll();
			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>()) win.CreateGUI();
		}

		[MenuItem("Tools/UI Furnace/[Grid] Toggle Mirror Mode &m", true, 14)]
		private static bool ValidateToggleMirrorMode() => GridSettings.IsEditModeActive;
		
		private static void ExitEditMode()
		{
			GridSettings.IsEditModeActive   = false;
			GridSettings.IsMirrorModeActive = false; // #8 reset mirror on exit
			s_IsAddingLine   = false;
			IsAddLinesMode   = false;
			s_IsAxisDragging = false;
			s_HoverAxisX = false;
			s_HoverAxisY = false;
			GUIUtility.hotControl = 0;
			SelectedIndicesX.Clear();
			SelectedIndicesY.Clear();
			
			// Force Repaint for all SceneViews and Manager Windows so the toggle switch visually turns off
			SceneView.RepaintAll();
			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
				win.Repaint();
		}
	}
}
#endif
