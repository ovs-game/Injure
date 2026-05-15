// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Injure.Assets;
using Injure.Core;
using Injure.Graphics;
using Injure.Graphics.Text;
using Injure.ModKit.Runtime;
using Injure.Timing;
using Injure.Scheduling;

using TestGame.ModApi;

namespace TestGame;

internal sealed class TestApi : ITestGameModApi {
	public void MarkLoaded(string ownerID) {
		Console.WriteLine($"loaded: {ownerID}");
	}
}

public sealed class Game : IGame {
	public const string OwnerID = "TestGame";
	public static readonly string AssetsDirectory = Path.Combine(AppContext.BaseDirectory, "Assets");

	public static GameServices GameServices {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}
	public static ITickerRegistry Tickers => GameServices.Tickers;
	public static InputServices Input => GameServices.Input;
	public static LayerServices Layers => GameServices.Layers;
	public static AssetStore Assets => GameServices.Assets;
	public static TextSystem Text => GameServices.Text;
	public static WindowState WindowState => GameServices.Host.Window.State;

	public static ModRuntime<ITestGameModApi> Mods {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}

	public const string TestFontFilename = "Aileron-Regular.otf";
	public static AssetRef<Font> TestFont {
		get => field ?? throw new InvalidOperationException("game not initialized yet or already shut down");
		private set;
	}

	public static async Task Main() {
		Game g = new();

		string root = AppContext.BaseDirectory;
		string mods = Path.Combine(root, "Mods");
		string cache = Path.Combine(root, ".mod-cache");
		Mods = new(new ModRuntimeOptions<ITestGameModApi> {
			ModDirectory = mods,
			CacheDirectory = cache,
			ApiFactory = _ => new TestApi(),
			SharedAssemblies = [
				"Injure",
				"Injure.ModKit",
				"TestGame.ModApi",
				"MonoMod.RuntimeDetour",
				"MonoMod.Utils",
			],
			HookTargetStoreAssemblies = [
				typeof(Game).Assembly,
			],
			MaxParallelCodeLoads = Environment.ProcessorCount - 1,
		});
		await Mods.StartAsync(CancellationToken.None);

		Runner.Run(g, new GameConfig(
			Service: new ServiceConfig(Assets: true, Audio: false, Text: true),
			Window: new WindowConfig(new WindowSettings(Title: "TestGame", Width: 640, Height: 480)),
			Render: new RenderConfig(new RenderSettings(PresentMode.Adaptive)),
			Timing: new TimingConfig(new TimingSettings(RenderTimingMode.Capped, TargetFPS: 60.0))
		));
	}

	public void Init(GameServices sv) {
		GameServices = sv;
		Assets.RegisterSource(OwnerID, new DirectoryAssetSource(OwnerID, AssetsDirectory), "AssetsDirectory");
		Actions.Init();
		LayerTags.Init();
		TestFont = Assets.GetAsset<Font>(new AssetID(OwnerID, TestFontFilename));

		TickerHandle gameplayTicker = Tickers.Add(new TickerSpec(
			Timing: new TickerTiming(MonoTick.PeriodFromHz(60.0)),
			Options: TickerOptions.Default with {
				OverrunMode = TickerOverrunMode.CatchUp
			}
		));
		Layers.Stack.PushTop(new GameplayLayer(), gameplayTicker);
	}

	public void Render(Canvas cv) {
		Layers.Stack.Render(cv);
	}

	public void Shutdown() {
		GameServices = null!;
	}

	public void BetweenSchedulerTicks() {
		Mods.AtSafeBoundary(); // stub, you shouldn't actually call this here
	}
}
