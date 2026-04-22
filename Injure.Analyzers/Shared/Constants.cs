// SPDX-License-Identifier: MIT

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Injure.Analyzers.Shared;

// TODO: this was made before AttributeSources, decide whether it should be merged into there
internal static class Constants {
	public static readonly FrozenSet<string> ClosedTypeNeutralZeroNames = new HashSet<string>(StringComparer.Ordinal) {
		"None",
		"Unknown",
		"Unspecified",
		"Undefined",
		"Invalid",
		"Default",
		"Normal",
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
	public static readonly ImmutableArray<string> ClosedTypeNeutralZeroPrefixes = ImmutableArray.Create(
		"No",
		"Not",
		"Without"
	);

	public const string ClosedEnumGeneratedSourceSuffix = ".ClosedEnum.g.cs";
	public const string ClosedEnumBackingFieldName = "__ClosedEnum_tag";
	public const string ClosedEnumIsDefinedMethodName = "__ClosedEnum_isDefined";
	public static readonly FrozenSet<string> ClosedEnumReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
		"Case",
		"Tag",
		"Enum",
		"Equals",
		"GetHashCode",
		"ToString",
		ClosedEnumBackingFieldName,
		ClosedEnumIsDefinedMethodName
	}.ToFrozenSet(StringComparer.Ordinal);

	public const string ClosedFlagsGeneratedSourceSuffix = ".ClosedFlags.g.cs";
	public const string ClosedFlagsBackingFieldName = "__ClosedFlags_bits";
	public const string ClosedFlagsAllBitsConstName = "__ClosedFlags_allBits";
	public const string ClosedFlagsIsDefinedMethodName = "__ClosedFlags_isDefined";
	public const string ClosedFlagsValidateMethodName = "__ClosedFlags_validate";
	public static readonly FrozenSet<string> ClosedFlagsReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) {
		"Bits",
		"Mask",
		"HasAny",
		"HasAll",
		"HasNone",
		"Flags",
		"Equals",
		"GetHashCode",
		"ToString",
		ClosedFlagsBackingFieldName,
		ClosedFlagsAllBitsConstName,
		ClosedFlagsIsDefinedMethodName,
		ClosedFlagsValidateMethodName
	}.ToFrozenSet(StringComparer.Ordinal);

	public const string StronglyTypedIntGeneratedSourceSuffix = ".StronglyTypedInt.g.cs";
	public const string StronglyTypedIntBackingFieldName = "__StronglyTypedInt_value";
}
