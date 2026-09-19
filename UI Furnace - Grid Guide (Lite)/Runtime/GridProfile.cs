using System.Collections.Generic;
using UnityEngine;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>Sub-modes for Symmetry grid.</summary>
	public enum SymmetryMode { FixedSpacing, StretchWithCanvas }

	/// <summary>Sub-modes for Dynamic grid.</summary>
	public enum DynamicGridType { FixedPosition, StretchWithCanvas }

	/// <summary>Top-level grid mode.</summary>
	public enum GridMode { UniformGrid, CustomLines }

	/// <summary>
	/// ScriptableObject storing grid guidelines, colors, spacing, and snapping settings for a Canvas.
	/// Live editing is performed via the Grid Manager window.
	/// </summary>
	[CreateAssetMenu(fileName = "New Grid Profile", menuName = "UI Furnace/Grid Profile", order = 100)]
	public class GridProfile : ScriptableObject
	{
		[Header("Colors & Visuals")]
		public Color GridColor = new Color(0.3f, 0.7f, 1f, 0.25f);
		public Color XAxisColor = new Color(1f, 0.3f, 0.3f, 0.8f);
		public Color YAxisColor = new Color(0.3f, 1f, 0.3f, 0.8f);
		public float AxisThickness = 3f;
		public float GridThickness = 1f;
		[Range(0f, 1f)] public float GridOpacity = 1f;

		[Header("Grid Mode & Symmetry")]
		public GridMode CurrentMode = GridMode.UniformGrid;
		public SymmetryMode CurrentSymmetryMode = SymmetryMode.StretchWithCanvas;
		public DynamicGridType CurrentDynamicType = DynamicGridType.StretchWithCanvas;

		[Header("Uniform Grid Settings")]
		public float GridSpacingX = 50f;
		public float GridSpacingY = 50f;
		public int GridColumns = 10;
		public int GridRows = 10;

		[Header("Custom Dynamic Lines")]
		public List<float> DynamicStretchX = new List<float>();
		public List<float> DynamicStretchY = new List<float>();
		public List<float> DynamicFixedX = new List<float>();
		public List<float> DynamicFixedY = new List<float>();

		[Header("Snapping")]
		public bool SnapToElements = true;
		public bool SnapElementsToGrid = false;
		public float SnapDistance = 3f;
		public float ElementSnapDistance = 1f;

		/// <summary>
		/// Clones all settings from another profile into this one.
		/// </summary>
		public void CopyFrom(GridProfile other)
		{
			if (other == null) return;

			GridColor = other.GridColor;
			XAxisColor = other.XAxisColor;
			YAxisColor = other.YAxisColor;
			AxisThickness = other.AxisThickness;
			GridThickness = other.GridThickness;
			GridOpacity = other.GridOpacity;

			CurrentMode = other.CurrentMode;
			CurrentSymmetryMode = other.CurrentSymmetryMode;
			CurrentDynamicType = other.CurrentDynamicType;

			GridSpacingX = other.GridSpacingX;
			GridSpacingY = other.GridSpacingY;
			GridColumns = other.GridColumns;
			GridRows = other.GridRows;

			DynamicStretchX = new List<float>(other.DynamicStretchX);
			DynamicStretchY = new List<float>(other.DynamicStretchY);
			DynamicFixedX = new List<float>(other.DynamicFixedX);
			DynamicFixedY = new List<float>(other.DynamicFixedY);

			SnapToElements = other.SnapToElements;
			SnapElementsToGrid = other.SnapElementsToGrid;
			SnapDistance = other.SnapDistance;
			ElementSnapDistance = other.ElementSnapDistance;
		}
	}
}
