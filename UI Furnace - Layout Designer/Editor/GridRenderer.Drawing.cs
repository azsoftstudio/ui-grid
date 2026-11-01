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

			// Highlight colors (Dynamically adapted to guarantee perceptual contrast against GridColor)
			Color selectedColor = GetAdaptiveSelectedColor();
			Color hoverColor    = GetAdaptiveHoverColor();
			Color mirrorColor   = GetAdaptiveMirrorColor();
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
			}
		}

		/// <summary>
		/// Returns an adaptive high-visibility selection color that guarantees high contrast
		/// against the current GridColor and background.
		/// </summary>
		public static Color GetAdaptiveSelectedColor()
		{
			Color.RGBToHSV(GridSettings.GridColor, out float h, out float s, out float v);
			// If grid is cyan/blue (h 0.44-0.66), shift selection to electric magenta (h 0.85) to avoid hue collision
			float selectHue = (h >= 0.44f && h <= 0.66f) ? 0.85f : 0.54f;
			Color c = Color.HSVToRGB(selectHue, 0.88f, 1f);
			c.a = 1f;
			return c;
		}

		/// <summary>
		/// Returns an adaptive hover color that contrasts sharply with GridColor.
		/// </summary>
		public static Color GetAdaptiveHoverColor()
		{
			Color.RGBToHSV(GridSettings.GridColor, out float h, out float s, out float v);
			// If grid is yellow/gold (h 0.08-0.24), shift hover to electric cyan (h 0.52)
			float hoverHue = (h >= 0.08f && h <= 0.24f) ? 0.52f : 0.15f;
			Color c = Color.HSVToRGB(hoverHue, 0.88f, 1f);
			c.a = 0.95f;
			return c;
		}

		/// <summary>
		/// Returns an adaptive interactive preview color for Add Line and Ruler Pull drags,
		/// with guaranteed full opacity (never faded by subtle grid alpha).
		/// </summary>
		public static Color GetAdaptivePreviewColor()
		{
			Color.RGBToHSV(GridSettings.GridColor, out float h, out float s, out float v);
			float previewHue = (h >= 0.44f && h <= 0.66f) ? 0.83f : 0.54f;
			Color c = Color.HSVToRGB(previewHue, 0.82f, 1f);
			c.a = 0.95f;
			return c;
		}

		/// <summary>
		/// Returns an adaptive mirror preview color that contrasts with both the grid and main preview.
		/// </summary>
		public static Color GetAdaptiveMirrorColor()
		{
			Color.RGBToHSV(GridSettings.GridColor, out float h, out float s, out float v);
			float mirrorHue = (h >= 0.03f && h <= 0.14f) ? 0.75f : 0.08f;
			Color c = Color.HSVToRGB(mirrorHue, 0.85f, 1f);
			c.a = 0.90f;
			return c;
		}
	}
}

#endif
