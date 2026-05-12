// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Graphics;
using Injure.Input;
using Injure.Scheduling;

namespace Injure.Layers;

public sealed class LayerStack : IDisposable {
	// ==========================================================================
	// internal types
	private sealed class LayerEntry {
		public required Layer Layer { get; init; }
		public required TickerHandle Ticker { get; set; }
		public required LayerRuntime Runtime { get; init; }
		public InputCursor InputCursor;
		public LayerTagSet Tags;
	}

	private readonly struct ResolvedPassState(bool updateAllowed, LayerFeatures activeFeatures) {
		public readonly bool UpdateAllowed = updateAllowed;
		public readonly LayerFeatures ActiveFeatures = activeFeatures;
	}

	private sealed class BlockAccumulator {
		private readonly List<LayerTag> updateTags = new();
		private readonly List<LayerTag> renderTags = new();
		private readonly List<LayerTag> inputTags = new();

		public LayerBlockMask GetBlocked(ReadOnlySpan<LayerTag> tags) {
			LayerBlockMask blocked = LayerBlockMask.None;
			if (intersects(updateTags, tags))
				blocked |= LayerBlockMask.Update;
			if (intersects(renderTags, tags))
				blocked |= LayerBlockMask.Render;
			if (intersects(inputTags, tags))
				blocked |= LayerBlockMask.Input;
			return blocked;
		}

		public bool IsBlocked(LayerBlockMask pass, ReadOnlySpan<LayerTag> tags) {
			if (pass == LayerBlockMask.Update)
				return intersects(updateTags, tags);
			if (pass == LayerBlockMask.Render)
				return intersects(renderTags, tags);
			if (pass == LayerBlockMask.Input)
				return intersects(inputTags, tags);
			return false;
		}

		public void AddRules(ReadOnlySpan<LayerBlockRule> rules) {
			for (int i = 0; i < rules.Length; i++) {
				ref readonly LayerBlockRule rule = ref rules[i];
				if (rule.Blocked.HasAny(LayerBlockMask.Update))
					merge(updateTags, rule.MatchTags);
				if (rule.Blocked.HasAny(LayerBlockMask.Render))
					merge(renderTags, rule.MatchTags);
				if (rule.Blocked.HasAny(LayerBlockMask.Input))
					merge(inputTags, rule.MatchTags);
			}
		}

		private static void merge(List<LayerTag> dst, in LayerTagSet src) {
			foreach (LayerTag tag in src.AsSpan())
				dst.Add(tag);
		}

		private static bool intersects(List<LayerTag> set, ReadOnlySpan<LayerTag> tags) {
			if (tags.IsEmpty)
				return false;
			for (int i = 0; i < tags.Length; i++)
				if (set.Contains(tags[i]))
					return true;
			return false;
		}
	}

	private enum PendingOpKind {
		PushTop,
		PushBottom,
		Remove,
		Replace,
		Clear,
	}

	private readonly record struct PendingOp(
		PendingOpKind Kind,
		Layer? Layer,
		Layer? NewLayer,
		TickerHandle Ticker
	);

	private sealed class TickerSubscription {
		private readonly LayerStack owner;
		public readonly TickerHandle Ticker;
		public readonly TickerCallback Callback;
		public TickerSubscription(LayerStack owner, TickerHandle ticker) {
			this.owner = owner;
			Ticker = ticker;
			Callback = callback;
		}
		private void callback(in TickCallbackInfo info) => owner.tickerCallback(Ticker, in info);
	}

	// ==========================================================================
	// fields
	private readonly ITickerRegistry tickers;
	private readonly InputSystem input;

	private readonly List<LayerEntry> entries = new(); // bottom -> top
	private readonly List<PendingOp> pending = new();

	private readonly Dictionary<TickerHandle, int> refcounts = new();
	private readonly Dictionary<TickerHandle, TickerSubscription> subs = new();

	private bool disposed;
	private bool applying;
	private int callbackDepth;
	private int renderDepth;

	internal LayerStack(ITickerRegistry tickers, InputSystem input) {
		this.tickers = tickers ?? throw new ArgumentNullException(nameof(tickers));
		this.input = input ?? throw new ArgumentNullException(nameof(input));
	}

	// ==========================================================================
	// public api (layer management)
	private void push(PendingOpKind kind, Layer layer, TickerHandle ticker) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(layer);
		if (layer.Owner is not null)
			throw new InvalidOperationException("layer already belongs to a stack");

		layer.Owner = this;
		pending.Add(new PendingOp(kind, layer, NewLayer: null, ticker));
		maybeApplyPending();
	}
	public void PushTop(Layer layer, TickerHandle ticker) => push(PendingOpKind.PushTop, layer, ticker);
	public void PushBottom(Layer layer, TickerHandle ticker) => push(PendingOpKind.PushBottom, layer, ticker);

	public bool Remove(Layer layer) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(layer);
		if (!ReferenceEquals(layer.Owner, this))
			return false;
		if (!mentions(layer))
			return false;

		pending.Add(new PendingOp(PendingOpKind.Remove, layer, NewLayer: null, Ticker: default));
		maybeApplyPending();
		return true;
	}

	public bool Replace(Layer oldLayer, Layer newLayer, TickerHandle ticker) {
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(oldLayer);
		ArgumentNullException.ThrowIfNull(newLayer);
		if (!ReferenceEquals(oldLayer.Owner, this))
			return false;
		if (!mentions(oldLayer))
			return false;
		if (newLayer.Owner is not null)
			throw new InvalidOperationException("new layer already belongs to a stack");

		newLayer.Owner = this;
		pending.Add(new PendingOp(PendingOpKind.Replace, oldLayer, newLayer, ticker));
		maybeApplyPending();
		return true;
	}

	public void Clear() {
		ObjectDisposedException.ThrowIf(disposed, this);
		pending.Add(new PendingOp(PendingOpKind.Clear, null, null, default));
		maybeApplyPending();
	}

	// ==========================================================================
	// render / dispose
	public void Render(Canvas cv) {
		ObjectDisposedException.ThrowIf(disposed, this);
		maybeApplyPending();
		renderDepth++;
		try {
			if (entries.Count == 0)
				return;
			Span<ResolvedPassState> states = entries.Count <= 64 ? stackalloc ResolvedPassState[entries.Count] : new ResolvedPassState[entries.Count];
			resolvePassStates(states);

			for (int i = 0; i < entries.Count; i++) {
				LayerEntry ent = entries[i];
				if (ent.Layer.Features.HasNone(LayerFeatures.Render))
					continue;
				if (states[i].ActiveFeatures.HasNone(LayerFeatures.Render))
					continue;

				ent.Layer.Render(cv);
			}
		} finally {
			renderDepth--;
		}
		maybeApplyPending();
	}

	public void Dispose() {
		if (disposed)
			return;
		disposed = true;
		clear();
		foreach (PendingOp op in pending) {
			switch (op.Kind) {
			case PendingOpKind.PushTop:
			case PendingOpKind.PushBottom:
				if (op.Layer is not null && ReferenceEquals(op.Layer.Owner, this))
					op.Layer.Owner = null;
				break;
			case PendingOpKind.Replace:
				if (op.NewLayer is not null && ReferenceEquals(op.NewLayer.Owner, this))
					op.NewLayer.Owner = null;
				break;
			}
		}
		pending.Clear();
	}

	// ==========================================================================
	// ticker
	private void tickerCallback(TickerHandle ticker, in TickCallbackInfo info) {
		if (disposed)
			return;

		applyPending();
		callbackDepth++;
		try {
			if (entries.Count == 0)
				return;
			Span<ResolvedPassState> states = entries.Count <= 64 ? stackalloc ResolvedPassState[entries.Count] : new ResolvedPassState[entries.Count];
			resolvePassStates(states);

			double rawDt = info.Elapsed.ToSeconds();
			for (int i = entries.Count - 1; i >= 0; i--) {
				LayerEntry ent = entries[i];
				ref readonly ResolvedPassState st = ref states[i];
				if (ent.Ticker != ticker)
					continue;

				if (!st.UpdateAllowed) {
					input.AdvanceToCurrent(ref ent.InputCursor);
					ent.Runtime.SuppressControls(info.ActualAt);
					continue;
				}

				bool allowInput = st.ActiveFeatures.HasAny(LayerFeatures.Input);
				InputView inputView;
				if (allowInput) {
					inputView = input.CreateViewSince(ref ent.InputCursor);
				} else {
					input.AdvanceToCurrent(ref ent.InputCursor);
					inputView = InputView.Empty;
				}

				LayerRuntime rt = ent.Runtime;
				LayerTimeDomain tm = rt.Time;

				double dt = tm.Transform(rawDt);
				tm.Advance(dt, rawDt);
				rt.UpdatePerfTracked(info.ActualAt);

				ControlView controls = rt.UpdateControls(info.ActualAt, inputView);
				LayerTickContext ctx = new(
					tickInfo: info,
					dt: dt,
					rawDt: rawDt,
					time: tm.Time,
					rawTime: tm.RawTime,
					tickNum: tm.TickNum,
					input: inputView,
					controls: controls
				);
				ent.Layer.Update(in ctx);
				rt.TickCoroutines(dt, rawDt);
			}
		} finally {
			callbackDepth--;
		}
		applyPending();
		if (tryGetOldestLiveInputCursor(out InputCursor oldest))
			input.DiscardBefore(oldest);
		else
			input.DiscardAll();
	}

	private void resolvePassStates(Span<ResolvedPassState> dst) {
		if (dst.Length < entries.Count)
			throw new ArgumentException("destination span too small", nameof(dst));

		BlockAccumulator blocked = new();
		for (int i = entries.Count - 1; i >= 0; i--) {
			LayerEntry ent = entries[i];
			ReadOnlySpan<LayerTag> tags = ent.Tags.AsSpan();
			LayerBlockMask blockedMask = blocked.GetBlocked(tags);

			bool updateAllowed = blockedMask.HasNone(LayerBlockMask.Update);
			LayerFeatures active = LayerFeatures.None;
			LayerFeatures features = ent.Layer.Features;
			if (features.HasAny(LayerFeatures.Render) && blockedMask.HasNone(LayerBlockMask.Render))
				active |= LayerFeatures.Render;
			if (features.HasAny(LayerFeatures.Input) && blockedMask.HasNone(LayerBlockMask.Input))
				active |= LayerFeatures.Input;

			dst[i] = new ResolvedPassState(updateAllowed, active);
			blocked.AddRules(ent.Layer.BlockRules);
		}
	}

	private bool tryGetOldestLiveInputCursor(out InputCursor oldest) {
		bool found = false;
		oldest = default;

		foreach (LayerEntry ent in entries) {
			if (ent.Layer.Features.HasNone(LayerFeatures.Input))
				continue;

			if (!found || ent.InputCursor < oldest) {
				oldest = ent.InputCursor;
				found = true;
			}
		}
		return found;
	}

	// ==========================================================================
	// pending ops
	private void maybeApplyPending() {
		if (callbackDepth == 0 && renderDepth == 0)
			applyPending();
	}

	private void applyPending() {
		if (disposed || applying || pending.Count == 0)
			return;

		applying = true;
		try {
			while (pending.Count > 0) {
				PendingOp[] batch = pending.ToArray();
				pending.Clear();

				foreach (PendingOp op in batch) {
					switch (op.Kind) {
					case PendingOpKind.PushTop:
						enter(op.Layer!, op.Ticker, pushToTop: true);
						break;
					case PendingOpKind.PushBottom:
						enter(op.Layer!, op.Ticker, pushToTop: false);
						break;
					case PendingOpKind.Remove:
						leave(op.Layer!);
						break;
					case PendingOpKind.Replace:
						replace(op.Layer!, op.NewLayer!, op.Ticker);
						break;
					case PendingOpKind.Clear:
						clear();
						break;
					}
				}
			}
		} finally {
			applying = false;
		}
	}

	private void enter(Layer layer, TickerHandle ticker, bool pushToTop) {
		if (!ReferenceEquals(layer.Owner, this) || findEntryIdx(layer) >= 0)
			return;

		LayerRuntime runtime = new();
		runtime.InitActions(layer.ActionProfile);

		LayerEntry ent = new() {
			Layer = layer,
			Ticker = ticker,
			Runtime = runtime,
			InputCursor = input.CreateCursor(),
			Tags = layer.Tags,
		};

		if (pushToTop)
			entries.Add(ent);
		else
			entries.Insert(0, ent);

		grabTicker(ticker);
		layer.AttachRuntime(runtime);
		layer.OnEnter();
	}

	private void leave(Layer layer) {
		int idx = findEntryIdx(layer);
		if (idx < 0) {
			if (ReferenceEquals(layer.Owner, this))
				layer.Owner = null;
			return;
		}

		LayerEntry entry = entries[idx];
		entries.RemoveAt(idx);

		entry.Layer.OnLeave();
		entry.Layer.DetachRuntime();
		entry.Layer.Owner = null;
		entry.Runtime.Dispose();
		releaseTicker(entry.Ticker);
	}

	private void replace(Layer oldLayer, Layer newLayer, TickerHandle newTicker) {
		int idx = findEntryIdx(oldLayer);
		if (idx < 0) {
			if (ReferenceEquals(newLayer.Owner, this))
				newLayer.Owner = null;
			return;
		}

		LayerEntry oldEntry = entries[idx];
		TickerHandle oldTicker = oldEntry.Ticker;

		oldLayer.OnLeave();
		oldLayer.DetachRuntime();
		oldLayer.Owner = null;
		oldEntry.Runtime.Dispose();
		if (oldTicker != newTicker)
			releaseTicker(oldTicker);

		LayerRuntime newRuntime = new();
		newRuntime.InitActions(newLayer.ActionProfile);

		entries[idx] = new LayerEntry {
			Layer = newLayer,
			Ticker = newTicker,
			Runtime = newRuntime,
			InputCursor = input.CreateCursor(),
			Tags = newLayer.Tags,
		};

		if (oldTicker != newTicker)
			grabTicker(newTicker);
		newLayer.AttachRuntime(newRuntime);
		newLayer.OnEnter();
	}

	private void clear() {
		foreach (LayerEntry ent in entries) {
			ent.Layer.OnLeave();
			ent.Layer.DetachRuntime();
			ent.Layer.Owner = null;
			ent.Runtime.Dispose();
		}
		entries.Clear();
		foreach (TickerSubscription sub in subs.Values)
			tickers.Unsubscribe(sub.Ticker, sub.Callback);
		refcounts.Clear();
		subs.Clear();
		input.DiscardAll();
	}

	// ==========================================================================
	// bookkeeping
	private bool mentions(Layer layer) {
		bool ret = findEntryIdx(layer) >= 0;
		foreach (PendingOp op in pending) {
			switch (op.Kind) {
			case PendingOpKind.PushTop:
			case PendingOpKind.PushBottom:
				if (ReferenceEquals(op.Layer, layer))
					ret = true;
				break;
			case PendingOpKind.Remove:
				if (ReferenceEquals(op.Layer, layer))
					ret = false;
				break;
			case PendingOpKind.Replace:
				if (ReferenceEquals(op.Layer, layer))
					ret = false;
				if (ReferenceEquals(op.NewLayer, layer))
					ret = true;
				break;
			case PendingOpKind.Clear:
				ret = false;
				break;
			}
		}
		return ret;
	}

	private int findEntryIdx(Layer layer) {
		for (int i = 0; i < entries.Count; i++)
			if (ReferenceEquals(entries[i].Layer, layer))
				return i;
		return -1;
	}

	// ==========================================================================
	// ticker refcounting
	private void grabTicker(TickerHandle ticker) {
		if (refcounts.TryGetValue(ticker, out int n)) {
			refcounts[ticker] = n + 1;
			return;
		}

		TickerSubscription sub = new(this, ticker);
		if (!tickers.Subscribe(ticker, sub.Callback))
			throw new InternalStateException("failed to subscribe callback for ticker");

		refcounts.Add(ticker, 1);
		subs.Add(ticker, sub);
	}

	private void releaseTicker(TickerHandle ticker) {
		if (!refcounts.TryGetValue(ticker, out int n))
			return;

		if (n > 1) {
			refcounts[ticker] = n - 1;
			return;
		}

		refcounts.Remove(ticker);
		if (subs.Remove(ticker, out TickerSubscription? sub))
			tickers.Unsubscribe(sub.Ticker, sub.Callback);
	}
}
