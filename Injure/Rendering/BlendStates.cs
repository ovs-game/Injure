// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public static class BlendStates {
	public static readonly BlendState Alpha = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	public static readonly BlendState PremultipliedAlpha = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	public static readonly BlendState Additive = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.One,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};

	public static readonly BlendState PremultipliedAdditive = new() {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.One,
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha,
		},
	};
}
