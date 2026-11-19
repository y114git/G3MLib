using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Decompiler;

namespace G3MLib.Tests;

public sealed class CompilerSyntaxTests
{
    private static void AssertCompiles(string source)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        group.QueueReplace("gml_Script_probe", source);
        group.Import();
        Assert.NotNull(data.Code.ByName("gml_Script_probe"));
    }

    [Fact]
    public void ReturnWithoutValueBeforeBlockClose()
    {
        AssertCompiles("function f(a)\n{\n\tif (a)\n\t{\n\t\treturn\n\t}\n\ta = 1;\n}");
    }

    [Fact]
    public void ReturnWithoutValueBeforeEnd()
    {
        AssertCompiles("if (argument0) begin return end");
    }

    [Fact]
    public void ArrayAccessOnFunctionCallResult()
    {
        AssertCompiles("var a = array_create(2)[0];\nvar b = array_create(2)[1];");
    }

    [Fact]
    public void ArrayAccessOnFunctionCallResultAsArgument()
    {
        AssertCompiles("show_debug_message(array_create(4, 0)[2]);");
    }

    [Fact]
    public void ChainedAccessOnFunctionCallResult()
    {
        AssertCompiles("var a = array_create(2)[0][1];");
    }

    [Fact]
    public void TemplateStringCompilesOnLts2026()
    {
        AssertCompiles("var n = 3;\nvar s = $\"WAVE {n}\";");
    }

    [Fact]
    public void NestedTernaryAndChainedNullishCompile()
    {
        AssertCompiles("var a = 1;\nvar b = a ? 1 : a ? 2 : 3;\nvar c = a ? (a ? 1 : 2) : 3;\nvar d = a ?? b ?? 5;");
    }

    [Fact]
    public void TrailingCommaInArrayAndStructLiterals()
    {
        AssertCompiles("var a = [1, 2,];\nvar b = { x : 1, y : [3,], };");
    }

    // Compiles the entries in order and returns the disassembly and referenced variable names of "gml_Script_user".
    private static (string Assembly, HashSet<string> Variables) CompileUser(string userSource, string? definerSource, Action<CodeImportGroup>? configure = null)
    {
        using var data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup group = new(data);
        configure?.Invoke(group);
        if (definerSource is not null)
            group.QueueReplace("gml_Script_definer", definerSource);
        group.QueueReplace("gml_Script_user", userSource);
        group.Import();
        var code = data.Code.ByName("gml_Script_user");
        return (code.Disassemble(data.Variables, null, ignoreMissingCodeLocals: true), code.FindReferencedVars().Select(v => v.Name.Content).ToHashSet());
    }

    [Fact]
    public void MacroFromAnotherEntryIsReplacedByItsValue()
    {
        var (assembly, variables) = CompileUser("var n = LIMIT + 1;", "#macro LIMIT 4242");
        Assert.Contains("pushi.e 4243", assembly);
        Assert.DoesNotContain("LIMIT", variables);
    }

    [Fact]
    public void MacroFromAnotherEntryKeepsExpressionPrecedence()
    {
        var (assembly, _) = CompileUser("var n = 2 * SUM;", "#macro SUM 1 + 3");
        Assert.Contains("pushi.e 8", assembly); // 2 * (1 + 3), not 2 * 1 + 3
    }

    [Fact]
    public void LocalVariableWinsOverMacroFromAnotherEntry()
    {
        var (assembly, _) = CompileUser("var LIMIT = 7;\nvar n = LIMIT;", "#macro LIMIT 4242");
        Assert.DoesNotContain("4242", assembly);
    }

    [Fact]
    public void UnknownNameStaysAnInstanceVariable()
    {
        var (_, variables) = CompileUser("var n = OTHER;", "#macro LIMIT 4242");
        Assert.Contains("OTHER", variables);
    }

    [Fact]
    public void MacroTableGivenByTheCallerIsApplied()
    {
        var (assembly, variables) = CompileUser("var n = LIMIT;", null, group => group.Macros["LIMIT"] = "4242");
        Assert.Contains("4242", assembly);
        Assert.DoesNotContain("LIMIT", variables);
    }

    [Fact]
    public void MacroMayUseAnotherExternalMacro()
    {
        var (assembly, variables) = CompileUser("var n = TOP;", "#macro BASE 4242\n#macro TOP BASE");
        Assert.Contains("4242", assembly);
        Assert.Empty(variables.Intersect(["BASE", "TOP"]));
    }

    [Fact]
    public void SelfReferencingMacroFailsInsteadOfLooping()
    {
        var task = Task.Run(() => Record.Exception(() => CompileUser("var n = LOOP;", null, group => group.Macros["LOOP"] = "LOOP + 1")), TestContext.Current.CancellationToken);
        Assert.True(task.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken), "the compile did not end");
        Assert.NotNull(task.Result);
    }

    [Fact]
    public void MacrosThatDoubleAtEveryLevelStopAtTheSizeLimit()
    {
        var task = Task.Run(() => Record.Exception(() => CompileUser("var n = M0;", null, group =>
        {
            for (var i = 0; i < 30; i++) group.Macros["M" + i] = $"M{i + 1} + M{i + 1}";
            group.Macros["M30"] = "1";
        })), TestContext.Current.CancellationToken);
        Assert.True(task.Wait(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken), "the compile did not end");
        Assert.NotNull(task.Result);
    }
}
