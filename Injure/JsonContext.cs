// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

using Injure.Assets.Builtin;

namespace Injure;

[JsonSerializable(typeof(Texture2DAssetMetadata))]
internal partial class InjureJsonContext : JsonSerializerContext {}
