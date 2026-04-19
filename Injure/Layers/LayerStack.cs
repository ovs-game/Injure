// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

using Injure.Graphics;
using Injure.Input;
using Injure.Scheduling;

namespace Injure.Layers;

public sealed class LayerStack(ITickerRegistry tickers) : IDisposable {
	// ==========================================================================
	// internal types
	private sealed class LayerEntry {
		public required Layer Layer { get; init; }
		public required TickerHandle Ticker { get; set; }
		//public InputViewScratch Scratch { get; } = new InputViewScratch();
		//public InputEventSeq NextInputSeq { get; set; }
		public LayerTagSet Tags;
	}

	private readonly struct ResolvedPassState(LayerPassMask activePasses) {
		public readonly LayerPassMask ActivePasses = activePasses;
		public bool Has(LayerPassMask pass) => (ActivePasses & pass) != 0;
	}

	private struct BlockAccumulator {
		private LayerTagSet updateTags;
		private LayerTagSet renderTags;
		private LayerTagSet inputTags;

		public readonly bool IsBlocked(LayerPassMask pass, in LayerTagSet tags) {
			if (pass == LayerPassMask.Update)
				return updateTags.Intersects(in tags);
			if (pass == LayerPassMask.Render)
				return renderTags.Intersects(in tags);
			if (pass == LayerPassMask.Input)
				return inputTags.Intersects(in tags);
			return false;
		}

		public void AddRules(ReadOnlySpan<LayerBlockRule> rules, LayerPassMask activePasses) {
			for (int i = 0; i < rules.Length; i++) {
				ref readonly LayerBlockRule rule = ref rules[i];
				LayerPassMask passes = rule.Passes & activePasses;
				if ((passes & LayerPassMask.Update) != 0)
					merge(ref updateTags, rule.MatchTags);
				if ((passes & LayerPassMask.Render) != 0)
					merge(ref renderTags, rule.MatchTags);
				if ((passes & LayerPassMask.Input) != 0)
					merge(ref inputTags, rule.MatchTags);
			}
		}

		private static void merge(ref LayerTagSet dst, in LayerTagSet src) {
			foreach (LayerTag tag in src.AsSpan())
				dst.Add(tag);
		}
	}

	private enum PendingOpKind {
		PushTop,
		PushBottom,
		Remove,
		Replace,
		Clear
	}

	private readonly record struct PendingOp(PendingOpKind Kind, Layer? Layer, Layer? NewLayer, TickerHandle Ticker);

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
	// internal objects / properties
	private readonly ITickerRegistry tickers = tickers ?? throw new ArgumentNullException(nameof(tickers));
	private readonly List<LayerEntry> entries = new List<LayerEntry>(); // bottom to top
	private readonly List<PendingOp> pending = new List<PendingOp>();

	private readonly Dictionary<TickerHandle, int> refcounts = new Dictionary<TickerHandle, int>();
	private readonly Dictionary<TickerHandle, TickerSubscription> subs = new Dictionary<TickerHandle, TickerSubscription>();

	private bool disposed = false;
	private bool applying = false;
	private int callbackDepth = 0;
	private int renderDepth = 0;

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
		if (mentions(layer))
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
		if (mentions(oldLayer))
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
	// public api (render, IDisposable)
	public void Render(Canvas cv) {
		ObjectDisposedException.ThrowIf(disposed, this);
		maybeApplyPending();
		renderDepth++;
		try {
			foreach (LayerEntry ent in entries)
				ent.Layer.RenderCore(cv);
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
			Span<ResolvedPassState> states = entries.Count <= 64 ? stackalloc ResolvedPassState[entries.Count] : new ResolvedPassState[entries.Count];
			resolvePassStates(states);

			/*
			InputEventSeq nowSeq = InputCollector.NextSeq;
			for (int i = 0; i < entries.Count; i++) {
				LayerEntry ent = entries[i];
				if ((ent.Layer.ParticipatingPasses & LayerPassMask.Input) == 0)
					continue;
				if (!states[i].Has(LayerPassMask.Input))
					ent.NextInputSeq = nowSeq;
			}
			*/

			double rawDt = info.Elapsed.ToSeconds();
			for (int i = entries.Count - 1; i >= 0; i--) {
				LayerEntry ent = entries[i];
				ref readonly ResolvedPassState st = ref states[i];
				LayerTimeDomain tm = ent.Layer.Runtime!.Time;
				if (!st.Has(LayerPassMask.Update))
					continue;
				if (ent.Ticker != ticker)
					continue;
				bool consumeInput = st.Has(LayerPassMask.Input);
				/*
				ReadOnlySpan<InputActionEvent> events = consumeInput ?
					InputCollector.ReadSince(ent.NextInputSeq) :
					ReadOnlySpan<InputActionEvent>.Empty;
				InputView input = new InputView(events, ent.Scratch);
				*/

				double dt = tm.Transform(rawDt);
				tm.Advance(dt, rawDt);
				LayerTickContext ctx = new LayerTickContext(info.ActualAt, dt, rawDt, tm.Time, tm.RawTime, tm.TickNum, InputView.Rest);
				ent.Layer.UpdateCore(in ctx);
				/*
				if (consumeInput)
					ent.NextInputSeq = InputCollector.NextSeq;
				*/
			}
		} finally {
			callbackDepth--;
		}
		applyPending();
		/*
		if (tryGetOldestLiveInputSeq(out InputEventSeq oldest))
			InputCollector.DiscardBefore(oldest);
		else
			InputCollector.DiscardAll();
		*/
	}

	private void resolvePassStates(Span<ResolvedPassState> dst) {
		if (dst.Length < entries.Count)
			throw new ArgumentException("destination span too small", nameof(dst));
		BlockAccumulator blocked = default;
		for (int i = entries.Count - 1; i >= 0; i--) {
			LayerEntry ent = entries[i];
			LayerPassMask ptcp = ent.Layer.ParticipatingPasses;
			LayerPassMask active = LayerPassMask.None;
			if ((ptcp & LayerPassMask.Update) != 0 && !blocked.IsBlocked(LayerPassMask.Update, in ent.Tags))
				active |= LayerPassMask.Update;
			if ((ptcp & LayerPassMask.Render) != 0 && !blocked.IsBlocked(LayerPassMask.Render, in ent.Tags))
				active |= LayerPassMask.Render;
			if ((ptcp & LayerPassMask.Input) != 0 && !blocked.IsBlocked(LayerPassMask.Input, in ent.Tags))
				active |= LayerPassMask.Input;
			dst[i] = new ResolvedPassState(active);
			if (active != LayerPassMask.None)
				blocked.AddRules(ent.Layer.BlockRules, active);
		}
	}

	/*
	private bool tryGetOldestLiveInputSeq(out InputEventSeq oldest) {
		bool found = false;
		oldest = InputEventSeq.Zero;
		foreach (LayerEntry ent in entries) {
			if ((ent.Layer.ParticipatingPasses & LayerPassMask.Input) == 0)
				continue;
			if (!found || ent.NextInputSeq < oldest) {
				oldest = ent.NextInputSeq;
				found = true;
			}
		}
		return found;
	}
	*/

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
					case PendingOpKind.PushTop:    enter(op.Layer!, op.Ticker, true); break;
					case PendingOpKind.PushBottom: enter(op.Layer!, op.Ticker, false); break;
					case PendingOpKind.Remove:     leave(op.Layer!); break;
					case PendingOpKind.Replace:    replace(op.Layer!, op.NewLayer!, op.Ticker); break;
					case PendingOpKind.Clear:      clear(); break;
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
		LayerEntry ent = new LayerEntry {
			Layer = layer,
			Ticker = ticker,
			//NextInputSeq = 0,//InputCollector.NextSeq,
			Tags = new LayerTagSet(layer.Tags)
		};
		if (pushToTop)
			entries.Add(ent);
		else
			entries.Insert(0, ent);
		grabTicker(ticker);
		layer.OnEnterCore();
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
		entry.Layer.OnLeaveCore();
		entry.Layer.Owner = null;
		releaseTicker(entry.Ticker);
	}

	private void replace(Layer oldLayer, Layer newLayer, TickerHandle newTicker) {
		int idx = findEntryIdx(oldLayer);
		if (idx < 0) {
			// old layer disappeared before the replace got applied
			if (ReferenceEquals(newLayer.Owner, this))
				newLayer.Owner = null;
			return;
		}
		TickerHandle oldTicker = entries[idx].Ticker;
		oldLayer.OnLeaveCore();
		oldLayer.Owner = null;
		if (oldTicker != newTicker)
			releaseTicker(oldTicker);
		entries[idx] = new LayerEntry {
			Layer = newLayer,
			Ticker = newTicker,
			//NextInputSeq = 0,//InputCollector.NextSeq,
			Tags = new LayerTagSet(newLayer.Tags)
		};
		if (oldTicker != newTicker)
			grabTicker(newTicker);
		newLayer.OnEnterCore();
	}

	private void clear() {
		foreach (LayerEntry ent in entries) {
			ent.Layer.OnLeaveCore();
			ent.Layer.Owner = null;
		}
		entries.Clear();
		foreach (TickerSubscription sub in subs.Values)
			tickers.Unsubscribe(sub.Ticker, sub.Callback);
		refcounts.Clear();
		subs.Clear();
		//InputCollector.DiscardAll();
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

		TickerSubscription sub = new TickerSubscription(this, ticker);
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
		} else {
			refcounts.Remove(ticker);
			if (subs.Remove(ticker, out TickerSubscription? sub))
				tickers.Unsubscribe(sub.Ticker, sub.Callback);
		}
	}
}
