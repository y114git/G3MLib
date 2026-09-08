using System.Collections.Generic;
using System.Runtime.InteropServices;
using G3MLib.Analyzer;
using G3MLib.Analyzer.Decompiler.AST;
using G3MLib.Analyzer.Mock;
using Xunit;

namespace G3MLib.Tests;

public class VariableNodeTests
{
    [Fact("VariableNodeTests.cs", 9)]
    public void ComparisonDistinguishesArrayAccess()
    {
        GMVariable variable = new(new GMString("value"));
        VariableNode first = new(variable, IGMInstruction.VariableType.Normal, new Int16Node(7, regularPush: false));
        VariableNode second = new(variable, IGMInstruction.VariableType.Normal, new Int16Node(7, regularPush: false));
        Assert.True(first.IdenticalToInExpression(second));
        Assert.True(first.SimilarToInForIncrementor(second));
        int num = 1;
        List<IExpressionNode> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        CollectionsMarshal.AsSpan(list)[0] = new Int16Node(0, regularPush: false);
        first.ArrayIndices = list;
        Assert.False(first.IdenticalToInExpression(second));
        Assert.False(first.SimilarToInForIncrementor(second));
    }
}
