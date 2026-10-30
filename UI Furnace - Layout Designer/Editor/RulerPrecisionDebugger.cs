#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Interactive in-scene debugging and precision verification tool for UI Furnace Canvas Edge Rulers.
	/// Provides real-time geometric inspections, sub-pixel round-trip verification,
	/// corner alignment probes, and laser projection markers.
	/// </summary>
	public static class RulerPrecisionDebugger
	{
		private static Rect s_DebugWindowRect = new Rect(20, 90, 360, 290);
		private static bool s_ShowLaserProbes = false;
		private static bool s_ShowCornerCrosshairs = false;
		private static bool s_ShowTestPins = false;
		private static readonly List<Vector2> s_TestPins = new List<Vector2>
		{
			new Vector2(0, 0),
			new Vector2(100, 100),
			new Vector2(-100, -100)
		};

		// ─── Menu Item to Toggle Overlay ─────────────────────────────────────
		[MenuItem("Tools/UI Furnace/Diagnostics/Toggle Ruler Precision Debugger &d", false, 10)]
		public static void ToggleDebugger()
		{
			GridSettings.ShowRulerDebugOverlay = !GridSettings.ShowRulerDebugOverlay;
			SceneView.RepaintAll();
		}

		// ─── Main On-Screen Drawing Entry Point ──────────────────────────────
		public static void DrawPrecisionOverlay(
			RectTransform canvasRect,
			Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float localWidth, float localHeight)
		{
			if (canvasRect == null) return;
			SceneView sv = SceneView.currentDrawingSceneView;
			if (sv == null) return;

			float halfW = localWidth * 0.5f;
			float halfH = localHeight * 0.5f;
			var origin = GridSettings.RulerOrigin;

			// 1. Draw Visual Laser Probes directly in Scene View World/GUI space
			if (s_ShowLaserProbes)
			{
				DrawLaserProbes(halfW, halfH, origin);
			}

			// 2. Draw Corner Crosshairs & Labels
			if (s_ShowCornerCrosshairs)
			{
				DrawCornerCrosshairs(BL, TL, TR, BR, halfW, halfH, origin);
			}

			// 3. Draw Test Pins
			if (s_ShowTestPins)
			{
				DrawTestPins(halfW, halfH, origin);
			}

			// 4. Draw Floating Precision Inspection HUD
			Handles.BeginGUI();
			DrawInspectorHUD(canvasRect, localWidth, localHeight, halfW, halfH, origin, sv);
			Handles.EndGUI();
		}

		// ─── Laser Probes: Projected lines from ruler ticks across canvas ────
		private static void DrawLaserProbes(float halfW, float halfH, RulerCoordinateOrigin origin)
		{
			var prevZTest = Handles.zTest;
			try
			{
				Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

				// Origin Probe (0, 0)
				float originLocalX = GridRenderer.FromDisplayCoord(0f, halfW, origin, false);
				float originLocalY = GridRenderer.FromDisplayCoord(0f, halfH, origin, true);

				float originScreenX = Mathf.Round(GridRenderer.CanvasLocalToScreenX(originLocalX, halfW));
				float originScreenY = Mathf.Round(GridRenderer.CanvasLocalToScreenY(originLocalY, halfH));

				// Draw 0-coordinate lasers (Cyan / Emerald)
				Color originColor = new Color(0.0f, 1.0f, 0.65f, 0.85f);
				Handles.color = originColor;

				Vector3 worldOrigin = ScreenToWorldOnCanvas(new Vector2(originScreenX, originScreenY));
				Vector3 worldOriginTop = ScreenToWorldOnCanvas(new Vector2(originScreenX, GridRenderer.s_TopRulerRect.yMax));
				Vector3 worldOriginLeft = ScreenToWorldOnCanvas(new Vector2(GridRenderer.s_LeftRulerRect.xMax, originScreenY));

				if (originScreenX >= GridRenderer.s_TopRulerRect.xMin && originScreenX <= GridRenderer.s_TopRulerRect.xMax)
				{
					Handles.DrawDottedLine(
						ScreenToWorldOnCanvas(new Vector2(originScreenX, 0)),
						ScreenToWorldOnCanvas(new Vector2(originScreenX, SceneView.currentDrawingSceneView.position.height)),
						3f);
				}

				if (originScreenY >= GridRenderer.s_LeftRulerRect.yMin && originScreenY <= GridRenderer.s_LeftRulerRect.yMax)
				{
					Handles.DrawDottedLine(
						ScreenToWorldOnCanvas(new Vector2(0, originScreenY)),
						ScreenToWorldOnCanvas(new Vector2(SceneView.currentDrawingSceneView.position.width, originScreenY)),
						3f);
				}
			}
			finally
			{
				Handles.zTest = prevZTest;
			}
		}

		// ─── Corner Crosshair Badges ─────────────────────────────────────────
		private static void DrawCornerCrosshairs(
			Vector3 BL, Vector3 TL, Vector3 TR, Vector3 BR,
			float halfW, float halfH, RulerCoordinateOrigin origin)
		{
			Color markerCol = new Color(1f, 0.3f, 0.8f, 0.95f);
			Handles.color = markerCol;

			void DrawCross(Vector3 worldPos, string label, float expectedDispX, float expectedDispY)
			{
				Vector2 guiPt = HandleUtility.WorldToGUIPoint(worldPos);
				float size = 8f;
				Handles.DrawLine(worldPos - Vector3.right * 0.2f, worldPos + Vector3.right * 0.2f);
				Handles.DrawLine(worldPos - Vector3.up * 0.2f, worldPos + Vector3.up * 0.2f);

				Handles.BeginGUI();
				Rect labelRect = new Rect(guiPt.x + 6f, guiPt.y - 10f, 180f, 24f);
				EditorGUI.DrawRect(new Rect(guiPt.x - 1f, guiPt.y - size, 2f, size * 2f), markerCol);
				EditorGUI.DrawRect(new Rect(guiPt.x - size, guiPt.y - 1f, size * 2f, 2f), markerCol);

				string text = $"{label}\nDisp: ({expectedDispX:F0}, {expectedDispY:F0})";
				GUI.Label(labelRect, text, EditorStyles.boldLabel);
				Handles.EndGUI();
			}

			// TL Corner
			DrawCross(TL, "Top-Left", GridRenderer.ToDisplayCoord(-halfW, halfW, origin, false), GridRenderer.ToDisplayCoord(halfH, halfH, origin, true));
			// TR Corner
			DrawCross(TR, "Top-Right", GridRenderer.ToDisplayCoord(halfW, halfW, origin, false), GridRenderer.ToDisplayCoord(halfH, halfH, origin, true));
			// BL Corner
			DrawCross(BL, "Bottom-Left", GridRenderer.ToDisplayCoord(-halfW, halfW, origin, false), GridRenderer.ToDisplayCoord(-halfH, halfH, origin, true));
			// BR Corner
			DrawCross(BR, "Bottom-Right", GridRenderer.ToDisplayCoord(halfW, halfW, origin, false), GridRenderer.ToDisplayCoord(-halfH, halfH, origin, true));
			// Center
			Vector3 center = (BL + TR) * 0.5f;
			DrawCross(center, "Center", GridRenderer.ToDisplayCoord(0f, halfW, origin, false), GridRenderer.ToDisplayCoord(0f, halfH, origin, true));
		}

		// ─── Test Pins ───────────────────────────────────────────────────────
		private static void DrawTestPins(float halfW, float halfH, RulerCoordinateOrigin origin)
		{
			Handles.BeginGUI();
			Color pinColor = new Color(0.2f, 0.85f, 1.0f, 1.0f);

			for (int i = 0; i < s_TestPins.Count; i++)
			{
				Vector2 pinDisp = s_TestPins[i];
				float localX = GridRenderer.FromDisplayCoord(pinDisp.x, halfW, origin, false);
				float localY = GridRenderer.FromDisplayCoord(pinDisp.y, halfH, origin, true);

				float screenX = Mathf.Round(GridRenderer.CanvasLocalToScreenX(localX, halfW));
				float screenY = Mathf.Round(GridRenderer.CanvasLocalToScreenY(localY, halfH));

				// Pin dot
				EditorGUI.DrawRect(new Rect(screenX - 4f, screenY - 4f, 8f, 8f), pinColor);
				GUI.Label(new Rect(screenX + 6f, screenY - 8f, 140f, 20f), $"Pin #{i+1}: ({pinDisp.x:F0}, {pinDisp.y:F0})", EditorStyles.miniBoldLabel);
			}
			Handles.EndGUI();
		}

		// ─── Floating Precision Inspector Window ─────────────────────────────
		private static void DrawInspectorHUD(
			RectTransform canvasRect, float localWidth, float localHeight,
			float halfW, float halfH, RulerCoordinateOrigin origin, SceneView sv)
		{
			s_DebugWindowRect.x = Mathf.Clamp(s_DebugWindowRect.x, 10f, sv.position.width - s_DebugWindowRect.width - 10f);
			s_DebugWindowRect.y = Mathf.Clamp(s_DebugWindowRect.y, 10f, sv.position.height - s_DebugWindowRect.height - 10f);

			s_DebugWindowRect = GUI.Window(
				"UIFurnaceRulerDebugger".GetHashCode(),
				s_DebugWindowRect,
				id => DrawDebugWindowContent(canvasRect, localWidth, localHeight, halfW, halfH, origin),
				"UI Furnace — Ruler Precision Diagnostics"
			);
		}

		private static void DrawDebugWindowContent(
			RectTransform canvasRect, float localWidth, float localHeight,
			float halfW, float halfH, RulerCoordinateOrigin origin)
		{
			Event e = Event.current;
			Vector2 mouse = e != null ? e.mousePosition : Vector2.zero;

			// Live Cursor Coordinate Calculations
			float mouseLocalX = GridRenderer.ScreenToCanvasLocalX(mouse.x, halfW);
			float mouseLocalY = GridRenderer.ScreenToCanvasLocalY(mouse.y, halfH);

			float mouseDispX = GridRenderer.ToDisplayCoord(mouseLocalX, halfW, origin, false);
			float mouseDispY = GridRenderer.ToDisplayCoord(mouseLocalY, halfH, origin, true);

			// Round-trip Precision Check: Screen -> Local -> Screen
			float roundtripScreenX = GridRenderer.CanvasLocalToScreenX(mouseLocalX, halfW);
			float roundtripScreenY = GridRenderer.CanvasLocalToScreenY(mouseLocalY, halfH);
			float errorDeltaX = Mathf.Abs(roundtripScreenX - mouse.x);
			float errorDeltaY = Mathf.Abs(roundtripScreenY - mouse.y);
			float maxError = Mathf.Max(errorDeltaX, errorDeltaY);

			// Origin (0,0) Alignment Check
			float originLocalX = GridRenderer.FromDisplayCoord(0f, halfW, origin, false);
			float originLocalY = GridRenderer.FromDisplayCoord(0f, halfH, origin, true);
			float originScreenX = GridRenderer.CanvasLocalToScreenX(originLocalX, halfW);
			float originScreenY = GridRenderer.CanvasLocalToScreenY(originLocalY, halfH);

			// Compute Spx
			Vector2 tlGUI = HandleUtility.WorldToGUIPoint(canvasRect.TransformPoint(new Vector3(-halfW, halfH, 0)));
			Vector2 trGUI = HandleUtility.WorldToGUIPoint(canvasRect.TransformPoint(new Vector3(halfW, halfH, 0)));
			float Spx = Mathf.Abs(trGUI.x - tlGUI.x) / Mathf.Max(localWidth, 0.001f);

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			// Status banner
			bool isPrecisionPerfect = maxError < 0.001f;
			GUI.color = isPrecisionPerfect ? new Color(0.2f, 1f, 0.4f) : Color.red;
			GUILayout.Label(isPrecisionPerfect ? "● MATHEMATICAL PRECISION: 100% PERFECT (0.000 px error)" : "▲ PRECISION DRIFT DETECTED", EditorStyles.boldLabel);
			GUI.color = Color.white;

			EditorGUILayout.Space(2);
			EditorGUILayout.LabelField("Canvas Size", $"{localWidth:F1} x {localHeight:F1} px (Aspect: {localWidth/Mathf.Max(localHeight,1):F2})");
			EditorGUILayout.LabelField("Zoom / Screen Scale", $"{Spx * 100f:F1}% (Spx: {Spx:F4} GUI px/unit)");
			EditorGUILayout.LabelField("Active Origin Mode", $"{origin}");

			EditorGUILayout.Space(4);
			EditorGUILayout.LabelField("Mouse GUI Point", $"({mouse.x:F1}, {mouse.y:F1}) pt");
			EditorGUILayout.LabelField("Canvas Local Coords", $"({mouseLocalX:F3}, {mouseLocalY:F3}) px");
			EditorGUILayout.LabelField("Ruler Display Coords", $"X: {mouseDispX:F2} px,  Y: {mouseDispY:F2} px", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Round-trip Invariance", $"ΔX = {errorDeltaX:E2} px, ΔY = {errorDeltaY:E2} px");
			EditorGUILayout.LabelField("Origin (0,0) Screen Pt", $"({originScreenX:F1}, {originScreenY:F1}) pt");

			EditorGUILayout.EndVertical();

			EditorGUILayout.Space(4);
			EditorGUILayout.BeginHorizontal();
			s_ShowLaserProbes = GUILayout.Toggle(s_ShowLaserProbes, "Laser Probes", "Button");
			s_ShowCornerCrosshairs = GUILayout.Toggle(s_ShowCornerCrosshairs, "Corners", "Button");
			s_ShowTestPins = GUILayout.Toggle(s_ShowTestPins, "Test Pins", "Button");
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Origin: Center", EditorStyles.miniButtonLeft)) GridSettings.RulerOrigin = RulerCoordinateOrigin.CanvasCenter;
			if (GUILayout.Button("Origin: TopLeft", EditorStyles.miniButtonMid)) GridSettings.RulerOrigin = RulerCoordinateOrigin.TopLeft;
			if (GUILayout.Button("Origin: BottomLeft", EditorStyles.miniButtonRight)) GridSettings.RulerOrigin = RulerCoordinateOrigin.BottomLeft;
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(4);
			if (GUILayout.Button("Run Full Automated Diagnostics Suite", GUILayout.Height(24)))
			{
				RulerPrecisionValidator.RunAllDiagnostics();
			}

			GUI.DragWindow(new Rect(0, 0, 10000, 20));
		}

		private static Vector3 ScreenToWorldOnCanvas(Vector2 guiPoint)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
			if (GridRenderer.ActiveCanvasRectTransform != null)
			{
				Plane canvasPlane = new Plane(-GridRenderer.ActiveCanvasRectTransform.forward, GridRenderer.ActiveCanvasRectTransform.position);
				if (canvasPlane.Raycast(ray, out float enter))
					return ray.GetPoint(enter);
			}
			return ray.GetPoint(10f);
		}
	}
}
#endif
