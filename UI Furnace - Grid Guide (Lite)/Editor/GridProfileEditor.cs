#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	[CustomEditor(typeof(GridProfile))]
	public class GridProfileEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			var profile = (GridProfile)target;

			EditorGUILayout.Space(6);
			EditorGUILayout.HelpBox(
				"Grid Profile settings are live-edited through the Grid Manager window.\n" +
				"Use the button below to open and configure this profile.",
				MessageType.Info);

			EditorGUILayout.Space(6);

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("Profile Overview", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Mode", profile.CurrentMode.ToString());
			if (profile.CurrentMode == GridMode.UniformGrid)
			{
				if (profile.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
					EditorGUILayout.LabelField("Spacing", $"{profile.GridSpacingX} × {profile.GridSpacingY} px");
				else
					EditorGUILayout.LabelField("Divisions", $"{profile.GridColumns} cols × {profile.GridRows} rows");
			}
			else
			{
				int lineCount = (profile.CurrentDynamicType == DynamicGridType.FixedPosition)
					? (profile.DynamicFixedX.Count + profile.DynamicFixedY.Count)
					: (profile.DynamicStretchX.Count + profile.DynamicStretchY.Count);
				EditorGUILayout.LabelField("Custom Lines", $"{lineCount} active lines");
			}
			EditorGUILayout.EndVertical();

			EditorGUILayout.Space(8);

			if (GUILayout.Button("Open in Grid Manager", GUILayout.Height(30)))
			{
				GridSettings.ActiveProfile = profile;
				GridManagerWindow.ShowWindow();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}
	}

	[CustomEditor(typeof(GridCanvasLink))]
	public class GridCanvasLinkEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			var link = (GridCanvasLink)target;

			EditorGUILayout.Space(6);
			EditorGUILayout.HelpBox(
				"This component links this Canvas to its dedicated Grid Profile.\n" +
				"When you select UI elements on this Canvas, the Grid Guide loads this profile automatically.",
				MessageType.Info);

			EditorGUILayout.Space(6);
			var profileProp = serializedObject.FindProperty("_profile");
			EditorGUILayout.PropertyField(profileProp, new GUIContent("Grid Profile"));
			serializedObject.ApplyModifiedProperties();

			EditorGUILayout.Space(8);

			if (GUILayout.Button("Open in Grid Manager", GUILayout.Height(28)))
			{
				if (link.Canvas != null)
					Selection.activeGameObject = link.Canvas.gameObject;

				GridSettings.ActiveCanvas = link.Canvas;
				GridSettings.ActiveProfile = link.Profile;
				GridManagerWindow.ShowWindow();
				GridManagerWindow.Instance?.CreateGUI();
			}
		}
	}
}
#endif
