#if UNITY_2021_2_OR_NEWER
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Native Unity Scene View Overlay for UI Furnace Grid Guide.
	/// Appears directly in Unity's native Scene View Overlay menu (under Custom).
	/// Can be docked anywhere in the Scene View or floated natively by Unity.
	/// Automatically displayed whenever UI Furnace Grid is active on a Canvas.
	/// </summary>
	[Overlay(typeof(SceneView), "ui-furnace-grid", "UI Furnace Grid", defaultDisplay = true)]
	public class UIFurnaceOverlay : Overlay, ITransientOverlay
	{
		public bool visible => GridSettings.ShowGrid && (GridSettings.IsEditModeActive || GridSettings.ActiveCanvas != null);

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, paddingBottom = 2, paddingTop = 2 } };

			var imgui = new IMGUIContainer(() =>
			{
				GUILayout.BeginHorizontal();

				// 1. Canvas Info HUD Toggle (shows / hides movable in-scene spec HUD)
				bool curInfo = GridSettings.ShowCanvasSpecHUD;
				bool newInfo = GUILayout.Toggle(curInfo, new GUIContent("Canvas Info", "Toggle the movable in-scene Canvas & Grid reading HUD"), EditorStyles.toolbarButton, GUILayout.Height(20));
				if (newInfo != curInfo)
				{
					GridSettings.ShowCanvasSpecHUD = newInfo;
					SceneView.RepaintAll();
				}

				// 2. Snap to UI Toggle
				bool curSnap = GridSettings.SnapToElements;
				bool newSnap = GUILayout.Toggle(curSnap, new GUIContent("Snap to UI", "Magnetically snap guides to UI elements and canvas quarters"), EditorStyles.toolbarButton, GUILayout.Height(20));
				if (newSnap != curSnap)
				{
					GridSettings.SaveSnapSettings(newSnap, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
					SceneView.RepaintAll();
				}

				// Contextual Edit Controls
				if (GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive)
				{
					// 3. Mirror Mode Toggle
					bool curMirror = GridSettings.IsMirrorModeActive;
					bool newMirror = GUILayout.Toggle(curMirror, new GUIContent("Mirror Mode", "Place guides symmetrically across the canvas center"), EditorStyles.toolbarButton, GUILayout.Height(20));
					if (newMirror != curMirror)
					{
						GridSettings.IsMirrorModeActive = newMirror;
						SceneView.RepaintAll();
					}

					// 4. Add Horizontal Guide (Interactive Placement)
					if (GUILayout.Button(new GUIContent("Add Horizontal", "Click to interactively place a new horizontal guide"), EditorStyles.toolbarButton, GUILayout.Height(20)))
					{
						GridRenderer.StartAddingHorizontalLine();
					}

					// 5. Add Vertical Guide (Interactive Placement)
					if (GUILayout.Button(new GUIContent("Add Vertical", "Click to interactively place a new vertical guide"), EditorStyles.toolbarButton, GUILayout.Height(20)))
					{
						GridRenderer.StartAddingVerticalLine();
					}

					// 6. Delete Selected Guides
					int selCount = GridRenderer.SelectedIndicesX.Count + GridRenderer.SelectedIndicesY.Count;
					bool hasSelection = selCount > 0;
					GUI.enabled = hasSelection;
					string delLabel = hasSelection ? $"Delete Selected ({selCount})" : "Delete Selected";
					if (GUILayout.Button(new GUIContent(delLabel, "Delete the selected guide lines (Del)"), EditorStyles.toolbarButton, GUILayout.Height(20)))
					{
						GridRenderer.DeleteSelectedLines();
					}
					GUI.enabled = true;

					// 7. Done Editing Button
					if (GUILayout.Button(new GUIContent("Done Editing", "Save guides and exit Edit Mode"), EditorStyles.toolbarButton, GUILayout.Height(20)))
					{
						GridRenderer.ExitEditMode();
					}
				}
				else
				{
					if (GridSettings.CurrentMode == GridMode.CustomLines)
					{
						if (GUILayout.Button(new GUIContent("Edit Guides", "Enter Custom Lines Edit Mode to position or draw guides"), EditorStyles.toolbarButton, GUILayout.Height(20)))
						{
							GridRenderer.ToggleEditMode();
						}
					}
					else
					{
						if (GUILayout.Button(new GUIContent("Grid Settings", "Open UI Furnace Grid Manager Window"), EditorStyles.toolbarButton, GUILayout.Height(20)))
						{
							GridManagerWindow.ShowWindow();
						}
					}
				}

				GUILayout.EndHorizontal();
			});

			root.Add(imgui);
			return root;
		}
	}
}
#endif
