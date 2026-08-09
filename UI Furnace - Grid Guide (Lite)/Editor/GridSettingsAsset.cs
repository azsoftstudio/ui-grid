#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// ScriptableSingleton that persists all grid settings to UserSettings/.
	/// Replaces EditorPrefs, enabling Undo/Redo support via Undo.RecordObject.
	/// </summary>
	[FilePath("UserSettings/UIFurnaceGridGuide.asset", FilePathAttribute.Location.ProjectFolder)]
	internal sealed class GridSettingsAsset : ScriptableSingleton<GridSettingsAsset>
	{
		[SerializeField] internal Color GridColor     = new Color(0.3f, 0.7f, 1f,   0.25f);
		[SerializeField] internal Color XAxisColor    = new Color(1f,   0.3f, 0.3f, 0.8f);
		[SerializeField] internal Color YAxisColor    = new Color(0.3f, 1f,   0.3f, 0.8f);
		[SerializeField] internal float GridSpacingX  = 50f;
		[SerializeField] internal float GridSpacingY  = 50f;
		[SerializeField] internal int   GridColumns       = 10;
		[SerializeField] internal int   GridRows          = 10;
		[SerializeField] internal int   GridModeIndex     = 0; // 0 = UniformGrid, 1 = CustomLines
		[SerializeField] internal int   SymmetryModeIndex = 1; // 0 = FixedSpacing, 1 = StretchWithCanvas
		[SerializeField] internal int   DynamicTypeIndex  = 1; // 0 = FixedPosition, 1 = StretchWithCanvas

		[SerializeField] internal List<float> DynamicStretchX = new List<float>();
		[SerializeField] internal List<float> DynamicStretchY = new List<float>();
		[SerializeField] internal List<float> DynamicFixedX   = new List<float>();
		[SerializeField] internal List<float> DynamicFixedY   = new List<float>();

		[SerializeField] internal float AxisThickness     = 3f;
		[SerializeField] internal float GridThickness = 1f;
		[SerializeField] internal bool  ShowGrid      = true;
		[SerializeField] internal float GridOpacity   = 1f;

		[SerializeField] internal bool  SnapToElements = true;
		[SerializeField] internal bool  SnapElementsToGrid = false;
		[SerializeField] internal float SnapDistance   = 3f;
		[SerializeField] internal float ElementSnapDistance = 1f;

		// Card collapse states
		[SerializeField] internal bool LayoutCollapsed     = false;
		[SerializeField] internal bool SnappingCollapsed   = false; // expanded by default — it's a core feature
		[SerializeField] internal bool EditingCollapsed    = false;
		[SerializeField] internal bool ColorsCollapsed     = false;

		/// <summary>Write the asset to disk (UserSettings/ folder).</summary>
		internal void SaveToDisk() => Save(true);
	}
}
#endif
