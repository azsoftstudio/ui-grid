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
	/// </summary>
	[Overlay(typeof(SceneView), "ui-furnace-grid", "UI Furnace Grid", defaultDisplay = false)]
	public class UIFurnaceOverlay : Overlay, ITransientOverlay
	{
		public bool visible => GridSettings.CurrentMode == GridMode.CustomLines && GridSettings.IsEditModeActive;

		public override VisualElement CreatePanelContent()
		{
			var root = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

			var imgui = new IMGUIContainer(() =>
			{
				GUILayout.BeginHorizontal();

				// 1. Snap Toggle
				bool curSnap = GridSettings.SnapToElements;
				bool newSnap = GUILayout.Toggle(curSnap, new GUIContent("Snap", "Toggle magnetic snapping to UI Elements"), EditorStyles.toolbarButton, GUILayout.Height(20));
				if (newSnap != curSnap)
				{
					GridSettings.SaveSnapSettings(newSnap, GridSettings.SnapElementsToGrid, GridSettings.SnapDistance, GridSettings.ElementSnapDistance);
					SceneView.RepaintAll();
				}

				// 2. Mirror Toggle
				bool curMirror = GridSettings.IsMirrorModeActive;
				bool newMirror = GUILayout.Toggle(curMirror, new GUIContent("Mirror", "Toggle symmetric guide mirroring"), EditorStyles.toolbarButton, GUILayout.Height(20));
				if (newMirror != curMirror)
				{
					GridSettings.IsMirrorModeActive = newMirror;
					SceneView.RepaintAll();
				}

				// 3. Add Horizontal Guide
				if (GUILayout.Button(new GUIContent("+ H Line", "Enter interactive placement mode for a horizontal guide"), EditorStyles.toolbarButton, GUILayout.Height(20)))
				{
					GridRenderer.StartAddingHorizontalLine();
				}

				// 4. Add Vertical Guide
				if (GUILayout.Button(new GUIContent("+ V Line", "Enter interactive placement mode for a vertical guide"), EditorStyles.toolbarButton, GUILayout.Height(20)))
				{
					GridRenderer.StartAddingVerticalLine();
				}

				// 5. Delete Selected Guides
				int selCount = GridRenderer.SelectedIndicesX.Count + GridRenderer.SelectedIndicesY.Count;
				bool hasSelection = selCount > 0;
				GUI.enabled = hasSelection;
				string delLabel = hasSelection ? $"Delete ({selCount})" : "Delete";
				if (GUILayout.Button(new GUIContent(delLabel, "Delete Selected Guides (Del)"), EditorStyles.toolbarButton, GUILayout.Height(20)))
				{
					GridRenderer.DeleteSelectedLines();
				}
				GUI.enabled = true;

				// 6. Exit Edit Mode
				if (GUILayout.Button(new GUIContent("Exit", "Exit Edit Mode"), EditorStyles.toolbarButton, GUILayout.Height(20)))
				{
					GridSettings.IsEditModeActive = false;
					GridSettings.IsMirrorModeActive = false;
					GridRenderer.SelectedIndicesX.Clear();
					GridRenderer.SelectedIndicesY.Clear();
					SceneView.RepaintAll();
				}

				GUILayout.EndHorizontal();
			});

			root.Add(imgui);
			return root;
		}
	}
}
#endif
