/*
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at https://mozilla.org/MPL/2.0/.
*/

using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Decompiler.AST;

namespace G3MLib.Analyzer.Decompiler;

/// <summary>
/// Decompiled text together with its <see cref="SourceMap"/>.
/// </summary>
public sealed record DecompiledSource(string Text, SourceMap Map);

/// <summary>
/// One printed AST node: where it sits in the text and which bytecode it came from.
/// </summary>
public sealed class SourceMapEntry
{
    /// <summary>The AST node.</summary>
    public IBaseASTNode Node { get; }

    /// <summary>Text range of the node, without surrounding whitespace.</summary>
    public SourceSpan Span { get; }

    /// <summary>Index in <see cref="SourceMap.Entries"/> of the enclosing entry, or -1 for the root.</summary>
    public int Parent { get; }

    /// <summary>Whether the node is a statement (as opposed to an expression or other part).</summary>
    public bool IsStatement { get; }

    /// <summary>
    /// Byte offset of the instruction that produced this node, or -1 when the node was created by control flow
    /// analysis or by a cleanup pass and has no instruction of its own.
    /// </summary>
    public int InstructionOffset { get; }

    /// <summary>Lowest instruction byte offset among this node and all its children, or -1 when none has one.</summary>
    public int FirstInstructionOffset { get; }

    /// <summary>Highest instruction byte offset among this node and all its children, or -1 when none has one.</summary>
    public int LastInstructionOffset { get; }

    internal SourceMapEntry(IBaseASTNode node, SourceSpan span, int parent, int instructionOffset, int first, int last)
    {
        Node = node;
        Span = span;
        Parent = parent;
        IsStatement = node is IStatementNode;
        InstructionOffset = instructionOffset;
        FirstInstructionOffset = first;
        LastInstructionOffset = last;
    }
}

/// <summary>
/// Maps ranges of decompiled text to AST nodes and bytecode instruction offsets, and back.
/// Instruction offsets are byte offsets within the code entry, the same values the disassembler
/// derives its addresses from (four bytes per address unit).
/// </summary>
public sealed class SourceMap
{
    /// <summary>Entries in document order: a parent comes before its children, siblings by position.</summary>
    public IReadOnlyList<SourceMapEntry> Entries { get; }

    /// <summary>Line and column lookup for the decompiled text.</summary>
    public LineMap Lines { get; }

    internal SourceMap(List<SourceMapEntry> entries, LineMap lines)
    {
        Entries = entries;
        Lines = lines;
    }

    /// <summary>Returns the innermost entry that contains the text offset, or <see langword="null"/>.</summary>
    public SourceMapEntry? FindAtTextOffset(int offset)
    {
        SourceMapEntry? best = null;
        foreach (var entry in Entries)
        {
            if (entry.Span.StartOffset > offset)
            {
                break;
            }
            if (entry.Span.Contains(offset))
            {
                best = entry;
            }
        }
        return best;
    }

    /// <summary>
    /// Returns the innermost entry for a bytecode instruction: one produced by exactly that instruction if there
    /// is one, otherwise the narrowest entry whose instruction range includes the offset.
    /// </summary>
    public SourceMapEntry? FindAtInstruction(int instructionOffset)
    {
        SourceMapEntry? exact = null;
        SourceMapEntry? range = null;
        foreach (var entry in Entries)
        {
            if (entry.InstructionOffset == instructionOffset)
            {
                exact = entry;
            }
            if (entry.FirstInstructionOffset >= 0 && entry.FirstInstructionOffset <= instructionOffset && instructionOffset <= entry.LastInstructionOffset)
            {
                range = entry;
            }
        }
        return exact ?? range;
    }

    /// <summary>
    /// Returns the first statement (other than a plain block) that starts on the one-based line and has bytecode, or <see langword="null"/>.
    /// Its <see cref="SourceMapEntry.FirstInstructionOffset"/> is where a breakpoint on that line goes.
    /// </summary>
    public SourceMapEntry? FindStatementOnLine(int line)
    {
        foreach (var entry in Entries)
        {
            if (entry.Span.StartLine > line)
            {
                break;
            }
            if (entry.IsStatement && entry.Node is not BlockNode && entry.Span.StartLine == line && entry.FirstInstructionOffset >= 0)
            {
                return entry;
            }
        }
        return null;
    }
}

// Collects node text ranges while the printer runs, then resolves them into a SourceMap.
internal sealed class SourceMapRecorder
{
    private sealed class Record(IBaseASTNode node, int start, int parent)
    {
        public readonly IBaseASTNode Node = node;
        public readonly int Start = start;
        public readonly int Parent = parent;
        public int End;
    }

    private readonly List<Record> _records = new(256);
    private readonly Stack<int> _open = new();

    public int Begin(IBaseASTNode node, int start)
    {
        _records.Add(new Record(node, start, _open.Count > 0 ? _open.Peek() : -1));
        _open.Push(_records.Count - 1);
        return _records.Count - 1;
    }

    public void End(int index, int end)
    {
        _records[index].End = end;
        _open.Pop();
    }

    public SourceMap Build(string text, Dictionary<IBaseASTNode, int> nodeOffsets)
    {
        LineMap lines = new(text);
        Dictionary<IBaseASTNode, (int First, int Last)> ranges = new(ReferenceEqualityComparer.Instance);
        List<SourceMapEntry> entries = new(_records.Count);

        // Record index to entry index; dropped (empty) records map to their nearest kept ancestor
        var entryOf = new int[_records.Count];
        for (var i = 0; i < _records.Count; i++)
        {
            var record = _records[i];
            var start = record.Start;
            var end = Math.Min(record.End, text.Length);
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
            var parent = record.Parent < 0 ? -1 : entryOf[record.Parent];
            if (start >= end)
            {
                entryOf[i] = parent;
                continue;
            }

            (var first, var last) = InstructionRange(record.Node, nodeOffsets, ranges);
            var own = nodeOffsets.TryGetValue(record.Node, out var ownOffset) ? ownOffset : -1;
            entries.Add(new SourceMapEntry(record.Node, lines.GetSpan(start, end), parent, own, first, last));
            entryOf[i] = entries.Count - 1;
        }
        return new SourceMap(entries, lines);
    }

    private static (int First, int Last) InstructionRange(IBaseASTNode node, Dictionary<IBaseASTNode, int> nodeOffsets,
                                                          Dictionary<IBaseASTNode, (int First, int Last)> memo)
    {
        if (memo.TryGetValue(node, out var known))
        {
            return known;
        }
        var first = int.MaxValue;
        var last = -1;
        if (nodeOffsets.TryGetValue(node, out var own))
        {
            first = last = own;
        }
        foreach (var child in node.EnumerateChildren())
        {
            (var childFirst, var childLast) = InstructionRange(child, nodeOffsets, memo);
            if (childLast >= 0)
            {
                first = Math.Min(first, childFirst);
                last = Math.Max(last, childLast);
            }
        }
        var result = last < 0 ? (-1, -1) : (first, last);
        memo[node] = result;
        return result;
    }
}
