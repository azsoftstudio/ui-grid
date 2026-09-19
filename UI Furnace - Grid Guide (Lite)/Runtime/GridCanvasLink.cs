using UnityEngine;

namespace AZSoftStudio.UIFurnace.GridGuide
{
	/// <summary>
	/// Attaches to a Canvas GameObject to link it with its dedicated GridProfile.
	/// In player builds, this component is inert with zero update overhead.
	/// </summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Canvas))]
	[AddComponentMenu("UI Furnace/Grid Canvas Link")]
	public class GridCanvasLink : MonoBehaviour
	{
		[SerializeField]
		private GridProfile _profile;

		/// <summary>The grid profile assigned to this Canvas.</summary>
		public GridProfile Profile
		{
			get => _profile;
			set => _profile = value;
		}

		/// <summary>Quick helper to get the Canvas component this link is attached to.</summary>
		public Canvas Canvas => GetComponent<Canvas>();
	}
}
