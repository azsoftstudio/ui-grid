#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Interactive guide editing, dragging, notch pulling, and keyboard shortcuts for Layout Designer.
	/// </summary>
	public static partial class GridRenderer
	{
		// ─── Drag & Hover State ───────────────────────────────────────────────────────
		private static int      s_DragIndexX       = -1;
		private static int      s_DragIndexY       = -1;
		private static int      s_MirrorDragIndexX = -1;
		private static int      s_MirrorDragIndexY = -1;
		private static int      s_HoverIndexX      = -1;
		private static int      s_HoverIndexY      = -1;
		private static bool     s_IsDragging       = false;
		private static bool     s_IsShiftDown      = false;
		private static bool     s_IsAltDown        = false;
		private static DragMode s_CurrentDragMode  = DragMode.None;

		private enum DragMode { None, Move, Resize }
		
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

		// ─── Add Lines Mode (Drag from Axis) State ──────────────────────────────────────
		/// <summary>When true, users can drag from the canvas edges to pull out a new horizontal/vertical guide.</summary>
		public static bool IsAddLinesMode { get; set; } = false;
		private static bool s_IsAxisDragging = false;     // True while mid-drag creating a new line
		private static bool s_AxisDragIsVertical = false; // Which guide orientation is being pulled (true = vertical, false = horizontal)
		private static bool s_HoverEdgeH = false;         // Mouse hovering near horizontal pull edges (Top or Bottom)
		private static bool s_HoverEdgeV = false;         // Mouse hovering near vertical pull edges (Left or Right)
		private static bool s_HoverEdgeTop = false;       // Specifically top edge
		private static bool s_HoverEdgeLeft = false;      // Specifically left edge

		// ─── Selection State (Transient) ────────────────────────────────────────────────
		public static readonly List<int> SelectedIndicesX = new List<int>();
		public static readonly List<int> SelectedIndicesY = new List<int>();

		// ─── Helpers ─────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Returns <c>true</c> when <paramref name="val2"/> is the symmetric mirror of
		/// <paramref name="val1"/> on the canvas.<br/>
		/// Fixed-position mode: mirror = <c>-val</c>.<br/>
		/// Stretch mode: mirror = <c>1 − val</c>.
		/// </summary>
		private static bool IsMirroredValue(float val1, float val2, bool isFixed)
		{
			float target = isFixed ? -val1 : (1f - val1);
			return Mathf.Abs(val2 - target) < 0.001f;
		}

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
			// BUG 4 fix: check Plane.Raycast return value Ã¢â‚¬â€ if the ray is parallel to
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
						// #8 Ã¢â‚¬â€ Ghost mirror preview
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
						DrawPositionBadge(mouseWorldPos, $"X: {(pxOffX >= 0 ? "+" : "")}{pxOffX:F0} px");
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
						DrawPositionBadge(mouseWorldPos, $"Y: {(pxOffY >= 0 ? "+" : "")}{pxOffY:F0} px");
					}

					// Snap highlights are drawn after DrawDynamicGrid in OnSceneGUI — they're always on top.
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

			// ─── Add Lines Mode: Drag from Axis to Create New Line ──────────────────────────
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
						// #8 Ã¢â‚¬â€ Ghost mirror preview for axis drag
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
						// Side-effect: populates s_SnapHighlightedRects so snap brackets render this frame.
						ApplySnap(Vector3.Dot(mouseWorldPos - center, right), s_SnapWorldLinesX, s_SnapSourcesX);
					}
					if (s_DragIndexY != -1)
					{
						EditorGUIUtility.AddCursorRect(cursorRect, MouseCursor.ResizeVertical, controlID);
						// Side-effect: populates s_SnapHighlightedRects so snap brackets render this frame.
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
	}
}
#endif
