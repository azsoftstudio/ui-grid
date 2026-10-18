#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using System.Globalization;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Main editor window for Layout Designer.
	/// </summary>
	public class LayoutDesignerWindow : EditorWindow
	{
		private static LayoutDesignerWindow _window;
		internal static LayoutDesignerWindow Instance => _window;
		private VisualElement _layoutCardSlot;
		private ObjectField _profileField;
		private VisualElement _profileActionsSlot;
		private VisualElement _settingsContainer;

		[MenuItem("Tools/UI Furnace/Layout Designer", false, 0)]
		public static void ShowWindow()
		{
			_window = GetWindow<LayoutDesignerWindow>("Layout Designer");
			_window.minSize = new Vector2(300, 300);

			var icon = EditorGUIUtility.IconContent("d_GridLayoutGroup Icon");
			_window.titleContent = new GUIContent("Layout Designer", icon?.image);
			_window.Focus();
		}

		public static void ShowWindowAndHighlight()
		{
			ShowWindow();
			if (_window != null)
			{
				_window.FlashHighlight();
			}
		}

		public void FlashHighlight()
		{
			Focus();
			if (rootVisualElement == null) return;

			var highlightOverlay = rootVisualElement.Q<VisualElement>("window-highlight-border");
			if (highlightOverlay == null)
			{
				highlightOverlay = new VisualElement
				{
					name = "window-highlight-border",
					pickingMode = PickingMode.Ignore,
					style =
					{
						position = Position.Absolute,
						left = 0, right = 0, top = 0, bottom = 0,
						borderLeftWidth = 3, borderRightWidth = 3,
						borderTopWidth = 3, borderBottomWidth = 3,
						borderLeftColor = new Color(0.22f, 0.58f, 0.94f, 0.95f),
						borderRightColor = new Color(0.22f, 0.58f, 0.94f, 0.95f),
						borderTopColor = new Color(0.22f, 0.58f, 0.94f, 0.95f),
						borderBottomColor = new Color(0.22f, 0.58f, 0.94f, 0.95f),
						backgroundColor = new Color(0.22f, 0.58f, 0.94f, 0.08f),
						opacity = 1f
					}
				};
				rootVisualElement.Add(highlightOverlay);
			}
			else
			{
				highlightOverlay.style.opacity = 1f;
			}

			highlightOverlay.schedule.Execute(() =>
			{
				highlightOverlay.style.opacity = 0f;
			}).StartingIn(650);

			highlightOverlay.schedule.Execute(() =>
			{
				if (highlightOverlay.parent != null)
					highlightOverlay.RemoveFromHierarchy();
			}).StartingIn(1100);
		}

		private void OnEnable()
		{
			_window = this;
			GridSettings.OnSettingsChanged -= OnSettingsChangedHandler;
			GridSettings.OnSettingsChanged += OnSettingsChangedHandler;
		}

		private void OnDisable()
		{
			GridSettings.OnSettingsChanged -= OnSettingsChangedHandler;
			if (_window == this) _window = null;
		}

		private void OnDestroy()
		{
			GridSettings.OnSettingsChanged -= OnSettingsChangedHandler;
			if (_window == this) _window = null;
		}

		private bool _isUpdatingInternally = false;

		private void OnSettingsChangedHandler()
		{
			if (_isUpdatingInternally) return;
			if (rootVisualElement?.focusController?.focusedElement != null) return;
			CreateGUI();
		}

		private static Texture2D FindEditorIcon(params string[] names)
		{
			foreach (var name in names)
			{
				if (string.IsNullOrEmpty(name)) continue;
				var tex = EditorGUIUtility.FindTexture(name);
				if (tex != null) return tex;
			}
			return null;
		}

		private static Button CreateHeaderIconButton(Texture2D icon, string tooltip, System.Action onClick, string fallbackText = "?")
		{
			var btn = new Button(onClick)
			{
				tooltip = tooltip,
				style =
				{
					width = 24, height = 24,
					marginLeft = 4,
					paddingLeft = 0, paddingRight = 0, paddingTop = 0, paddingBottom = 0,
					backgroundColor = Color.clear,
					borderLeftWidth = 0, borderRightWidth = 0, borderTopWidth = 0, borderBottomWidth = 0,
					borderTopLeftRadius = 4, borderTopRightRadius = 4,
					borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
					justifyContent = Justify.Center, alignItems = Align.Center,
					flexShrink = 0
				}
			};

			if (icon != null)
			{
				var img = new Image
				{
					image = icon,
					scaleMode = ScaleMode.ScaleToFit,
					pickingMode = PickingMode.Ignore,
					style = { width = 16, height = 16, opacity = 0.85f }
				};
				btn.Add(img);
				btn.RegisterCallback<MouseEnterEvent>(evt => {
					btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
					img.style.opacity = 1f;
				});
				btn.RegisterCallback<MouseLeaveEvent>(evt => {
					btn.style.backgroundColor = Color.clear;
					img.style.opacity = 0.85f;
				});
			}
			else
			{
				btn.text = fallbackText;
				btn.style.fontSize = 11;
				btn.style.unityFontStyleAndWeight = FontStyle.Bold;
				btn.RegisterCallback<MouseEnterEvent>(evt => btn.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f));
				btn.RegisterCallback<MouseLeaveEvent>(evt => btn.style.backgroundColor = Color.clear);
			}

			return btn;
		}

		private static Label CreateKeycapBadge(string text, string tooltip = "Shortcut")
		{
			return new Label(text)
			{
				tooltip = tooltip,
				style =
				{
					fontSize = 9,
					unityFontStyleAndWeight = FontStyle.Bold,
					backgroundColor = new Color(0f, 0f, 0f, 0.25f),
					borderLeftWidth = 1, borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
					borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.22f),
					borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.22f),
					borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.22f),
					borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.22f),
					borderTopLeftRadius = 3, borderTopRightRadius = 3,
					borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
					paddingLeft = 5, paddingRight = 5,
					paddingTop = 2, paddingBottom = 2,
					opacity = 0.75f,
					alignSelf = Align.Center
				}
			};
		}

		public void CreateGUI()
		{
			GridSettings.LoadCurrentSettings();
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

			// =========================================================================
			// HEADER BANNER (Branded & Native Unity Look)
			// =========================================================================
			var header = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Row,
					alignItems = Align.Center,
					justifyContent = Justify.SpaceBetween,
					paddingLeft = 8, paddingRight = 8,
					paddingTop = 7, paddingBottom = 7,
					marginBottom = 8,
					backgroundColor = new Color(0f, 0f, 0f, 0.16f),
					borderBottomWidth = 1,
					borderBottomColor = new Color(0f, 0f, 0f, 0.28f),
					borderTopLeftRadius = 6, borderTopRightRadius = 6,
					borderBottomLeftRadius = 6, borderBottomRightRadius = 6
				}
			};

			var brandRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

			var gridIcon = EditorGUIUtility.IconContent("d_GridLayoutGroup Icon")?.image as Texture2D;
			if (gridIcon != null)
			{
				var iconImg = new Image
				{
					image = gridIcon,
					pickingMode = PickingMode.Ignore,
					style = { width = 22, height = 22, marginRight = 8, opacity = 0.9f }
				};
				brandRow.Add(iconImg);
			}

			var titleCol = new VisualElement { style = { flexDirection = FlexDirection.Column, justifyContent = Justify.Center } };

			var superTitle = new Label("UI FURNACE")
			{
				style =
				{
					fontSize = 8,
					unityFontStyleAndWeight = FontStyle.Bold,
					opacity = 0.45f,
					marginBottom = -2
				}
			};
			titleCol.Add(superTitle);

			var nameRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
			nameRow.Add(new Label("Layout Designer")
			{
				style =
				{
					fontSize = 14,
					unityFontStyleAndWeight = FontStyle.Bold,
					marginRight = 6
				}
			});

			var badge = new Label("LITE")
			{
				style =
				{
					fontSize = 8,
					unityFontStyleAndWeight = FontStyle.Bold,
					color = new Color(0.45f, 0.78f, 1f, 1f),
					backgroundColor = new Color(0.12f, 0.42f, 0.85f, 0.25f),
					borderLeftWidth = 1, borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
					borderLeftColor = new Color(0.3f, 0.6f, 1f, 0.35f),
					borderRightColor = new Color(0.3f, 0.6f, 1f, 0.35f),
					borderTopColor = new Color(0.3f, 0.6f, 1f, 0.35f),
					borderBottomColor = new Color(0.3f, 0.6f, 1f, 0.35f),
					borderTopLeftRadius = 3, borderTopRightRadius = 3,
					borderBottomLeftRadius = 3, borderBottomRightRadius = 3,
					paddingLeft = 4, paddingRight = 4,
					paddingTop = 1, paddingBottom = 1,
					alignSelf = Align.Center
				}
			};
			nameRow.Add(badge);
			titleCol.Add(nameRow);
			brandRow.Add(titleCol);
			header.Add(brandRow);

			// Right action buttons (Native Unity icons)
			var actionsRow = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Row,
					alignItems = Align.Center,
					flexShrink = 0,
					marginLeft = 6
				}
			};

			var shortcutIcon = FindEditorIcon("SettingsIcon", "d_SettingsIcon", "_Popup", "CustomTool", "d_CustomTool");
			actionsRow.Add(CreateHeaderIconButton(shortcutIcon, "Configure Shortcuts (Edit → Shortcuts)", () =>
			{
				EditorApplication.ExecuteMenuItem("Edit/Shortcuts...");
			}, "⌨"));

			var helpIcon = EditorGUIUtility.IconContent("_Help")?.image as Texture2D;
			actionsRow.Add(CreateHeaderIconButton(helpIcon, "Open Documentation (PDF)", () =>
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
			}, "?"));

			header.Add(actionsRow);
			root.Add(header);

			// World Space warning if active
			if (GridRenderer.IsWorldSpaceCanvas)
			{
				var worldWarning = new HelpBox("World Space canvases don't work with this grid. Please click on any UI element inside a normal Screen Space canvas first.", HelpBoxMessageType.Info);
				root.Add(worldWarning);
			}

			// =========================================================================
			// 1. MASTER "SHOW GRID" CARD
			// =========================================================================
			var masterCard = new VisualElement
			{
				style =
				{
					flexDirection = FlexDirection.Row,
					alignItems = Align.Center,
					justifyContent = Justify.SpaceBetween,
					backgroundColor = new Color(0f, 0f, 0f, 0.12f),
					borderTopLeftRadius = 5, borderTopRightRadius = 5,
					borderBottomLeftRadius = 5, borderBottomRightRadius = 5,
					borderLeftWidth = 3,
					borderLeftColor = GridSettings.ShowGrid ? new Color(0.2f, 0.78f, 0.38f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.3f),
					borderRightWidth = 1, borderTopWidth = 1, borderBottomWidth = 1,
					borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.15f),
					borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.15f),
					borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.15f),
					paddingLeft = 8, paddingRight = 8,
					paddingTop = 6, paddingBottom = 6,
					marginBottom = 8
				}
			};

			var masterLeft = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

			var statusDot = new Label(GridSettings.ShowGrid ? "●" : "○")
			{
				style =
				{
					fontSize = 11,
					color = GridSettings.ShowGrid ? new Color(0.2f, 0.85f, 0.4f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.5f),
					marginRight = 6,
					marginTop = -1
				}
			};
			masterLeft.Add(statusDot);

			var showGridToggle = new Toggle("Show Grid")
			{
				value = GridSettings.ShowGrid,
				style =
				{
					unityFontStyleAndWeight = FontStyle.Bold,
					fontSize = 12
				}
			};
			showGridToggle.RegisterValueChangedCallback(evt =>
			{
				GridSettings.SaveVisibility(evt.newValue);
				statusDot.text = evt.newValue ? "●" : "○";
				statusDot.style.color = evt.newValue ? new Color(0.2f, 0.85f, 0.4f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
				masterCard.style.borderLeftColor = evt.newValue ? new Color(0.2f, 0.78f, 0.38f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.3f);
				SceneView.RepaintAll();
			});
			masterLeft.Add(showGridToggle);
			masterCard.Add(masterLeft);

			masterCard.Add(CreateKeycapBadge("Alt+G", "Toggle Grid Shortcut (Edit → Shortcuts)"));
			root.Add(masterCard);

			// =========================================================================
			// 2. CANVAS & PROFILE MANAGEMENT
			// =========================================================================
			root.Add(CreateCanvasAndProfileCard());

			_settingsContainer = new VisualElement();
			root.Add(_settingsContainer);
			UpdateSettingsSection(GridSettings.ActiveProfile);
		}

		private void UpdateSettingsSection(GridProfile profile)
		{
			if (_settingsContainer == null) return;

			bool hasProf = profile != null;
			_settingsContainer.SetEnabled(hasProf);
			_settingsContainer.style.opacity = hasProf ? 1f : 0.45f;

			_settingsContainer.Clear();
			BuildSettings(_settingsContainer);
		}

		private void BuildSettings(VisualElement settingsContainer)
		{
			// =========================================================================
			// 3. MAIN SETTINGS (The Core Grid)
			// =========================================================================
			_layoutCardSlot = new VisualElement();
			_layoutCardSlot.Add(CreateGridLayoutCard());
			settingsContainer.Add(_layoutCardSlot);

			// =========================================================================
			// 4. GRID EDITING (Mode-Specific Editing Tools)
			// =========================================================================
			var editingFoldout = new Foldout { text = "Grid Editing", value = !GridSettings.EditingCollapsed, style = { marginTop = 8, unityFontStyleAndWeight = FontStyle.Bold } };
			editingFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Editing", !evt.newValue));
			settingsContainer.Add(editingFoldout);

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
					_isUpdatingInternally = true;
					try
					{
						if (evt.newValue)
						{
							GridSettings.IsEditModeActive = true;
							ApplyEditModeStyle(true);
							GridSettings.TriggerSettingsChanged();
						}
						else
						{
							GridRenderer.ExitEditMode();
						}
					}
					finally { _isUpdatingInternally = false; }
				});
				editRow.Add(editToggle);
				editingGroup.Add(editRow);

				var addLinesRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var addLinesToggle = new Toggle("Pull Guides from Edges") { value = GridRenderer.IsAddLinesMode, tooltip = "Turn this on to click and drag new guides directly from the canvas borders into your layout in the Scene view (Photoshop / Illustrator style)!" };
				addLinesToggle.RegisterValueChangedCallback(evt => { 
					_isUpdatingInternally = true;
					try { GridRenderer.IsAddLinesMode = evt.newValue; GridSettings.TriggerSettingsChanged(); }
					finally { _isUpdatingInternally = false; }
				});
				addLinesRow.Add(addLinesToggle);
				subToolsGroup.Add(addLinesRow);

				var mirrorRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var mirrorToggle = new Toggle("Mirror Mode") { value = GridSettings.IsMirrorModeActive, tooltip = "Turn this on to work symmetrically. When you pull a line to the right, a matching line will automatically appear on the left!" };
				mirrorToggle.RegisterValueChangedCallback(evt => { 
					_isUpdatingInternally = true;
					try {
						GridSettings.IsMirrorModeActive = evt.newValue; 
						GridSettings.TriggerSettingsChanged(); 
					}
					finally { _isUpdatingInternally = false; }
				});
				mirrorRow.Add(mirrorToggle);
				subToolsGroup.Add(mirrorRow);

				var deleteRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
				var deleteBtn = new Button(GridRenderer.DeleteSelectedLines) { text = "Delete Selected", style = { height = 20, flexGrow = 1, marginRight = 8 } };
				deleteRow.Add(deleteBtn);
				deleteRow.Add(CreateKeycapBadge("Del", "Delete selected grid lines in Scene View"));
				subToolsGroup.Add(deleteRow);

				subToolsGroup.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginBottom = 8 } });

				var snapToggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
				var snapToggleObj = new Toggle("Snap Lines to UI") { value = GridSettings.SnapToElements, tooltip = "When you drag a grid line, it will magnetically snap to the edges of your UI elements, making it super easy to align things!" };
				snapToggleObj.RegisterValueChangedCallback(evt => {
					_isUpdatingInternally = true;
					try { GridSettings.SaveSnapSettings(evt.newValue, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance); }
					finally { _isUpdatingInternally = false; }
				});
				snapToggleRow.Add(snapToggleObj);
				subToolsGroup.Add(snapToggleRow);

				var snapDistField = GridUIHelpers.AddFloatFieldWithRef(subToolsGroup, "Line Snap Distance", "SnapDistance", GridSettings.SnapDistance, (val) => 
					{ GridSettings.SnapDistance = Mathf.Clamp(val, 0f, 100f); }, minVal: 0f);
				if (snapDistField != null) snapDistField.tooltip = "How close does your mouse need to get before the line snaps to the UI? 3 to 10 pixels is usually a good setting.";

				ApplyEditModeStyle(GridSettings.IsEditModeActive);
				editingGroup.Add(subToolsGroup);
			}

			// =========================================================================
			// 5. UI INTERACTION (Snapping UI Elements)
			// =========================================================================
			var interactionFoldout = new Foldout { text = "UI Interaction", value = !GridSettings.InteractionCollapsed, style = { marginTop = 8, unityFontStyleAndWeight = FontStyle.Bold } };
			interactionFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Interaction", !evt.newValue));
			settingsContainer.Add(interactionFoldout);

			var interactionGroup = new VisualElement { style = { marginLeft = 15 } };
			
			var gridSnapToggleRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 8 } };
			var gridSnapToggleObj = new Toggle("Snap UI Elements to Grid") { value = GridSettings.SnapElementsToGrid, tooltip = "When you drag your UI elements around, they will magnetically snap to your grid lines!" };
			gridSnapToggleObj.RegisterValueChangedCallback(evt => {
				_isUpdatingInternally = true;
				try { GridSettings.SaveSnapSettings(GridSettings.SnapToElements, evt.newValue, GridSettings.SnapDistance, GridSettings.ElementSnapDistance); }
				finally { _isUpdatingInternally = false; }
			});
			gridSnapToggleRow.Add(gridSnapToggleObj);
			gridSnapToggleRow.Add(CreateKeycapBadge("Alt+Shift+S", "Toggle Snapping Shortcut (Edit → Shortcuts)"));
			interactionGroup.Add(gridSnapToggleRow);

			var elemSnapDistField = GridUIHelpers.AddFloatFieldWithRef(interactionGroup, "Element Snap Dist.", "ElementSnapDistance", GridSettings.ElementSnapDistance, (val) => 
				{ GridSettings.ElementSnapDistance = Mathf.Clamp(val, 0f, 100f); }, minVal: 0f);
			if (elemSnapDistField != null) elemSnapDistField.tooltip = "How close does the UI element need to get before it snaps to the grid? 3 to 10 pixels is usually best.";

			interactionGroup.Add(new Label("This makes moving UI elements easy—they will magnetically snap straight to your grid lines!")
			{
				style = { fontSize = 10, opacity = 0.6f, unityFontStyleAndWeight = FontStyle.Italic, whiteSpace = WhiteSpace.Normal, marginTop = 4 }
			});

			interactionFoldout.Add(interactionGroup);

			// =========================================================================
			// 6. VISUALS & APPEARANCE (The Look)
			// =========================================================================
			var visualsFoldout = new Foldout { text = "Visuals & Appearance", value = !GridSettings.VisualsCollapsed, style = { marginTop = 8, unityFontStyleAndWeight = FontStyle.Bold } };
			visualsFoldout.RegisterValueChangedCallback(evt => GridSettings.SaveCollapseState("Visuals", !evt.newValue));
			settingsContainer.Add(visualsFoldout);

			var visualsContainer = new VisualElement { style = { marginLeft = 15 } };
			visualsFoldout.Add(visualsContainer);

			GridUIHelpers.AddOpacitySlider(visualsContainer, GridSettings.GridOpacity, val =>
			{
				GridSettings.SaveOpacity(val);
				SceneView.RepaintAll();
			}, onBlur: () => GridSettings.FlushPendingSaves());

			visualsContainer.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginTop = 4, marginBottom = 6 } });

			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "Grid",   "GridColor",  () => GridSettings.GridColor,  c => GridSettings.GridColor  = c, new Color(0.3f, 0.7f, 1f, 0.25f));
			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "X Axis", "XAxisColor", () => GridSettings.XAxisColor, c => GridSettings.XAxisColor = c, new Color(1f, 0.3f, 0.3f, 0.8f));
			GridUIHelpers.AddColorFieldWithReset(visualsContainer, "Y Axis", "YAxisColor", () => GridSettings.YAxisColor, c => GridSettings.YAxisColor = c, new Color(0.3f, 1f, 0.3f, 0.8f));

			visualsContainer.Add(new VisualElement { style = { height = 1, backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.2f), marginTop = 4, marginBottom = 6 } });

			GridUIHelpers.AddFloatField(visualsContainer, "Axis Thickness",  "AxisThickness", GridSettings.AxisThickness, val =>
			{
				GridSettings.SaveThickness(Mathf.Max(0.1f, val), GridSettings.GridThickness);
				SceneView.RepaintAll();
			});
			GridUIHelpers.AddFloatField(visualsContainer, "Grid Thickness",  "GridThickness", GridSettings.GridThickness, val =>
			{
				GridSettings.SaveThickness(GridSettings.AxisThickness, Mathf.Max(0.1f, val));
				SceneView.RepaintAll();
			});


			// Footer & Reset
			settingsContainer.Add(new Label($"UI Furnace • Layout Designer v{GridSettings.Version}")
			{
				style =
				{
					fontSize = 9, unityTextAlign = TextAnchor.MiddleCenter,
					alignSelf = Align.Center, opacity = 0.45f, marginTop = 18, marginBottom = 4
				}
			});

			var resetBtn = new Button(() =>
			{
				int choice = EditorUtility.DisplayDialogComplex(
					"Reset Settings",
					"What would you like to reset?\n\n• Reset Appearance & Snapping:\nKeeps your grid columns, spacing, mode, and custom lines safe. Only resets colors, opacity, thickness, and snapping back to defaults.\n\n• Reset All to Factory Defaults:\nWipes all settings including uniform grid columns, rows, spacing, and mode back to fresh installation state.",
					"Reset Appearance & Snapping",
					"Cancel",
					"Reset All to Factory Defaults");

				if (choice == 0)
				{
					GridSettings.ResetAllToDefaults(resetGridLayout: false);
				}
				else if (choice == 2)
				{
					GridSettings.ResetAllToDefaults(resetGridLayout: true);
				}
			})
			{
				text = "Reset Settings...",
				tooltip = "Reset colors, thickness, opacity, and snapping without losing your grid columns, rows, or custom lines",
				style = { height = 22, opacity = 0.85f }
			};
			settingsContainer.Add(resetBtn);
		}

		private VisualElement CreateCanvasAndProfileCard()
		{
			var card = new VisualElement
			{
				style =
				{
					backgroundColor = new Color(0f, 0f, 0f, 0.15f),
					borderTopLeftRadius = 6, borderTopRightRadius = 6,
					borderBottomLeftRadius = 6, borderBottomRightRadius = 6,
					borderLeftWidth = 1, borderRightWidth = 1,
					borderTopWidth = 1, borderBottomWidth = 1,
					borderLeftColor = new Color(0.5f, 0.5f, 0.5f, 0.2f),
					borderRightColor = new Color(0.5f, 0.5f, 0.5f, 0.2f),
					borderTopColor = new Color(0.5f, 0.5f, 0.5f, 0.2f),
					borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.2f),
					paddingLeft = 8, paddingRight = 8,
					paddingTop = 8, paddingBottom = 8,
					marginBottom = 10
				}
			};

			var canvas = GridSettings.ActiveCanvas;
			var profile = GridSettings.ActiveProfile;

			// Active Canvas Row
			var canvasRow = new VisualElement
			{
				style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 6 }
			};

			var canvasField = new ObjectField("Canvas")
			{
				objectType = typeof(Canvas),
				value = canvas,
				style = { flexGrow = 1, marginRight = 4 }
			};
			canvasField.RegisterValueChangedCallback(evt =>
			{
				var newCanvas = evt.newValue as Canvas;
				GridSettings.SyncCanvasSelection(newCanvas);
			});
			canvasRow.Add(canvasField);

			if (canvas != null)
			{
				var pingCanvasBtn = new Button(() =>
				{
					EditorGUIUtility.PingObject(canvas.gameObject);
					Selection.activeGameObject = canvas.gameObject;
				})
				{
					text = "Ping",
					tooltip = "Select and ping this Canvas in the Hierarchy",
					style = { height = 20, paddingLeft = 6, paddingRight = 6 }
				};
				canvasRow.Add(pingCanvasBtn);
			}

			card.Add(canvasRow);

			// State 0: Multiple Canvases Selected
			if (GridSettings.HasMultipleCanvasesSelected)
			{
				var multiWarning = new HelpBox($"Multiple Canvases Selected ({GridSettings.SelectedCanvases.Count} Canvases).\nChoose a single Canvas below to view and configure its grid profile:", HelpBoxMessageType.Warning);
				card.Add(multiWarning);

				var canvasButtonsContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap, marginTop = 6 } };
				foreach (var c in GridSettings.SelectedCanvases)
				{
					if (c == null) continue;
					var targetCanvas = c;
					var selectBtn = new Button(() =>
					{
						Selection.activeGameObject = targetCanvas.gameObject;
						GridSettings.SyncCanvasSelection(targetCanvas);
					})
					{
						text = targetCanvas.gameObject.name,
						tooltip = $"Click to focus and edit grid for '{targetCanvas.gameObject.name}'",
						style = { height = 22, marginRight = 4, marginBottom = 4, paddingLeft = 8, paddingRight = 8 }
					};
					canvasButtonsContainer.Add(selectBtn);
				}
				card.Add(canvasButtonsContainer);
				return card;
			}

			// State 1: No Canvas Selected
			if (canvas == null)
			{
				var noCanvasInfo = new HelpBox("Select a Canvas or UI element in the Hierarchy or Scene to edit its grid profile.", HelpBoxMessageType.Info);
				card.Add(noCanvasInfo);
				return card;
			}

			// Single persistent ObjectField for Grid Profile
			var profileRow = new VisualElement
			{
				style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween }
			};
			_profileField = new ObjectField("Grid Profile")
			{
				objectType = typeof(GridProfile),
				value = profile,
				style = { flexGrow = 1 }
			};

			_profileActionsSlot = new VisualElement();

			_profileField.RegisterValueChangedCallback(evt =>
			{
				var newProf = evt.newValue as GridProfile;
				GridSettings.AssignProfileToCanvas(canvas, newProf, refreshWindow: false);
				UpdateActionsSlot(_profileActionsSlot, canvas, newProf);
				UpdateSettingsSection(newProf);
			});
			profileRow.Add(_profileField);
			card.Add(profileRow);

			UpdateActionsSlot(_profileActionsSlot, canvas, profile);
			card.Add(_profileActionsSlot);

			return card;
		}

		private void UpdateActionsSlot(VisualElement actionsSlot, Canvas canvas, GridProfile profile)
		{
			actionsSlot.Clear();
			if (canvas == null) return;

			if (profile == null)
			{
				var noProfileBox = new HelpBox($"Canvas '{canvas.name}' does not have a Grid Profile linked yet.", HelpBoxMessageType.Warning)
				{
					style = { marginBottom = 6, marginTop = 6 }
				};
				actionsSlot.Add(noProfileBox);

				var createBtn = new Button(() =>
				{
					GridSettings.CreateProfileForCanvas(canvas);
				})
				{
					text = $"+ Create Grid Profile for '{canvas.name}'",
					tooltip = "Create a new ScriptableObject Grid Profile for this Canvas",
					style =
					{
						height = 28,
						unityFontStyleAndWeight = FontStyle.Bold,
						backgroundColor = new Color(0.18f, 0.45f, 0.8f, 0.9f),
						color = Color.white
					}
				};
				actionsSlot.Add(createBtn);
			}
			else
			{
				var actionRow = new VisualElement
				{
					style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd, marginTop = 4 }
				};

				var newProfBtn = new Button(() =>
				{
					GridSettings.CreateProfileForCanvas(canvas);
				})
				{
					text = "+ New",
					tooltip = "Create and link a fresh new profile for this Canvas",
					style = { height = 20, paddingLeft = 8, paddingRight = 8, marginRight = 4 }
				};
				actionRow.Add(newProfBtn);

				var cloneBtn = new Button(() =>
				{
					var clone = GridSettings.CloneActiveProfile();
					if (_profileField != null && clone != null)
						_profileField.SetValueWithoutNotify(clone);
					UpdateActionsSlot(actionsSlot, canvas, clone);
					UpdateSettingsSection(clone);
				})
				{
					text = "Clone",
					tooltip = "Duplicate this profile to save a copy or create a variant",
					style = { height = 20, paddingLeft = 8, paddingRight = 8, marginRight = 4 }
				};
				actionRow.Add(cloneBtn);

				var pingProfBtn = new Button(() =>
				{
					EditorGUIUtility.PingObject(profile);
				})
				{
					text = "Ping Profile",
					tooltip = "Highlight this Grid Profile asset in the Project window",
					style = { height = 20, paddingLeft = 8, paddingRight = 8 }
				};
				actionRow.Add(pingProfBtn);

				actionsSlot.Add(actionRow);
			}
		}

		private VisualElement CreateGridLayoutCard()
		{
			var layoutFoldout = new Foldout { text = "Main Settings", value = !GridSettings.MainSettingsCollapsed, style = { unityFontStyleAndWeight = FontStyle.Bold } };
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
			
			var helpIcon = EditorGUIUtility.IconContent("_Help")?.image as Texture2D;
			var modeHelpBtn = CreateHeaderIconButton(helpIcon, "What is the difference between Uniform Grid and Custom Lines?", () => 
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
			}, "?");
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

	// Backwards compatibility alias for existing saved Unity window layouts
	public class GridManagerWindow : LayoutDesignerWindow { }
}
#endif
