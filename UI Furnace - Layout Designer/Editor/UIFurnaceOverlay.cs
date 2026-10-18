#if UNITY_2021_2_OR_NEWER
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Native Unity Scene View Overlay for Layout Designer.
	/// Appears directly in Unity's native Scene View Overlay menu (under Custom).
	/// Can be docked anywhere in the Scene View or floated natively by Unity.
	/// Grouped logically into Status, Assist Toggles, Guide Tools, Danger Zone, and Finish actions.
	/// </summary>
	[Overlay(typeof(SceneView), "ui-furnace-layout-designer", "Layout Designer", defaultDisplay = true)]
	public class UIFurnaceOverlay : Overlay, ITransientOverlay
	{
		// Visible when grid display is enabled and either Edit Mode is active or a Canvas is selected
		public bool visible => GridSettings.ShowGrid && (GridSettings.IsEditModeActive || GridSettings.ActiveCanvas != null);

		// Color Tokens (Studio Aesthetic)
		private static readonly Color ColorActiveToggle = new Color(0.22f, 0.58f, 0.94f, 1f);      // Vibrant active blue
		private static readonly Color ColorDanger       = new Color(0.92f, 0.28f, 0.28f, 1f);      // Clear warning red
		private static readonly Color ColorSuccess      = new Color(0.22f, 0.72f, 0.38f, 1f);      // Confirmation green
		private static readonly Color ColorActiveAction  = new Color(0.98f, 0.65f, 0.15f, 1f);      // Amber placing indicator

		private static GUIStyle s_StatusPillStyle;
		private static GUIStyle s_ButtonNormalStyle;
		private static GUIStyle s_ButtonBoldStyle;

		private static void InitStyles()
		{
			if (s_StatusPillStyle == null)
			{
				s_StatusPillStyle = new GUIStyle(EditorStyles.label)
				{
					fontSize = 11,
					richText = true,
					alignment = TextAnchor.MiddleLeft,
					padding = new RectOffset(6, 6, 1, 1),
					margin = new RectOffset(0, 0, 0, 0)
				};
			}
			if (s_ButtonNormalStyle == null)
			{
				s_ButtonNormalStyle = new GUIStyle(EditorStyles.toolbarButton)
				{
					fontSize = 11,
					alignment = TextAnchor.MiddleCenter,
					padding = new RectOffset(6, 6, 2, 2)
				};
			}
			if (s_ButtonBoldStyle == null)
			{
				s_ButtonBoldStyle = new GUIStyle(EditorStyles.toolbarButton)
				{
					fontSize = 11,
					fontStyle = FontStyle.Bold,
					alignment = TextAnchor.MiddleCenter,
					padding = new RectOffset(8, 8, 2, 2)
				};
			}
		}

		private static void DrawVerticalSeparator()
		{
			GUILayout.Space(4);
			Rect r = GUILayoutUtility.GetRect(1f, 16f, GUILayout.Width(1f), GUILayout.Height(16f));
			r.y += 2f;
			Color sep = EditorGUIUtility.isProSkin
				? new Color(1f, 1f, 1f, 0.18f)
				: new Color(0f, 0f, 0f, 0.22f);
			EditorGUI.DrawRect(r, sep);
			GUILayout.Space(4);
		}

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Row,
					alignItems = Align.Center,
					flexShrink = 0,
					paddingBottom = 2,
					paddingTop = 2,
					paddingLeft = 2,
					paddingRight = 2
				}
			};

			var imgui = new IMGUIContainer(() =>
			{
				InitStyles();
				Color defaultBg = GUI.backgroundColor;

				GUILayout.BeginHorizontal();

				// ─────────────────────────────────────────────────────────────
				// 1. STATUS & MODE BADGE (Compact, no redundant counters)
				// ─────────────────────────────────────────────────────────────
				bool isEditing = GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive;
				string dotColorHex = isEditing ? "#FF9E1B" : "#38D468"; // Amber if editing, Green if active

				string detailText;
				Vector2 canvasSize = GridRenderer.GetCanvasSize();
				string resText = canvasSize.x > 0 ? $"{canvasSize.x:0}×{canvasSize.y:0}" : "";

				if (GridSettings.CurrentMode == GridMode.UniformGrid)
				{
					string modeSpec = GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing
						? $"{GridSettings.GridSpacingX:0.#}px"
						: $"{GridSettings.GridColumns}×{GridSettings.GridRows}";
					detailText = string.IsNullOrEmpty(resText) ? modeSpec : $"{resText}  <color=#666666>•</color>  {modeSpec}";
				}
				else
				{
					string type = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition ? "Fixed" : "Stretch";
					string modeTag = isEditing ? $"<b>EDIT</b> <color=#AAAAAA>({type})</color>" : $"Guides <color=#AAAAAA>({type})</color>";
					detailText = string.IsNullOrEmpty(resText) ? modeTag : $"{modeTag}  <color=#666666>•</color>  <color=#CCCCCC>{resText}</color>";
				}

				string tooltipInfo = string.IsNullOrEmpty(resText) ? "UI Furnace Active Grid" : $"Canvas Resolution: {resText} px";
				GUIContent statusContent = new GUIContent($"<color={dotColorHex}>●</color>  {detailText}", tooltipInfo);

				// Draw compact status pill
				Rect statusRect = GUILayoutUtility.GetRect(statusContent, s_StatusPillStyle, GUILayout.Height(20));
				Color pillBg = EditorGUIUtility.isProSkin
					? new Color(0f, 0f, 0f, 0.28f)
					: new Color(0f, 0f, 0f, 0.08f);
				EditorGUI.DrawRect(statusRect, pillBg);
				GUI.Label(statusRect, statusContent, s_StatusPillStyle);

				DrawVerticalSeparator();

				// ─────────────────────────────────────────────────────────────
				// 2. ASSIST TOGGLES (Snap & Mirror)
				// ─────────────────────────────────────────────────────────────
				// Snap Toggle (Contextual: Snap Lines to UI in Edit Mode, Snap UI to Grid in Normal Mode)
				bool curSnap = isEditing ? GridSettings.SnapToElements : GridSettings.SnapElementsToGrid;
				if (curSnap) GUI.backgroundColor = ColorActiveToggle;
				string snapLabel = isEditing ? "Snap (Lines)" : "Snap (UI)";
				string snapTooltip = isEditing 
					? "Snap grid line to UI" 
					: "Snap UI to grid line";
				bool newSnap = GUILayout.Toggle(curSnap, new GUIContent(snapLabel, snapTooltip), s_ButtonNormalStyle, GUILayout.Height(20));
				GUI.backgroundColor = defaultBg;
				if (newSnap != curSnap)
				{
					if (isEditing)
						GridSettings.SaveSnapSettings(newSnap, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
					else
						GridSettings.SaveSnapSettings(GridSettings.SnapToElements, newSnap, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
				}

				// Contextual Edit Controls
				if (isEditing)
				{
					GUILayout.Space(2);

					// Mirror Mode Toggle
					bool curMirror = GridSettings.IsMirrorModeActive;
					if (curMirror) GUI.backgroundColor = ColorActiveToggle;
					bool newMirror = GUILayout.Toggle(curMirror, new GUIContent("Mirror", "Place guides symmetrically across the canvas center"), s_ButtonNormalStyle, GUILayout.Height(20));
					GUI.backgroundColor = defaultBg;
					if (newMirror != curMirror)
					{
						GridSettings.IsMirrorModeActive = newMirror;
						GridSettings.TriggerSettingsChanged();
					}

					DrawVerticalSeparator();

					// ─────────────────────────────────────────────────────────
					// 3. CREATE ACTIONS (+ Horiz, + Vert)
					// ─────────────────────────────────────────────────────────
					bool isAdding = GridRenderer.IsAddingLine;
					bool isAddingV = GridRenderer.AddingLineIsVertical;

					// Add Horizontal Guide
					bool addingThisH = isAdding && !isAddingV;
					if (addingThisH) GUI.backgroundColor = ColorActiveAction;
					string hLabel = addingThisH ? "Cancel H" : "+ Horiz";
					string hTooltip = addingThisH ? "Click canvas to place, or click here to cancel" : "Click to interactively place a new horizontal guide";
					if (GUILayout.Button(new GUIContent(hLabel, hTooltip), s_ButtonNormalStyle, GUILayout.Height(20)))
					{
						if (addingThisH) GridRenderer.CancelAddingLine();
						else GridRenderer.StartAddingHorizontalLine();
					}
					GUI.backgroundColor = defaultBg;

					GUILayout.Space(2);

					// Add Vertical Guide
					bool addingThisV = isAdding && isAddingV;
					if (addingThisV) GUI.backgroundColor = ColorActiveAction;
					string vLabel = addingThisV ? "Cancel V" : "+ Vert";
					string vTooltip = addingThisV ? "Click canvas to place, or click here to cancel" : "Click to interactively place a new vertical guide";
					if (GUILayout.Button(new GUIContent(vLabel, vTooltip), s_ButtonNormalStyle, GUILayout.Height(20)))
					{
						if (addingThisV) GridRenderer.CancelAddingLine();
						else GridRenderer.StartAddingVerticalLine();
					}
					GUI.backgroundColor = defaultBg;

					DrawVerticalSeparator();

					// ─────────────────────────────────────────────────────────
					// 4. DESTRUCTIVE ACTION (Delete Selected)
					// ─────────────────────────────────────────────────────────
					int selCount = GridRenderer.SelectedIndicesX.Count + GridRenderer.SelectedIndicesY.Count;
					bool hasSelection = selCount > 0;
					GUI.enabled = hasSelection;
					if (hasSelection) GUI.backgroundColor = ColorDanger;

					string delLabel = hasSelection ? $"Delete ({selCount})" : "Delete";
					string delTooltip = hasSelection ? $"Delete the {selCount} selected guide line(s) (or press Delete)" : "Select one or more guides in Scene View to delete";
					if (GUILayout.Button(new GUIContent(delLabel, delTooltip), s_ButtonNormalStyle, GUILayout.Height(20)))
					{
						GridRenderer.DeleteSelectedLines();
					}
					GUI.backgroundColor = defaultBg;
					GUI.enabled = true;

					DrawVerticalSeparator();

					// ─────────────────────────────────────────────────────────
					// 5. FINISH ACTION (Done Editing)
					// ─────────────────────────────────────────────────────────
					GUI.backgroundColor = ColorSuccess;
					if (GUILayout.Button(new GUIContent("✓ Done", "Save guide positions and exit Edit Mode"), s_ButtonBoldStyle, GUILayout.Height(20)))
					{
						GridRenderer.ExitEditMode();
					}
					GUI.backgroundColor = defaultBg;
				}
				else
				{
					DrawVerticalSeparator();

					// ─────────────────────────────────────────────────────────
					// 6. MODE ACTIONS (When Not Editing)
					// ─────────────────────────────────────────────────────────
					if (GridSettings.CurrentMode == GridMode.CustomLines)
					{
						GUI.backgroundColor = ColorActiveToggle;
						if (GUILayout.Button(new GUIContent("✎ Edit Guides", "Enter Custom Lines Edit Mode to position or draw guides"), s_ButtonBoldStyle, GUILayout.Height(20)))
						{
							GridRenderer.ToggleEditMode();
						}
						GUI.backgroundColor = defaultBg;
					}
					else
					{
						if (GUILayout.Button(new GUIContent("⚙ Settings", "Open and focus Layout Designer Window"), s_ButtonNormalStyle, GUILayout.Height(20)))
						{
							LayoutDesignerWindow.ShowWindowAndHighlight();
						}
					}
				}

				GUILayout.EndHorizontal();
			})
			{
				style = { flexShrink = 0 }
			};

			root.Add(imgui);
			return root;
		}
	}
}
#endif
