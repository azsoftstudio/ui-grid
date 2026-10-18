#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.Collections.Generic;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
{
	/// <summary>
	/// Snapping engine, projection math, and visual feedback for Layout Designer.
	/// </summary>
	public static partial class GridRenderer
	{
		// --- Snapping Caching State ---
		private static readonly List<float> s_SnapWorldLinesX = new List<float>();
		private static readonly List<float> s_SnapWorldLinesY = new List<float>();
		// Maps each snap line entry index to the source RectTransform — same order as snap lists above
		private static readonly List<RectTransform> s_SnapSourcesX = new List<RectTransform>();
		private static readonly List<RectTransform> s_SnapSourcesY = new List<RectTransform>();
		// The elements whose borders are currently being snapped to (for highlight drawing when dragging grid lines)
		private static readonly List<RectTransform> s_SnapHighlightedRects = new List<RectTransform>();
		// Elements snapped while dragging UI elements with Unity's Rect Tool
		private static readonly List<RectTransform> s_ElementSnapHighlightedRects = new List<RectTransform>();
		// The specific grid line offsets that should be highlighted (relative to canvas center)
		private static readonly HashSet<float> s_HighlightOffsetsX = new HashSet<float>();
		private static readonly HashSet<float> s_HighlightOffsetsY = new HashSet<float>();

		private static readonly List<float> s_ActiveWorldGridLinesX = new List<float>();
		private static readonly List<float> s_ActiveWorldGridLinesY = new List<float>();

		// Cache for optimizing the O(N) lookup of elements during drag loops
		private static RectTransform[] s_CachedSnapHierarchy = null;
		private static Matrix4x4[] s_CachedSnapMatrices = null;
		private static Rect[] s_CachedSnapRects = null;
		private static bool s_SnapHierarchyIsDirty = true;
		
		private static readonly List<RectTransform> s_HierarchyListBuffer = new List<RectTransform>();
		private static readonly HashSet<GameObject> s_SelectionBuffer = new HashSet<GameObject>();

		private static Vector3 s_LastDraggedPos;
		private static Vector2 s_LastSizeDelta;
		private static float s_LastMinX, s_LastMaxX, s_LastMinY, s_LastMaxY;
		private static int s_LastDraggedID = -1;

		private static void GetRectWorldProjections(RectTransform rt, out float minX, out float maxX, out float minY, out float maxY, out Vector3 center, out Vector3 right, out Vector3 up)
		{
			minX = maxX = minY = maxY = 0;
			center = right = up = Vector3.zero;

			if (s_CachedCanvasRect == null) return;

			s_CachedCanvasRect.GetWorldCorners(s_CornersCache);
			Vector3 BL = s_CornersCache[0], TL = s_CornersCache[1], TR = s_CornersCache[2], BR = s_CornersCache[3];
			center = (BL + TR) * 0.5f;
			Vector3 rightDir = BR - BL;
			Vector3 upDir = TL - BL;
			float canvasWidth = rightDir.magnitude;
			float canvasHeight = upDir.magnitude;
			right = rightDir / canvasWidth;
			up = upDir / canvasHeight;

			rt.GetWorldCorners(s_CornersCache);
			minX = float.MaxValue; maxX = float.MinValue;
			minY = float.MaxValue; maxY = float.MinValue;

			for (int i = 0; i < 4; i++)
			{
				float projX = Vector3.Dot(s_CornersCache[i] - center, right);
				float projY = Vector3.Dot(s_CornersCache[i] - center, up);

				if (projX < minX) minX = projX;
				if (projX > maxX) maxX = projX;
				if (projY < minY) minY = projY;
				if (projY > maxY) maxY = projY;
			}
		}

		private static void ProcessElementSnapping()
		{
			if (s_IsDragging || s_IsAxisDragging || s_IsAddingLine) return;
			if (GUIUtility.hotControl == 0)
			{
				s_CurrentDragMode = DragMode.None;
			}
			if (!GridSettings.SnapElementsToGrid) return;
			if (GridSettings.ActiveProfile == null) return;
			if (Tools.current != Tool.Rect) return;
			
			var validRTs = new List<RectTransform>();
			foreach (var transform in Selection.transforms)
			{
				RectTransform rt = transform as RectTransform;
				if (rt != null && rt != s_CachedCanvasRect)
				{
					validRTs.Add(rt);
				}
			}
			if (validRTs.Count == 0) return;

			RectTransform firstRT = validRTs[0];
			int id = firstRT.GetInstanceID();
			
			if (id != s_LastDraggedID)
			{
				s_LastDraggedID = id;
				s_LastDraggedPos = firstRT.position;
				s_LastSizeDelta = firstRT.sizeDelta;
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			bool posChanged = Vector3.Distance(firstRT.position, s_LastDraggedPos) > 0.0001f;
			bool sizeChanged = Vector2.Distance(firstRT.sizeDelta, s_LastSizeDelta) > 0.0001f;

			if (!posChanged && !sizeChanged) 
			{
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			if (validRTs.Count > 1 && sizeChanged)
			{
				s_LastDraggedPos = firstRT.position;
				s_LastSizeDelta = firstRT.sizeDelta;
				if (s_CachedCanvasRect != null)
				{
					GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
				}
				return;
			}

			if (sizeChanged)
			{
				s_CurrentDragMode = DragMode.Resize;
				ApplyResizeSnapCorrection(firstRT);
			}
			else if (posChanged)
			{
				s_CurrentDragMode = DragMode.Move;
				ApplySnapCorrection(validRTs);
			}

			s_LastDraggedPos = firstRT.position;
			s_LastSizeDelta = firstRT.sizeDelta;
			if (s_CachedCanvasRect != null)
			{
				GetRectWorldProjections(firstRT, out s_LastMinX, out s_LastMaxX, out s_LastMinY, out s_LastMaxY, out _, out _, out _);
			}
		}

		private static void ApplyResizeSnapCorrection(RectTransform rt)
		{
			if (s_ActiveWorldGridLinesX.Count == 0 && s_ActiveWorldGridLinesY.Count == 0) return;

			GetRectWorldProjections(rt, out float minX, out float maxX, out float minY, out float maxY, out _, out _, out _);

			float deltaMinX = Mathf.Abs(minX - s_LastMinX);
			float deltaMaxX = Mathf.Abs(maxX - s_LastMaxX);
			float deltaMinY = Mathf.Abs(minY - s_LastMinY);
			float deltaMaxY = Mathf.Abs(maxY - s_LastMaxY);

			float threshold = 0.001f;
			bool movedLeft   = deltaMinX > threshold && deltaMaxX <= threshold;
			bool movedRight  = deltaMaxX > threshold && deltaMinX <= threshold;
			bool movedBottom = deltaMinY > threshold && deltaMaxY <= threshold;
			bool movedTop    = deltaMaxY > threshold && deltaMinY <= threshold;

			float snapDist = GridSettings.ElementSnapDistance;
			float bestDiffX = float.MaxValue;
			float snapDeltaX = 0f;

			if (movedLeft)
			{
				for (int i = 0; i < s_ActiveWorldGridLinesX.Count; i++)
				{
					float diff = s_ActiveWorldGridLinesX[i] - minX;
					if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < Mathf.Abs(bestDiffX))
					{
						bestDiffX = diff;
						snapDeltaX = diff;
					}
				}
			}
			else if (movedRight)
			{
				for (int i = 0; i < s_ActiveWorldGridLinesX.Count; i++)
				{
					float diff = s_ActiveWorldGridLinesX[i] - maxX;
					if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < Mathf.Abs(bestDiffX))
					{
						bestDiffX = diff;
						snapDeltaX = diff;
					}
				}
			}

			float bestDiffY = float.MaxValue;
			float snapDeltaY = 0f;

			if (movedBottom)
			{
				for (int i = 0; i < s_ActiveWorldGridLinesY.Count; i++)
				{
					float diff = s_ActiveWorldGridLinesY[i] - minY;
					if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < Mathf.Abs(bestDiffY))
					{
						bestDiffY = diff;
						snapDeltaY = diff;
					}
				}
			}
			else if (movedTop)
			{
				for (int i = 0; i < s_ActiveWorldGridLinesY.Count; i++)
				{
					float diff = s_ActiveWorldGridLinesY[i] - maxY;
					if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < Mathf.Abs(bestDiffY))
					{
						bestDiffY = diff;
						snapDeltaY = diff;
					}
				}
			}

			if (Mathf.Abs(snapDeltaX) > 0.0001f || Mathf.Abs(snapDeltaY) > 0.0001f)
			{
				Transform parent = rt.parent;
				float scaleFactorX = 1f;
				float scaleFactorY = 1f;
				if (parent != null)
				{
					scaleFactorX = parent.lossyScale.x;
					scaleFactorY = parent.lossyScale.y;
				}
				if (Mathf.Abs(scaleFactorX) < 0.0001f) scaleFactorX = 1f;
				if (Mathf.Abs(scaleFactorY) < 0.0001f) scaleFactorY = 1f;

				Vector2 size = rt.sizeDelta;
				Vector2 anchoredPos = rt.anchoredPosition;

				if (Mathf.Abs(snapDeltaX) > 0.0001f)
				{
					float localDeltaX = snapDeltaX / scaleFactorX;
					if (movedLeft)
					{
						size.x -= localDeltaX;
						anchoredPos.x += localDeltaX * (1f - rt.pivot.x);
					}
					else if (movedRight)
					{
						size.x += localDeltaX;
						anchoredPos.x += localDeltaX * rt.pivot.x;
					}
				}

				if (Mathf.Abs(snapDeltaY) > 0.0001f)
				{
					float localDeltaY = snapDeltaY / scaleFactorY;
					if (movedBottom)
					{
						size.y -= localDeltaY;
						anchoredPos.y += localDeltaY * (1f - rt.pivot.y);
					}
					else if (movedTop)
					{
						size.y += localDeltaY;
						anchoredPos.y += localDeltaY * rt.pivot.y;
					}
				}

				rt.sizeDelta = size;
				rt.anchoredPosition = anchoredPos;
			}
		}

		private static void ApplySnapCorrection(List<RectTransform> validRTs)
		{
			if (s_ActiveWorldGridLinesX.Count == 0 && s_ActiveWorldGridLinesY.Count == 0) return;

			float snapDist = GridSettings.ElementSnapDistance;
			float globalSnapOffsetX = 0f;
			float globalSnapOffsetY = 0f;
			bool globalSnappedX = false;
			bool globalSnappedY = false;

			float bestDistX = float.MaxValue;
			float bestDistY = float.MaxValue;

			for (int r = 0; r < validRTs.Count; r++)
			{
				RectTransform rt = validRTs[r];
				GetRectWorldProjections(rt, out float minX, out float maxX, out float minY, out float maxY, out _, out _, out _);
				float midX = (minX + maxX) * 0.5f;
				float midY = (minY + maxY) * 0.5f;

				float[] testX = new float[] { minX, midX, maxX };
				float[] testY = new float[] { minY, midY, maxY };

				for (int t = 0; t < testX.Length; t++)
				{
					float val = testX[t];
					for (int i = 0; i < s_ActiveWorldGridLinesX.Count; i++)
					{
						float diff = s_ActiveWorldGridLinesX[i] - val;
						if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < bestDistX)
						{
							bestDistX = Mathf.Abs(diff);
							globalSnapOffsetX = diff;
							globalSnappedX = true;
						}
					}
				}

				for (int t = 0; t < testY.Length; t++)
				{
					float val = testY[t];
					for (int i = 0; i < s_ActiveWorldGridLinesY.Count; i++)
					{
						float diff = s_ActiveWorldGridLinesY[i] - val;
						if (Mathf.Abs(diff) <= snapDist && Mathf.Abs(diff) < bestDistY)
						{
							bestDistY = Mathf.Abs(diff);
							globalSnapOffsetY = diff;
							globalSnappedY = true;
						}
					}
				}
			}

			if (globalSnappedX || globalSnappedY)
			{
				GetRectWorldProjections(validRTs[0], out _, out _, out _, out _, out _, out Vector3 right, out Vector3 up);
				for (int r = 0; r < validRTs.Count; r++)
				{
					RectTransform rt = validRTs[r];
					Vector3 newPos = rt.position;
					if (globalSnappedX) newPos += right * globalSnapOffsetX;
					if (globalSnappedY) newPos += up * globalSnapOffsetY;
					rt.position = newPos;
				}
			}
		}

		private static void PrepareSnapLines(Vector3 center, Vector3 right, Vector3 up)
		{
			if (!GridSettings.SnapToElements || s_CachedCanvasRect == null) return;

			if (s_SnapHierarchyIsDirty || s_CachedSnapHierarchy == null)
			{
				s_HierarchyListBuffer.Clear();
				s_CachedCanvasRect.GetComponentsInChildren<RectTransform>(false, s_HierarchyListBuffer);
				
				var validRTs = new List<RectTransform>(s_HierarchyListBuffer.Count);
				for (int i = 0; i < s_HierarchyListBuffer.Count; i++)
				{
					if (s_HierarchyListBuffer[i] != null)
					{
						validRTs.Add(s_HierarchyListBuffer[i]);
					}
				}

				s_CachedSnapHierarchy = validRTs.ToArray();
				s_CachedSnapMatrices = new Matrix4x4[s_CachedSnapHierarchy.Length];
				s_CachedSnapRects = new Rect[s_CachedSnapHierarchy.Length];
				s_SnapHierarchyIsDirty = false;
				
				RebuildSnapBounds(center, right, up);
				return;
			}

			bool needsRebuild = false;
			for (int i = 0; i < s_CachedSnapHierarchy.Length; i++)
			{
				var rt = s_CachedSnapHierarchy[i];
				if (rt == null) 
				{
					s_SnapHierarchyIsDirty = true;
					needsRebuild = true;
					break;
				}

				if (rt.localToWorldMatrix != s_CachedSnapMatrices[i] || rt.rect != s_CachedSnapRects[i])
				{
					needsRebuild = true;
					break;
				}
			}

			if (needsRebuild)
			{
				RebuildSnapBounds(center, right, up);
			}
		}

		private static void RebuildSnapBounds(Vector3 center, Vector3 right, Vector3 up)
		{
			s_SnapWorldLinesX.Clear();
			s_SnapWorldLinesY.Clear();
			s_SnapSourcesX.Clear();
			s_SnapSourcesY.Clear();

			if (s_CachedSnapHierarchy == null) return;

			s_SelectionBuffer.Clear();
			foreach (var go in Selection.gameObjects)
			{
				if (go != null) s_SelectionBuffer.Add(go);
			}

			for (int i = 0; i < s_CachedSnapHierarchy.Length; i++)
			{
				RectTransform rt = s_CachedSnapHierarchy[i];
				if (rt == null || rt == s_CachedCanvasRect) continue;
				if (s_SelectionBuffer.Contains(rt.gameObject)) continue;

				s_CachedSnapMatrices[i] = rt.localToWorldMatrix;
				s_CachedSnapRects[i] = rt.rect;

				rt.GetWorldCorners(s_CornersCache);

				float minX = float.MaxValue, maxX = float.MinValue;
				float minY = float.MaxValue, maxY = float.MinValue;

				for (int c = 0; c < 4; c++)
				{
					float projX = Vector3.Dot(s_CornersCache[c] - center, right);
					float projY = Vector3.Dot(s_CornersCache[c] - center, up);

					if (projX < minX) minX = projX;
					if (projX > maxX) maxX = projX;
					if (projY < minY) minY = projY;
					if (projY > maxY) maxY = projY;
				}

				s_SnapWorldLinesX.Add(minX); s_SnapSourcesX.Add(rt);
				s_SnapWorldLinesX.Add(maxX); s_SnapSourcesX.Add(rt);
				s_SnapWorldLinesX.Add((minX + maxX) * 0.5f); s_SnapSourcesX.Add(rt);

				s_SnapWorldLinesY.Add(minY); s_SnapSourcesY.Add(rt);
				s_SnapWorldLinesY.Add(maxY); s_SnapSourcesY.Add(rt);
				s_SnapWorldLinesY.Add((minY + maxY) * 0.5f); s_SnapSourcesY.Add(rt);
			}
		}

		private static float ApplySnap(float rawProj, List<float> snapLines, List<RectTransform> snapSources, float snapThreshold)
		{
			if (snapLines == null || snapLines.Count == 0) return rawProj;

			float bestLine = rawProj;
			float minDiff = snapThreshold;
			RectTransform matchedRT = null;

			for (int i = 0; i < snapLines.Count; i++)
			{
				float line = snapLines[i];
				float diff = Mathf.Abs(line - rawProj);
				if (diff < minDiff)
				{
					minDiff = diff;
					bestLine = line;
					if (snapSources != null && i < snapSources.Count)
						matchedRT = snapSources[i];
				}
			}

			if (matchedRT != null && !s_SnapHighlightedRects.Contains(matchedRT))
			{
				s_SnapHighlightedRects.Add(matchedRT);
			}

			return bestLine;
		}

		private static void HandleElementSnapping(Vector3 center, Vector3 right, Vector3 up, float canvasWidth, float canvasHeight, float scaleX, float scaleY)
		{
			s_ActiveWorldGridLinesX.Clear();
			s_ActiveWorldGridLinesY.Clear();

			float halfW = canvasWidth * 0.5f;
			float halfH = canvasHeight * 0.5f;

			// Center Axes
			s_ActiveWorldGridLinesX.Add(0f);
			s_ActiveWorldGridLinesY.Add(0f);

			if (GridSettings.CurrentMode == GridMode.UniformGrid)
			{
				if (GridSettings.CurrentSymmetryMode == SymmetryMode.FixedSpacing)
				{
					float spacingX = GridSettings.GridSpacingX;
					float spacingY = GridSettings.GridSpacingY;
					if (spacingX > 0.001f && spacingY > 0.001f)
					{
						float worldSpacingX = spacingX * scaleX;
						float worldSpacingY = spacingY * scaleY;
						for (float offset = worldSpacingX; offset <= halfW; offset += worldSpacingX)
						{
							s_ActiveWorldGridLinesX.Add(offset);
							s_ActiveWorldGridLinesX.Add(-offset);
						}
						for (float offset = worldSpacingY; offset <= halfH; offset += worldSpacingY)
						{
							s_ActiveWorldGridLinesY.Add(offset);
							s_ActiveWorldGridLinesY.Add(-offset);
						}
					}
				}
				else
				{
					int cols = GridSettings.GridColumns;
					int rows = GridSettings.GridRows;
					for (int i = 1; i < cols; i++)
					{
						float t = (float)i / cols;
						s_ActiveWorldGridLinesX.Add((t - 0.5f) * canvasWidth);
					}
					for (int j = 1; j < rows; j++)
					{
						float t = (float)j / rows;
						s_ActiveWorldGridLinesY.Add((t - 0.5f) * canvasHeight);
					}
				}
			}
			else if (GridSettings.CurrentMode == GridMode.CustomLines)
			{
				bool isFixed = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition;
				var linesX = isFixed ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
				var linesY = isFixed ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;

				for (int i = 0; i < linesX.Count; i++)
				{
					float val = linesX[i];
					float worldOffset = isFixed ? (val * scaleX) : ((val - 0.5f) * canvasWidth);
					s_ActiveWorldGridLinesX.Add(worldOffset);
				}
				for (int j = 0; j < linesY.Count; j++)
				{
					float val = linesY[j];
					float worldOffset = isFixed ? (val * scaleY) : ((val - 0.5f) * canvasHeight);
					s_ActiveWorldGridLinesY.Add(worldOffset);
				}
			}

			// Snapping visual cues when dragging UI elements
			if (GridSettings.SnapElementsToGrid && Tools.current == Tool.Rect)
			{
				float snapDist = GridSettings.ElementSnapDistance;

				foreach (var transform in Selection.transforms)
				{
					RectTransform rt = transform as RectTransform;
					if (rt != null && rt != s_CachedCanvasRect)
					{
						GetRectWorldProjections(rt, out float minX, out float maxX, out float minY, out float maxY, out _, out _, out _);
						float midX = (minX + maxX) * 0.5f;
						float midY = (minY + maxY) * 0.5f;

						float[] testX = new float[] { minX, midX, maxX };
						float[] testY = new float[] { minY, midY, maxY };

						bool isSnapped = false;

						for (int t = 0; t < testX.Length; t++)
						{
							float val = testX[t];
							for (int i = 0; i < s_ActiveWorldGridLinesX.Count; i++)
							{
								if (Mathf.Abs(s_ActiveWorldGridLinesX[i] - val) <= snapDist + 0.05f)
								{
									isSnapped = true;
									float snappedLineX = s_ActiveWorldGridLinesX[i];
									Vector3 bot = center + right * snappedLineX - up * halfH;
									Vector3 top = center + right * snappedLineX + up * halfH;
									Handles.color = GetGridComplementColor();
									DrawCrispLine(Mathf.Max(GridSettings.GridThickness + 1.5f, 2.5f), bot, top);

									if (GridSettings.CurrentMode == GridMode.CustomLines)
									{
										bool isFixed = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition;
										var linesX = isFixed ? GridSettings.DynamicFixedX : GridSettings.DynamicStretchX;
										for (int lx = 0; lx < linesX.Count; lx++)
										{
											float originalOffset = linesX[lx];
											float wOff = isFixed ? (originalOffset * scaleX) : ((originalOffset - 0.5f) * canvasWidth);
											if (Mathf.Abs(wOff - snappedLineX) < 0.001f)
											{
												s_HighlightOffsetsX.Add(originalOffset);
											}
										}
									}
									break;
								}
							}
						}

						for (int t = 0; t < testY.Length; t++)
						{
							float val = testY[t];
							for (int i = 0; i < s_ActiveWorldGridLinesY.Count; i++)
							{
								if (Mathf.Abs(s_ActiveWorldGridLinesY[i] - val) <= snapDist + 0.05f)
								{
									isSnapped = true;
									float snappedLineY = s_ActiveWorldGridLinesY[i];
									Vector3 leftPt = center + up * snappedLineY - right * halfW;
									Vector3 rightPt = center + up * snappedLineY + right * halfW;
									Handles.color = GetGridComplementColor();
									DrawCrispLine(Mathf.Max(GridSettings.GridThickness + 1.5f, 2.5f), leftPt, rightPt);

									if (GridSettings.CurrentMode == GridMode.CustomLines)
									{
										bool isFixed = GridSettings.CurrentDynamicType == DynamicGridType.FixedPosition;
										var linesY = isFixed ? GridSettings.DynamicFixedY : GridSettings.DynamicStretchY;
										for (int ly = 0; ly < linesY.Count; ly++)
										{
											float originalOffset = linesY[ly];
											float wOff = isFixed ? (originalOffset * scaleY) : ((originalOffset - 0.5f) * canvasHeight);
											if (Mathf.Abs(wOff - snappedLineY) < 0.001f)
											{
												s_HighlightOffsetsY.Add(originalOffset);
											}
										}
									}
									break;
								}
							}
						}

						if (isSnapped && !s_ElementSnapHighlightedRects.Contains(rt))
						{
							s_ElementSnapHighlightedRects.Add(rt);
						}
					}
				}
			}
		}

		/// <summary>
		/// Computes the 180-degree optical complementary highlight color for the snapped UI element.
		/// </summary>
		private static Color GetAdaptiveSnapColor(RectTransform target)
		{
			Color baseColor = Color.white;
			if (target != null)
			{
				var graphic = target.GetComponent<Graphic>();
				if (graphic != null && graphic.color.a > 0.05f)
					baseColor = graphic.color;
			}

			Color.RGBToHSV(baseColor, out float H, out float S, out float V);

			if (S < 0.15f)
			{
				return V > 0.5f
					? new Color(1.0f, 0.05f, 0.55f, 1.0f) // Electric magenta on light
					: new Color(0.0f, 0.95f, 1.0f, 1.0f); // Electric cyan on dark
			}

			// Complementary 180 degree rotation
			float compH = (H + 0.5f) % 1.0f;
			float compS = Mathf.Clamp(S + 0.35f, 0.85f, 1.0f);
			float compV = V > 0.6f ? 0.98f : 1.0f;

			Color complement = Color.HSVToRGB(compH, compS, compV);
			complement.a = 1.0f;
			return complement;
		}

		/// <summary>
		/// Returns complementary color for grid lines when snapping occurs.
		/// </summary>
		private static Color GetGridComplementColor()
		{
			Color.RGBToHSV(GridSettings.GridColor, out float H, out float S, out float V);
			if (S < 0.12f)
			{
				return V > 0.5f
					? new Color(1.0f, 0.45f, 0.0f, 1.0f) // Amber on light
					: new Color(0.0f, 0.95f, 1.0f, 1.0f); // Cyan on dark
			}
			float compH = (H + 0.5f) % 1.0f;
			Color complement = Color.HSVToRGB(compH, Mathf.Clamp(S + 0.3f, 0.85f, 1f), 1f);
			complement.a = 1.0f;
			return complement;
		}

		/// <summary>
		/// Draws magnetic snap indicator brackets and center crosshair around a snapped UI element.
		/// </summary>
		private static void DrawSnapHighlight(Vector3[] c, RectTransform target = null)
		{
			if (c == null || c.Length < 4) return;

			Vector3 b0 = PixelSnap(c[0]), b1 = PixelSnap(c[1]);
			Vector3 b2 = PixelSnap(c[2]), b3 = PixelSnap(c[3]);

			float armW = Vector3.Distance(b0, b3) * 0.22f;
			float armH = Vector3.Distance(b0, b1) * 0.22f;
			float arm = Mathf.Max(6f, Mathf.Min(armW, armH));

			Vector3 right = (b3 - b0).normalized;
			Vector3 up    = (b1 - b0).normalized;

			Color adaptiveColor = GetAdaptiveSnapColor(target);
			Color shadowColor   = new Color(0.05f, 0.05f, 0.08f, 0.55f);

			float shadowThick = 6.0f;
			float mainThick   = 3.5f;

			void DrawBracketPass(float thickness, Color color)
			{
				Handles.color = color;
				DrawCrispLineScreenSpace(thickness, b1, b1 + right * arm);
				DrawCrispLineScreenSpace(thickness, b1, b1 - up    * arm);

				DrawCrispLineScreenSpace(thickness, b2, b2 - right * arm);
				DrawCrispLineScreenSpace(thickness, b2, b2 - up    * arm);

				DrawCrispLineScreenSpace(thickness, b0, b0 + right * arm);
				DrawCrispLineScreenSpace(thickness, b0, b0 + up    * arm);

				DrawCrispLineScreenSpace(thickness, b3, b3 - right * arm);
				DrawCrispLineScreenSpace(thickness, b3, b3 + up    * arm);
			}

			// Pass 1: Drop Shadow
			DrawBracketPass(shadowThick, shadowColor);
			// Pass 2: Foreground Color
			DrawBracketPass(mainThick, adaptiveColor);

			// Center Crosshair
			Vector3 center = (b0 + b2) * 0.5f;
			float crosshairArm = Mathf.Min(arm * 0.5f, 10f);
			if (crosshairArm > 3f)
			{
				// Shadow
				Handles.color = shadowColor;
				DrawCrispLineScreenSpace(3.5f, center - right * crosshairArm, center + right * crosshairArm);
				DrawCrispLineScreenSpace(3.5f, center - up    * crosshairArm, center + up    * crosshairArm);

				// Foreground
				Handles.color = adaptiveColor;
				DrawCrispLineScreenSpace(1.75f, center - right * crosshairArm, center + right * crosshairArm);
				DrawCrispLineScreenSpace(1.75f, center - up    * crosshairArm, center + up    * crosshairArm);
			}
		}
	}
}
#endif
