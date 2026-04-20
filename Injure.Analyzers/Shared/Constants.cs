// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Injure.Analyzers.Shared;

// TODO: this was made before AttributeSources, decide whether it should be merged into there
internal static class Constants {
	public const string ClosedEnumGeneratedSourceSuffix = ".ClosedEnum.g.cs";
	public const string ClosedEnumBackingFieldName = "__ClosedEnum_tag";
	public static readonly FrozenSet<string> ClosedEnumReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
		"Case",
		"Tag",
		"Enum",
		"Equals",
		"GetHashCode",
		"ToString",
		ClosedEnumBackingFieldName,
		"__ClosedEnum_isDefined"
	}.ToFrozenSet(StringComparer.Ordinal);
	public static readonly FrozenSet<string> ClosedEnumNeutralZeroNames = new HashSet<string>(StringComparer.Ordinal) {
		"None",
		"Unknown",
		"Unspecified",
		"Undefined",
		"Invalid",
		"Default",
		"Empty",
		"Unset",
		"Null",
		"Zero",
		"NotHandled",
		"Unhandled",
		"Noop",
		"NoOp",
		"Ignore",
		"Ignored"
	}.ToFrozenSet(StringComparer.Ordinal);
	public static readonly ImmutableArray<string> ClosedEnumNeutralZeroPrefixes = ImmutableArray.Create(
		"No",
		"Not",
		"Without"
	);

	public const string StronglyTypedIntGeneratedSourceSuffix = ".StronglyTypedInt.g.cs";
	public const string StronglyTypedIntBackingFieldName = "__StronglyTypedInt_value";
}
