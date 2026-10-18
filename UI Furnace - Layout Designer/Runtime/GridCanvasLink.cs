using UnityEngine;

namespace AZSoftStudio.UIFurnace.LayoutDesigner
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

		/// <summary>
		/// The Canvas this link is attached to.
		/// Cached on <c>Awake</c> and <c>OnValidate</c> — never calls GetComponent at runtime.
		/// </summary>
		public Canvas Canvas { get; private set; }

		private void Awake()     => Canvas = GetComponent<Canvas>();
		private void OnValidate() => Canvas = GetComponent<Canvas>();
	}
}
