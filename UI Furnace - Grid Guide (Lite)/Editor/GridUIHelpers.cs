#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.UIElements;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// UI helper methods for creating editor window elements.
	/// </summary>
	public static class GridUIHelpers
	{
		// ─── Field helpers ────────────────────────────────────────────────────────
		public static FloatField CreateFloatField(string label, float value, string key, System.Action<float> setter)
		{
			var field = new FloatField(label) { value = value, style = { flexGrow = 1 } };
			field.RegisterValueChangedCallback(evt =>
			{
				float v = Mathf.Max(0.1f, evt.newValue);
				field.SetValueWithoutNotify(v);
				setter(v);
				GridSettings.ScheduleDelayedSave(key, () => { var a = GridSettingsAsset.instance; SetAssetFloat(a, key, v); });
				SceneView.RepaintAll();
			});
			field.RegisterCallback<BlurEvent>(evt =>
			{
				field.SetValueWithoutNotify(Mathf.Max(0.1f, field.value));
				GridSettings.FlushPendingSaves();
			});
			return field;
		}

		public static IntegerField CreateIntField(string label, int value, string key, System.Action<int> setter)
		{
			var field = new IntegerField(label) { value = value, style = { flexGrow = 1 } };
			field.RegisterValueChangedCallback(evt =>
			{
				int v = Mathf.Max(1, evt.newValue);
				field.SetValueWithoutNotify(v);
				setter(v);
				GridSettings.ScheduleDelayedSave(key, () => { var a = GridSettingsAsset.instance; SetAssetInt(a, key, v); });
				SceneView.RepaintAll();
			});
			field.RegisterCallback<BlurEvent>(evt =>
			{
				field.SetValueWithoutNotify(Mathf.Max(1, field.value));
				GridSettings.FlushPendingSaves();
			});
			return field;
		}

		public static void AddFloatField(VisualElement parent, string label, string key, float initialValue, System.Action<float> setter)
			=> AddFloatFieldWithRef(parent, label, key, initialValue, setter);

		/// <summary>Same as AddFloatField but returns the FloatField so the caller can set .tooltip etc.</summary>
		public static FloatField AddFloatFieldWithRef(VisualElement parent, string label, string key, float initialValue, System.Action<float> setter)
		{
			var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };

			var field = new FloatField(label) { value = initialValue, style = { flexGrow = 1 } };
			field.Q<Label>().style.minWidth = 80;

			field.RegisterValueChangedCallback(evt =>
			{
				float v = Mathf.Max(0.1f, evt.newValue);
				field.SetValueWithoutNotify(v);
				setter(v);
				GridSettings.ScheduleDelayedSave(key, () => { var a = GridSettingsAsset.instance; SetAssetFloat(a, key, v); });
				SceneView.RepaintAll();
			});
			field.RegisterCallback<BlurEvent>(evt =>
			{
				float v = Mathf.Max(0.1f, field.value);
				field.SetValueWithoutNotify(v);
				SetAssetFloat(GridSettingsAsset.instance, key, v);
				GridSettings.FlushPendingSaves();
			});
			row.Add(field);
			parent.Add(row);
			return field;
		}

		public static void AddOpacitySlider(VisualElement parent, float initialValue, System.Action<float> onChanged, System.Action onBlur)
		{
			var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
			row.Add(new Label("Grid Opacity"));

			var slider = new Slider(0f, 1f) { value = initialValue, style = { flexGrow = 1, marginLeft = 8, marginRight = 8 } };
			var pct = new Label($"{Mathf.RoundToInt(initialValue * 100)}%") { style = { minWidth = 32, unityTextAlign = TextAnchor.MiddleRight } };

			slider.RegisterValueChangedCallback(evt =>
			{
				pct.text = $"{Mathf.RoundToInt(evt.newValue * 100)}%";
				onChanged(evt.newValue);
			});
			slider.RegisterCallback<BlurEvent>(evt => onBlur());

			row.Add(slider);
			row.Add(pct);
			parent.Add(row);
		}

		// ─── Preset buttons ───────────────────────────────────────────────────────
		public static void AddPresetButton(VisualElement parent, string label, float spacingX, float spacingY, FloatField xField, FloatField yField)
		{
			var btn = new Button(() =>
			{
				GridSettings.SaveSpacing(spacingX, spacingY);
				xField.value = spacingX;
				yField.value = spacingY;
				SceneView.RepaintAll();
			}) { text = label, style = { width = 60, height = 22, marginRight = 4, marginBottom = 4 } };
			parent.Add(btn);
		}

		public static void AddPresetButton(VisualElement parent, string label, int cols, int rows, IntegerField xField, IntegerField yField)
		{
			var btn = new Button(() =>
			{
				GridSettings.SaveDivisions(cols, rows);
				xField.value = cols;
				yField.value = rows;
				SceneView.RepaintAll();
			}) { text = label, style = { width = 60, height = 22, marginRight = 4, marginBottom = 4 } };
			parent.Add(btn);
		}

		// ─── Color field with reset ───────────────────────────────────────────────
		public static void AddColorFieldWithReset(VisualElement parent, string label, string key, System.Func<Color> getter, System.Action<Color> setter, Color defaultColor)
		{
			var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, justifyContent = Justify.SpaceBetween, marginBottom = 4 } };
			row.Add(new Label(label));

			var rightGroup = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
			var colorField = new ColorField { value = getter(), style = { width = 150 } };

			colorField.RegisterValueChangedCallback(evt =>
			{
				setter(evt.newValue);
				GridSettings.QueueSaveColor(key, evt.newValue);
				SceneView.RepaintAll();
			});
			colorField.RegisterCallback<BlurEvent>(evt =>
			{
				GridSettings.FlushPendingSaves();
			});
			rightGroup.Add(colorField);

			var resetBtn = new Button(() =>
			{
				setter(defaultColor);
				GridSettings.SaveColor(key, defaultColor);
				SceneView.RepaintAll();
				colorField.value = defaultColor;
			})
			{
				style =
				{
					width = 24, height = 24, marginLeft = 4,
					justifyContent = Justify.Center, alignItems = Align.Center
				},
				tooltip = "Reset to Default Color"
			};
			var refreshIcon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_Refresh" : "Refresh")?.image;
			if (refreshIcon != null) resetBtn.Add(new Image { image = refreshIcon, style = { width = 14, height = 14 } });

			rightGroup.Add(resetBtn);
			row.Add(rightGroup);
			parent.Add(row);
		}

		// ─── Asset field helpers (maps string key → asset field) ─────────────────
		private static void SetAssetFloat(GridSettingsAsset a, string key, float v)
		{
			switch (key)
			{
				case "SpacingX":       a.GridSpacingX  = v; break;
				case "SpacingY":       a.GridSpacingY  = v; break;
				case "AxisThickness":  a.AxisThickness = v; break;
				case "GridThickness":  a.GridThickness = v; break;
			}
		}

		private static void SetAssetInt(GridSettingsAsset a, string key, int v)
		{
			switch (key)
			{
				case "Columns": a.GridColumns = v; break;
				case "Rows":    a.GridRows    = v; break;
			}
		}

		// ─── Recursive PickingMode propagation ───────────────────────────
		public static void SetChildPickingMode(VisualElement root, PickingMode mode)
		{
			root.pickingMode = mode;
			foreach (var child in root.Children())
				SetChildPickingMode(child, mode);
		}

		internal static FloatField CreateFloatFieldSafe(
			string label, float value,
			System.Action<float> runtimeSetter,
			System.Action<GridSettingsAsset, float> assetSetter)
		{
			var field = new FloatField(label) { value = value, style = { flexGrow = 1 } };
			field.RegisterValueChangedCallback(evt =>
			{
				float v = Mathf.Max(0.1f, evt.newValue);
				field.SetValueWithoutNotify(v);
				runtimeSetter(v);
				GridSettings.ScheduleDelayedSave(label, () => assetSetter(GridSettingsAsset.instance, v));
				SceneView.RepaintAll();
			});
			field.RegisterCallback<BlurEvent>(evt =>
			{
				field.SetValueWithoutNotify(Mathf.Max(0.1f, field.value));
				GridSettings.FlushPendingSaves();
			});
			return field;
		}
	}
}
#endif
