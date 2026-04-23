// SPDX-License-Identifier: MIT

using Injure.Layers;

namespace TestGame;

public static class LayerTags {
	public static LayerTag Gameplay { get; private set; }

	public static void Init() {
		Gameplay = TestGame.Layers.Tags.GetOrCreate(TestGame.OwnerID, "gameplay");
	}
}
