#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>Sub-modes for Symmetry grid.</summary>
	public enum SymmetryMode { FixedSpacing, StretchWithCanvas }

	/// <summary>Sub-modes for Dynamic grid.</summary>
	public enum DynamicGridType { FixedPosition, StretchWithCanvas }

	/// <summary>Top-level grid mode.</summary>
	public enum GridMode { UniformGrid, CustomLines }

	/// <summary>
	/// Manages grid settings. Persists via ScriptableSingleton (UserSettings/).
	/// All immediate Save* calls register with Unity's Undo system.
	/// Queue* variants are debounced for drag operations (no Undo per frame).
	/// </summary>
	public static class GridSettings
	{
		private const float SaveDelaySeconds = 0.3f;
		public const string Version = "1.0.0";

		// ─── Runtime cache (read every frame by GridRenderer) ──────────────────
		public static Color         GridColor           { get; set; } = new Color(0.3f, 0.7f, 1f,   0.25f);
		public static Color         XAxisColor          { get; set; } = new Color(1f,   0.3f, 0.3f, 0.8f);
		public static Color         YAxisColor          { get; set; } = new Color(0.3f, 1f,   0.3f, 0.8f);
		public static float         GridSpacingX        { get; set; } = 50f;
		public static float         GridSpacingY        { get; set; } = 50f;
		public static int           GridColumns         { get; set; } = 10;
		public static int           GridRows            { get; set; } = 10;
		public static GridMode        CurrentMode         { get; set; } = GridMode.UniformGrid;
		public static SymmetryMode    CurrentSymmetryMode { get; set; } = SymmetryMode.StretchWithCanvas;
		public static DynamicGridType CurrentDynamicType  { get; set; } = DynamicGridType.StretchWithCanvas;

		public static List<float> DynamicStretchX { get; set; } = new List<float>();
		public static List<float> DynamicStretchY { get; set; } = new List<float>();
		public static List<float> DynamicFixedX   { get; set; } = new List<float>();
		public static List<float> DynamicFixedY   { get; set; } = new List<float>();

		public static float AxisThickness { get; set; } = 3f;
		public static float GridThickness { get; set; } = 1f;
		public static bool  ShowGrid      { get; set; } = true;
		public static float GridOpacity   { get; set; } = 1f;

		// ─── Snapping State ────────────────────────────────────────────────────
		public static bool  SnapToElements { get; set; } = true;
		public static bool  SnapElementsToGrid { get; set; } = false;
		public static float SnapDistance   { get; set; } = 3f;
		public static float ElementSnapDistance { get; set; } = 1f;

		// ─── Edit Mode state (runtime only) ────────────────────────────────────
		public static bool IsEditModeActive   { get; set; } = false;
		/// <summary>When true, placing or dragging a line also places its mirror on the opposite side.</summary>
		public static bool IsMirrorModeActive { get; set; } = false;

		// ─── UI Collapse State ─────────────────────────────────────────────────
		public static bool MainSettingsCollapsed { get; set; } = false;
		public static bool InteractionCollapsed  { get; set; } = false;
		public static bool VisualsCollapsed      { get; set; } = false;

		// ─── Debounce state  ────────────────────────────────────────────────────
		private static readonly Dictionary<string, System.Action> s_PendingSaveActions = new Dictionary<string, System.Action>();
		private static double s_LastChangeTime        = 0.0;
		private static bool   s_IsDelayedSaveScheduled = false;

		// ─── Undo/Redo ─────────────────────────────────────────────────────────
		static GridSettings()
		{
			Undo.undoRedoPerformed += OnUndoRedo;
		}

		private static void OnUndoRedo()
		{
			// Load from asset explicitly so the static RAM properties (used by SceneView)
			// reflect the Undone/Redone values instead of staying stuck.
			LoadFromAsset();
			SceneView.RepaintAll();

			// Rebuild any open GridManagerWindow so UIElements fields reflect the undone/redone state.
			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
				win.CreateGUI();
		}

		private static void RecordUndo(string label) =>
			Undo.RecordObject(GridSettingsAsset.instance, label);

		// ─── Load ──────────────────────────────────────────────────────────────
		/// <summary>Load all settings from the persisted asset.</summary>
		public static void LoadFromAsset()
		{
			var a     = GridSettingsAsset.instance;
			GridColor     = a.GridColor;
			XAxisColor    = a.XAxisColor;
			YAxisColor    = a.YAxisColor;
			GridSpacingX  = a.GridSpacingX;
			GridSpacingY  = a.GridSpacingY;
			GridColumns         = a.GridColumns;
			GridRows            = a.GridRows;
			CurrentMode         = (GridMode)a.GridModeIndex;
			CurrentSymmetryMode = (SymmetryMode)a.SymmetryModeIndex;
			CurrentDynamicType  = (DynamicGridType)a.DynamicTypeIndex;

			DynamicStretchX = new List<float>(a.DynamicStretchX);
			DynamicStretchY = new List<float>(a.DynamicStretchY);
			DynamicFixedX   = new List<float>(a.DynamicFixedX);
			DynamicFixedY   = new List<float>(a.DynamicFixedY);

			AxisThickness = a.AxisThickness;
			GridThickness = a.GridThickness;
			ShowGrid       = a.ShowGrid;
			GridOpacity    = a.GridOpacity;
			SnapToElements = a.SnapToElements;
			SnapElementsToGrid = a.SnapElementsToGrid;
			SnapDistance   = a.SnapDistance;
			ElementSnapDistance = a.ElementSnapDistance;

			MainSettingsCollapsed = a.LayoutCollapsed;
			InteractionCollapsed  = a.SnappingCollapsed;
			VisualsCollapsed      = a.ColorsCollapsed;
		}


		// ─── Immediate saves (with Undo) ───────────────────────────────────────
		public static void SaveVisibility(bool show)
		{
			RecordUndo("Toggle Grid Visibility");
			ShowGrid = show;
			var a = GridSettingsAsset.instance; a.ShowGrid = show; a.SaveToDisk();
		}

		public static void SaveMode(GridMode mode)
		{
			RecordUndo("Change Grid Mode");
			CurrentMode = mode;
			var a = GridSettingsAsset.instance; a.GridModeIndex = (int)mode; a.SaveToDisk();
		}

		public static void SaveSnapSettings(bool snap, bool snapGrid, float distance, float elementDistance)
		{
			RecordUndo("Change Snap Settings");
			SnapToElements = snap;
			SnapElementsToGrid = snapGrid;
			SnapDistance = distance;
			ElementSnapDistance = elementDistance;
			var a = GridSettingsAsset.instance;
			a.SnapToElements = snap;
			a.SnapElementsToGrid = snapGrid;
			a.SnapDistance = distance;
			a.ElementSnapDistance = elementDistance;
			a.SaveToDisk();
		}

		public static void SaveSymmetryMode(SymmetryMode mode)
		{
			RecordUndo("Change Symmetry Mode");
			CurrentSymmetryMode = mode;
			var a = GridSettingsAsset.instance; a.SymmetryModeIndex = (int)mode; a.SaveToDisk();
		}

		public static void SaveDynamicType(DynamicGridType type, float canvasWidth, float canvasHeight)
		{
			// Guard against zero canvas size to avoid division-by-zero
			// and silent zero-overwrite of all guides.
			if (canvasWidth < 0.001f || canvasHeight < 0.001f)
			{
				UnityEngine.Debug.LogWarning("[UI Furnace] Cannot convert Dynamic grid type: canvas size is zero. Select a UI element first.");
				return;
			}

			RecordUndo("Change Dynamic Grid Type");

			// Conversion WITHOUT clamping so out-of-bounds guides stay intact
			if (CurrentDynamicType == DynamicGridType.StretchWithCanvas && type == DynamicGridType.FixedPosition)
			{
				DynamicFixedX.Clear();
				foreach (var t in DynamicStretchX) DynamicFixedX.Add((t - 0.5f) * canvasWidth);
				DynamicFixedY.Clear();
				foreach (var t in DynamicStretchY) DynamicFixedY.Add((t - 0.5f) * canvasHeight);
			}
			else if (CurrentDynamicType == DynamicGridType.FixedPosition && type == DynamicGridType.StretchWithCanvas)
			{
				DynamicStretchX.Clear();
				foreach (var p in DynamicFixedX) DynamicStretchX.Add((p / canvasWidth) + 0.5f);
				DynamicStretchY.Clear();
				foreach (var p in DynamicFixedY) DynamicStretchY.Add((p / canvasHeight) + 0.5f);
			}

			CurrentDynamicType = type;

			// Record Undo on the asset right before mutating its list fields
			// so the serialized data is included in the Undo snapshot.
			var a = GridSettingsAsset.instance;
			Undo.RecordObject(a, "Change Dynamic Grid Type");
			a.DynamicTypeIndex = (int)type;
			a.DynamicStretchX  = new List<float>(DynamicStretchX);
			a.DynamicStretchY  = new List<float>(DynamicStretchY);
			a.DynamicFixedX    = new List<float>(DynamicFixedX);
			a.DynamicFixedY    = new List<float>(DynamicFixedY);
			a.SaveToDisk();
		}

		public static void SaveDynamicLists()
		{
			RecordUndo("Edit Dynamic Lines");
			var a = GridSettingsAsset.instance;
			a.DynamicStretchX = new List<float>(DynamicStretchX);
			a.DynamicStretchY = new List<float>(DynamicStretchY);
			a.DynamicFixedX   = new List<float>(DynamicFixedX);
			a.DynamicFixedY   = new List<float>(DynamicFixedY);
			a.SaveToDisk();
		}

		public static void SaveSpacing(float spacingX, float spacingY)
		{
			RecordUndo("Set Grid Spacing");
			GridSpacingX = Mathf.Max(0.1f, spacingX);
			GridSpacingY = Mathf.Max(0.1f, spacingY);
			var a = GridSettingsAsset.instance;
			a.GridSpacingX = GridSpacingX; a.GridSpacingY = GridSpacingY; a.SaveToDisk();
		}

		public static void SaveDivisions(int columns, int rows)
		{
			RecordUndo("Set Grid Divisions");
			GridColumns = Mathf.Max(1, columns);
			GridRows    = Mathf.Max(1, rows);
			var a = GridSettingsAsset.instance;
			a.GridColumns = GridColumns; a.GridRows = GridRows; a.SaveToDisk();
		}

		public static void SaveThickness(float axisThickness, float gridThickness)
		{
			RecordUndo("Change Grid Thickness");
			// Clamp here so callers (e.g. undo restore) can never push
			// a negative value to Handles.DrawAAPolyLine, which has undefined behaviour.
			AxisThickness = Mathf.Max(0.1f, axisThickness);
			GridThickness = Mathf.Max(0.1f, gridThickness);
			var a = GridSettingsAsset.instance;
			a.AxisThickness = AxisThickness; a.GridThickness = GridThickness; a.SaveToDisk();
		}

		/// <summary>Immediate save for a named color key (GridColor / XAxisColor / YAxisColor).</summary>
		public static void SaveColor(string key, Color color)
		{
			RecordUndo($"Change {key}");
			ApplyColorToCache(key, color);
			var a = GridSettingsAsset.instance;
			ApplyColorToAsset(a, key, color);
			a.SaveToDisk();
		}

		public static void SaveCollapseState(string key, bool collapsed)
		{
			RecordUndo("Toggle Section Collapse");
			var a = GridSettingsAsset.instance;
			switch (key)
			{
				case "MainSettings": MainSettingsCollapsed = collapsed; a.LayoutCollapsed = collapsed; break;
				case "Interaction":  InteractionCollapsed  = collapsed; a.SnappingCollapsed = collapsed; break;
				case "Visuals":      VisualsCollapsed      = collapsed; a.ColorsCollapsed = collapsed; break;
			}
			a.SaveToDisk();
		}

		public static void SaveOpacity(float opacity)
		{
			RecordUndo("Change Grid Opacity");
			GridOpacity = Mathf.Clamp01(opacity);
			var a = GridSettingsAsset.instance; a.GridOpacity = GridOpacity; a.SaveToDisk();
		}

		// ─── Debounced saves — no Undo per drag frame ──────────────────────────
		public static void QueueSaveSpacing(float spacingX, float spacingY)
		{
			GridSpacingX = Mathf.Max(0.1f, spacingX);
			GridSpacingY = Mathf.Max(0.1f, spacingY);
			var sx = GridSpacingX; var sy = GridSpacingY;
			ScheduleDelayedSave("SaveSpacing", () => { var a = GridSettingsAsset.instance; a.GridSpacingX = sx; a.GridSpacingY = sy; });
		}

		public static void QueueSaveDivisions(int columns, int rows)
		{
			GridColumns = Mathf.Max(1, columns);
			GridRows    = Mathf.Max(1, rows);
			var c = GridColumns; var r = GridRows;
			ScheduleDelayedSave("SaveDivisions", () => { var a = GridSettingsAsset.instance; a.GridColumns = c; a.GridRows = r; });
		}

		public static void QueueSaveThickness(float axisThickness, float gridThickness)
		{
			AxisThickness = axisThickness; GridThickness = gridThickness;
			var at = axisThickness; var gt = gridThickness;
			ScheduleDelayedSave("SaveThickness", () => { var a = GridSettingsAsset.instance; a.AxisThickness = at; a.GridThickness = gt; });
		}

		/// <summary>Debounced color save — used during color picker drag (no Undo per frame).</summary>
		public static void QueueSaveColor(string key, Color color)
		{
			ApplyColorToCache(key, color);
			var k = key; var c = color;
			ScheduleDelayedSave("SaveColor_" + key, () => { var a = GridSettingsAsset.instance; ApplyColorToAsset(a, k, c); });
		}

		// ─── Debounce engine ───────────────────────────────────────────────────
		public static void ScheduleDelayedSave(string id, System.Action saveAction)
		{
			s_PendingSaveActions[id] = saveAction;
			s_LastChangeTime = EditorApplication.timeSinceStartup;

			if (!s_IsDelayedSaveScheduled)
			{
				s_IsDelayedSaveScheduled = true;
				EditorApplication.update += CheckPendingSave;
			}
		}

		private static void CheckPendingSave()
		{
			if (s_PendingSaveActions.Count == 0)
			{
				s_IsDelayedSaveScheduled = false;
				EditorApplication.update -= CheckPendingSave;
				return;
			}

			if (EditorApplication.timeSinceStartup - s_LastChangeTime >= SaveDelaySeconds)
			{
				Undo.RecordObject(GridSettingsAsset.instance, "Change Grid Settings");
				foreach (var action in s_PendingSaveActions.Values) action?.Invoke();
				s_PendingSaveActions.Clear();
				GridSettingsAsset.instance.SaveToDisk(); // single disk write after all field updates
				s_IsDelayedSaveScheduled = false;
				EditorApplication.update -= CheckPendingSave;
			}
		}

		public static void FlushPendingSaves()
		{
			if (s_PendingSaveActions.Count > 0)
			{
				Undo.RecordObject(GridSettingsAsset.instance, "Change Grid Settings");
				foreach (var action in s_PendingSaveActions.Values) action?.Invoke();
				s_PendingSaveActions.Clear();
				GridSettingsAsset.instance.SaveToDisk();
				s_IsDelayedSaveScheduled = false;
				EditorApplication.update -= CheckPendingSave;
			}
		}

		/// <summary>
		/// Resets every persisted setting back to its factory default and saves to disk.
		/// Registered with Unity's Undo system so it can be undone.
		/// </summary>
		public static void ResetAllToDefaults()
		{
			RecordUndo("Reset All Grid Settings");
			var a = GridSettingsAsset.instance;

			a.GridColor     = GridColor     = new Color(0.3f, 0.7f, 1f, 0.25f);
			a.XAxisColor    = XAxisColor    = new Color(1f,   0.3f, 0.3f, 0.8f);
			a.YAxisColor    = YAxisColor    = new Color(0.3f, 1f,   0.3f, 0.8f);
			a.GridSpacingX  = GridSpacingX  = 50f;
			a.GridSpacingY  = GridSpacingY  = 50f;
			a.GridColumns   = GridColumns   = 10;
			a.GridRows      = GridRows      = 10;
			a.GridModeIndex = 0;  CurrentMode         = GridMode.UniformGrid;
			a.SymmetryModeIndex = 1; CurrentSymmetryMode = SymmetryMode.StretchWithCanvas;
			a.DynamicTypeIndex  = 1; CurrentDynamicType  = DynamicGridType.StretchWithCanvas;
			a.AxisThickness = AxisThickness = 3f;
			a.GridThickness = GridThickness = 1f;
			a.ShowGrid      = ShowGrid      = true;
			a.GridOpacity   = GridOpacity   = 1f;
			a.SnapToElements = SnapToElements = true;
			a.SnapElementsToGrid = SnapElementsToGrid = false;
			a.SnapDistance  = SnapDistance  = 3f;
			a.ElementSnapDistance = ElementSnapDistance = 1f;
			// Dynamic lines are NOT cleared — user must clear those manually
			a.SaveToDisk();
		}

		// ─── Private helpers ───────────────────────────────────────────────────
		private static void ApplyColorToCache(string key, Color color)
		{
			switch (key)
			{
				case "GridColor":  GridColor  = color; break;
				case "XAxisColor": XAxisColor = color; break;
				case "YAxisColor": YAxisColor = color; break;
			}
		}

		private static void ApplyColorToAsset(GridSettingsAsset a, string key, Color color)
		{
			switch (key)
			{
				case "GridColor":  a.GridColor  = color; break;
				case "XAxisColor": a.XAxisColor = color; break;
				case "YAxisColor": a.YAxisColor = color; break;
			}
		}
	}
}
#endif
