#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Interaction pipeline for Canvas Edge Rulers (Revision 3: Designer-Grade, Lean & Viewport-Adaptive).
	/// Handles Shift+R shortcut, origin switching, drag-to-create guides, and drag-back-to-delete.
	/// </summary>
	public static partial class GridRenderer
	{
		// ─── Interaction Reset Hook ──────────────────────────────────────────
		public static void ResetRulerInteractionState()
		{
			s_IsHoveringDeleteZone = false;
		}

		// ─── Main Ruler Event Handling ───────────────────────────────────────
		public static void HandleRulerEvents(
			Event e, int rulerControlID, RectTransform canvasRect,
			Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight)
		{
			// 1. Universal Hotkey: Shift + R toggles Rulers
			if (e.type == EventType.KeyDown && e.shift && e.keyCode == KeyCode.R && !EditorGUIUtility.editingTextField)
			{
				GridSettings.ShowRulers = !GridSettings.ShowRulers;
				if (SceneView.currentDrawingSceneView != null)
					SceneView.currentDrawingSceneView.Repaint();
				e.Use();
				return;
			}

			if (!GridSettings.ShowRulers || !s_RulersVisibleThisFrame)
				return;

			Vector2 mouse = e.mousePosition;
			bool hoverTop    = s_TopRulerRect.Contains(mouse);
			bool hoverLeft   = s_LeftRulerRect.Contains(mouse);
			bool hoverCorner = s_CornerRect.Contains(mouse);

			// 2. Drag-to-Delete Detection for Active Guide Drags
			if (s_IsDragging || s_IsAxisDragging)
			{
				s_IsHoveringDeleteZone = hoverTop || hoverLeft || hoverCorner;

				if (s_IsHoveringDeleteZone)
				{
					// Display delete cursor badge
					EditorGUIUtility.AddCursorRect(new Rect(0, 0, 10000, 10000), MouseCursor.ArrowMinus, rulerControlID);

					if (e.type == EventType.MouseUp && e.button == 0)
					{
						if (s_IsDragging)
						{
							// Delete the dragged existing guide
							GridSettings.RecordUndo("Delete Guide Line");
							if (s_DragIndexX >= 0)
							{
								if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
								{
									if (s_DragIndexX < GridSettings.DynamicFixedX.Count)
										GridSettings.DynamicFixedX.RemoveAt(s_DragIndexX);
								}
								else
								{
									if (s_DragIndexX < GridSettings.DynamicStretchX.Count)
										GridSettings.DynamicStretchX.RemoveAt(s_DragIndexX);
								}
							}
							else if (s_DragIndexY >= 0)
							{
								if (GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition)
								{
									if (s_DragIndexY < GridSettings.DynamicFixedY.Count)
										GridSettings.DynamicFixedY.RemoveAt(s_DragIndexY);
								}
								else
								{
									if (s_DragIndexY < GridSettings.DynamicStretchY.Count)
										GridSettings.DynamicStretchY.RemoveAt(s_DragIndexY);
								}
							}

							GridSettings.SaveDynamicLists();
							s_IsDragging = false;
							s_DragIndexX = -1;
							s_DragIndexY = -1;
							s_MirrorDragIndexX = -1;
							s_MirrorDragIndexY = -1;
							GUIUtility.hotControl = 0;
							s_IsHoveringDeleteZone = false;
							if (SceneView.currentDrawingSceneView != null)
								SceneView.currentDrawingSceneView.Repaint();
							e.Use();
							return;
						}
						else if (s_IsAxisDragging)
						{
							// Cancel freshly pulled guide without committing
							s_IsAxisDragging = false;
							GUIUtility.hotControl = 0;
							s_IsHoveringDeleteZone = false;
							if (SceneView.currentDrawingSceneView != null)
								SceneView.currentDrawingSceneView.Repaint();
							e.Use();
							return;
						}
					}
				}
			}
			else
			{
				s_IsHoveringDeleteZone = false;
			}

			// 3. Corner Origin Box Click (Toggle Origin Mode: Center -> TopLeft -> BottomLeft)
			if (hoverCorner)
			{
				EditorGUIUtility.AddCursorRect(s_CornerRect, MouseCursor.Link, rulerControlID);

				if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
				{
					int next = ((int)GridSettings.RulerOrigin + 1) % 3;
					GridSettings.RulerOrigin = (RulerCoordinateOrigin)next;
					if (SceneView.currentDrawingSceneView != null)
						SceneView.currentDrawingSceneView.Repaint();
					e.Use();
					return;
				}
			}

			// 4. Cursor Feedback for Ruler Hovering
			if (hoverTop)
			{
				EditorGUIUtility.AddCursorRect(s_TopRulerRect, MouseCursor.ResizeVertical, rulerControlID);
			}
			else if (hoverLeft)
			{
				EditorGUIUtility.AddCursorRect(s_LeftRulerRect, MouseCursor.ResizeHorizontal, rulerControlID);
			}

			// 5. Layout Pass: Register control ID so Unity RectTool does not intercept ruler drags
			if (e.type == EventType.Layout && !s_IsAxisDragging && !s_IsDragging)
			{
				if (hoverTop || hoverLeft || hoverCorner)
				{
					HandleUtility.AddControl(rulerControlID, 0f);
				}
			}

			// 6. MouseDown: Initiate Guide Drag directly from Ruler Rails
			if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !s_IsAxisDragging)
			{
				if (hoverTop || hoverLeft)
				{
					// Ensure we are in CustomLines mode and editing is enabled
					if (GridSettings.CurrentMode != GridMode.CustomLines)
					{
						GridSettings.CurrentMode = GridMode.CustomLines;
						if (GridSettings.ActiveProfile != null)
							GridSettings.ActiveProfile.CurrentMode = GridMode.CustomLines;
					}
					GridSettings.IsEditModeActive = true;

					Vector3 center = (BL + TR) * 0.5f;
					Vector3 right  = (BR - BL).normalized;
					Vector3 up     = (TL - BL).normalized;

					s_IsAxisDragging     = true;
					s_AxisDragIsVertical = hoverLeft; // Left ruler creates vertical guide; Top ruler creates horizontal guide
					GUIUtility.hotControl = rulerControlID;

					PrepareSnapLines(center, right, up);

					if (SceneView.currentDrawingSceneView != null)
						SceneView.currentDrawingSceneView.Repaint();

					e.Use();
				}
			}
		}
	}
}
#endif
