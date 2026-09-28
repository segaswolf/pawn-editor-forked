using System;

namespace PawnEditorInGameTests;

/// <summary>
/// Marks a public static, parameterless method as an in-game test. The runner discovers them by
/// reflection, so adding a test is writing the method: no registry to keep in sync.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class InGameTestAttribute : Attribute
{
}
