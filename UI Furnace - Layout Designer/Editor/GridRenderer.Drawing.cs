#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Visual rendering pipeline for Layout Designer (crisp line drawing, uniform grid, stretch grid, and custom lines).
	/// </summary>
	public static partial class GridRenderer
	{
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

			if (t == 1f)
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
		/// screenThickness is the desired width in physical screen pixels.
		/// PixelSnap is applied to the line endpoints so lines stay locked to whole pixels as camera pans.
		/// </summary>
		private static void DrawCrispLine(float screenThickness, Vector3 p1, Vector3 p2)
		{
			if (Camera.current != null && Camera.current.orthographic)
			{
				p1 = PixelSnap(p1);
				p2 = PixelSnap(p2);
			}

			DrawCrispLineScreenSpace(screenThickness, p1, p2);
		}

		/// <summary>Apply global opacity to a color.</summary>
		private static Color WithOpacity(Color color)
		{
			color.a *= GridSettings.GridOpacity;
			return color;
		}

		/// <summary>
		/// Draw grid with FIXED pixel spacing starting from center axes outwards.
		/// </summary>
		private static void DrawFixedSpacingGrid(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR, float localWidth, float localHeight)
		{
			float spacingX = GridSettings.GridSpacingX;
			float spacingY = GridSettings.GridSpacingY;

			if (spacingX <= 0.001f || spacingY <= 0.001f) return;

			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			float scaleX = localWidth  > 0.001f ? (canvasWidth  / localWidth)  : 1f;
			float scaleY = localHeight > 0.001f ? (canvasHeight / localHeight) : 1f;

			float worldSpacingX = spacingX * scaleX;
			float worldSpacingY = spacingY * scaleY;

			float halfW = canvasWidth  * 0.5f;
			float halfH = canvasHeight * 0.5f;

			float gridThickness = GridSettings.GridThickness;
			float axisThickness = GridSettings.AxisThickness;
			Color gridColor     = WithOpacity(GridSettings.GridColor);
			Color xAxisColor    = WithOpacity(GridSettings.XAxisColor);
			Color yAxisColor    = WithOpacity(GridSettings.YAxisColor);

			Handles.color = gridColor;

			// Vertical lines to the right of center
			for (float offset = worldSpacingX; offset <= halfW; offset += worldSpacingX)
			{
				Vector3 p = center + right * offset;
				DrawCrispLine(gridThickness, p - up * halfH, p + up * halfH);
			}
			// Vertical lines to the left of center
			for (float offset = worldSpacingX; offset <= halfW; offset += worldSpacingX)
			{
				Vector3 p = center - right * offset;
				DrawCrispLine(gridThickness, p - up * halfH, p + up * halfH);
			}
			// Horizontal lines above center
			for (float offset = worldSpacingY; offset <= halfH; offset += worldSpacingY)
			{
				Vector3 p = center + up * offset;
				DrawCrispLine(gridThickness, p - right * halfW, p + right * halfW);
			}
			// Horizontal lines below center
			for (float offset = worldSpacingY; offset <= halfH; offset += worldSpacingY)
			{
				Vector3 p = center - up * offset;
				DrawCrispLine(gridThickness, p - right * halfW, p + right * halfW);
			}

			// Center Axes
			Handles.color = yAxisColor;
			DrawCrispLine(axisThickness, center - up * halfH, center + up * halfH);

			Handles.color = xAxisColor;
			DrawCrispLine(axisThickness, center - right * halfW, center + right * halfW);
		}

		/// <summary>
		/// Draw grid by DIVIDING canvas bounds equally (Columns x Rows).
		/// </summary>
		private static void DrawStretchGrid(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR)
		{
			int cols = GridSettings.GridColumns;
			int rows = GridSettings.GridRows;

			float gridThickness = GridSettings.GridThickness;
			float axisThickness = GridSettings.AxisThickness;
			Color gridColor     = WithOpacity(GridSettings.GridColor);
			Color xAxisColor    = WithOpacity(GridSettings.XAxisColor);
			Color yAxisColor    = WithOpacity(GridSettings.YAxisColor);

			Handles.color = gridColor;

			for (int i = 1; i < cols; i++)
			{
				float t = (float)i / cols;
				Vector3 bot = Vector3.Lerp(BL, BR, t);
				Vector3 top = Vector3.Lerp(TL, TR, t);
				DrawCrispLine(gridThickness, bot, top);
			}

			for (int j = 1; j < rows; j++)
			{
				float t = (float)j / rows;
				Vector3 left  = Vector3.Lerp(BL, TL, t);
				Vector3 right = Vector3.Lerp(BR, TR, t);
				DrawCrispLine(gridThickness, left, right);
			}

			// Center Axes
			Vector3 midLeft  = (BL + TL) * 0.5f;
			Vector3 midRight = (BR + TR) * 0.5f;
			Handles.color = xAxisColor;
			DrawCrispLine(axisThickness, midLeft, midRight);

			Vector3 midBot = (BL + BR) * 0.5f;
			Vector3 midTop = (TL + TR) * 0.5f;
			Handles.color = yAxisColor;
			DrawCrispLine(axisThickness, midBot, midTop);
		}

		/// <summary>
		/// Draws edge pull rails and interactive notch along canvas boundary for edge-pull guide creation.
		/// </summary>
		private static void DrawEdgePullTabs(Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight, Vector3 right, Vector3 up, int controlID, Vector3 mouseWorldPos)
		{
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

			// Handle notch at mouse projection along hovered edge
			if (isHoverTop || isHoverBottom || isHoverLeft || isHoverRight)
			{
				Vector3 edgePoint;
				if (isHoverTop) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, TL, TR);
				else if (isHoverBottom) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BL, BR);
				else if (isHoverLeft) edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BL, TL);
				else edgePoint = HandleUtility.ProjectPointLine(mouseWorldPos, BR, TR);

				DrawEdgeHandleNotch(edgePoint, right, up, isHoverTop || isHoverBottom);
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

			Vector3 center     = (BL + TR) * 0.5f;
			Vector3 rightDir   = BR - BL;
			Vector3 upDir      = TL - BL;
			float canvasWidth  = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			Vector3 right      = rightDir / canvasWidth;
			Vector3 up         = upDir / canvasHeight;

			// Center Axes
			Handles.color = xAxisColor;
			DrawCrispLine(axisThickness, center - right * canvasWidth * 0.5f, center + right * canvasWidth * 0.5f);

			Handles.color = yAxisColor;
			DrawCrispLine(axisThickness, center - up * canvasHeight * 0.5f, center + up * canvasHeight * 0.5f);

			// Highlight colors
			Color selectedColor = new Color(0.2f, 0.8f, 1f, 1f);
			Color hoverColor    = new Color(1f, 1f, 0.2f, 0.9f);
			Color mirrorColor   = new Color(1f, 0.55f, 0.1f, 0.95f);
			Color snapLineColor = GetGridComplementColor();

			bool isFixed = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition;
			float scaleX = localWidth  > 0.001f ? (canvasWidth  / localWidth)  : 1f;
			float scaleY = localHeight > 0.001f ? (canvasHeight / localHeight) : 1f;

			var linesX = isFixed ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
			var linesY = isFixed ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;

			// Vertical Lines
			for (int i = 0; i < linesX.Count; i++)
			{
				Vector3 bot, top;
				float offsetVal = linesX[i];

				if (isFixed)
				{
					Vector3 p = center + right * (offsetVal * scaleX);
					bot = p - up * (canvasHeight * 0.5f);
					top = p + up * (canvasHeight * 0.5f);
				}
				else
				{
					bot = Vector3.Lerp(BL, BR, offsetVal);
					top = Vector3.Lerp(TL, TR, offsetVal);
				}

				bool isSelected = SelectedIndicesX.Contains(i);
				bool isHovered  = (i == s_HoverIndexX);
				bool isDragged  = (i == s_DragIndexX);
				bool isMirror   = (i == s_MirrorDragIndexX);
				bool isSnapped  = s_HighlightOffsetsX.Contains(offsetVal);

				if (isSnapped)
				{
					Handles.color = snapLineColor;
					DrawCrispLine(Mathf.Max(gridThickness + 1.5f, 2.5f), bot, top);
				}
				else if (isDragged)
				{
					Handles.color = selectedColor;
					DrawCrispLine(gridThickness + 1.5f, bot, top);
				}
				else if (isMirror)
				{
					Handles.color = mirrorColor;
					DrawCrispLine(gridThickness + 1f, bot, top);
				}
				else if (isSelected)
				{
					Handles.color = selectedColor;
					DrawCrispLine(gridThickness + 1f, bot, top);
				}
				else if (isHovered)
				{
					Handles.color = hoverColor;
					DrawCrispLine(gridThickness + 0.5f, bot, top);
				}
				else
				{
					Handles.color = gridColor;
					DrawCrispLine(gridThickness, bot, top);
				}

				if (GridSettings.IsEditModeActive)
				{
					float handleSize = HandleUtility.GetHandleSize(bot) * 0.04f;
					Color handleColor = isSnapped ? snapLineColor : (isDragged ? selectedColor : (isMirror ? mirrorColor : (isSelected ? selectedColor : (isHovered ? hoverColor : gridColor))));
					Handles.color = handleColor;
					Handles.DrawSolidDisc(bot, -Camera.current.transform.forward, handleSize);
					Handles.DrawSolidDisc(top, -Camera.current.transform.forward, handleSize);
				}
			}

			// Horizontal Lines
			for (int j = 0; j < linesY.Count; j++)
			{
				Vector3 left, rightPt;
				float offsetVal = linesY[j];

				if (isFixed)
				{
					Vector3 p = center + up * (offsetVal * scaleY);
					left    = p - right * (canvasWidth * 0.5f);
					rightPt = p + right * (canvasWidth * 0.5f);
				}
				else
				{
					left    = Vector3.Lerp(BL, TL, offsetVal);
					rightPt = Vector3.Lerp(BR, TR, offsetVal);
				}

				bool isSelected = SelectedIndicesY.Contains(j);
				bool isHovered  = (j == s_HoverIndexY);
				bool isDragged  = (j == s_DragIndexY);
				bool isMirror   = (j == s_MirrorDragIndexY);
				bool isSnapped  = s_HighlightOffsetsY.Contains(offsetVal);

				if (isSnapped)
				{
					Handles.color = snapLineColor;
					DrawCrispLine(Mathf.Max(gridThickness + 1.5f, 2.5f), left, rightPt);
				}
				else if (isDragged)
				{
					Handles.color = selectedColor;
					DrawCrispLine(gridThickness + 1.5f, left, rightPt);
				}
				else if (isMirror)
				{
					Handles.color = mirrorColor;
					DrawCrispLine(gridThickness + 1f, left, rightPt);
				}
				else if (isSelected)
				{
					Handles.color = selectedColor;
					DrawCrispLine(gridThickness + 1f, left, rightPt);
				}
				else if (isHovered)
				{
					Handles.color = hoverColor;
					DrawCrispLine(gridThickness + 0.5f, left, rightPt);
				}
				else
				{
					Handles.color = gridColor;
					DrawCrispLine(gridThickness, left, rightPt);
				}

				if (GridSettings.IsEditModeActive)
				{
					float handleSize = HandleUtility.GetHandleSize(left) * 0.04f;
					Color handleColor = isSnapped ? snapLineColor : (isDragged ? selectedColor : (isMirror ? mirrorColor : (isSelected ? selectedColor : (isHovered ? hoverColor : gridColor))));
					Handles.color = handleColor;
					Handles.DrawSolidDisc(left, -Camera.current.transform.forward, handleSize);
					Handles.DrawSolidDisc(rightPt, -Camera.current.transform.forward, handleSize);
				}
			}

			// Interactive guide placement cursor line preview
			if (s_IsAddingLine)
			{
				Handles.color = new Color(0.2f, 0.8f, 1f, 0.8f);
				Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
				Plane plane = new Plane(-Camera.current.transform.forward, center);
				if (plane.Raycast(ray, out float enter))
				{
					Vector3 hit = ray.GetPoint(enter);
					if (s_AddingLineIsVertical)
					{
						Vector3 p = center + right * Vector3.Dot(hit - center, right);
						DrawCrispLine(gridThickness + 1f, p - up * (canvasHeight * 0.5f), p + up * (canvasHeight * 0.5f));
					}
					else
					{
						Vector3 p = center + up * Vector3.Dot(hit - center, up);
						DrawCrispLine(gridThickness + 1f, p - right * (canvasWidth * 0.5f), p + right * (canvasWidth * 0.5f));
					}
				}
			}
		}
	}
}
#endif
