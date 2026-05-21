using System;

namespace PawnEditor;

public static class Guard
{
    public static void NotNull(object value, string paramName)
    {
        if (value == null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}