#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Scene View floating HUDs, position badges, and canvas spec overlays for Layout Designer.
	/// </summary>
	public static partial class GridRenderer
	{
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

		// #7 â€” Modern In-Scene Dual-Bar HUD
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
			GUILayout.Label(new GUIContent("â ¿", "Drag to reposition toolbar"), EditorStyles.miniLabel, GUILayout.Width(14));

			// Green LED Status Dot
			var defColor = GUI.color;
			GUI.color = new Color(0.2f, 0.85f, 0.4f, 1f);
			GUILayout.Label("â—", EditorStyles.boldLabel, GUILayout.Width(12));
			GUI.color = defColor;

			// Title Label
			GUILayout.Label(s_HUDCollapsed ? "UI Furnace" : "UI Furnace (Edit)", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();

			// Collapse / Expand Button
			string collapseIcon = s_HUDCollapsed ? "â–¸" : "â€”";
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

			// 2. Solid dark pill background â€” guaranteed high contrast on white backgrounds
			EditorGUI.DrawRect(badgeRect, new Color(0.12f, 0.12f, 0.14f, 0.94f));

			// 3. Crisp border â€” ensures visibility against dark backgrounds
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
					specText = $"Grid: {GridSettings.GridSpacingX:0.#} Ã— {GridSettings.GridSpacingY:0.#} px";
				else
					specText = $"Layout: {GridSettings.GridColumns} Col Ã— {GridSettings.GridRows} Row";
			}
			else
			{
				if (GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas)
					specText = $"Guides: {GridSettings.DynamicStretchX.Count}V | {GridSettings.DynamicStretchY.Count}H (Stretch)";
				else
					specText = $"Guides: {GridSettings.DynamicFixedX.Count}V | {GridSettings.DynamicFixedY.Count}H (Fixed)";
			}

			string fullText = $"{localWidth:0} Ã— {localHeight:0} px  â€¢  {specText}";
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
			GUILayout.Label(new GUIContent("â ¿", "Drag to reposition Canvas Info"), EditorStyles.miniLabel, GUILayout.Width(12), GUILayout.Height(16));

			// Text
			SpecHUDTextStyle.normal.textColor = EditorGUIUtility.isProSkin ? new Color(0.9f, 0.9f, 0.92f, 1f) : new Color(0.12f, 0.12f, 0.14f, 1f);
			GUILayout.Label(textContent, SpecHUDTextStyle, GUILayout.Height(16));

			GUILayout.FlexibleSpace();

			// Hide Button
			if (GUILayout.Button(new GUIContent("âœ•", "Hide Canvas Info"), EditorStyles.miniLabel, GUILayout.Width(14), GUILayout.Height(16)))
			{
				GridSettings.ShowCanvasSpecHUD = false;
				if (SceneView.currentDrawingSceneView != null) SceneView.currentDrawingSceneView.Repaint();
			}

			GUILayout.EndHorizontal();

			// Drag window
			GUI.DragWindow(new Rect(0, 0, totalRect.width - 20f, totalRect.height));
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
				: (Mathf.Clamp01(rawOffset) - 0.5f) * localSize;   // stretch t -> pixels from center
			string sign = px >= 0 ? "+" : "";

			DrawPositionBadge(mouseWorldPos, $"{axis}: {sign}{px:F0} px");
		}

	}
}
#endif
