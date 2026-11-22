using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace G3MLib;

internal static class ListExtensions
{
    /// <summary>The list's elements as a writable span, for in-place edits by reference. The list must not grow or shrink while the span is used.</summary>
    public static Span<T> Items<T>(this List<T> list) => CollectionsMarshal.AsSpan(list);

    /// <summary>Replaces every element with the result of <paramref name="replace"/>, in order. The callback must not add or remove elements.</summary>
    public static void ReplaceEach<T, TArg>(this List<T> list, TArg arg, Func<T, TArg, T> replace)
    {
        var items = CollectionsMarshal.AsSpan(list);
        foreach (ref var item in items)
        {
            item = replace(item, arg);
            if (list.Count != items.Length)
            {
                throw new InvalidOperationException("The list changed while its elements were being replaced.");
            }
        }
    }
}
