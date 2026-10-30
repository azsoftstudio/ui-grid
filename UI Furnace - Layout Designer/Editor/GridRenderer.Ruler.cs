#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Rendering pipeline for Canvas Edge Rulers (Revision 3: Designer-Grade, Lean & Viewport-Adaptive).
	/// Features sticky viewport clamping, adaptive zoom subdivision, 0-GC string caching, and deep anchor notches.
	/// </summary>
	public static partial class GridRenderer
	{
		// ─── Color Tokens (Dark Studio Aesthetic) ───────────────────────────
		private static readonly Color RulerBgColor         = new Color(0.094f, 0.094f, 0.110f, 0.94f);
		private static readonly Color RulerBorderColor     = new Color(0.173f, 0.173f, 0.208f, 1.00f);
		private static readonly Color MajorTickColor       = new Color(0.878f, 0.878f, 0.902f, 1.00f);
		private static readonly Color MinorTickColor       = new Color(0.431f, 0.431f, 0.478f, 0.80f);
		private static readonly Color CursorHairlineColor  = new Color(0.220f, 0.741f, 0.973f, 1.00f);
		private static readonly Color AnchorPipColor       = new Color(1.000f, 0.702f, 0.000f, 1.00f);
		private static readonly Color PivotPipColor        = new Color(0.753f, 0.518f, 0.988f, 1.00f);
		private static readonly Color DeleteZoneBgColor    = new Color(0.937f, 0.267f, 0.267f, 0.35f);
		private static readonly Color CornerBtnBgColor     = new Color(0.133f, 0.133f, 0.157f, 1.00f);
		private static readonly Color CornerBtnHoverColor  = new Color(0.220f, 0.220f, 0.260f, 1.00f);

		// ─── Shared Geometry & State ─────────────────────────────────────────
		public static Rect s_TopRulerRect;
		public static Rect s_LeftRulerRect;
		public static Rect s_CornerRect;
		public static bool s_RulersVisibleThisFrame;
		public static bool s_IsHoveringDeleteZone;

		// Screen-space canvas corners (in GUI points)
		private static Vector2 s_TL_GUI;
		private static Vector2 s_TR_GUI;
		private static Vector2 s_BL_GUI;
		private static Vector2 s_BR_GUI;

		// ─── Zero-GC String & Style Caches ───────────────────────────────────
		private static readonly Dictionary<int, string> s_IntStringCache = new Dictionary<int, string>(128);

		public static string GetCachedIntString(int value)
		{
			if (!s_IntStringCache.TryGetValue(value, out string str))
			{
				str = value.ToString();
				s_IntStringCache[value] = str;
			}
			return str;
		}

		private static GUIStyle s_RulerLabelStyle;
		private static GUIStyle RulerLabelStyle
		{
			get
			{
				if (s_RulerLabelStyle == null)
				{
					s_RulerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
					{
						alignment = TextAnchor.UpperLeft,
						clipping = TextClipping.Overflow,
						fontSize = 9,
						padding = new RectOffset(0, 0, 0, 0),
						margin = new RectOffset(0, 0, 0, 0)
					};
					s_RulerLabelStyle.normal.textColor = MajorTickColor;
				}
				return s_RulerLabelStyle;
			}
		}

		private static GUIStyle s_VerticalRulerLabelStyle;
		private static GUIStyle VerticalRulerLabelStyle
		{
			get
			{
				if (s_VerticalRulerLabelStyle == null)
				{
					s_VerticalRulerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
					{
						alignment = TextAnchor.MiddleRight,
						clipping = TextClipping.Overflow,
						fontSize = 9,
						padding = new RectOffset(0, 0, 0, 0),
						margin = new RectOffset(0, 0, 0, 0)
					};
					s_VerticalRulerLabelStyle.normal.textColor = MajorTickColor;
				}
				return s_VerticalRulerLabelStyle;
			}
		}

		private static GUIStyle s_CornerBoxStyle;
		private static GUIStyle CornerBoxStyle
		{
			get
			{
				if (s_CornerBoxStyle == null)
				{
					s_CornerBoxStyle = new GUIStyle(EditorStyles.miniLabel)
					{
						alignment = TextAnchor.MiddleCenter,
						clipping = TextClipping.Overflow,
						fontStyle = FontStyle.Bold,
						fontSize = 8,
						padding = new RectOffset(0, 0, 0, 0),
						margin = new RectOffset(0, 0, 0, 0)
					};
					s_CornerBoxStyle.normal.textColor = MajorTickColor;
				}
				return s_CornerBoxStyle;
			}
		}

		// ─── Adaptive Subdivision Calculation ────────────────────────────────
		private static void GetTickSteps(float Spx, out int majorStep, out int minorStep)
		{
			if (Spx < 0.05f)      { majorStep = 2000; minorStep = 500; }
			else if (Spx < 0.15f) { majorStep = 1000; minorStep = 200; }
			else if (Spx < 0.40f) { majorStep = 500;  minorStep = 100; }
			else if (Spx < 1.20f) { majorStep = 100;  minorStep = 20;  }
			else if (Spx < 3.50f) { majorStep = 50;   minorStep = 10;  }
			else if (Spx < 8.00f) { majorStep = 10;   minorStep = 2;   }
			else                  { majorStep = 5;    minorStep = 1;   }
		}

		// ─── Display Coordinate Conversions ─────────────────────────────────
		public static float ToDisplayCoord(float localPx, float halfDim, RulerCoordinateOrigin origin, bool isYAxis)
		{
			switch (origin)
			{
				case RulerCoordinateOrigin.CanvasCenter:
					return localPx;
				case RulerCoordinateOrigin.TopLeft:
					return isYAxis ? (halfDim - localPx) : (localPx + halfDim); // Y increases downward
				case RulerCoordinateOrigin.BottomLeft:
					return localPx + halfDim;
				default:
					return localPx;
			}
		}

		public static float FromDisplayCoord(float displayPx, float halfDim, RulerCoordinateOrigin origin, bool isYAxis)
		{
			switch (origin)
			{
				case RulerCoordinateOrigin.CanvasCenter:
					return displayPx;
				case RulerCoordinateOrigin.TopLeft:
					return isYAxis ? (halfDim - displayPx) : (displayPx - halfDim);
				case RulerCoordinateOrigin.BottomLeft:
					return isYAxis ? (displayPx - halfDim) : (displayPx - halfDim);
				default:
					return displayPx;
			}
		}

		// ─── Geometry Calculation with Sticky Viewport Clamping ─────────────
		public static bool ComputeRulerGeometry(
			Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight, SceneView sv)
		{
			s_RulersVisibleThisFrame = false;
			if (sv == null || localWidth <= 0.001f || localHeight <= 0.001f)
				return false;

			s_TL_GUI = HandleUtility.WorldToGUIPoint(TL);
			s_TR_GUI = HandleUtility.WorldToGUIPoint(TR);
			s_BL_GUI = HandleUtility.WorldToGUIPoint(BL);
			s_BR_GUI = HandleUtility.WorldToGUIPoint(BR);

			float viewW = sv.position.width;
			float viewH = sv.position.height;

			float canvasMinX = Mathf.Min(s_TL_GUI.x, s_BL_GUI.x);
			float canvasMaxX = Mathf.Max(s_TR_GUI.x, s_BR_GUI.x);
			float canvasMinY = Mathf.Min(s_TL_GUI.y, s_TR_GUI.y);
			float canvasMaxY = Mathf.Max(s_BL_GUI.y, s_BR_GUI.y);

			// If canvas is completely outside SceneView viewport, skip drawing
			if (canvasMaxX < 0f || canvasMinX > viewW || canvasMaxY < 0f || canvasMinY > viewH)
				return false;

			float rulerThickness = 20f;

			// Sticky Clamping:
			// Top ruler sits above canvas top edge, clamped to viewport top margin
			float clampedTopY  = Mathf.Clamp(s_TL_GUI.y - rulerThickness, 0f, viewH - rulerThickness);
			// Left ruler sits to the left of canvas left edge, clamped to viewport left margin
			float clampedLeftX = Mathf.Clamp(s_TL_GUI.x - rulerThickness, 0f, viewW - rulerThickness);

			float topStart = Mathf.Max(clampedLeftX + rulerThickness, canvasMinX);
			float topEnd   = Mathf.Min(viewW, canvasMaxX);

			float leftStart = Mathf.Max(clampedTopY + rulerThickness, canvasMinY);
			float leftEnd   = Mathf.Min(viewH, canvasMaxY);

			if (topEnd - topStart < 10f || leftEnd - leftStart < 10f)
				return false;

			s_TopRulerRect  = new Rect(topStart, clampedTopY, topEnd - topStart, rulerThickness);
			s_LeftRulerRect = new Rect(clampedLeftX, leftStart, rulerThickness, leftEnd - leftStart);
			s_CornerRect    = new Rect(clampedLeftX, clampedTopY, rulerThickness, rulerThickness);

			s_RulersVisibleThisFrame = true;
			return true;
		}

		// ─── Main Drawing Entry Point ────────────────────────────────────────
		public static void DrawCanvasRulers(
			Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight, int controlID)
		{
			if (!GridSettings.ShowRulers) return;

			SceneView sv = SceneView.currentDrawingSceneView;
			if (sv == null) return;

			if (!ComputeRulerGeometry(BL, TL, TR, BR, localWidth, localHeight, sv))
				return;

			Handles.BeginGUI();

			float ppp = EditorGUIUtility.pixelsPerPoint;
			float borderThick = Mathf.Max(1f, Mathf.Round(1f * ppp));
			float halfW = localWidth * 0.5f;
			float halfH = localHeight * 0.5f;
			var origin = GridSettings.RulerOrigin;

			// 1. Draw Background Tracks
			Color topBgColor = (s_IsHoveringDeleteZone && s_TopRulerRect.Contains(Event.current.mousePosition)) ? DeleteZoneBgColor : RulerBgColor;
			Color leftBgColor = (s_IsHoveringDeleteZone && s_LeftRulerRect.Contains(Event.current.mousePosition)) ? DeleteZoneBgColor : RulerBgColor;

			EditorGUI.DrawRect(s_TopRulerRect, topBgColor);
			EditorGUI.DrawRect(s_LeftRulerRect, leftBgColor);
			EditorGUI.DrawRect(s_CornerRect, RulerBgColor);

			// 2. Draw Outer Separation Hairlines along the canvas-facing borders
			// Top ruler bottom edge
			EditorGUI.DrawRect(new Rect(s_TopRulerRect.x, s_TopRulerRect.yMax - borderThick, s_TopRulerRect.width, borderThick), RulerBorderColor);
			// Left ruler right edge
			EditorGUI.DrawRect(new Rect(s_LeftRulerRect.xMax - borderThick, s_LeftRulerRect.y, borderThick, s_LeftRulerRect.height), RulerBorderColor);
			// Corner box dividing edges
			EditorGUI.DrawRect(new Rect(s_CornerRect.xMax - borderThick, s_CornerRect.y, borderThick, s_CornerRect.height), RulerBorderColor);
			EditorGUI.DrawRect(new Rect(s_CornerRect.x, s_CornerRect.yMax - borderThick, s_CornerRect.width, borderThick), RulerBorderColor);

			// 3. Compute Screen Pixels per Canvas Unit (Spx)
			float canvasWidthGUI  = Mathf.Abs(s_TR_GUI.x - s_TL_GUI.x);
			float canvasHeightGUI = Mathf.Abs(s_BL_GUI.y - s_TL_GUI.y);
			float Spx_X = canvasWidthGUI / Mathf.Max(localWidth, 0.001f);
			float Spx_Y = canvasHeightGUI / Mathf.Max(localHeight, 0.001f);

			GetTickSteps(Spx_X, out int majorStepX, out int minorStepX);
			GetTickSteps(Spx_Y, out int majorStepY, out int minorStepY);

			// 4. Draw Horizontal Ticks & Labels on Top Ruler
			DrawHorizontalTicks(halfW, Spx_X, minorStepX, majorStepX, origin, ppp);

			// 5. Draw Vertical Ticks & Labels on Left Ruler
			DrawVerticalTicks(halfH, Spx_Y, minorStepY, majorStepY, origin, ppp);

			// 6. Project Selected Element Anchors (Subtle Inner Edge Notch Pips)
			DrawSelectedAnchorNotches(halfW, halfH, localWidth, localHeight, ppp);

			// 7. Draw Cursor Tracking Hairlines
			DrawCursorTrackingHairlines(ppp);

			// 8. Draw Corner Origin Box & Coordinate Readout HUD
			DrawCornerOriginBox(halfW, halfH, origin, ppp);

			Handles.EndGUI();
		}

		// ─── Horizontal Ticks Drawing ────────────────────────────────────────
		private static void DrawHorizontalTicks(
			float halfW, float Spx, int minorStep, int majorStep, RulerCoordinateOrigin origin, float ppp)
		{
			float majorTickH = 6f;
			float minorTickH = 3f;
			float tickW      = 1f;
			float minLabelGap = 35f;
			float lastLabelX = float.NaN;

			// Visible local range across top ruler
			float visibleStartLocal = ScreenToCanvasLocalX(s_TopRulerRect.xMin, halfW);
			float visibleEndLocal   = ScreenToCanvasLocalX(s_TopRulerRect.xMax, halfW);

			// Convert range to display coordinates so ticks step in the active origin's coordinate system
			float startDisp = ToDisplayCoord(visibleStartLocal, halfW, origin, false);
			float endDisp   = ToDisplayCoord(visibleEndLocal, halfW, origin, false);

			float minDisp = Mathf.Min(startDisp, endDisp);
			float maxDisp = Mathf.Max(startDisp, endDisp);

			int startTick = Mathf.FloorToInt(minDisp / minorStep) * minorStep;
			int endTick   = Mathf.CeilToInt(maxDisp / minorStep) * minorStep;

			// Clamp bounds to prevent loop runaways
			startTick = Mathf.Max(startTick, -50000);
			endTick   = Mathf.Min(endTick, 50000);

			for (int dispTick = startTick; dispTick <= endTick; dispTick += minorStep)
			{
				float localX = FromDisplayCoord(dispTick, halfW, origin, false);
				float screenX = Mathf.Round(CanvasLocalToScreenX(localX, halfW));
				if (screenX < s_TopRulerRect.xMin || screenX > s_TopRulerRect.xMax)
					continue;

				bool isMajor = (dispTick % majorStep) == 0;
				float h = isMajor ? majorTickH : minorTickH;
				Color col = isMajor ? MajorTickColor : MinorTickColor;

				// Draw tick line extending upward from bottom border of top ruler
				Rect tickRect = new Rect(screenX - tickW * 0.5f, s_TopRulerRect.yMax - h, tickW, h);
				EditorGUI.DrawRect(tickRect, col);

				// Draw label for major ticks (direction-independent gap check)
				if (isMajor && (float.IsNaN(lastLabelX) || Mathf.Abs(screenX - lastLabelX) >= minLabelGap))
				{
					string labelText = GetCachedIntString(dispTick);
					GUI.Label(new Rect(screenX + 2f, s_TopRulerRect.y + 1f, 50f, 13f), labelText, RulerLabelStyle);
					lastLabelX = screenX;
				}
			}
		}

		// ─── Vertical Ticks Drawing ──────────────────────────────────────────
		private static void DrawVerticalTicks(
			float halfH, float Spx, int minorStep, int majorStep, RulerCoordinateOrigin origin, float ppp)
		{
			float majorTickW = 6f;
			float minorTickW = 3f;
			float tickH      = 1f;
			float minLabelGap = 35f;
			float lastLabelY = float.NaN;

			// Visible local range across left ruler
			float visibleStartLocal = ScreenToCanvasLocalY(s_LeftRulerRect.yMin, halfH);
			float visibleEndLocal   = ScreenToCanvasLocalY(s_LeftRulerRect.yMax, halfH);

			// Convert range to display coordinates so ticks step in the active origin's coordinate system
			float startDisp = ToDisplayCoord(visibleStartLocal, halfH, origin, true);
			float endDisp   = ToDisplayCoord(visibleEndLocal, halfH, origin, true);

			float minDisp = Mathf.Min(startDisp, endDisp);
			float maxDisp = Mathf.Max(startDisp, endDisp);

			int startTick = Mathf.FloorToInt(minDisp / minorStep) * minorStep;
			int endTick   = Mathf.CeilToInt(maxDisp / minorStep) * minorStep;

			startTick = Mathf.Max(startTick, -50000);
			endTick   = Mathf.Min(endTick, 50000);

			for (int dispTick = startTick; dispTick <= endTick; dispTick += minorStep)
			{
				float localY = FromDisplayCoord(dispTick, halfH, origin, true);
				float screenY = Mathf.Round(CanvasLocalToScreenY(localY, halfH));
				if (screenY < s_LeftRulerRect.yMin || screenY > s_LeftRulerRect.yMax)
					continue;

				bool isMajor = (dispTick % majorStep) == 0;
				float w = isMajor ? majorTickW : minorTickW;
				Color col = isMajor ? MajorTickColor : MinorTickColor;

				// Draw tick line extending leftward from right border of left ruler
				Rect tickRect = new Rect(s_LeftRulerRect.xMax - w, screenY - tickH * 0.5f, w, tickH);
				EditorGUI.DrawRect(tickRect, col);

				// Draw label for major ticks rotated -90 deg along the vertical ruler rail
				if (isMajor && (float.IsNaN(lastLabelY) || Mathf.Abs(screenY - lastLabelY) >= minLabelGap))
				{
					string labelText = GetCachedIntString(dispTick);

					Matrix4x4 savedMatrix = GUI.matrix;
					float railCenterX = s_LeftRulerRect.x + (s_LeftRulerRect.width - majorTickW) * 0.5f;
					Vector2 pivot = new Vector2(railCenterX, screenY + 2f);
					GUIUtility.RotateAroundPivot(-90f, pivot);
					GUI.Label(new Rect(pivot.x - 45f, pivot.y - 5f, 45f, 10f), labelText, VerticalRulerLabelStyle);
					GUI.matrix = savedMatrix;

					lastLabelY = screenY;
				}
			}
		}

		// ─── Selected Anchor Notch Pips ─────────────────────────────────────
		private static void DrawSelectedAnchorNotches(
			float halfW, float halfH, float localWidth, float localHeight, float ppp)
		{
			var activeGO = Selection.activeGameObject;
			if (activeGO == null) return;

			var selectedRT = activeGO.GetComponent<RectTransform>();
			if (selectedRT == null || s_CachedCanvasRect == null) return;

			// Do not project canvas itself onto its own ruler
			if (selectedRT == s_CachedCanvasRect) return;

			RectTransform parentRT = selectedRT.parent as RectTransform ?? s_CachedCanvasRect;
			float parentW = parentRT.rect.width;
			float parentH = parentRT.rect.height;

			Vector3[] parentCorners = new Vector3[4];
			parentRT.GetWorldCorners(parentCorners);
			Vector3 parentBL_local = s_CachedCanvasRect.InverseTransformPoint(parentCorners[0]);
			float parentOffsetX = parentBL_local.x + parentW * 0.5f;
			float parentOffsetY = parentBL_local.y + parentH * 0.5f;

			float aMinX = (selectedRT.anchorMin.x * parentW) - (parentW * 0.5f) + parentOffsetX;
			float aMaxX = (selectedRT.anchorMax.x * parentW) - (parentW * 0.5f) + parentOffsetX;
			float aMinY = (selectedRT.anchorMin.y * parentH) - (parentH * 0.5f) + parentOffsetY;
			float aMaxY = (selectedRT.anchorMax.y * parentH) - (parentH * 0.5f) + parentOffsetY;

			float pipSize = Mathf.Round(4f * ppp);

			// Top ruler notches (facing downward into canvas)
			float sMinX = CanvasLocalToScreenX(aMinX, halfW);
			float sMaxX = CanvasLocalToScreenX(aMaxX, halfW);

			if (sMinX >= s_TopRulerRect.xMin && sMinX <= s_TopRulerRect.xMax)
				EditorGUI.DrawRect(new Rect(sMinX - pipSize * 0.5f, s_TopRulerRect.yMax - pipSize, pipSize, pipSize), AnchorPipColor);

			if (Mathf.Abs(aMaxX - aMinX) > 0.5f && sMaxX >= s_TopRulerRect.xMin && sMaxX <= s_TopRulerRect.xMax)
			{
				EditorGUI.DrawRect(new Rect(sMaxX - pipSize * 0.5f, s_TopRulerRect.yMax - pipSize, pipSize, pipSize), AnchorPipColor);
				// Connecting amber hairline for stretched anchor
				float spanLeft  = Mathf.Max(s_TopRulerRect.xMin, Mathf.Min(sMinX, sMaxX));
				float spanRight = Mathf.Min(s_TopRulerRect.xMax, Mathf.Max(sMinX, sMaxX));
				if (spanRight > spanLeft)
					EditorGUI.DrawRect(new Rect(spanLeft, s_TopRulerRect.yMax - 2f * ppp, spanRight - spanLeft, 2f * ppp), new Color(AnchorPipColor.r, AnchorPipColor.g, AnchorPipColor.b, 0.4f));
			}

			// Left ruler notches (facing rightward into canvas)
			float sMinY = CanvasLocalToScreenY(aMinY, halfH);
			float sMaxY = CanvasLocalToScreenY(aMaxY, halfH);

			if (sMinY >= s_LeftRulerRect.yMin && sMinY <= s_LeftRulerRect.yMax)
				EditorGUI.DrawRect(new Rect(s_LeftRulerRect.xMax - pipSize, sMinY - pipSize * 0.5f, pipSize, pipSize), AnchorPipColor);

			if (Mathf.Abs(aMaxY - aMinY) > 0.5f && sMaxY >= s_LeftRulerRect.yMin && sMaxY <= s_LeftRulerRect.yMax)
			{
				EditorGUI.DrawRect(new Rect(s_LeftRulerRect.xMax - pipSize, sMaxY - pipSize * 0.5f, pipSize, pipSize), AnchorPipColor);
				float spanTop = Mathf.Max(s_LeftRulerRect.yMin, Mathf.Min(sMinY, sMaxY));
				float spanBot = Mathf.Min(s_LeftRulerRect.yMax, Mathf.Max(sMinY, sMaxY));
				if (spanBot > spanTop)
					EditorGUI.DrawRect(new Rect(s_LeftRulerRect.xMax - 2f * ppp, spanTop, 2f * ppp, spanBot - spanTop), new Color(AnchorPipColor.r, AnchorPipColor.g, AnchorPipColor.b, 0.4f));
			}

			// Project Pivot (Subtle purple notch dot)
			float pivLocalX = (selectedRT.pivot.x * parentW) - (parentW * 0.5f) + parentOffsetX;
			float pivLocalY = (selectedRT.pivot.y * parentH) - (parentH * 0.5f) + parentOffsetY;
			float sPivX = CanvasLocalToScreenX(pivLocalX, halfW);
			float sPivY = CanvasLocalToScreenY(pivLocalY, halfH);
			float pivDotSize = Mathf.Round(3f * ppp);
			if (sPivX >= s_TopRulerRect.xMin && sPivX <= s_TopRulerRect.xMax)
				EditorGUI.DrawRect(new Rect(sPivX - pivDotSize * 0.5f, s_TopRulerRect.yMax - pivDotSize, pivDotSize, pivDotSize), PivotPipColor);
			if (sPivY >= s_LeftRulerRect.yMin && sPivY <= s_LeftRulerRect.yMax)
				EditorGUI.DrawRect(new Rect(s_LeftRulerRect.xMax - pivDotSize, sPivY - pivDotSize * 0.5f, pivDotSize, pivDotSize), PivotPipColor);
		}

		// ─── Cursor Hairline Tracking ────────────────────────────────────────
		private static void DrawCursorTrackingHairlines(float ppp)
		{
			Vector2 mouse = Event.current.mousePosition;
			float hairlineThick = Mathf.Max(1f, Mathf.Round(1f * ppp));

			// Top ruler vertical hairline
			if (mouse.x >= s_TopRulerRect.xMin && mouse.x <= s_TopRulerRect.xMax)
			{
				EditorGUI.DrawRect(new Rect(mouse.x - hairlineThick * 0.5f, s_TopRulerRect.y, hairlineThick, s_TopRulerRect.height), CursorHairlineColor);
			}

			// Left ruler horizontal hairline
			if (mouse.y >= s_LeftRulerRect.yMin && mouse.y <= s_LeftRulerRect.yMax)
			{
				EditorGUI.DrawRect(new Rect(s_LeftRulerRect.x, mouse.y - hairlineThick * 0.5f, s_LeftRulerRect.width, hairlineThick), CursorHairlineColor);
			}
		}

		// ─── Corner Origin Box & HUD ─────────────────────────────────────────
		private static void DrawCornerOriginBox(float halfW, float halfH, RulerCoordinateOrigin origin, float ppp)
		{
			Vector2 mouse = Event.current.mousePosition;
			bool isHovered = s_CornerRect.Contains(mouse);
			EditorGUI.DrawRect(s_CornerRect, isHovered ? CornerBtnHoverColor : CornerBtnBgColor);

			// Indicator badge for Origin Mode: C (Center), TL (Top-Left), BL (Bottom-Left)
			string originTag = origin == RulerCoordinateOrigin.CanvasCenter ? "C" :
			                   origin == RulerCoordinateOrigin.TopLeft      ? "TL" : "BL";

			GUI.Label(s_CornerRect, originTag, CornerBoxStyle);

			// If hovering over Corner Box, show rich tooltip with current coordinate status
			if (isHovered)
			{
				float mouseLocalX = ScreenToCanvasLocalX(mouse.x, halfW);
				float mouseLocalY = ScreenToCanvasLocalY(mouse.y, halfH);
				float dispX = ToDisplayCoord(mouseLocalX, halfW, origin, false);
				float dispY = ToDisplayCoord(mouseLocalY, halfH, origin, true);
				string tip = $"Origin: {origin}\nClick to cycle (Center → TopLeft → BottomLeft)\nCursor: [X: {dispX:F0}px, Y: {dispY:F0}px]";
				GUI.Label(new Rect(s_CornerRect.xMax + 4f, s_CornerRect.yMax + 4f, 260f, 44f), tip, EditorStyles.helpBox);
			}
		}

		public static void SetSyntheticCornersForTesting(Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br)
		{
			s_TL_GUI = tl;
			s_TR_GUI = tr;
			s_BL_GUI = bl;
			s_BR_GUI = br;
		}

		public static float ScreenToCanvasLocalX(float screenX, float halfW)
		{
			float span = s_TR_GUI.x - s_TL_GUI.x;
			if (Mathf.Abs(span) < 0.001f) return 0f;
			float t = (screenX - s_TL_GUI.x) / span;
			return (t - 0.5f) * (halfW * 2f);
		}

		public static float CanvasLocalToScreenX(float localX, float halfW)
		{
			float totalW = halfW * 2f;
			if (totalW < 0.001f) return s_TL_GUI.x;
			float t = (localX / totalW) + 0.5f;
			return Mathf.LerpUnclamped(s_TL_GUI.x, s_TR_GUI.x, t);
		}

		public static float ScreenToCanvasLocalY(float screenY, float halfH)
		{
			float span = s_BL_GUI.y - s_TL_GUI.y;
			if (Mathf.Abs(span) < 0.001f) return 0f;
			float t = (screenY - s_TL_GUI.y) / span;
			return (0.5f - t) * (halfH * 2f); // GUI Y increases downward, local Y increases upward
		}

		public static float CanvasLocalToScreenY(float localY, float halfH)
		{
			float totalH = halfH * 2f;
			if (totalH < 0.001f) return s_TL_GUI.y;
			float t = 0.5f - (localY / totalH);
			return Mathf.LerpUnclamped(s_TL_GUI.y, s_BL_GUI.y, t);
		}
	}
}
#endif
