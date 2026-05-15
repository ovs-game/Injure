// SPDX-License-Identifier: MIT

using System;
using System.Reflection;

namespace Injure.ModKit.Abstractions.MonoMod;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadHookAttribute(string targetID) : Attribute {
	public string TargetID { get; } = targetID;

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadILHookAttribute(string targetID) : Attribute {
	public string TargetID { get; } = targetID;

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadMethodHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : Attribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class LoadMethodILHookAttribute(Type targetType, string methodName, BindingFlags bindingFlags) : Attribute {
	public Type TargetType { get; } = targetType;
	public string MethodName { get; } = methodName;
	public BindingFlags BindingFlags { get; } = bindingFlags;

	public Type[]? ParameterTypes { get; init; }

	public string? OrderDomain { get; init; }
	public int LocalPriority { get; init; }
}
