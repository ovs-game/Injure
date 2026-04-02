// SPDX-License-Identifier: MIT

using Silk.NET.WebGPU;

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
}
