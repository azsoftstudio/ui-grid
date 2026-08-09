#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Globalization;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Main editor window for grid management.
	/// </summary>
	public class GridManagerWindow : EditorWindow
	{
		private static GridManagerWindow _window;
		internal static GridManagerWindow Instance => _window;
		private VisualElement _layoutCardSlot;

		[MenuItem("Tools/UI Furnace/Grid Manager", false, 0)]
		public static void ShowWindow()
		{
			_window = GetWindow<GridManagerWindow>("Grid Manager");
			_window.minSize = new Vector2(300, 300);

			var icon = EditorGUIUtility.IconContent("d_GridLayoutGroup Icon");
			_window.titleContent = new GUIContent("Grid Manager", icon?.image);
		}

		private void OnDestroy()
		{
			if (_window == this) _window = null;
		}

		public void CreateGUI()
		{
			GridSettings.LoadFromAsset();
			rootVisualElement.Clear();

			var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
			rootVisualElement.Add(scroll);

			var root = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Column,
					paddingLeft = 8, paddingRight = 8,
					paddingTop = 8, paddingBottom = 8,
					minWidth = 280
				}
			};
			scroll.Add(root);

			// Title row — label + ? help button
			var titleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.Center, marginBottom = 8 } };
			titleRow.Add(new Label("Grid Manager")
			{
				style =
				{
					unityFontStyleAndWeight = FontStyle.Bold,
					fontSize = 16,
					unityTextAlign = TextAnchor.MiddleCenter,
				}
			});
			var helpBtn = new Button(() =>
			{
				string pdfPath = System.IO.Path.Combine(
					System.IO.Path.GetDirectoryName(UnityEditor.AssetDatabase.GetAssetPath(
						MonoScript.FromScriptableObject(GridSettingsAsset.instance))),
					".." , "Documentation", "Documentation.pdf");
				pdfPath = System.IO.Path.GetFullPath(pdfPath);
				if (System.IO.File.Exists(pdfPath))
					UnityEditor.EditorUtility.OpenWithDefaultApp(pdfPath);
				else
					UnityEditor.EditorUtility.DisplayDialog("Documentation", "Could not locate Documentation.pdf. Please check the Documentation folder inside the asset.", "OK");
			})
			{
				text = "?",
				tooltip = "Open the full documentation PDF",
				style = { width = 20, height = 20, marginLeft = 8, paddingLeft = 0, paddingRight = 0, paddingTop = 0, paddingBottom = 0 }
			};
			titleRow.Add(helpBtn);
			root.Add(titleRow);

			// "No canvas selected" empty state + World Space warning
			var contextBox = new IMGUIContainer(() =>
			{
				if (GridRenderer.IsWorldSpaceCanvas)
					EditorGUILayout.HelpBox("World Space canvases don't work with this grid. Please click on any UI element inside a normal Screen Space canvas first.", MessageType.Info);
				else if (GridRenderer.GetCanvasSize() == Vector2.zero && !GridRenderer.IsWorldSpaceCanvas)
					EditorGUILayout.HelpBox("Click on any UI element in your canvas to turn on the grid.", MessageType.Info);
			});
			root.Add(contextBox);

			// =========================================================================
			// 1. GLOBAL SETTINGS (Unburied)
			// =========================================================================
			var visRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginTop = 4, marginBottom = 12 } };
			var showGridToggle = new Toggle("Show Grid") { value = GridSettings.ShowGrid, style = { unityFontStyleAndWeight = FontStyle.Bold } };
			showGridToggle.RegisterValueChangedCallback(evt => { GridSettings.SaveVisibility(evt.newValue); SceneView.RepaintAll(); });
			visRow.Add(showGridToggle);
			visRow.Add(new Label("Alt+G") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
			root.Add(visRow);


			// =========================================================================
			// 2. MAIN SETTINGS (The Core Grid)
			// =========================================================================
			_layoutCardSlot = new VisualElement();
			_layoutCardSlot.Add(CreateGridLayoutCard());
			root.Add(_layoutCardSlot);

			// =========================================================================
			// 3. UI INTERACTION (Snapping UI Elements)
			// =========================================================================
			var interactionFoldout = new Foldout { text = "UI Interaction", value = !GridSettings.InteractionCollapsed, style = { marginTop = 8 } };
			interactionFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Interaction", !evt.newValue));
			root.Add(interactionFoldout);

			var interactionGroup = new VisualElement { style = { marginLeft = 15 } };
			
			var gridSnapToggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			var gridSnapToggleObj = new Toggle("Snap UI Elements to Grid") { value = GridSettings.SnapElementsToGrid, tooltip = "When you drag your UI elements around, they will magnetically snap to your grid lines!" };
			gridSnapToggleObj.RegisterValueChangedCallback(evt => GridSettings.SaveSnapSettings(GridSettings.SnapToElements, evt.newValue, GridSettings.SnapDistance, GridSettings.ElementSnapDistance));
			gridSnapToggleRow.Add(gridSnapToggleObj);
			gridSnapToggleRow.Add(new Label("Alt+Shift+S") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
			interactionGroup.Add(gridSnapToggleRow);

			var elemSnapDistField = GridUIHelpers.AddFloatFieldWithRef(interactionGroup, "Element Snap Dist.", "", GridSettings.ElementSnapDistance, (val) => 
				{ GridSettings.SaveSnapSettings(GridSettings.SnapToElements, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, Mathf.Clamp(val, 0f, 100f)); });
			if (elemSnapDistField != null) elemSnapDistField.tooltip = "How close does the UI element need to get before it snaps to the grid? 3 to 10 pixels is usually best.";

			interactionGroup.Add(new Label("This makes moving UI elements easy—they will magnetically snap straight to your grid lines!")
			{
				style = { fontSize = 10, opacity = 0.6f, unityFontStyleAndWeight = FontStyle.Italic, whiteSpace = WhiteSpace.Normal, marginTop = 4 }
			});

			interactionFoldout.Add(interactionGroup);

			// =========================================================================
			// 4. GRID EDITING (Mode-Specific Editing Tools)
			// =========================================================================
			var editingFoldout = new Foldout { text = "Grid Editing", value = !GridSettings.EditingCollapsed, style = { marginTop = 8 } };
			editingFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Editing", !evt.newValue));
			root.Add(editingFoldout);

			var editingGroup = new VisualElement { style = { marginLeft = 15 } };
			editingFoldout.Add(editingGroup);

			if (GridSettings.CurrentMode == GridMode.UniformGrid)
			{
				if (GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
					CreateFixedSpacingFields(editingGroup);
				else
					CreateStretchFields(editingGroup);
			}
			else if (GridSettings.CurrentMode == GridMode.CustomLines)
			{
				CreateDynamicFields(editingGroup);

				editingGroup.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginTop = 8, marginBottom = 8 } });

				var subToolsGroup = new VisualElement()
				{
					style =
					{
						marginLeft = 8, flexDirection = FlexDirection.Column,
						borderLeftWidth = 2, borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.3f), paddingLeft = 8
					}
				};

				void ApplyEditModeStyle(bool active)
				{
					subToolsGroup.style.opacity = active ? 1f : 0.4f;
					GridUIHelpers.SetChildPickingMode(subToolsGroup, active ? PickingMode.Position : PickingMode.Ignore);
				}

				var editRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
				var editToggle = new Toggle("Edit Grid Lines") { value = GridSettings.IsEditModeActive, tooltip = "Turns on Editing Mode. This lets you add, move, or delete your custom grid lines right inside the Scene view." };
				editToggle.RegisterValueChangedCallback(evt => { 
					GridSettings.IsEditModeActive = evt.newValue; 
					ApplyEditModeStyle(evt.newValue);
					SceneView.RepaintAll(); 
				});
				editRow.Add(editToggle);
				editRow.Add(new Label("Alt+E") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
				editingGroup.Add(editRow);

				var addLinesRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var addLinesToggle = new Toggle("[Grid] Add Lines") { value = GridRenderer.IsAddLinesMode, tooltip = "Turn this on to click and drag new lines straight out of the center crosshair in your Scene view!" };
				addLinesToggle.RegisterValueChangedCallback(evt => { GridRenderer.IsAddLinesMode = evt.newValue; SceneView.RepaintAll(); });
				addLinesRow.Add(addLinesToggle);
				subToolsGroup.Add(addLinesRow);

				var mirrorRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var mirrorToggle = new Toggle("[Grid] Mirror Mode") { value = GridSettings.IsMirrorModeActive, tooltip = "Turn this on to work symmetrically. When you pull a line to the right, a matching line will automatically appear on the left!" };
				mirrorToggle.RegisterValueChangedCallback(evt => { GridSettings.IsMirrorModeActive = evt.newValue; SceneView.RepaintAll(); });
				mirrorRow.Add(mirrorToggle);
				mirrorRow.Add(new Label("Alt+M") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
				subToolsGroup.Add(mirrorRow);

				var deleteRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
				var deleteBtn = new Button(GridRenderer.DeleteSelectedLines) { text = "Delete Selected", style = { height = 20, flexGrow = 1, marginRight = 8 } };
				deleteRow.Add(deleteBtn);
				deleteRow.Add(new Label("Alt+Bksp") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
				subToolsGroup.Add(deleteRow);

				subToolsGroup.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginBottom = 8 } });

				var snapToggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var snapToggleObj = new Toggle("Snap Lines to UI") { value = GridSettings.SnapToElements, tooltip = "When you drag a grid line, it will magnetically snap to the edges of your UI elements, making it super easy to align things!" };
				snapToggleObj.RegisterValueChangedCallback(evt => GridSettings.SaveSnapSettings(evt.newValue, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance));
				snapToggleRow.Add(snapToggleObj);
				snapToggleRow.Add(new Label("Alt+S") { style = { fontSize = 10, unityFontStyleAndWeight = FontStyle.Italic, opacity = 0.6f } });
				subToolsGroup.Add(snapToggleRow);

				var snapDistField = GridUIHelpers.AddFloatFieldWithRef(subToolsGroup, "Line Snap Distance", "", GridSettings.SnapDistance, (val) => 
					{ GridSettings.SaveSnapSettings(GridSettings.SnapToElements, GridSettings.SnapElementsToGrid, Mathf.Clamp(val, 0f, 100f), GridSettings.ElementSnapDistance); });
				if (snapDistField != null) snapDistField.tooltip = "How close does your mouse need to get before the line snaps to the UI? 3 to 10 pixels is usually a good setting.";

				ApplyEditModeStyle(GridSettings.IsEditModeActive);
				editingGroup.Add(subToolsGroup);
			}

			// =========================================================================
			// 5. VISUALS & APPEARANCE (The Look)
			// =========================================================================
			var visualsFoldout = new Foldout { text = "Visuals & Appearance", value = !GridSettings.VisualsCollapsed, style = { marginTop = 8 } };
			visualsFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Visuals", !evt.newValue));
			root.Add(visualsFoldout);

			var visualsContainer = new VisualElement { style = { marginLeft = 15 } };
			visualsFoldout.Add(visualsContainer);

			GridUIHelpers.AddOpacitySlider(visualsContainer, GridSettings.GridOpacity, val =>
			{
				GridSettings.GridOpacity = val;
				GridSettings.ScheduleDelayedSave("GridOpacity", () => { GridSettingsAsset.instance.GridOpacity = val; });
				SceneView.RepaintAll();
			}, onBlur: () => GridSettings.FlushPendingSaves());

			visualsContainer.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginTop = 4, marginBottom = 6 } });

			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "Grid",   "GridColor",  () => GridSettings.GridColor,  c => GridSettings.GridColor  = c, new Color(0.3f, 0.7f, 1f, 0.25f));
			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "X Axis", "XAxisColor", () => GridSettings.XAxisColor, c => GridSettings.XAxisColor = c, new Color(1f, 0.3f, 0.3f, 0.8f));
			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "Y Axis", "YAxisColor", () => GridSettings.YAxisColor, c => GridSettings.YAxisColor = c, new Color(0.3f, 1f, 0.3f, 0.8f));

			visualsContainer.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginTop = 4, marginBottom = 6 } });

			GridUIHelpers.AddFloatField(visualsContainer, "Axis Thickness",  "AxisThickness", GridSettings.AxisThickness, val =>
			{
				GridSettings.AxisThickness = Mathf.Max(0.1f, val);
				SceneView.RepaintAll();
			});
			GridUIHelpers.AddFloatField(visualsContainer, "Grid Thickness",  "GridThickness", GridSettings.GridThickness, val =>
			{
				GridSettings.GridThickness = Mathf.Max(0.1f, val);
				SceneView.RepaintAll();
			});


			// Footer & Reset
			root.Add(new Label($"UI Furnace - Grid Guide (Lite) v{GridSettings.Version}")
			{
				style =
				{
					fontSize = 10, unityTextAlign = TextAnchor.MiddleCenter,
					alignSelf = Align.Center, opacity = 0.5f, marginTop = 20
				}
			});

			var resetBtn = new Button(() =>
			{
				if (EditorUtility.DisplayDialog(
					"Reset everything to default?",
					"This puts all the colors, spacing, and toggles back to how they were when you first installed the tool.\n\nDon't worry, we won't delete your custom lines!\n\nYou can also undo this by pressing Ctrl+Z.",
					"Reset", "Cancel"))
				{
					GridSettings.ResetAllToDefaults();
					CreateGUI();
				}
			})
			{
				text = "Reset All Settings",
				tooltip = "Click to put all settings back to default. Your custom lines are safe!",
				style = { marginTop = 8, height = 22 }
			};
			root.Add(resetBtn);
		}

		private VisualElement CreateGridLayoutCard()
		{
			var layoutFoldout = new Foldout { text = "Main Settings", value = !GridSettings.MainSettingsCollapsed };
			layoutFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("MainSettings", !evt.newValue));

			var layoutContent = new VisualElement { style = { marginLeft = 15 } };
			layoutFoldout.Add(layoutContent);

			// Main Mode
			var mainModeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			mainModeRow.Add(new Label("Grid Mode"));
			var modeDropdown = new EnumField(GridSettings.CurrentMode) { style = { flexGrow = 1, marginLeft = 10 } };
			modeDropdown.tooltip = "Uniform Grid makes perfect rows and columns. Custom Lines lets you put lines exactly where you want them manually.";
			modeDropdown.RegisterValueChangedCallback(evt =>
			{
				GridSettings.SaveMode((GridMode)evt.newValue);
				CreateGUI();
				SceneView.RepaintAll();
			});
			mainModeRow.Add(modeDropdown);
			
			var modeHelpBtn = new Button(() => 
			{
				EditorUtility.DisplayDialog(
					"Grid Mode — What's the difference?",
					"⭐ UNIFORM GRID\n" +
					"Covers your screen in a perfect, repeating pattern.\n\n" +
					" • Fixed Spacing: Gaps between lines are always the same size.\n" +
					" • Stretch with Canvas: Cuts the screen into equal slices. If your screen gets bigger, the slices stretch.\n\n" +
					"----------------------------------------\n\n" +
					"⭐ CUSTOM LINES\n" +
					"You place every line exactly where you want it.\n\n" +
					" • Fixed Position: Lines stay exactly where you dropped them.\n" +
					" • Stretch with Canvas: Lines stick to a percentage of the screen. If the screen grows, the lines slide to stay in the same relative spot.",
					"Got it");
			})
			{
				text = "?", tooltip = "What is the difference between Uniform Grid and Custom Lines?",
				style = { width = 20, height = 20, marginLeft = 4, paddingLeft = 0, paddingRight = 0, paddingTop = 0, paddingBottom = 0 }
			};
			mainModeRow.Add(modeHelpBtn);
			layoutContent.Add(mainModeRow);

			// Grid Type Row (For both modes)
			var gridTypeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
			gridTypeRow.Add(new Label("Grid Type"));

			if (GridSettings.CurrentMode == GridMode.UniformGrid)
			{
				var symDropdown = new EnumField(GridSettings.CurrentSymmetryMode) { style = { flexGrow = 1, marginLeft = 10 } };
				symDropdown.tooltip = "Fixed Spacing uses exact pixels for gaps. Stretch With Canvas uses percentages so it stretches when the screen size changes.";
				symDropdown.RegisterValueChangedCallback(evt =>
				{
					GridSettings.SaveSymmetryMode((SymmetryMode)evt.newValue);
					CreateGUI();
					SceneView.RepaintAll();
				});
				gridTypeRow.Add(symDropdown);
			}
			else
			{
				var dynDropdown = new EnumField(GridSettings.CurrentDynamicType) { style = { flexGrow = 1, marginLeft = 10 } };
				dynDropdown.tooltip = "Fixed Position locks lines to exact pixels. Stretch With Canvas lets them slide gracefully when the screen resizes.";
				dynDropdown.RegisterValueChangedCallback(evt =>
				{
					var oldType = (DynamicGridType)evt.previousValue;
					var newType = (DynamicGridType)evt.newValue;
					if (oldType != newType)
					{
						bool hasLines = GridSettings.DynamicStretchX.Count > 0 || GridSettings.DynamicStretchY.Count > 0 || 
										GridSettings.DynamicFixedX.Count > 0 || GridSettings.DynamicFixedY.Count > 0;
						
						if (hasLines)
						{
							if (!EditorUtility.DisplayDialog("Convert Grid Lines?",
								"Changing the Line Behavior will convert all existing lines.\n\n" +
								"• Stretch is relative to the canvas size.\n" +
								"• Fixed is absolute pixels from the center.",
								"Convert", "Cancel"))
							{
								dynDropdown.SetValueWithoutNotify(oldType);
								return; 
							}
						}
						Vector2 dims = GridRenderer.GetCanvasSize();
						GridSettings.SaveDynamicType(newType, dims.x, dims.y);
					}
					CreateGUI();
					SceneView.RepaintAll();
				});
				gridTypeRow.Add(dynDropdown);
			}
			layoutContent.Add(gridTypeRow);

			return layoutFoldout;
		}

		private static readonly string[,] FixedSpacingPresets = { { "25u", "25", "25" }, { "50u", "50", "50" }, { "100u", "100", "100" }, { "16:9", "16", "9" }, { "200u", "200", "200" } };
		private static readonly string[,] StretchPresets      = { { "3x3", "3", "3" },   { "6x6", "6", "6" },   { "12x12", "12", "12" }, { "16x9", "16", "9" }, { "24x24", "24", "24" } };

		private void CreateFixedSpacingFields(VisualElement parent)
		{
			var customRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			var gridXField = GridUIHelpers.CreateFloatField("Spacing X", GridSettings.GridSpacingX, "SpacingX", val => GridSettings.GridSpacingX = val);
			var gridYField = GridUIHelpers.CreateFloatField("Spacing Y", GridSettings.GridSpacingY, "SpacingY", val => GridSettings.GridSpacingY = val);
			gridXField.style.marginRight = 4;
			customRow.Add(gridXField); customRow.Add(gridYField);
			parent.Add(customRow);

			parent.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginBottom = 6 } });
			parent.Add(new Label("Presets") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });

			var presetsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
			for (int i = 0; i < FixedSpacingPresets.GetLength(0); i++)
			{
				float sx = float.Parse(FixedSpacingPresets[i, 1], CultureInfo.InvariantCulture);
				float sy = float.Parse(FixedSpacingPresets[i, 2], CultureInfo.InvariantCulture);
				GridUIHelpers.AddPresetButton(presetsContainer, FixedSpacingPresets[i, 0], sx, sy, gridXField, gridYField);
			}
			parent.Add(presetsContainer);
		}

		private void CreateStretchFields(VisualElement parent)
		{
			var customRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			var gridXField = GridUIHelpers.CreateIntField("Grid X", GridSettings.GridColumns, "Columns", val => GridSettings.GridColumns = val);
			var gridYField = GridUIHelpers.CreateIntField("Grid Y", GridSettings.GridRows,    "Rows",    val => GridSettings.GridRows    = val);
			gridXField.style.marginRight = 4;
			customRow.Add(gridXField); customRow.Add(gridYField);
			parent.Add(customRow);

			parent.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginBottom = 6 } });
			parent.Add(new Label("Presets") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 } });

			var presetsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
			for (int i = 0; i < StretchPresets.GetLength(0); i++)
			{
				int cols = int.Parse(StretchPresets[i, 1], CultureInfo.InvariantCulture);
				int rows = int.Parse(StretchPresets[i, 2], CultureInfo.InvariantCulture);
				GridUIHelpers.AddPresetButton(presetsContainer, StretchPresets[i, 0], cols, rows, gridXField, gridYField);
			}
			parent.Add(presetsContainer);
		}

		private void CreateDynamicFields(VisualElement parent)
		{
			var isStretch = GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas;
			int xLines = isStretch ? GridSettings.DynamicStretchX.Count : GridSettings.DynamicFixedX.Count;
			int yLines = isStretch ? GridSettings.DynamicStretchY.Count : GridSettings.DynamicFixedY.Count;
			int displayLines = Mathf.Max(0, xLines + yLines);

			var canvasSizeLabel = new Label() { style = { unityTextAlign = TextAnchor.MiddleRight, fontSize = 10, opacity = 0.7f, marginBottom = 4 } };
			parent.Add(canvasSizeLabel);

			var statRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 6 } };
			statRow.Add(new Label("Active Lines:"));
			var countRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
			countRow.Add(new Label("X:") { style = { marginRight = 2 } });
			var countXLabel = new Label(xLines.ToString()) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginRight = 10 } };
			countRow.Add(countXLabel);
			countRow.Add(new Label("Y:") { style = { marginRight = 2 } });
			var countYLabel = new Label(yLines.ToString()) { style = { unityFontStyleAndWeight = FontStyle.Bold } };
			countRow.Add(countYLabel);
			statRow.Add(countRow);
			parent.Add(statRow);

			statRow.schedule.Execute(() =>
			{
				Vector2 cs = GridRenderer.GetCanvasSize();
				canvasSizeLabel.text = cs != Vector2.zero ? $"Canvas  {(int)cs.x} × {(int)cs.y} px" : "";

				bool isStr = GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas;
				int nx = isStr ? GridSettings.DynamicStretchX.Count : GridSettings.DynamicFixedX.Count;
				int ny = isStr ? GridSettings.DynamicStretchY.Count : GridSettings.DynamicFixedY.Count;
				countXLabel.text = nx.ToString();
				countYLabel.text = ny.ToString();
			}).Every(100);

			if (displayLines == 0)
			{
				parent.Add(new Label("You haven't made any custom lines yet!\n\nTurn on \"Edit Grid Lines\" below, then try right-clicking anywhere in the Scene view to add your first line!")
				{
					style =
					{
						fontSize = 10, opacity = 0.7f, unityFontStyleAndWeight = FontStyle.Italic,
						whiteSpace = WhiteSpace.Normal, marginBottom = 10,
						backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.1f), paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6
					}
				});
			}

			var advBtn = new Button(GridDynamicGeneratorWindow.ShowWindow) { text = "Layout Utilities" };
			advBtn.style.height = 24;
			advBtn.tooltip = "Use this if you want to quickly add a bunch of lines at once instead of clicking them in one by one.";
			parent.Add(advBtn);
		}
	}
}
#endif
