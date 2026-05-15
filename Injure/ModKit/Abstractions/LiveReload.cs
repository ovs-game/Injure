// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Injure.ModKit.Abstractions;

public interface IModLiveReload {
	ValueTask<ModLiveStateBlob> CaptureReloadStateAsync(CancellationToken ct);
	ValueTask RestoreReloadStateAsync(ModLiveStateBlob state, CancellationToken ct);
}
