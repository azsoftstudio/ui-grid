#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Globalization;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Sub-window opened from the main Grid Manager.
	/// Generates template grid lines and pushes them into the Dynamic array.
	/// </summary>
	public class GridDynamicGeneratorWindow : EditorWindow
	{
		private SymmetryMode _genMode 
		{ 
			get => (SymmetryMode)EditorPrefs.GetInt("GridGuide_GenMode", (int)SymmetryMode.StretchWithCanvas);
			set => EditorPrefs.SetInt("GridGuide_GenMode", (int)value);
		}
		private float _spacingX
		{
			get => EditorPrefs.GetFloat("GridGuide_GenSpacingX", 50f);
			set => EditorPrefs.SetFloat("GridGuide_GenSpacingX", value);
		}
		private float _spacingY
		{
			get => EditorPrefs.GetFloat("GridGuide_GenSpacingY", 50f);
			set => EditorPrefs.SetFloat("GridGuide_GenSpacingY", value);
		}
		private int _cols
		{
			get => EditorPrefs.GetInt("GridGuide_GenCols", 10);
			set => EditorPrefs.SetInt("GridGuide_GenCols", value);
		}
		private int _rows
		{
			get => EditorPrefs.GetInt("GridGuide_GenRows", 10);
			set => EditorPrefs.SetInt("GridGuide_GenRows", value);
		}
		
		public static void ShowWindow()
		{
			var window = GetWindow<GridDynamicGeneratorWindow>("Layout Utilities");
			window.minSize = new Vector2(300, 360);
			window.maxSize = new Vector2(400, 500);
			// Removed ShowUtility() — GetWindow<> already focuses the window
			// and calling ShowUtility() afterward can override docking behaviour
			// inconsistently across Unity versions.
		}

		public void CreateGUI()
		{
			rootVisualElement.Clear();

			var root = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Column,
					paddingLeft = 8, paddingRight = 8,
					paddingTop = 8, paddingBottom = 8,
				}
			};
			rootVisualElement.Add(root);

			root.Add(new Label("Automatically generate a bunch of lines at once instead of clicking them in one by one.")
			{
				style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, fontSize = 11, marginBottom = 12 }
			});

			var card = new VisualElement { style = { marginBottom = 10, paddingLeft = 4, paddingRight = 4 } };
			card.Add(new Label("Generator Settings") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8 } });
			root.Add(card);

			// Setup Type
			var typeRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			typeRow.Add(new Label("Template Type"));
			
			var options = new List<string> { "Fixed Spacing", "Canvas Division" };
			int selectedIndex = _genMode == SymmetryMode.FixedSpacing ? 0 : 1;
			var typeDropdown = new PopupField<string>(options, selectedIndex) { style = { flexGrow = 1, marginLeft = 10 } };
			
			typeDropdown.RegisterValueChangedCallback(evt => 
			{ 
				_genMode = evt.newValue == "Fixed Spacing" ? SymmetryMode.FixedSpacing : SymmetryMode.StretchWithCanvas; 
				CreateGUI(); 
			});
			typeRow.Add(typeDropdown);
			card.Add(typeRow);

			card.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginBottom = 6 } });

			var paramsContainer = new VisualElement { style = { flexGrow = 1 } };
			card.Add(paramsContainer);

			if (_genMode == SymmetryMode.FixedSpacing)
			{
				var xp = GridUIHelpers.CreateFloatField("Spacing X", _spacingX, "", v => _spacingX = v);
				var yp = GridUIHelpers.CreateFloatField("Spacing Y", _spacingY, "", v => _spacingY = v);
				paramsContainer.Add(xp); paramsContainer.Add(yp);
			}
			else
			{
				var xp = GridUIHelpers.CreateIntField("Columns", _cols, "", v => _cols = v);
				var yp = GridUIHelpers.CreateIntField("Rows",    _rows, "", v => _rows = v);
				paramsContainer.Add(xp); paramsContainer.Add(yp);
			}

			// Add buttons
			var btnContainer = new VisualElement { style = { marginTop = 16, flexDirection = FlexDirection.Column } };

			var btnAdd = new Button(GenerateLines) { text = "Add Lines" };
			btnAdd.style.height = 24;
			btnAdd.style.unityFontStyleAndWeight = FontStyle.Bold;
			btnAdd.style.marginBottom = 6;

			var clearRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween } };

			var btnClearOff = new Button(ClearOffCanvasLines) { text = "Clear Off-Canvas" };
			btnClearOff.style.height = 20;
			btnClearOff.style.flexGrow = 1;
			btnClearOff.style.marginRight = 2;

			var btnClear = new Button(ClearLines) { text = "Clear All Lines" };
			btnClear.style.height = 20;
			btnClear.style.flexGrow = 1;
			btnClear.style.marginLeft = 2;

			clearRow.Add(btnClearOff);
			clearRow.Add(btnClear);

			btnContainer.Add(btnAdd);
			btnContainer.Add(clearRow);
			root.Add(btnContainer);
		}

		private void GenerateLines()
		{
			Undo.RecordObject(GridSettingsAsset.instance, "Advanced Generate Dynamic Lines");

			if (GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas)
			{
				if (_genMode == SymmetryMode.StretchWithCanvas)
				{
					// Start at i=1 and end at i<_cols (not i<=_cols)
					// to exclude t=0 and t=1, which are on the canvas edge and
					// would cause a double-line artefact on top of the canvas border.
					for (int i = 1; i < _cols; i++) GridSettings.DynamicStretchX.Add((float)i / _cols);
					for (int j = 1; j < _rows; j++) GridSettings.DynamicStretchY.Add((float)j / _rows);
				}
				else // Generate Fixed Spacing translated into Stretch percentages
				{
					// Check if GetCanvasSize() returns zero; if so, no canvas
					// is selected — abort with a clear error dialog.
					Vector2 canvas = GridRenderer.GetCanvasSize();
					if (canvas == Vector2.zero)
					{
						EditorUtility.DisplayDialog("No Canvas Selected",
							"Oops! You need to select a UI element (like an Image or a Button) in your Canvas before we can generate lines.",
							"Got it");
						return;
					}
					float cx = canvas.x * 0.5f; float cy = canvas.y * 0.5f;

					for (float o = _spacingX; o <= cx; o += _spacingX) { GridSettings.DynamicStretchX.Add((cx + o) / canvas.x); GridSettings.DynamicStretchX.Add((cx - o) / canvas.x); }
					GridSettings.DynamicStretchX.Add(0.5f); // center line
					for (float o = _spacingY; o <= cy; o += _spacingY) { GridSettings.DynamicStretchY.Add((cy + o) / canvas.y); GridSettings.DynamicStretchY.Add((cy - o) / canvas.y); }
					GridSettings.DynamicStretchY.Add(0.5f); // center line
				}
			}
			else // DynamicGridType == FixedPosition
			{
				if (_genMode == SymmetryMode.FixedSpacing)
				{
					Vector2 canvas = GridRenderer.GetCanvasSize();
					if (canvas == Vector2.zero)
					{
						EditorUtility.DisplayDialog("No Canvas Selected",
							"Oops! You need to select a UI element (like an Image or a Button) in your Canvas before we can generate lines.",
							"Got it");
						return;
					}
					float cx = canvas.x * 0.5f; float cy = canvas.y * 0.5f;

					for (float o = _spacingX; o <= cx; o += _spacingX) { GridSettings.DynamicFixedX.Add(o); GridSettings.DynamicFixedX.Add(-o); }
					GridSettings.DynamicFixedX.Add(0f); // center line
					for (float o = _spacingY; o <= cy; o += _spacingY) { GridSettings.DynamicFixedY.Add(o); GridSettings.DynamicFixedY.Add(-o); }
					GridSettings.DynamicFixedY.Add(0f); // center line
				}
				else // Generate Stretch translated to Fixed pixels
				{
					Vector2 canvas = GridRenderer.GetCanvasSize();
					if (canvas == Vector2.zero)
					{
						EditorUtility.DisplayDialog("No Canvas Selected",
							"Oops! You need to select a UI element (like an Image or a Button) in your Canvas before we can generate lines.",
							"Got it");
						return;
					}
					// Use i=1..cols-1 to skip the canvas edge positions.
					for (int i = 1; i < _cols; i++) GridSettings.DynamicFixedX.Add((((float)i / _cols) - 0.5f) * canvas.x);
					for (int j = 1; j < _rows; j++) GridSettings.DynamicFixedY.Add((((float)j / _rows) - 0.5f) * canvas.y);
				}
			}

			GridSettings.SaveDynamicLists();
			SceneView.RepaintAll();
			GridManagerWindow.Instance?.CreateGUI();
		}

		private void ClearOffCanvasLines()
		{
			if (EditorUtility.DisplayDialog("Clear Off-Canvas Lines?", "Do you want to delete all the lines that got pushed off the edge of your screen?", "Delete Them", "Cancel"))
			{
				Vector2 canvas = GridRenderer.GetCanvasSize();
				if (canvas == Vector2.zero)
				{
					EditorUtility.DisplayDialog("No Canvas Selected",
						"Oops! You need to select a UI element (like an Image or a Button) in your Canvas before we can clear off-canvas lines.",
						"Got it");
					return;
				}

				Undo.RecordObject(GridSettingsAsset.instance, "Clear Off-Canvas Lines");
				
				if (GridSettings.CurrentDynamicType == DynamicGridType.StretchWithCanvas)
				{
					GridSettings.DynamicStretchX.RemoveAll(val => val < 0f || val > 1f);
					GridSettings.DynamicStretchY.RemoveAll(val => val < 0f || val > 1f);
				}
				else
				{
					float cx = canvas.x * 0.5f;
					float cy = canvas.y * 0.5f;
					GridSettings.DynamicFixedX.RemoveAll(val => val < -cx || val > cx);
					GridSettings.DynamicFixedY.RemoveAll(val => val < -cy || val > cy);
				}

				GridSettings.SaveDynamicLists();
				SceneView.RepaintAll();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}

		private void ClearLines()
		{
			if (EditorUtility.DisplayDialog("Delete All Lines?", "Are you sure you want to delete every single custom line you've made?", "Delete Everything", "Cancel"))
			{
				Undo.RecordObject(GridSettingsAsset.instance, "Clear Dynamic Lines");
				GridSettings.DynamicStretchX.Clear(); GridSettings.DynamicStretchY.Clear();
				GridSettings.DynamicFixedX.Clear();   GridSettings.DynamicFixedY.Clear();
				GridSettings.SaveDynamicLists();
				SceneView.RepaintAll();
				foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>()) win.CreateGUI();
			}
		}
	}
}
#endif
