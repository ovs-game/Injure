// SPDX-License-Identifier: MIT

namespace Injure.Rendering;

public static class BlendStates {
	public static readonly BlendState Alpha = new BlendState {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		}
	};

	public static readonly BlendState PremultipliedAlpha = new BlendState {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		}
	};

	public static readonly BlendState Additive = new BlendState {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.SrcAlpha,
			DstFactor = BlendFactor.One
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		}
	};

	public static readonly BlendState PremultipliedAdditive = new BlendState {
		Color = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.One
		},
		Alpha = new BlendComponent {
			Operation = BlendOperation.Add,
			SrcFactor = BlendFactor.One,
			DstFactor = BlendFactor.OneMinusSrcAlpha
		}
	};
}
