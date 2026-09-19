#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Manages grid settings. Connects the active Canvas's GridProfile with live editor state.
	/// Supports Undo/Redo and persists profile assets to disk.
	/// </summary>
	public static class GridSettings
	{
		private const float SaveDelaySeconds = 0.3f;
		public const string Version = "1.0.0";

		// ─── Active Canvas & Profile State ─────────────────────────────────────
		public static Canvas ActiveCanvas { get; set; }
		public static GridCanvasLink ActiveLink => ActiveCanvas != null ? ActiveCanvas.GetComponent<GridCanvasLink>() : null;
		public static GridProfile ActiveProfile { get; set; }

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
		
		// ShowGrid is outside of the profile — it is an editor-wide view toggle
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

		// ─── UI Collapse State (Editor-side) ───────────────────────────────────
		public static bool MainSettingsCollapsed { get; set; } = false;
		public static bool InteractionCollapsed  { get; set; } = false;
		public static bool EditingCollapsed      { get; set; } = false;
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
			LoadCurrentSettings();
			SceneView.RepaintAll();

			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
				win.CreateGUI();
		}

		private static void RecordUndo(string label)
		{
			if (ActiveProfile != null)
				Undo.RecordObject(ActiveProfile, label);
			Undo.RecordObject(GridSettingsAsset.instance, label);
		}

		// ─── Load ──────────────────────────────────────────────────────────────
		/// <summary>
		/// Loads settings from the ActiveProfile if available, otherwise falls back to GridSettingsAsset.
		/// ShowGrid is always loaded from GridSettingsAsset (editor-wide).
		/// </summary>
		public static void LoadCurrentSettings()
		{
			// ShowGrid is strictly editor-wide
			ShowGrid = GridSettingsAsset.instance.ShowGrid;
			MainSettingsCollapsed = GridSettingsAsset.instance.LayoutCollapsed;
			InteractionCollapsed  = GridSettingsAsset.instance.SnappingCollapsed;
			EditingCollapsed      = GridSettingsAsset.instance.EditingCollapsed;
			VisualsCollapsed      = GridSettingsAsset.instance.ColorsCollapsed;

			if (ActiveProfile != null)
			{
				var p = ActiveProfile;
				GridColor           = p.GridColor;
				XAxisColor          = p.XAxisColor;
				YAxisColor          = p.YAxisColor;
				GridSpacingX        = p.GridSpacingX;
				GridSpacingY        = p.GridSpacingY;
				GridColumns         = p.GridColumns;
				GridRows            = p.GridRows;
				CurrentMode         = p.CurrentMode;
				CurrentSymmetryMode = p.CurrentSymmetryMode;
				CurrentDynamicType  = p.CurrentDynamicType;

				DynamicStretchX     = new List<float>(p.DynamicStretchX);
				DynamicStretchY     = new List<float>(p.DynamicStretchY);
				DynamicFixedX       = new List<float>(p.DynamicFixedX);
				DynamicFixedY       = new List<float>(p.DynamicFixedY);

				AxisThickness       = p.AxisThickness;
				GridThickness       = p.GridThickness;
				GridOpacity         = p.GridOpacity;
				SnapToElements      = p.SnapToElements;
				SnapElementsToGrid  = p.SnapElementsToGrid;
				SnapDistance        = p.SnapDistance;
				ElementSnapDistance = p.ElementSnapDistance;
			}
			else
			{
				ResetCacheToDefaults();
			}
		}

		/// <summary>Resets the runtime cache to standard default values when no profile is active.</summary>
		public static void ResetCacheToDefaults()
		{
			GridColor     = new Color(0.3f, 0.7f, 1f, 0.25f);
			XAxisColor    = new Color(1f,   0.3f, 0.3f, 0.8f);
			YAxisColor    = new Color(0.3f, 1f,   0.3f, 0.8f);
			GridSpacingX  = 50f;
			GridSpacingY  = 50f;
			GridColumns   = 10;
			GridRows      = 10;
			CurrentMode         = GridMode.UniformGrid;
			CurrentSymmetryMode = SymmetryMode.StretchWithCanvas;
			CurrentDynamicType  = DynamicGridType.StretchWithCanvas;
			DynamicStretchX.Clear();
			DynamicStretchY.Clear();
			DynamicFixedX.Clear();
			DynamicFixedY.Clear();
			AxisThickness = 3f;
			GridThickness = 1f;
			GridOpacity   = 1f;
			SnapToElements = true;
			SnapElementsToGrid = false;
			SnapDistance  = 3f;
			ElementSnapDistance = 1f;
		}

		/// <summary>Load settings from the persisted global fallback asset.</summary>
		public static void LoadFromAsset()
		{
			var a = GridSettingsAsset.instance;
			GridColor           = a.GridColor;
			XAxisColor          = a.XAxisColor;
			YAxisColor          = a.YAxisColor;
			GridSpacingX        = a.GridSpacingX;
			GridSpacingY        = a.GridSpacingY;
			GridColumns         = a.GridColumns;
			GridRows            = a.GridRows;
			CurrentMode         = (GridMode)a.GridModeIndex;
			CurrentSymmetryMode = (SymmetryMode)a.SymmetryModeIndex;
			CurrentDynamicType  = (DynamicGridType)a.DynamicTypeIndex;

			DynamicStretchX     = new List<float>(a.DynamicStretchX);
			DynamicStretchY     = new List<float>(a.DynamicStretchY);
			DynamicFixedX       = new List<float>(a.DynamicFixedX);
			DynamicFixedY       = new List<float>(a.DynamicFixedY);

			AxisThickness       = a.AxisThickness;
			GridThickness       = a.GridThickness;
			ShowGrid            = a.ShowGrid;
			GridOpacity         = a.GridOpacity;
			SnapToElements      = a.SnapToElements;
			SnapElementsToGrid  = a.SnapElementsToGrid;
			SnapDistance        = a.SnapDistance;
			ElementSnapDistance = a.ElementSnapDistance;
		}

		private static void MarkProfileDirty()
		{
			if (ActiveProfile != null)
				EditorUtility.SetDirty(ActiveProfile);
		}

		// ─── Profile Creation & Assignment ─────────────────────────────────────
		/// <summary>
		/// Synchronizes the active Canvas selection from the Scene or Hierarchy.
		/// Loads the linked profile if one exists, or clears ActiveProfile if not.
		/// Refreshes open GridManagerWindow instances and repaints SceneView.
		/// </summary>
		public static void SyncCanvasSelection(Canvas canvas)
		{
			var link = canvas != null ? canvas.GetComponent<GridCanvasLink>() : null;
			var prof = link != null ? link.Profile : null;

			if (ActiveCanvas == canvas && ActiveProfile == prof) return;

			ActiveCanvas = canvas;
			ActiveProfile = prof;

			LoadCurrentSettings();
			SceneView.RepaintAll();

			foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
			{
				win.CreateGUI();
			}
		}

		/// <summary>
		/// Creates a new GridProfile asset in the Profiles/ folder with clean default settings,
		/// attaches GridCanvasLink to the canvas, and binds it as active.
		/// </summary>
		public static GridProfile CreateProfileForCanvas(Canvas canvas, string customName = null)
		{
			if (canvas == null) return null;

			string folderPath = "Assets/AZSoftStudio/UI Furnace - Grid Guide (Lite)/Profiles";
			EnsureFolderExists(folderPath);

			string rawName = string.IsNullOrEmpty(customName) ? canvas.gameObject.name : customName;
			string cleanName = string.Join("_", rawName.Split(System.IO.Path.GetInvalidFileNameChars()));
			string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{cleanName}_GridProfile.asset");

			var profile = ScriptableObject.CreateInstance<GridProfile>();
			profile.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

			AssetDatabase.CreateAsset(profile, assetPath);
			AssetDatabase.SaveAssets();

			AssignProfileToCanvas(canvas, profile);
			return profile;
		}

		/// <summary>Assigns an existing GridProfile to a Canvas, marks scene dirty, and refreshes views.</summary>
		public static void AssignProfileToCanvas(Canvas canvas, GridProfile profile, bool refreshWindow = true)
		{
			if (canvas == null) return;

			var link = canvas.GetComponent<GridCanvasLink>();
			if (link == null)
				link = Undo.AddComponent<GridCanvasLink>(canvas.gameObject);
			else
				Undo.RecordObject(link, "Assign Grid Profile");

			link.Profile = profile;
			EditorUtility.SetDirty(link);

			if (!Application.isPlaying && canvas.gameObject.scene.IsValid())
			{
				UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
			}

			ActiveCanvas = canvas;
			ActiveProfile = profile;
			LoadCurrentSettings();

			SceneView.RepaintAll();

			if (refreshWindow)
			{
				foreach (var win in Resources.FindObjectsOfTypeAll<GridManagerWindow>())
					win.CreateGUI();
			}
		}

		public static GridProfile CloneActiveProfile(string newName = null)
		{
			if (ActiveProfile == null || ActiveCanvas == null) return null;

			string folderPath = "Assets/AZSoftStudio/UI Furnace - Grid Guide (Lite)/Profiles";
			EnsureFolderExists(folderPath);

			string baseName = string.IsNullOrEmpty(newName) ? $"{ActiveProfile.name}_Copy" : newName;
			string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{baseName}.asset");

			var clone = ScriptableObject.CreateInstance<GridProfile>();
			clone.CopyFrom(ActiveProfile);

			AssetDatabase.CreateAsset(clone, assetPath);
			AssetDatabase.SaveAssets();

			AssignProfileToCanvas(ActiveCanvas, clone);
			return clone;
		}

		private static void EnsureFolderExists(string targetFolder)
		{
			string[] parts = targetFolder.Split('/');
			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
					AssetDatabase.CreateFolder(current, parts[i]);
				current = next;
			}
		}

		// ─── Immediate saves (with Undo) ───────────────────────────────────────
		public static void SaveVisibility(bool show)
		{
			Undo.RecordObject(GridSettingsAsset.instance, "Toggle Grid Visibility");
			ShowGrid = show;
			var a = GridSettingsAsset.instance; a.ShowGrid = show; a.SaveToDisk();
		}

		public static void SaveMode(GridMode mode)
		{
			RecordUndo("Change Grid Mode");
			CurrentMode = mode;
			if (mode == GridMode.UniformGrid)
			{
				IsEditModeActive = false;
				IsMirrorModeActive = false;
				GridRenderer.IsAddLinesMode = false;
			}

			if (ActiveProfile != null)
			{
				ActiveProfile.CurrentMode = mode;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance; a.GridModeIndex = (int)mode; a.SaveToDisk();
		}

		public static void SaveSnapSettings(bool snap, bool snapGrid, float distance, float elementDistance)
		{
			RecordUndo("Change Snap Settings");
			SnapToElements = snap;
			SnapElementsToGrid = snapGrid;
			SnapDistance = distance;
			ElementSnapDistance = elementDistance;

			if (ActiveProfile != null)
			{
				ActiveProfile.SnapToElements = snap;
				ActiveProfile.SnapElementsToGrid = snapGrid;
				ActiveProfile.SnapDistance = distance;
				ActiveProfile.ElementSnapDistance = elementDistance;
				MarkProfileDirty();
			}

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

			if (ActiveProfile != null)
			{
				ActiveProfile.CurrentSymmetryMode = mode;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance; a.SymmetryModeIndex = (int)mode; a.SaveToDisk();
		}

		public static void SaveDynamicType(DynamicGridType type, float canvasWidth, float canvasHeight)
		{
			if (canvasWidth < 0.001f || canvasHeight < 0.001f)
			{
				Debug.LogWarning("[UI Furnace] Cannot convert Dynamic grid type: canvas size is zero. Select a UI element first.");
				return;
			}

			RecordUndo("Change Dynamic Grid Type");

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

			if (ActiveProfile != null)
			{
				ActiveProfile.CurrentDynamicType = type;
				ActiveProfile.DynamicStretchX  = new List<float>(DynamicStretchX);
				ActiveProfile.DynamicStretchY  = new List<float>(DynamicStretchY);
				ActiveProfile.DynamicFixedX    = new List<float>(DynamicFixedX);
				ActiveProfile.DynamicFixedY    = new List<float>(DynamicFixedY);
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance;
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

			if (ActiveProfile != null)
			{
				ActiveProfile.DynamicStretchX = new List<float>(DynamicStretchX);
				ActiveProfile.DynamicStretchY = new List<float>(DynamicStretchY);
				ActiveProfile.DynamicFixedX   = new List<float>(DynamicFixedX);
				ActiveProfile.DynamicFixedY   = new List<float>(DynamicFixedY);
				MarkProfileDirty();
			}

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

			if (ActiveProfile != null)
			{
				ActiveProfile.GridSpacingX = GridSpacingX;
				ActiveProfile.GridSpacingY = GridSpacingY;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance;
			a.GridSpacingX = GridSpacingX; a.GridSpacingY = GridSpacingY; a.SaveToDisk();
		}

		public static void SaveDivisions(int columns, int rows)
		{
			RecordUndo("Set Grid Divisions");
			GridColumns = Mathf.Max(1, columns);
			GridRows    = Mathf.Max(1, rows);

			if (ActiveProfile != null)
			{
				ActiveProfile.GridColumns = GridColumns;
				ActiveProfile.GridRows    = GridRows;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance;
			a.GridColumns = GridColumns; a.GridRows = GridRows; a.SaveToDisk();
		}

		public static void SaveThickness(float axisThickness, float gridThickness)
		{
			RecordUndo("Change Grid Thickness");
			AxisThickness = Mathf.Max(0.1f, axisThickness);
			GridThickness = Mathf.Max(0.1f, gridThickness);

			if (ActiveProfile != null)
			{
				ActiveProfile.AxisThickness = AxisThickness;
				ActiveProfile.GridThickness = GridThickness;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance;
			a.AxisThickness = AxisThickness; a.GridThickness = GridThickness; a.SaveToDisk();
		}

		public static void SaveColor(string key, Color color)
		{
			RecordUndo($"Change {key}");
			ApplyColorToCache(key, color);

			if (ActiveProfile != null)
			{
				ApplyColorToProfile(ActiveProfile, key, color);
				MarkProfileDirty();
			}

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
				case "Editing":      EditingCollapsed      = collapsed; a.EditingCollapsed  = collapsed; break;
				case "Visuals":      VisualsCollapsed      = collapsed; a.ColorsCollapsed   = collapsed; break;
			}
			a.SaveToDisk();
		}

		public static void SaveOpacity(float opacity)
		{
			RecordUndo("Change Grid Opacity");
			GridOpacity = Mathf.Clamp01(opacity);

			if (ActiveProfile != null)
			{
				ActiveProfile.GridOpacity = GridOpacity;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance; a.GridOpacity = GridOpacity; a.SaveToDisk();
		}

		// ─── Debounced saves — no Undo per drag frame ──────────────────────────
		public static void QueueSaveSpacing(float spacingX, float spacingY)
		{
			GridSpacingX = Mathf.Max(0.1f, spacingX);
			GridSpacingY = Mathf.Max(0.1f, spacingY);
			var sx = GridSpacingX; var sy = GridSpacingY;
			ScheduleDelayedSave("SaveSpacing", () =>
			{
				if (ActiveProfile != null)
				{
					ActiveProfile.GridSpacingX = sx;
					ActiveProfile.GridSpacingY = sy;
					MarkProfileDirty();
				}
				var a = GridSettingsAsset.instance; a.GridSpacingX = sx; a.GridSpacingY = sy;
			});
		}

		public static void QueueSaveDivisions(int columns, int rows)
		{
			GridColumns = Mathf.Max(1, columns);
			GridRows    = Mathf.Max(1, rows);
			var c = GridColumns; var r = GridRows;
			ScheduleDelayedSave("SaveDivisions", () =>
			{
				if (ActiveProfile != null)
				{
					ActiveProfile.GridColumns = c;
					ActiveProfile.GridRows = r;
					MarkProfileDirty();
				}
				var a = GridSettingsAsset.instance; a.GridColumns = c; a.GridRows = r;
			});
		}

		public static void QueueSaveThickness(float axisThickness, float gridThickness)
		{
			AxisThickness = axisThickness; GridThickness = gridThickness;
			var at = axisThickness; var gt = gridThickness;
			ScheduleDelayedSave("SaveThickness", () =>
			{
				if (ActiveProfile != null)
				{
					ActiveProfile.AxisThickness = at;
					ActiveProfile.GridThickness = gt;
					MarkProfileDirty();
				}
				var a = GridSettingsAsset.instance; a.AxisThickness = at; a.GridThickness = gt;
			});
		}

		public static void QueueSaveColor(string key, Color color)
		{
			ApplyColorToCache(key, color);
			var k = key; var c = color;
			ScheduleDelayedSave("SaveColor_" + key, () =>
			{
				if (ActiveProfile != null)
				{
					ApplyColorToProfile(ActiveProfile, k, c);
					MarkProfileDirty();
				}
				var a = GridSettingsAsset.instance; ApplyColorToAsset(a, k, c);
			});
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
				RecordUndo("Change Grid Settings");
				foreach (var action in s_PendingSaveActions.Values) action?.Invoke();
				s_PendingSaveActions.Clear();
				GridSettingsAsset.instance.SaveToDisk();
				s_IsDelayedSaveScheduled = false;
				EditorApplication.update -= CheckPendingSave;
			}
		}

		public static void FlushPendingSaves()
		{
			if (s_PendingSaveActions.Count > 0)
			{
				RecordUndo("Change Grid Settings");
				foreach (var action in s_PendingSaveActions.Values) action?.Invoke();
				s_PendingSaveActions.Clear();
				GridSettingsAsset.instance.SaveToDisk();
				s_IsDelayedSaveScheduled = false;
				EditorApplication.update -= CheckPendingSave;
			}
		}

		public static void ResetAllToDefaults()
		{
			RecordUndo("Reset All Grid Settings");

			GridColor     = new Color(0.3f, 0.7f, 1f, 0.25f);
			XAxisColor    = new Color(1f,   0.3f, 0.3f, 0.8f);
			YAxisColor    = new Color(0.3f, 1f,   0.3f, 0.8f);
			GridSpacingX  = 50f;
			GridSpacingY  = 50f;
			GridColumns   = 10;
			GridRows      = 10;
			CurrentMode         = GridMode.UniformGrid;
			CurrentSymmetryMode = SymmetryMode.StretchWithCanvas;
			CurrentDynamicType  = DynamicGridType.StretchWithCanvas;
			AxisThickness = 3f;
			GridThickness = 1f;
			GridOpacity   = 1f;
			SnapToElements = true;
			SnapElementsToGrid = false;
			SnapDistance  = 3f;
			ElementSnapDistance = 1f;

			if (ActiveProfile != null)
			{
				ActiveProfile.GridColor = GridColor;
				ActiveProfile.XAxisColor = XAxisColor;
				ActiveProfile.YAxisColor = YAxisColor;
				ActiveProfile.GridSpacingX = GridSpacingX;
				ActiveProfile.GridSpacingY = GridSpacingY;
				ActiveProfile.GridColumns = GridColumns;
				ActiveProfile.GridRows = GridRows;
				ActiveProfile.CurrentMode = CurrentMode;
				ActiveProfile.CurrentSymmetryMode = CurrentSymmetryMode;
				ActiveProfile.CurrentDynamicType = CurrentDynamicType;
				ActiveProfile.AxisThickness = AxisThickness;
				ActiveProfile.GridThickness = GridThickness;
				ActiveProfile.GridOpacity = GridOpacity;
				ActiveProfile.SnapToElements = SnapToElements;
				ActiveProfile.SnapElementsToGrid = SnapElementsToGrid;
				ActiveProfile.SnapDistance = SnapDistance;
				ActiveProfile.ElementSnapDistance = ElementSnapDistance;
				MarkProfileDirty();
			}

			var a = GridSettingsAsset.instance;
			a.GridColor     = GridColor;
			a.XAxisColor    = XAxisColor;
			a.YAxisColor    = YAxisColor;
			a.GridSpacingX  = GridSpacingX;
			a.GridSpacingY  = GridSpacingY;
			a.GridColumns   = GridColumns;
			a.GridRows      = GridRows;
			a.GridModeIndex = 0;
			a.SymmetryModeIndex = 1;
			a.DynamicTypeIndex  = 1;
			a.AxisThickness = AxisThickness;
			a.GridThickness = GridThickness;
			a.GridOpacity   = GridOpacity;
			a.SnapToElements = SnapToElements;
			a.SnapElementsToGrid = SnapElementsToGrid;
			a.SnapDistance  = SnapDistance;
			a.ElementSnapDistance = ElementSnapDistance;
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

		private static void ApplyColorToProfile(GridProfile p, string key, Color color)
		{
			switch (key)
			{
				case "GridColor":  p.GridColor  = color; break;
				case "XAxisColor": p.XAxisColor = color; break;
				case "YAxisColor": p.YAxisColor = color; break;
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
