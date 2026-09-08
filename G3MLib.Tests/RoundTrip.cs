using G3MLib.Analyzer;
using G3MLib.Analyzer.Compiler;
using G3MLib.Analyzer.Compiler.Bytecode;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Mock;
using Xunit;

namespace G3MLib.Tests;

public class RoundTrip
{
    [Fact("RoundTrip.cs", 14)]
    public void TestEmpty()
    {
        TestUtil.VerifyRoundTrip("");
    }

    [Fact("RoundTrip.cs", 20)]
    public void TestSimpleReturns()
    {
        TestUtil.VerifyRoundTrip("return 123;\nreturn true;\nreturn false;\nreturn \"test string\";\nreturn 123123123123123123;");
    }

    [Fact("RoundTrip.cs", 34)]
    public void TestIf()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    if (d)\n    {\n    }\n    else\n    {\n    }\n}\nelse if (b)\n{\n    if (c)\n    {\n    }\n}");
    }

    [Fact("RoundTrip.cs", 58)]
    public void TestWhileIfContinueBreak()
    {
        TestUtil.VerifyRoundTrip("top = 1;\nwhile (a)\n{\n    if (b)\n    {\n        continue;\n    }\n    else\n    {\n        break;\n    }\n}\nbottom = 2;");
    }

    [Fact("RoundTrip.cs", 80)]
    public void TestForIfContinueBreak()
    {
        TestUtil.VerifyRoundTrip("top = 1;\nfor (var i = 0; i < 10; i++)\n{\n    if (a)\n    {\n        break;\n    }\n    continue;\n}\nbottom = 2;");
    }

    [Fact("RoundTrip.cs", 99)]
    public void TestDoUntilContinueBreak()
    {
        TestUtil.VerifyRoundTrip("top = 1;\ndo\n{\n    if (a)\n    {\n        break;\n    }\n    continue;\n}\nuntil (b);\nbottom = 2;");
    }

    [Fact("RoundTrip.cs", 119)]
    public void TestConditional()
    {
        TestUtil.VerifyRoundTrip("basic = a ? b : c;\nbasic_cast = a ? 1 : 2;\nnested = (a ? b : c) ? d : (e ? f : g);");
    }

    [Fact("RoundTrip.cs", 131)]
    public void TestBinaryAndUnary()
    {
        TestUtil.VerifyRoundTrip("a = b + (c - d);\na = (b + c) - d;\na = (b * c) / d;\na = (b div c) % d;\na = b + c + d + e;\na = b - c - d - 5;\na = b * c * 5 * e;\na = b && c && d;\na = b || c || d;\na = (b && c) || (d && e);\na = b << (c >> d);\na = b ^ (c & (d | e));\na = !b + ~b + -b;");
    }

    [Fact("RoundTrip.cs", 153)]
    public void TestTypesOld()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = false,
            UsingTypedBooleans = false
        };
        mock.DefineMockAsset(AssetType.Sprite, 8, "spr_test");
        TestUtil.VerifyRoundTrip("a = spr_test;\nb = true;", "a = 8;\nb = 1;", isGlobalScript: false, mock);
    }

    [Fact("RoundTrip.cs", 176)]
    public void TestTypesNew()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = true,
            UsingTypedBooleans = true
        };
        mock.DefineMockAsset(AssetType.Sprite, 8, "spr_test");
        TestUtil.VerifyRoundTrip("a = spr_test;\nb = true;", isGlobalScript: false, mock);
    }

    [Fact("RoundTrip.cs", 195)]
    public void TestWithContinueBreakExit()
    {
        GameContextMock mock = new()
        {
            UsingAssetReferences = true,
            UsingTypedBooleans = true
        };
        mock.DefineMockAsset(AssetType.Object, 8, "obj_test");
        TestUtil.VerifyRoundTrip("with (-3)\n{\n    a = 123;\n    return 0;\n}\nwith (abc)\n{\n    b = 456;\n    exit;\n}\nwith (obj_test)\n{\n    if (c)\n    {\n        continue;\n    }\n    else\n    {\n        break;\n    }\n}", isGlobalScript: false, mock);
    }

    [Fact("RoundTrip.cs", 233)]
    public void TestRepeatContinueBreakExit()
    {
        TestUtil.VerifyRoundTrip("repeat (123)\n{\n    if (a)\n    {\n        continue;\n    }\n    else if (b)\n    {\n        exit;\n    }\n    else\n    {\n        break;\n    }\n}");
    }

    [Fact("RoundTrip.cs", 257)]
    public void TestSwitchContinueBreakExit()
    {
        TestUtil.VerifyRoundTrip("repeat (a)\n{\n    switch (b)\n    {\n        case 123:\n            c = 0;\n            break;\n        case 456:\n            d = 0;\n            break;\n            return 123;\n        case 789:\n            continue;\n        default:\n            exit;\n    }\n}", isGlobalScript: false, new GameContextMock
        {
            UsingExtraRepeatInstruction = true
        });
        TestUtil.VerifyRoundTrip("repeat (a)\n{\n    switch (b)\n    {\n        case 123:\n            c = 0;\n            break;\n        case 456:\n            d = 0;\n            break;\n            return 123;\n        case 789:\n            continue;\n        default:\n            exit;\n    }\n}", isGlobalScript: false, new GameContextMock
        {
            UsingExtraRepeatInstruction = false
        });
    }

    [Fact("RoundTrip.cs", 290)]
    public void TestAssignments()
    {
        TestUtil.VerifyRoundTrip("a = 0;\na.b = 0;\na.b.c = 0;\na[0] = 1;\na.b[0] = 1;\na.b.c[0] = 1;\na[0].b[1] = 2;\na[0].b[1].c = 2;\nglobal.a = 0;\nglobal.a.b = 0;\nglobal.a.b.c = 0;\nglobal.a[0] = 1;\nglobal.a.b[0] = 1;\nglobal.a.b.c[0] = 1;\nglobal.a[0].b[1] = 2;\nglobal.a[0].b[1].c = 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip("a = 0;\na.b = 0;\na.b.c = 0;\na[0] = 1;\na.b[0] = 1;\na.b.c[0] = 1;\na[0].b[1] = 2;\na[0].b[1].c = 2;\nglobal.a = 0;\nglobal.a.b = 0;\nglobal.a.b.c = 0;\nglobal.a[0] = 1;\nglobal.a.b[0] = 1;\nglobal.a.b.c[0] = 1;\nglobal.a[0].b[1] = 2;\nglobal.a[0].b[1].c = 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 322)]
    public void TestAssignments2dArray()
    {
        TestUtil.VerifyRoundTrip("a[0, 1] = 2;\na.b[0, 1] = 2;\na.b.c[0, 1] = 2;\na[0, 1].b[0, 1] = 2;\na[0, 1].b[0, 1].c = 2;\nglobal.a[0, 1] = 2;\nglobal.a.b[0, 1] = 2;\nglobal.a.b.c[0, 1] = 2;\nglobal.a[0, 1].b[0, 1] = 2;\nglobal.a[0, 1].b[0, 1].c = 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 345)]
    public void TestCompoundAssignments()
    {
        TestUtil.VerifyRoundTrip("a += 1;\na.b += 1;\na.b.c += 1;\na[0] += 1;\na.b[0] += 1;\na.b.c[0] += 1;\na[0].b[1] += 2;\na[0].b[1].c += 2;\nglobal.a += 1;\nglobal.a.b += 1;\nglobal.a.b.c += 1;\nglobal.a[0] += 1;\nglobal.a.b[0] += 1;\nglobal.a.b.c[0] += 1;\nglobal.a[0].b[1] += 2;\nglobal.a[0].b[1].c += 2;\na.b = a.b + 1;\na.b.c = a.b.c + 1;\na[0] = a[0] + 1;\na.b[0] = a.b[0] + 1;\na.b.c[0] = a.b.c[0] + 1;\na[0].b[1] = a[0].b[1] + 2;\na[0].b[1].c = a[0].b[1].c + 2;\nglobal.a = global.a + 1;\nglobal.a.b = global.a.b + 1;\nglobal.a.b.c = global.a.b.c + 1;\nglobal.a[0] = global.a[0] + 1;\nglobal.a.b[0] = global.a.b[0] + 1;\nglobal.a.b.c[0] = global.a.b.c[0] + 1;\nglobal.a[0].b[1] = global.a[0].b[1] + 2;\nglobal.a[0].b[1].c = global.a[0].b[1].c + 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip("a += 1;\na.b += 1;\na.b.c += 1;\na[0] += 1;\na.b[0] += 1;\na.b.c[0] += 1;\na[0].b[1] += 2;\na[0].b[1].c += 2;\nglobal.a += 1;\nglobal.a.b += 1;\nglobal.a.b.c += 1;\nglobal.a[0] += 1;\nglobal.a.b[0] += 1;\nglobal.a.b.c[0] += 1;\nglobal.a[0].b[1] += 2;\nglobal.a[0].b[1].c += 2;\na.b = a.b + 1;\na.b.c = a.b.c + 1;\na[0] = a[0] + 1;\na.b[0] = a.b[0] + 1;\na.b.c[0] = a.b.c[0] + 1;\na[0].b[1] = a[0].b[1] + 2;\na[0].b[1].c = a[0].b[1].c + 2;\nglobal.a = global.a + 1;\nglobal.a.b = global.a.b + 1;\nglobal.a.b.c = global.a.b.c + 1;\nglobal.a[0] = global.a[0] + 1;\nglobal.a.b[0] = global.a.b[0] + 1;\nglobal.a.b.c[0] = global.a.b.c[0] + 1;\nglobal.a[0].b[1] = global.a[0].b[1] + 2;\nglobal.a[0].b[1].c = global.a[0].b[1].c + 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 392)]
    public void TestCompoundAssignments2dArray()
    {
        TestUtil.VerifyRoundTrip("a[0, 1] += 2;\na.b[0, 1] += 2;\na.b.c[0, 1] += 2;\na[0, 1].b[0, 1] += 2;\na[0, 1].b[0, 1].c += 2;\nglobal.a[0, 1] += 2;\nglobal.a.b[0, 1] += 2;\nglobal.a.b.c[0, 1] += 2;\nglobal.a[0, 1].b[0, 1] += 2;\nglobal.a[0, 1].b[0, 1].c += 2;\na[0, 1] = a[0, 1] + 2;\na.b[0, 1] = a.b[0, 1] + 2;\na.b.c[0, 1] = a.b.c[0, 1] + 2;\na[0, 1].b[0, 1] = a[0, 1].b[0, 1] + 2;\na[0, 1].b[0, 1].c = a[0, 1].b[0, 1].c + 2;\nglobal.a[0, 1] = global.a[0, 1] + 2;\nglobal.a.b[0, 1] = global.a.b[0, 1] + 2;\nglobal.a.b.c[0, 1] = global.a.b.c[0, 1] + 2;\nglobal.a[0, 1].b[0, 1] = global.a[0, 1].b[0, 1] + 2;\nglobal.a[0, 1].b[0, 1].c = global.a[0, 1].b[0, 1].c + 2;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 425)]
    public void TestPrefixPostfix()
    {
        TestUtil.VerifyRoundTrip("a++;\na.b++;\na.b.c++;\na[0]++;\na.b[0]++;\na.b.c[0]++;\na[0].b[1]++;\na[0].b[1].c++;\nglobal.a++;\nglobal.a.b++;\nglobal.a.b.c++;\nglobal.a[0]++;\nglobal.a.b[0]++;\nglobal.a.b.c[0]++;\nglobal.a[0].b[1]++;\nglobal.a[0].b[1].c++;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip("a++;\na.b++;\na.b.c++;\na[0]++;\na.b[0]++;\na.b.c[0]++;\na[0].b[1]++;\na[0].b[1].c++;\nglobal.a++;\nglobal.a.b++;\nglobal.a.b.c++;\nglobal.a[0]++;\nglobal.a.b[0]++;\nglobal.a.b.c[0]++;\nglobal.a[0].b[1]++;\nglobal.a[0].b[1].c++;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 457)]
    public void TestPrefixExpr()
    {
        TestUtil.VerifyRoundTrip("d = ++a;\nd = ++a.b;\nd = ++a.b.c;\nd = ++a[0];\nd = ++a.b[0];\nd = ++a.b.c[0];\nd = ++a[0].b[1];\nd = ++a[0].b[1].c;\nd = ++global.a;\nd = ++global.a.b;\nd = ++global.a.b.c;\nd = ++global.a[0];\nd = ++global.a.b[0];\nd = ++global.a.b.c[0];\nd = ++global.a[0].b[1];\nd = ++global.a[0].b[1].c;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip("d = ++a;\nd = ++a.b;\nd = ++a.b.c;\nd = ++a[0];\nd = ++a.b[0];\nd = ++a.b.c[0];\nd = ++a[0].b[1];\nd = ++a[0].b[1].c;\nd = ++global.a;\nd = ++global.a.b;\nd = ++global.a.b.c;\nd = ++global.a[0];\nd = ++global.a.b[0];\nd = ++global.a.b.c[0];\nd = ++global.a[0].b[1];\nd = ++global.a[0].b[1].c;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 489)]
    public void TestPostfixExpr()
    {
        TestUtil.VerifyRoundTrip("d = a++;\nd = a.b++;\nd = a.b.c++;\nd = a[0]++;\nd = a.b[0]++;\nd = a.b.c[0]++;\nd = a[0].b[1]++;\nd = a[0].b[1].c++;\nd = global.a++;\nd = global.a.b++;\nd = global.a.b.c++;\nd = global.a[0]++;\nd = global.a.b[0]++;\nd = global.a.b.c[0]++;\nd = global.a[0].b[1]++;\nd = global.a[0].b[1].c++;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = true
        });
        TestUtil.VerifyRoundTrip("d = a++;\nd = a.b++;\nd = a.b.c++;\nd = a[0]++;\nd = a.b[0]++;\nd = a.b.c[0]++;\nd = a[0].b[1]++;\nd = a[0].b[1].c++;\nd = global.a++;\nd = global.a.b++;\nd = global.a.b.c++;\nd = global.a[0]++;\nd = global.a.b[0]++;\nd = global.a.b.c[0]++;\nd = global.a[0].b[1]++;\nd = global.a[0].b[1].c++;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 521)]
    public void TestPrefixExpr2dArray()
    {
        TestUtil.VerifyRoundTrip("d = ++a[0, 1];\nd = ++a.b[0, 1];\nd = ++a.b.c[0, 1];\nd = ++a[0, 1].b[0, 1];\nd = ++a[0, 1].b[0, 1].c;\nd = ++global.a[0, 1];\nd = ++global.a.b[0, 1];\nd = ++global.a.b.c[0, 1];\nd = ++global.a[0, 1].b[0, 1];\nd = ++global.a[0, 1].b[0, 1].c;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 544)]
    public void TestPostfixExpr2dArray()
    {
        TestUtil.VerifyRoundTrip("d = a[0, 1]++;\nd = a.b[0, 1]++;\nd = a.b.c[0, 1]++;\nd = a[0, 1].b[0, 1]++;\nd = a[0, 1].b[0, 1].c++;\nd = global.a[0, 1]++;\nd = global.a.b[0, 1]++;\nd = global.a.b.c[0, 1]++;\nd = global.a[0, 1].b[0, 1]++;\nd = global.a[0, 1].b[0, 1].c++;", isGlobalScript: false, new GameContextMock
        {
            UsingGMLv2 = false
        });
    }

    [Fact("RoundTrip.cs", 567)]
    public void TestMultiArrays()
    {
        TestUtil.VerifyRoundTrip("a[0] = 1;\na[0][1] = 2;\na[0][1][2] = 3;\na[0][1][2][3] = 4;\na.b[0][1][2] = 3;\na.b[0][1].c[2][3] = 4;\na.b[0][1].c[2][3].d = 4;\nglobal.a[0] = 1;\nglobal.a[0][1] = 2;\nglobal.a[0][1][2] = 3;\nglobal.a[0][1][2][3] = 4;\nglobal.a.b[0][1][2] = 3;\nglobal.a.b[0][1].c[2][3] = 4;\nglobal.a.b[0][1].c[2][3].d = 4;\ne = a[0];\ne = a[0][1];\ne = a[0][1][2];\ne = a[0][1][2][3];\ne = a.b[0][1][2];\ne = a.b[0][1].c[2][3];\ne = a.b[0][1].c[2][3].d;");
    }

    [Fact("RoundTrip.cs", 597)]
    public void TestMultiArraysPrePost()
    {
        TestUtil.VerifyRoundTrip("a[0]++;\na[0][1]++;\na[0][1][2]++;\na[0][1][2][3]++;\na.b[0][1][2]++;\na.b[0][1].c[2][3]++;\na.b[0][1].c[2][3].d++;\nglobal.a[0]++;\nglobal.a[0][1]++;\nglobal.a[0][1][2]++;\nglobal.a[0][1][2][3]++;\nglobal.a.b[0][1][2]++;\nglobal.a.b[0][1].c[2][3]++;\nglobal.a.b[0][1].c[2][3].d++;\ne = a[0]++;\ne = a[0][1]++;\ne = a[0][1][2]++;\ne = a[0][1][2][3]++;\ne = a.b[0][1][2]++;\ne = a.b[0][1].c[2][3]++;\ne = a.b[0][1].c[2][3].d++;\ne = ++a[0];\ne = ++a[0][1];\ne = ++a[0][1][2];\ne = ++a[0][1][2][3];\ne = ++a.b[0][1][2];\ne = ++a.b[0][1].c[2][3];\ne = ++a.b[0][1].c[2][3].d;");
    }

    [Fact("RoundTrip.cs", 634)]
    public void TestMultiArraysCompound()
    {
        TestUtil.VerifyRoundTrip("a[0] += 1;\na[0][1] += 2;\na[0][1][2] += 3;\na[0][1][2][3] += 4;\na.b[0][1][2] += 3;\na.b[0][1].c[2][3] += 4;\na.b[0][1].c[2][3].d += 4;\nglobal.a[0] += 1;\nglobal.a[0][1] += 2;\nglobal.a[0][1][2] += 3;\nglobal.a[0][1][2][3] += 4;\nglobal.a.b[0][1][2] += 3;\nglobal.a.b[0][1].c[2][3] += 4;\nglobal.a.b[0][1].c[2][3].d += 4;");
    }

    [Fact("RoundTrip.cs", 657)]
    public void TestVariableCalls()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] = new BuiltinFunctionMock("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("var d;\nshow_debug_message(\"a\");\ntest(\"a\");\nself.test(\"a\");\nother.test(\"a\");\nglobal.test(\"a\");\na.b(\"a\");\na.b.c(\"a\");\na[0].b(\"a\");\nd(\"a\");\na.d(\"a\");\nd.a(\"a\");\nobj_test.a(\"a\");\nobj_test.a.b(\"a\");", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 690)]
    public void TestVariableCallsAssetRefs()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = true,
            UsingSelfToBuiltin = true
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] = new BuiltinFunctionMock("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("var d;\nshow_debug_message(\"a\");\ntest(\"a\");\nself.test(\"a\");\nother.test(\"a\");\nglobal.test(\"a\");\na.b(\"a\");\na.b.c(\"a\");\na[0].b(\"a\");\nd(\"a\");\na.d(\"a\");\nd.a(\"a\");\nobj_test.a(\"a\");\nobj_test.a.b(\"a\");", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 724)]
    public void TestRepeatVariableCalls()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false,
            UsingSelfToBuiltin = true
        };
        ((BuiltinsMock)gameContext.Builtins).BuiltinFunctions["show_debug_message"] = new BuiltinFunctionMock("show_debug_message", 1, 1);
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("a(1)(2)(3)(4)(5);\na.b(1)(2)(3);\na(1).b(2);\nself.a(1).b(2);\na(1).b(2)(3)(4);\na(1).b(2)(3).c(4);\nobj_test.b(1)(2)(3);\nobj_test.b.c(1)(2)(3);\nobj_test.a(1).b(2);\nobj_test.a(1).b(2)(3)(4);\nobj_test.a(1).b(2)(3).c(4);", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 755)]
    public void TestRepeatVariableCallsAssetRefs()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = true,
            UsingSelfToBuiltin = true
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("a(1)(2)(3)(4)(5);\na.b(1)(2)(3);\na(1).b(2);\nself.a(1).b(2);\na(1).b(2)(3)(4);\na(1).b(2)(3).c(4);\nobj_test.b(1)(2)(3);\nobj_test.b.c(1)(2)(3);\nobj_test.a(1).b(2);\nobj_test.a(1).b(2)(3)(4);\nobj_test.a(1).b(2)(3).c(4);", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 784)]
    public void TestVariableCallsArrays()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("a[0](1);\nself.a[0](1);\nglobal.a[0](1);\nobj_test.a[0](1);\na[0].b(1);\na[0].b[1](2);\na[0](1).b[2](3);\na.b[0](1).c.d[2](3);\na.b[0].c(1).d.e[2].f(3);", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 810)]
    public void TestVariableCallsArraysMulti()
    {
        GameContextMock gameContext = new()
        {
            UsingGMLv2 = true,
            UsingAssetReferences = false
        };
        gameContext.DefineMockAsset(AssetType.Object, 123, "obj_test");
        TestUtil.VerifyRoundTrip("a[0][1](2);\nself.a[0][1](2);\nglobal.a[0][1](2);\nobj_test.a[0][1](2);\na.b[0][1](2);\na[0][1].b(2);\na[0][1].b[1][2](3);\na[0][1](2).b[2][3](4);\na.b[0][1](2).c.d[3][4](5);\na.b[0][1].c(2).d.e[3][4].f(5);", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 837)]
    public void TestFunctionDecls()
    {
        TestUtil.VerifyRoundTrip("abc();\n\nfunction abc()\n{\n}\n\nanon = function()\n{\n};\n\nstruct = \n{\n    a: 123,\n    \n    b: function()\n    {\n    },\n    \n    c: [1, 2, 3, 4, 5],\n    d: \n    {\n        e: 3.14\n    }\n};\n\nfunction Parent(arg0) constructor\n{\n    a = arg0;\n}\n\nfunction Child(arg0, arg1 = 789) : Parent(arg0) constructor\n{\n    static testFunc = function()\n    {\n    };\n    \n    b = arg1;\n}");
    }

    [Fact("RoundTrip.cs", 884)]
    public void TestNewObject()
    {
        TestUtil.VerifyRoundTrip("function Test() constructor\n{\n}\n\nnew Test();\nnew self.Test();\nnew global.Test();\na = new Test();\nb = new VariableCall();\nb = new self.VariableCall();\nb = new global.VariableCall();\nnew Complex.Variable.Call(123, 456);", "function Test() constructor\n{\n}\n\nnew Test();\nnew Test();\nnew Test();\na = new Test();\nb = new VariableCall();\nb = new self.VariableCall();\nb = new global.VariableCall();\nnew Complex.Variable.Call(123, 456);");
    }

    [Fact("RoundTrip.cs", 919)]
    public void TestNonSelfToBuiltin()
    {
        TestUtil.VerifyRoundTrip("a = 0;\nself.a = 0;\na = b;\na = self.b;\na += 1;\nself.a += 1;\na++;\nself.a++;\na.b = 0;\na[0] = 1;\nself.a[0] = 1;\na.b[0] = 1;\na[0] += 1;\nself.a[0] += 1;\na.b[0] += 1;\na = a[0];\na = self.a[0];\na = a.b[0];\nglobal.a = 0;\nglobal.a[0] = 0;\nself.a.b = 1;\na.b = 1;\na = 1;\nother.a[0] = 1;\nglobal.a[0] = 1;\na[0].b = 1;\na = a[0].b;\na = a[0];\na[0].b[0](\"a\");\na[0].b.c(\"a\");\na[0].b(\"a\");", "a = 0;\na = 0;\na = b;\na = b;\na += 1;\na += 1;\na++;\na++;\na.b = 0;\na[0] = 1;\nself.a[0] = 1;\na.b[0] = 1;\na[0] += 1;\nself.a[0] += 1;\na.b[0] += 1;\na = a[0];\na = self.a[0];\na = a.b[0];\nglobal.a = 0;\nglobal.a[0] = 0;\na.b = 1;\na.b = 1;\na = 1;\nother.a[0] = 1;\nglobal.a[0] = 1;\na[0].b = 1;\na = a[0].b;\na = a[0];\na[0].b[0](\"a\");\na[0].b.c(\"a\");\na[0].b(\"a\");", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = false,
            UsingGlobalConstantFunction = false
        });
    }

    [Fact("RoundTrip.cs", 998)]
    public void TestNonSelfToBuiltin2()
    {
        TestUtil.VerifyRoundTrip("a = 0;\nself.a = 0;\na = b;\na = self.b;\na += 1;\nself.a += 1;\na++;\nself.a++;\na.b = 0;\na[0] = 1;\nself.a[0] = 1;\na.b[0] = 1;\na[0] += 1;\nself.a[0] += 1;\na.b[0] += 1;\na = a[0];\na = self.a[0];\na = a.b[0];\nglobal.a = 0;\nglobal.a[0] = 0;\nself.a.b = 1;\na.b = 1;\na = 1;\nother.a[0] = 1;\nglobal.a[0] = 1;\na[0].b = 1;\na = a[0].b;\na = a[0];\na[0].b[0](\"a\");\na[0].b.c(\"a\");\na[0].b(\"a\");", "a = 0;\na = 0;\na = b;\na = b;\na += 1;\na += 1;\na++;\na++;\na.b = 0;\na[0] = 1;\na[0] = 1;\na.b[0] = 1;\na[0] += 1;\na[0] += 1;\na.b[0] += 1;\na = a[0];\na = a[0];\na = a.b[0];\nglobal.a = 0;\nglobal.a[0] = 0;\na.b = 1;\na.b = 1;\na = 1;\nother.a[0] = 1;\nglobal.a[0] = 1;\na[0].b = 1;\na = a[0].b;\na = a[0];\na[0].b[0](\"a\");\na[0].b.c(\"a\");\na[0].b(\"a\");", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = false,
            UsingGlobalConstantFunction = true
        });
    }

    [Fact("RoundTrip.cs", 1077)]
    public void TestSelfToBuiltin()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true,
            UsingGlobalConstantFunction = true
        };
        TestUtil.VerifyRoundTrip("a = 0;\nself.a = 0;\na = b;\na = self.b;\na += 1;\nself.a += 1;\na++;\nself.a++;\na.b = 0;\na[0] = 1;\nself.a[0] = 1;\na.b[0] = 1;\na[0] += 1;\nself.a[0] += 1;\na.b[0] += 1;\na = a[0];\na = self.a[0];\na = a.b[0];\nglobal.a = 0;\nglobal.a[0] = 0;\nself.a.b = 1;\na.b = 1;\na = 1;\nother.a[0] = 1;\nglobal.a[0] = 1;\na[0].b = 1;\na = a[0].b;\na = a[0];\na[0].b[0](\"a\");\na[0].b.c(\"a\");\na[0].b(\"a\");", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1124)]
    public void TestThrow()
    {
        TestUtil.VerifyRoundTrip("throw \"test\";\nthrow new ExampleException();");
    }

    [Fact("RoundTrip.cs", 1135)]
    public void TestNullishCoalesce()
    {
        TestUtil.VerifyRoundTrip("a = b ?? c;\nd = (e ?? f) ?? (g ?? h);\nj ??= k;\nl ??= m ?? n;");
    }

    [Fact("RoundTrip.cs", 1148)]
    public void TestBooleanControlFlow()
    {
        TestUtil.VerifyRoundTrip("while (1)\n{\n}\nwhile (true)\n{\n}\ndo\n{\n}\nuntil (1);\ndo\n{\n}\nuntil (true);\nif (a && 1 && b)\n{\n}\nif (a && true && b)\n{\n}\nswitch (1)\n{\n    case true:\n        break;\n    case false:\n        break;\n    case 1:\n        break;\n    case 0:\n        break;\n}\nswitch (true)\n{\n    case true:\n        break;\n    case false:\n        break;\n    case 1:\n        break;\n    case 0:\n        break;\n}");
    }

    [Fact("RoundTrip.cs", 1199)]
    public void TestTryCatchBasic()
    {
        TestUtil.VerifyRoundTrip("try\n{\n    throw \"Error!\";\n}\ncatch (ex)\n{\n    a = ex;\n}");
    }

    [Fact("RoundTrip.cs", 1216)]
    public void TestTryCatchFinally1()
    {
        TestUtil.VerifyRoundTrip("try\n{\n    throw \"Error!\";\n}\ncatch (ex)\n{\n    a = ex;\n}\nfinally\n{\n    b = 123;\n}");
    }

    [Fact("RoundTrip.cs", 1237)]
    public void TestTryCatchFinally2()
    {
        TestUtil.VerifyRoundTrip("try\n{\n    throw \"Error!\";\n    return 456;\n}\ncatch (ex)\n{\n    a = ex;\n}\nfinally\n{\n    b = 123;\n}");
    }

    [Fact("RoundTrip.cs", 1259)]
    public void TestTryFinally()
    {
        TestUtil.VerifyRoundTrip("try\n{\n    return 123;\n}\nfinally\n{\n    a = \"finally code\";\n}");
    }

    [Fact("RoundTrip.cs", 1276)]
    public void TestTryBreakContinue()
    {
        TestUtil.VerifyRoundTrip("repeat (123)\n{\n    try\n    {\n        if (c)\n        {\n            continue;\n        }\n        if (d)\n        {\n            break;\n        }\n    }\n    catch (ex)\n    {\n    }\n}");
    }

    [Fact("RoundTrip.cs", 1302)]
    public void TestTryCatchBreakContinue()
    {
        TestUtil.VerifyRoundTrip("repeat (123)\n{\n    try\n    {\n        continue;\n        break;\n    }\n    catch (ex)\n    {\n        continue;\n        break;\n    }\n}");
    }

    [Fact("RoundTrip.cs", 1324)]
    public void TestTrySwitchBreak()
    {
        TestUtil.VerifyRoundTrip("try\n{\n    switch (a)\n    {\n        default:\n            break;\n    }\n}\ncatch (ex)\n{\n}");
    }

    [Fact("RoundTrip.cs", 1344)]
    public void TestArguments()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true
        };
        TestUtil.VerifyRoundTrip("a = argument0;\nb = argument0[1];\nc = argument[1];\n\nfunction args(arg0)\n{\n    test = arg0;\n    test2 = arg0[0];\n    test3 = argument0;\n    test4 = argument0[0];\n    test5 = argument[0];\n}", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1371)]
    public void TestArgumentsOld()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = false
        };
        TestUtil.VerifyRoundTrip("a = argument0;\nb = argument0[1];\nc = argument[1];\n\nfunction args(arg0)\n{\n    test = arg0;\n    test2 = arg0[0];\n    test3 = argument0;\n    test4 = argument0[0];\n    test5 = argument[0];\n}", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1398)]
    public void TestStruct()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true
        };
        TestUtil.VerifyRoundTrip("a = \n{\n    b: c + d\n};", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1418)]
    public void TestRoomInstances()
    {
        GameContextMock gameContext = new()
        {
            UsingSelfToBuiltin = true
        };
        TestUtil.VerifyRoundTrip("a = (101234).b;\n(101234).c = d;", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1435)]
    public void TestSelfAssignOld()
    {
        TestUtil.VerifyRoundTrip("x = x;", isGlobalScript: false, new GameContextMock
        {
            Bytecode14OrLower = true
        });
    }

    [Fact("RoundTrip.cs", 1450)]
    public void TestSelfAssign()
    {
        TestUtil.VerifyRoundTrip("x = x;", "", isGlobalScript: false, new GameContextMock
        {
            Bytecode14OrLower = false
        });
    }

    [Fact("RoundTrip.cs", 1466)]
    public void TestInnerLoopLocalArray()
    {
        TestUtil.VerifyRoundTrip("while (a)\n{\n    var arr;\n    arr[0] = 1;\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 1486)]
    public void TestForWhileEdgeCases()
    {
        TestUtil.VerifyRoundTrip("a = 0;\nwhile (a < 10)\n{\n    a++;\n}\nfor (c = 0; c < 10; c++)\n{\n    test = test2;\n}\nfor (d = 10; d > 0; d--)\n{\n    test2 = test;\n}\nfor (;;)\n{\n}\nfor (; a;)\n{\n    continue;\n}\ni = 0;\nfor (;;)\n{\n    i++;\n}\nfor (;;)\n{\n    if (a)\n    {\n        continue;\n    }\n}\ni = 0;\nfor (;;)\n{\n    if (a)\n    {\n    }\n    else\n    {\n    }\n    i++;\n}\nfor (;;)\n{\n    if (a)\n    {\n        if (b)\n        {\n            continue;\n        }\n    }\n    c = 1;\n}\nfor (i = 0; ; i++)\n{\n    if (a)\n    {\n        if (b)\n        {\n            continue;\n        }\n    }\n    if (c)\n    {\n    }\n}\nwhile (UnknownEnum.Value_1)\n{\n    if (a)\n    {\n        continue;\n    }\n}\n\nenum UnknownEnum\n{\n    Value_1 = 1\n}");
    }

    [Fact("RoundTrip.cs", 1574)]
    public void TestStructKeys()
    {
        GameContextMock gameContext = new();
        gameContext.DefineMockAsset(AssetType.Sprite, 16, "spr_test");
        TestUtil.VerifyRoundTrip("a = \n{\n    b: 1,\n    spr_test: 2,\n    test_constant: 3,\n    \"even whitespace\": 4\n};", isGlobalScript: false, gameContext);
    }

    [Fact("RoundTrip.cs", 1594)]
    public void TestReturnNewObject()
    {
        TestUtil.VerifyRoundTrip("return new Test();");
    }

    [Fact("RoundTrip.cs", 1604)]
    public void TestWithWhileContinue()
    {
        TestUtil.VerifyRoundTrip("with (a)\n{\n    while (b)\n    {\n        if (c)\n        {\n            continue;\n        }\n    }\n}");
    }

    [Fact("RoundTrip.cs", 1623)]
    public void TestWithWhileBreakContinue()
    {
        TestUtil.VerifyRoundTrip("with (a)\n{\n    while (b)\n    {\n        if (c)\n        {\n            break;\n        }\n        else\n        {\n            continue;\n        }\n    }\n}");
    }

    [Fact("RoundTrip.cs", 1646)]
    public void TestArrayOwners()
    {
        TestUtil.VerifyRoundTrip("a[0] = 1;\na[0] = 1;\na.b[0] = 1;\na.b[0] = 1;\narray_set(a(), 0, 1);\nc = [1];\nd = array_create(15);\ne[0]++;\ne[0]++;\ne.f[0]++;\ne.f[0]++;\n++e[0];\n++e[0];\n++e.f[0];\n++e.f[0];\n\nfunction TestFunc()\n{\n    a[0] = 1;\n    a[0] = 1;\n    a.b[0] = 1;\n    a.b[0] = 1;\n    array_set(a(), 0, 1);\n    c = [1];\n    d = array_create(15);\n    e[0]++;\n    e[0]++;\n    e.f[0]++;\n    e.f[0]++;\n    ++e[0];\n    ++e[0];\n    ++e.f[0];\n    ++e.f[0];\n}", isGlobalScript: false, new GameContextMock
        {
            UsingArrayCopyOnWrite = true,
            UsingNewArrayOwners = true
        });
    }

    [Fact("RoundTrip.cs", 1695)]
    public void TestDivideQuirk()
    {
        TestUtil.VerifyRoundTrip("a /= 2;\na = a / 2;\nb /= 1.5;\nc.d /= 1.5;\nc.d = c.d / 1.5;");
    }

    [Fact("RoundTrip.cs", 1709)]
    public void TestKeywordLogicOperators()
    {
        TestUtil.VerifyRoundTrip("a = b and c and d;\ne = f or g or h;\ni = j xor k xor l;", "a = b && c && d;\ne = f || g || h;\ni = j ^^ k ^^ l;");
    }

    [Fact("RoundTrip.cs", 1726)]
    public void TestModernStructNames()
    {
        TestUtil.VerifyRoundTripAssembly("var a = 123;\nb = \n{\n    c: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"(\": a,\n    \"a(\": a,\n    begin: a,\n    if: a,\n    new: a,\n    \"function\": a,\n    string: a,\n    \"end\": a,\n    \"self\": a,\n    \"other\": a,\n    \"global\": a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};", "pushi.e 123\npop.v.i local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\nb [2]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[1]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.c\npushi.e -15\npushi.e 1\npush.v [array]self.argument\npop.v.v self.\"a b\"\npushi.e -15\npushi.e 2\npush.v [array]self.argument\npop.v.v self.\"a\\nb\"\npushi.e -15\npushi.e 3\npush.v [array]self.argument\npush.s \"(\"\nconv.s.v\ncall.i @@This@@ 0\ncall.i variable_struct_set 3\npopz.v\npushi.e -15\npushi.e 4\npush.v [array]self.argument\npop.v.v self.\"a(\"\npushi.e -15\npushi.e 5\npush.v [array]self.argument\npop.v.v self.begin\npushi.e -15\npushi.e 6\npush.v [array]self.argument\npop.v.v self.if\npushi.e -15\npushi.e 7\npush.v [array]self.argument\npop.v.v self.new\npushi.e -15\npushi.e 8\npush.v [array]self.argument\npop.v.v self.function\npushi.e -15\npushi.e 9\npush.v [array]self.argument\npop.v.v self.string\npushi.e -15\npushi.e 10\npush.v [array]self.argument\npop.v.v self.end\npushi.e -15\npushi.e 11\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.self\npushi.e -15\npushi.e 12\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.other\npushi.e -15\npushi.e 13\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 14\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.self\npushi.e -15\npushi.e 15\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.other\npushi.e -15\npushi.e 16\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 17\npush.v [array]self.argument\npop.v.v self.true\nexit.i\n\n:[2]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__1\ncall.i @@NewGMLObject@@ 19\npop.v.v builtin.b", "var a = 123;\nb = \n{\n    c: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"(\": a,\n    \"a(\": a,\n    begin: a,\n    if: a,\n    new: a,\n    \"function\": a,\n    string: a,\n    \"end\": a,\n    self: a,\n    other: a,\n    global: a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingConstructorSetStatic = true,
            UsingNewFunctionVariables = true,
            UsingOptimizedFunctionDeclarations = true,
            UsingStructAnyNonemptyString = true,
            UsingStructSpecialCaseNames = true
        });
    }

    [Fact("RoundTrip.cs", 1913)]
    public void TestNoArbitraryStringsStructNames()
    {
        TestUtil.VerifyRoundTripAssembly("var a = 123;\nb = \n{\n    case: a,\n    default: a,\n    throw: a,\n    \"function\": a,\n    regular_variable: a,\n    begin: a,\n    \"end\": a,\n    if: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"a(\": a,\n    new: a,\n    \"self\": a,\n    \"other\": a,\n    \"global\": a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};", ":[0]\npushi.e 123\npop.v.i local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\nb [2]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[1]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.case\npushi.e -15\npushi.e 1\npush.v [array]self.argument\npop.v.v self.default\npushi.e -15\npushi.e 2\npush.v [array]self.argument\npop.v.v self.throw\npushi.e -15\npushi.e 3\npush.v [array]self.argument\npop.v.v self.function\npushi.e -15\npushi.e 4\npush.v [array]self.argument\npop.v.v self.regular_variable\npushi.e -15\npushi.e 5\npush.v [array]self.argument\npop.v.v self.begin\npushi.e -15\npushi.e 6\npush.v [array]self.argument\npop.v.v self.end\npushi.e -15\npushi.e 7\npush.v [array]self.argument\npop.v.v self.if\npushi.e -15\npushi.e 8\npush.v [array]self.argument\npop.v.v self.\"a b\"\npushi.e -15\npushi.e 9\npush.v [array]self.argument\npop.v.v self.\"a\\nb\"\npushi.e -15\npushi.e 10\npush.v [array]self.argument\npop.v.v self.\"a(\"\npushi.e -15\npushi.e 11\npush.v [array]self.argument\npop.v.v self.new\npushi.e -15\npushi.e 12\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.self\npushi.e -15\npushi.e 13\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.other\npushi.e -15\npushi.e 14\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 15\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.self\npushi.e -15\npushi.e 16\npush.v [array]self.argument\ncall.i @@This@@ 0\npushi.e -9\npop.v.v [stacktop]self.other\npushi.e -15\npushi.e 17\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 18\npush.v [array]self.argument\npop.v.v self.true\nexit.i\n\n:[2]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -5\npop.v.v [stacktop]global.__struct__1\ncall.i @@NewGMLObject@@ 20\npop.v.v builtin.b", "var a = 123;\nb = \n{\n    case: a,\n    default: a,\n    throw: a,\n    \"function\": a,\n    regular_variable: a,\n    begin: a,\n    \"end\": a,\n    if: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"a(\": a,\n    new: a,\n    self: a,\n    other: a,\n    global: a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingConstructorSetStatic = true,
            UsingNewFunctionVariables = true,
            UsingStructSpecialCaseNames = true
        });
    }

    [Fact("RoundTrip.cs", 2103)]
    public void TestOldStructNames()
    {
        TestUtil.VerifyRoundTripAssembly("var a = 123;\nb = \n{\n    c: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"a(\": a,\n    \"begin\": a,\n    \"end\": a,\n    \"new\": a,\n    \"self\": a,\n    \"other\": a,\n    \"global\": a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};", "pushi.e 123\npop.v.i local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\npushloc.v local.a\nb [2]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[1]\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.c\npushi.e -15\npushi.e 1\npush.v [array]self.argument\npop.v.v self.\"a b\"\npushi.e -15\npushi.e 2\npush.v [array]self.argument\npop.v.v self.\"a\\nb\"\npushi.e -15\npushi.e 3\npush.v [array]self.argument\npop.v.v self.\"a(\"\npushi.e -15\npushi.e 4\npush.v [array]self.argument\npop.v.v self.begin\npushi.e -15\npushi.e 5\npush.v [array]self.argument\npop.v.v self.end\npushi.e -15\npushi.e 6\npush.v [array]self.argument\npop.v.v self.new\npushi.e -15\npushi.e 7\npush.v [array]self.argument\npop.v.v self.self\npushi.e -15\npushi.e 8\npush.v [array]self.argument\npop.v.v self.other\npushi.e -15\npushi.e 9\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 10\npush.v [array]self.argument\npop.v.v self.self\npushi.e -15\npushi.e 11\npush.v [array]self.argument\npop.v.v self.other\npushi.e -15\npushi.e 12\npush.v [array]self.argument\npop.v.v self.global\npushi.e -15\npushi.e 13\npush.v [array]self.argument\npop.v.v self.true\nexit.i\n\n:[2]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -16\npop.v.v [stacktop]static.__struct__1\ncall.i @@NewGMLObject@@ 15\npop.v.v self.b", "var a = 123;\nb = \n{\n    c: a,\n    \"a b\": a,\n    \"a\\nb\": a,\n    \"a(\": a,\n    \"begin\": a,\n    \"end\": a,\n    \"new\": a,\n    self: a,\n    other: a,\n    global: a,\n    self: a,\n    other: a,\n    global: a,\n    true: a\n};");
    }

    [Fact("RoundTrip.cs", 2240)]
    public void TestNonBuiltinDefaultArguments()
    {
        TestUtil.VerifyRoundTrip("function test_func(arg0 = 123, arg1 = 456, arg2)\n{\n}", isGlobalScript: true, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true,
            UsingConstructorSetStatic = false,
            UsingBuiltinDefaultArguments = false
        });
    }

    [Fact("RoundTrip.cs", 2260)]
    public void TestBuiltinDefaultArguments()
    {
        TestUtil.VerifyRoundTrip("function test_func(arg0 = 123, arg1 = 456, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, arg16 = 789)\n{\n}", isGlobalScript: true, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true,
            UsingConstructorSetStatic = true,
            UsingBuiltinDefaultArguments = true
        });
    }

    [Fact("RoundTrip.cs", 2280)]
    public void TestOldFunctionResolution()
    {
        TestUtil.VerifyRoundTrip("function event_function()\n{\n}\n\nfunction other_event_function()\n{\n    event_function();\n}\n\nevent_function();", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true,
            UsingFunctionScriptReferences = true,
            UsingNewFunctionResolution = false
        });
    }

    [Fact("RoundTrip.cs", 2307)]
    public void TestNewFunctionResolution()
    {
        TestUtil.VerifyRoundTrip("function event_function()\n{\n}\n\nfunction other_event_function()\n{\n    event_function();\n}\n\nevent_function();", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true,
            UsingFunctionScriptReferences = true,
            UsingNewFunctionResolution = true
        });
    }

    [Fact("RoundTrip.cs", 2334)]
    public void TestTryWithFinally()
    {
        TestUtil.VerifyRoundTrip("a = 1;\ntry\n{\n    with (b)\n    {\n        c = 3;\n    }\n}\nfinally\n{\n    d = 4;\n}");
    }

    [Fact("RoundTrip.cs", 2355)]
    public void TestTryWhileFinally()
    {
        TestUtil.VerifyRoundTrip("a = 1;\ntry\n{\n    while (b)\n    {\n        c = 3;\n    }\n}\nfinally\n{\n    d = 4;\n}");
    }

    [Fact("RoundTrip.cs", 2376)]
    public void TestTryDoUntilFinally()
    {
        TestUtil.VerifyRoundTrip("a = 1;\ntry\n{\n    do\n    {\n        c = 3;\n    }\n    until (b);\n}\nfinally\n{\n    d = 4;\n}");
    }

    [Fact("RoundTrip.cs", 2398)]
    public void TestAnonymousCallWithinStruct()
    {
        TestUtil.VerifyRoundTrip("a = \n{\n    b: (function()\n    {\n    })()\n};");
    }

    [Fact("RoundTrip.cs", 2413)]
    public void TestRoomInstanceIdCall()
    {
        TestUtil.VerifyRoundTrip("(100123).test();\ninst_id_100123.test();", "inst_id_100123.test();\ninst_id_100123.test();", isGlobalScript: false, new GameContextMock
        {
            UsingAssetReferences = true,
            UsingRoomInstanceReferences = false
        });
    }

    [Fact("RoundTrip.cs", 2434)]
    public void TestRoomInstanceIdCallRef()
    {
        TestUtil.VerifyRoundTrip("(100123).test();\ninst_id_100123.test();", isGlobalScript: false, new GameContextMock
        {
            UsingAssetReferences = true,
            UsingRoomInstanceReferences = true
        });
    }

    [Fact("RoundTrip.cs", 2451)]
    public void TestTemplateStrings()
    {
        TestUtil.VerifyRoundTrip("a = $\"meow {x} lalala {$\"\"}\";\nb = string(\"{0} now there are two of them. there are two {0}\", x);\nc = string(\"{0} {1}\", x);\nd = $\"\\{\";\ne = $\"\\{x\\} \\{ 3\\} \\{\\}\";\nf = $\"{5}\";\ng = $\"Split\\n{\nacross\n}\\nmultiple lines\";\nh = $\"\\{x\\} { 3} \\{\\}\";\nambiguous_example = string(\"{0} {1} {2}\", 1, 2, 3);\nunresolved = string(some_variable);\nold_bug = $\"\\{{123}}\";\nold_bug_equivalent = string(\"{{0}}\", 123);\nspaces_test = string(\"{ 0}\", 123);\nspaces_test_2 = string(\"{0 }\", 123);", "a = $\"meow {x} lalala {\"\"}\";\nb = string(\"{0} now there are two of them. there are two {0}\", x);\nc = string(\"{0} {1}\", x);\nd = \"{\";\ne = \"{x} { 3} {}\";\nf = $\"{5}\";\ng = $\"Split\\n{across}\\nmultiple lines\";\nh = $\"\\{x} {3} \\{}\";\nambiguous_example = $\"{1} {2} {3}\";\nunresolved = string(some_variable);\nold_bug = string(\"{{0}}\", 123);\nold_bug_equivalent = string(\"{{0}}\", 123);\nspaces_test = string(\"{ 0}\", 123);\nspaces_test_2 = string(\"{0 }\", 123);", isGlobalScript: false, new GameContextMock
        {
            UsingModernTemplateStrings = false
        });
    }

    [Fact("RoundTrip.cs", 2497)]
    public void TestModernTemplateStrings()
    {
        TestUtil.VerifyRoundTrip("a = $\"meow {x} lalala {$\"\"}\";\nb = @@string@@(\"{0} now there are two of them. there are two {0}\", x);\nc = @@string@@(\"{0} {1}\", x);\nd = $\"\\{\";\ne = $\"\\{x\\} \\{ 3\\} \\{\\}\";\nf = $\"{5}\";\ng = $\"Split\\n{\nacross\n}\\nmultiple lines\";\nh = $\"\\{x\\} { 3} \\{\\}\";\nno_longer_ambiguous_example = string(\"{0} {1} {2}\", 1, 2, 3);\nunresolved_1 = string(some_variable);\nunresolved_2 = @@string@@(some_variable);\nold_bug_now_fixed = $\"\\{{123}}\";\nspaces_test = @@string@@(\"{ 0}\", 123);\nspaces_test_2 = @@string@@(\"{0 }\", 123);", "a = $\"meow {x} lalala {\"\"}\";\nb = @@string@@(\"{0} now there are two of them. there are two {0}\", x);\nc = @@string@@(\"{0} {1}\", x);\nd = \"{\";\ne = \"{x} { 3} {}\";\nf = $\"{5}\";\ng = $\"Split\\n{across}\\nmultiple lines\";\nh = $\"\\{x} {3} \\{}\";\nno_longer_ambiguous_example = string(\"{0} {1} {2}\", 1, 2, 3);\nunresolved_1 = string(some_variable);\nunresolved_2 = @@string@@(some_variable);\nold_bug_now_fixed = $\"\\{{123}}\";\nspaces_test = @@string@@(\"{ 0}\", 123);\nspaces_test_2 = @@string@@(\"{0 }\", 123);");
    }

    [Fact("RoundTrip.cs", 2538)]
    public void TestStructWithImpossibleField()
    {
        TestUtil.VerifyRoundTrip("function test()\n{\n    a = \n    {\n        \";\": 4,\n        \"hello world\": \"hi programmer\",\n        b: 6\n    };\n}", isGlobalScript: true, new GameContextMock
        {
            UsingStructAnyNonemptyString = true,
            UsingStructSpecialCaseNames = true
        });
    }

    [Fact("RoundTrip.cs", 2561)]
    public void TestVariableGetHashOptimization()
    {
        TestUtil.VerifyRoundTripAssembly("test = variable_get_hash(\"abc\");\ntest = variable_get_hash(\"\");\ntest = variable_get_hash(\"a\\nb\");", "push.i [variable]abc\npop.v.i builtin.test\npush.i [variable]\"\"\npop.v.i builtin.test\npush.i [variable]\"a\\nb\"\npop.v.i builtin.test", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingVariableHashFunctions = true
        });
    }

    [Fact("RoundTrip.cs", 2587)]
    public void TestStructEmbeddedArrayOld()
    {
        TestUtil.VerifyRoundTripAssembly("a = \n{\n    b: [1, 2, 3, 4],\n    c: \n    {\n        d: 5678\n    }\n};", "b [4]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[1]\npushi.e 4\nconv.i.v\npushi.e 3\nconv.i.v\npushi.e 2\nconv.i.v\npushi.e 1\nconv.i.v\ncall.i @@NewGMLArray@@ 4\npop.v.v self.b\nb [3]\n\n> struct_func___struct__2 (locals=0, args=0)\n:[2]\npushi.e 5678\npop.v.i self.d\nexit.i\n\n:[3]\npush.i [function]struct_func___struct__2\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -5\npop.v.v [stacktop]global.__struct__2\ncall.i @@NewGMLObject@@ 1\npop.v.v self.c\nexit.i\n\n:[4]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -5\npop.v.v [stacktop]global.__struct__1\ncall.i @@NewGMLObject@@ 1\npop.v.v builtin.a", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true
        });
    }

    [Fact("RoundTrip.cs", 2656)]
    public void TestStructEmbeddedArrayNew()
    {
        TestUtil.VerifyRoundTripAssembly("a = \n{\n    b: [1, 2, 3, 4],\n    c: \n    {\n        d: 5678\n    }\n};", "pushi.e 4\nconv.i.v\npushi.e 3\nconv.i.v\npushi.e 2\nconv.i.v\npushi.e 1\nconv.i.v\ncall.i @@NewGMLArray@@ 4\nb [4]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[1]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.b\nb [3]\n\n> struct_func___struct__2 (locals=0, args=0)\n:[2]\ncall.i @@SetStatic@@ 0\npushi.e 5678\npop.v.i self.d\nexit.i\n\n:[3]\npush.i [function]struct_func___struct__2\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__2\ncall.i @@NewGMLObject@@ 1\npop.v.v self.c\nexit.i\n\n:[4]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__1\ncall.i @@NewGMLObject@@ 2\npop.v.v builtin.a", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewFunctionVariables = true,
            UsingOptimizedFunctionDeclarations = true,
            UsingConstructorSetStatic = true,
            UsingExternalStructArrays = true
        });
    }

    [Fact("RoundTrip.cs", 2731)]
    public void TestFunctionDeclsAdvancedOld()
    {
        CompileContext compileContext = TestUtil.VerifyRoundTripAssembly("function test_parent(arg0) constructor\n{\n}\n\nfunction test(arg0, arg1 = \n{\n    a: arg0\n}, arg2 = function()\n{\n}) : test_parent(\n{\n    b: arg0\n}) constructor\n{\n    static c = \n    {\n        d: 123\n    };\n    \n    e = \n    {\n        f: 456\n    };\n}", "b [2]\n\n> global_func_test_parent (locals=0, args=1)\n:[1]\nexit.i\n\n:[2]\npush.i [function]global_func_test_parent\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -6\npop.v.v [stacktop]self.test_parent\npopz.v\nb [20]\n\n> global_func_test (locals=0, args=3)\n:[3]\npush.v arg.argument1\npushbltn.v builtin.undefined\ncmp.v.v EQ\nbf [7]\n\n:[4]\npush.v arg.argument0\nb [6]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[5]\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.a\nexit.i\n\n:[6]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -16\npop.v.v [stacktop]static.__struct__1\ncall.i @@NewGMLObject@@ 2\npop.v.v arg.argument1\n\n:[7]\npush.v arg.argument2\npushbltn.v builtin.undefined\ncmp.v.v EQ\nbf [11]\n\n:[8]\nb [10]\n\n> anon_func_1 (locals=0, args=0)\n:[9]\nexit.i\n\n:[10]\npush.i [function]anon_func_1\nconv.i.v\npushi.e -1\nconv.i.v\ncall.i method 2\npop.v.v arg.argument2\n\n:[11]\npush.v arg.argument0\nb [13]\n\n> struct_func___struct__2 (locals=0, args=0)\n:[12]\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.b\nexit.i\n\n:[13]\npush.i [function]struct_func___struct__2\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -16\npop.v.v [stacktop]static.__struct__2\ncall.i @@NewGMLObject@@ 2\ncall.i global_func_test_parent 1\npush.i [function]global_func_test_parent\nconv.i.v\ncall.i @@CopyStatic@@ 1\nisstaticok.e\nbt [17]\n\n:[14]\nb [16]\n\n> struct_func___struct__3 (locals=0, args=0)\n:[15]\npushi.e 123\npop.v.i self.d\nexit.i\n\n:[16]\npush.i [function]struct_func___struct__3\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -16\npop.v.v [stacktop]static.__struct__3\ncall.i @@NewGMLObject@@ 1\npop.v.v static.c\n\n:[17]\nsetstatic.e\nb [19]\n\n> struct_func___struct__4 (locals=0, args=0)\n:[18]\npushi.e 456\npop.v.i self.f\nexit.i\n\n:[19]\npush.i [function]struct_func___struct__4\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -16\npop.v.v [stacktop]static.__struct__4\ncall.i @@NewGMLObject@@ 1\npop.v.v self.e\nexit.i\n\n:[20]\npush.i [function]global_func_test\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npushi.e -6\npop.v.v [stacktop]self.test\npopz.v", isGlobalScript: true);
        Assert.Equal(7, compileContext.OutputFunctionEntries.Count);
        Assert.True(compileContext.OutputFunctionEntries[0].IsConstructor);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[0].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[0].Parent);
        Assert.True(compileContext.OutputFunctionEntries[1].IsConstructor);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[1].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[1].Parent);
        Assert.Null(compileContext.OutputFunctionEntries[2].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[2].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[3].Parent);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[3].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[4].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[4].Kind);
        Assert.NotNull(compileContext.OutputFunctionEntries[5].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[5].Kind);
        Assert.NotNull(compileContext.OutputFunctionEntries[6].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[6].Kind);
    }

    [Fact("RoundTrip.cs", 2932)]
    public void TestFunctionDeclsAdvancedNew()
    {
        CompileContext compileContext = TestUtil.VerifyRoundTripAssembly("function test_parent(arg0) constructor\n{\n}\n\nfunction test(arg0, arg1 = \n{\n    a: arg0\n}, arg2 = function()\n{\n}) : test_parent(\n{\n    b: arg0\n}) constructor\n{\n    static c = \n    {\n        d: 123\n    };\n    \n    e = \n    {\n        f: 456\n    };\n}", "b [2]\n\n> global_func_test_parent (locals=0, args=1)\n:[1]\ncall.i @@SetStatic@@ 0\nexit.i\n\n:[2]\npush.i [function]global_func_test_parent\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v self.test_parent\npopz.v\nb [20]\n\n> global_func_test (locals=0, args=3)\n:[3]\npushbltn.v builtin.argument1\npushbltn.v builtin.undefined\ncmp.v.v EQ\nbf [7]\n\n:[4]\npush.v arg.argument0\nb [6]\n\n> struct_func___struct__1 (locals=0, args=0)\n:[5]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.a\nexit.i\n\n:[6]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__1\ncall.i @@NewGMLObject@@ 2\npop.v.v builtin.argument1\n\n:[7]\npushbltn.v builtin.argument2\npushbltn.v builtin.undefined\ncmp.v.v EQ\nbf [11]\n\n:[8]\nb [10]\n\n> anon_func_1 (locals=0, args=0)\n:[9]\nexit.i\n\n:[10]\npush.i [function]anon_func_1\nconv.i.v\npushi.e -1\nconv.i.v\ncall.i method 2\npop.v.v builtin.argument2\n\n:[11]\npush.v arg.argument0\nb [13]\n\n> struct_func___struct__2 (locals=0, args=0)\n:[12]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.b\nexit.i\n\n:[13]\npush.i [function]struct_func___struct__2\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__2\ncall.i @@NewGMLObject@@ 2\ncall.i global_func_test_parent 1\npush.i [function]global_func_test_parent\nconv.i.v\ncall.i @@CopyStatic@@ 1\ncall.i @@SetStatic@@ 0\nisstaticok.e\nbt [17]\n\n:[14]\nsetstatic.e\nb [16]\n\n> struct_func___struct__3 (locals=0, args=0)\n:[15]\ncall.i @@SetStatic@@ 0\npushi.e 123\npop.v.i self.d\nexit.i\n\n:[16]\npush.i [function]struct_func___struct__3\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__3\ncall.i @@NewGMLObject@@ 1\npop.v.v static.c\n\n:[17]\nb [19]\n\n> struct_func___struct__4 (locals=0, args=0)\n:[18]\ncall.i @@SetStatic@@ 0\npushi.e 456\npop.v.i self.f\nexit.i\n\n:[19]\npush.i [function]struct_func___struct__4\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__4\ncall.i @@NewGMLObject@@ 1\npop.v.v builtin.e\nexit.i\n\n:[20]\npush.i [function]global_func_test\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v self.test\npopz.v", isGlobalScript: true, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingConstructorSetStatic = true,
            UsingNewFunctionVariables = true,
            UsingOptimizedFunctionDeclarations = true,
            UsingBuiltinDefaultArguments = true,
            UsingReentrantStatic = false,
            UsingFixedDefaultArgumentFunctionDecls = true
        });
        Assert.Equal(7, compileContext.OutputFunctionEntries.Count);
        Assert.True(compileContext.OutputFunctionEntries[0].IsConstructor);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[0].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[0].Parent);
        Assert.True(compileContext.OutputFunctionEntries[1].IsConstructor);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[1].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[1].Parent);
        Assert.NotNull(compileContext.OutputFunctionEntries[2].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[2].Kind);
        Assert.NotNull(compileContext.OutputFunctionEntries[3].Parent);
        Assert.Equal(FunctionEntryKind.FunctionDeclaration, compileContext.OutputFunctionEntries[3].Kind);
        Assert.Null(compileContext.OutputFunctionEntries[4].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[4].Kind);
        Assert.NotNull(compileContext.OutputFunctionEntries[5].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[5].Kind);
        Assert.NotNull(compileContext.OutputFunctionEntries[6].Parent);
        Assert.Equal(FunctionEntryKind.StructInstantiation, compileContext.OutputFunctionEntries[6].Kind);
    }

    [Fact("RoundTrip.cs", 3143)]
    public void TestFunctionChainParentheses()
    {
        TestUtil.VerifyRoundTripAssembly("function first_call()\n{\n}\n\nfirst_call().test();\n(first_call()).test();\nvar_first_call().test();\n(var_first_call()).test();", "b [2]\n\n> global_func_first_call (locals=0, args=0)\n:[1]\nexit.i\n\n:[2]\npush.i [function]global_func_first_call\nconv.i.v\npushi.e -1\nconv.i.v\ncall.i method 2\ndup.v 0\npushi.e -6\npop.v.v [stacktop]self.first_call\npopz.v\ncall.i global_func_first_call 0\ndup.v 0\npushi.e -9\npush.v [stacktop]self.test\ncallv.v 0\npopz.v\ncall.i global_func_first_call 0\ndup.v 0 1\ndup.v 0\npush.v stacktop.test\ncallv.v 0\npopz.v\ncall.i @@This@@ 0\npush.v builtin.var_first_call\ncallv.v 0\ndup.v 0\npushi.e -9\npush.v [stacktop]self.test\ncallv.v 0\npopz.v\ncall.i @@This@@ 0\npush.v builtin.var_first_call\ncallv.v 0\ndup.v 0 1\ndup.v 0\npush.v stacktop.test\ncallv.v 0\npopz.v", isGlobalScript: true);
    }

    [Fact("RoundTrip.cs", 3207)]
    public void TestFunctionChainParenthesesNew()
    {
        TestUtil.VerifyRoundTripAssembly("function first_call()\n{\n}\n\nfirst_call().test();\n(first_call()).test();\nvar_first_call().test();\n(var_first_call()).test();", "b [2]\n\n> global_func_first_call (locals=0, args=0)\n:[1]\nexit.i\n\n:[2]\npush.i [function]global_func_first_call\nconv.i.v\npushi.e -1\nconv.i.v\ncall.i method 2\ndup.v 0\npop.v.v self.first_call\npopz.v\ncall.i global_func_first_call 0\ndup.v 0\npushi.e -9\npush.v [stacktop]self.test\ndup.e 2 0\ncallv.v 0\npopz.v\ncall.i global_func_first_call 0\ndup.v 0 1\ndup.v 0\npush.v stacktop.test\ncallv.v 0\npopz.v\ncall.i @@This@@ 0\npush.v builtin.var_first_call\ncallv.v 0\ndup.v 0\npushi.e -9\npush.v [stacktop]self.test\ndup.e 2 0\ncallv.v 0\npopz.v\ncall.i @@This@@ 0\npush.v builtin.var_first_call\ncallv.v 0\ndup.v 0 1\ndup.v 0\npush.v stacktop.test\ncallv.v 0\npopz.v", isGlobalScript: true, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingOptimizedFunctionDeclarations = true,
            UsingNewChainedFunctionArgumentOrder = true
        });
    }

    [Fact("RoundTrip.cs", 3278)]
    public void TestFunctionChainParentheses2()
    {
        TestUtil.VerifyRoundTrip("b.c();\nself.b().c();\na().b.c();");
    }

    [Fact("RoundTrip.cs", 3290)]
    public void TestFunctionChainParentheses3()
    {
        TestUtil.VerifyRoundTrip("if (a)\n    b = 123;\nif (c)\n{\n    (test()).test();\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3311)]
    public void TestFunctionChainParentheses4()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    (-b).c();\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3330)]
    public void TestFunctionChainParentheses5()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    (b ? c : d).e();\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3349)]
    public void TestUnaryGroupings()
    {
        TestUtil.VerifyRoundTrip("a = -b;\na = -(-b);\na = -(-(-b));");
    }

    [Fact("RoundTrip.cs", 3361)]
    public void TestUnaryGroupings2()
    {
        TestUtil.VerifyRoundTrip("(-a).b();");
    }

    [Fact("RoundTrip.cs", 3371)]
    public void TestUnaryGroupings3()
    {
        TestUtil.VerifyRoundTrip("(-a)();");
    }

    [Fact("RoundTrip.cs", 3381)]
    public void TestChainParentheses()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    (b ? c : d).e = 123;\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3400)]
    public void TestStandaloneStruct()
    {
        TestUtil.VerifyRoundTrip("({});");
    }

    [Fact("RoundTrip.cs", 3410)]
    public void TestStandaloneStruct2()
    {
        TestUtil.VerifyRoundTrip("(\n{\n    a: 123\n}\n);");
    }

    [Fact("RoundTrip.cs", 3424)]
    public void TestStandaloneStruct3()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    ({});\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3443)]
    public void TestStandaloneStruct4()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    (\n    {\n        b: 123\n    }\n    );\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3466)]
    public void TestStandaloneStruct4b()
    {
        TestUtil.VerifyRoundTrip("if (a) {\n    ({\n        b: 123\n    });\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true,
            OpenBlockBraceOnSameLine = true
        });
    }

    [Fact("RoundTrip.cs", 3487)]
    public void TestStandaloneStruct5()
    {
        TestUtil.VerifyRoundTripAssembly("({});", "call.i @@NewGMLObject@@ 0\npopz.v", isGlobalScript: false, new GameContextMock
        {
            UsingOptimizedFunctionDeclarations = true
        });
    }

    [Fact("RoundTrip.cs", 3506)]
    public void TestStandaloneStruct6()
    {
        TestUtil.VerifyRoundTripAssembly("if (a)\n{\n    ({});\n}", "push.v builtin.a\nconv.v.b\nbf [2]\n\n:[1]\ncall.i @@NewGMLObject@@ 0\npopz.v\n\n:[2]", isGlobalScript: false, new GameContextMock
        {
            UsingOptimizedFunctionDeclarations = true,
            UsingSelfToBuiltin = true
        }, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3540)]
    public void TestStandaloneStruct7()
    {
        TestUtil.VerifyRoundTrip("(\n{\n    a: -4\n}\n);\n(\n{\n    a: noone\n}\n);\n(\n{\n    \";\": 123\n}\n);\n(\n{\n    \"hello world\": 456\n}\n);", isGlobalScript: false, new GameContextMock
        {
            UsingStructAnyNonemptyString = true
        });
    }

    [Fact("RoundTrip.cs", 3574)]
    public void TestNonRoomInstanceParentheses()
    {
        TestUtil.VerifyRoundTrip("if (a)\n    a.b = 123;", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3591)]
    public void TestRoomInstanceParentheses()
    {
        TestUtil.VerifyRoundTrip("if (a)\n{\n    (101234).abc = 789;\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3610)]
    public void TestBinaryParentheses()
    {
        TestUtil.VerifyRoundTrip("if (a)\n    0(123, 456);\nif (b)\n{\n    (c + d)(789);\n}\nif (e)\n{\n    (function()\n    {\n    })(123, 456);\n}\nif (f)\n{\n    (function()\n    {\n    } + 1)(123, 456);\n}", isGlobalScript: false, null, new DecompileSettings
        {
            RemoveSingleLineBlockBraces = true
        });
    }

    [Fact("RoundTrip.cs", 3643)]
    public void TestNestedStruct()
    {
        TestUtil.VerifyRoundTrip("a = \n{\n    b: \n    {\n        c: 123\n    }\n};");
    }

    [Fact("RoundTrip.cs", 3659)]
    public void TestStructHoisting()
    {
        TestUtil.VerifyRoundTripAssembly("a = \n{\n    b: noone\n};\nc = \n{\n    d: -4\n};\ne = \n{\n    f: 4,\n    g: 4.5,\n    h: -4.5,\n    i: -1234\n};", "b [2]\n\n> struct_func___struct__1 (locals=0, ars=0)\n:[1]\ncall.i @@SetStatic@@ 0\npushi.e -4\npop.v.i self.b\nexit.i\n\n:[2]\npush.i [function]struct_func___struct__1\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__1\ncall.i @@NewGMLObject@@ 1\npop.v.v builtin.a\npushi.e -4\nconv.i.v\nb [4]\n\n> struct_func___struct__2 (locals=0, args=0)\n:[3]\ncall.i @@SetStatic@@ 0\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.d\nexit.i\n\n:[4]\npush.i [function]struct_func___struct__2\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__2\ncall.i @@NewGMLObject@@ 2\npop.v.v builtin.c\npushi.e -1234\nconv.i.v\npush.d -4.5\nconv.d.v\nb [6]\n\n> struct_func___struct__3 (locals=0, args=0)\n:[5]\ncall.i @@SetStatic@@ 0\npushi.e 4\npop.v.i self.f\npush.d 4.5\npop.v.d self.g\npushi.e -15\npushi.e 0\npush.v [array]self.argument\npop.v.v self.h\npushi.e -15\npushi.e 1\npush.v [array]self.argument\npop.v.v self.i\nexit.i\n\n:[6]\npush.i [function]struct_func___struct__3\nconv.i.v\ncall.i @@NullObject@@ 0\ncall.i method 2\ndup.v 0\npop.v.v global.__struct__3\ncall.i @@NewGMLObject@@ 3\npop.v.v builtin.e", isGlobalScript: true, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingConstructorSetStatic = true,
            UsingOptimizedFunctionDeclarations = true
        });
    }

    [Fact("RoundTrip.cs", 3764)]
    public void TestDivisionSpecialCase()
    {
        TestUtil.VerifyRoundTripAssembly("a = 2 * (3 + !b) / 4;\nc = (5 * (6 + !d)) / 7;", "pushi.e 2\npushi.e 3\npush.v self.b\nconv.v.b\nnot.b\nconv.b.i\nadd.i.i\nmul.i.i\npushi.e 4\nconv.i.d\ndiv.d.i\npop.v.d self.a\npushi.e 5\npushi.e 6\npush.v self.d\nconv.v.b\nnot.b\nconv.b.i\nadd.i.i\nmul.i.i\nconv.i.d\npushi.e 7\nconv.i.d\ndiv.d.d\npop.v.d self.c");
    }

    [Fact("RoundTrip.cs", 3802)]
    public void TestBitwiseSpecialCase()
    {
        TestUtil.VerifyRoundTripAssembly("a |= b;\na = a | b;\na &= b;\na = a & b;\na ^= b;\na = a ^ b;", "push.v self.a\npush.v self.b\nconv.v.l\nor.l.v\npop.v.v self.a\npush.v self.a\nconv.v.l\npush.v self.b\nconv.v.l\nor.l.l\npop.v.l self.a\npush.v self.a\npush.v self.b\nconv.v.l\nand.l.v\npop.v.v self.a\npush.v self.a\nconv.v.l\npush.v self.b\nconv.v.l\nand.l.l\npop.v.l self.a\npush.v self.a\npush.v self.b\nconv.v.l\nxor.l.v\npop.v.v self.a\npush.v self.a\nconv.v.l\npush.v self.b\nconv.v.l\nxor.l.l\npop.v.l self.a");
    }

    [Fact("RoundTrip.cs", 3852)]
    public void TestNullishPrePostOld()
    {
        TestUtil.VerifyRoundTripAssembly("a[b++] ??= 123;\na[++b] ??= 123;\na[b[c]++] ??= 123;\na[++b[c]] ??= 123;\na[b[c++]] ??= 123;\na[b[++c]] ??= 123;\na[b[c++]++] ??= 123;\na[b[++c]++] ??= 123;\na[++b[c++]] ??= 123;\na[++b[++c]] ??= 123;\na[b(c++, d++)] ??= 123;", "pushi.e -6\npush.v builtin.b\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [2]\n\n:[1]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.b\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.b\nconv.v.i\npop.v.v [array]self.a\nb [3]\n\n:[2]\npopz.v\n\n:[3]\npushi.e -6\npush.v builtin.b\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [5]\n\n:[4]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.b\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.b\nconv.v.i\npop.v.v [array]self.a\nb [6]\n\n:[5]\npopz.v\n\n:[6]\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [8]\n\n:[7]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [9]\n\n:[8]\npopz.v\n\n:[9]\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [11]\n\n:[10]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [12]\n\n:[11]\npopz.v\n\n:[12]\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [14]\n\n:[13]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [15]\n\n:[14]\npopz.v\n\n:[15]\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [17]\n\n:[16]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [18]\n\n:[17]\npopz.v\n\n:[18]\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [20]\n\n:[19]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [21]\n\n:[20]\npopz.v\n\n:[21]\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [23]\n\n:[22]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [24]\n\n:[23]\npopz.v\n\n:[24]\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [26]\n\n:[25]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [27]\n\n:[26]\npopz.v\n\n:[27]\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [29]\n\n:[28]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [30]\n\n:[29]\npopz.v\n\n:[30]\npushi.e -6\npush.v builtin.d\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.d\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\ncall.i @@This@@ 0\npush.v builtin.b\ncallv.v 2\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [32]\n\n:[31]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.d\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.d\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\ncall.i @@This@@ 0\npush.v builtin.b\ncallv.v 2\nconv.v.i\npop.v.v [array]self.a\nb [33]\n\n:[32]\npopz.v\n\n:[33]", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true
        });
    }

    [Fact("RoundTrip.cs", 4318)]
    public void TestNullishPrePostNew()
    {
        TestUtil.VerifyRoundTripAssembly("a[b++] ??= 123;\na[++b] ??= 123;\na[b[c]++] ??= 123;\na[++b[c]] ??= 123;\na[b[c++]] ??= 123;\na[b[++c]] ??= 123;\na[b[c++]++] ??= 123;\na[b[++c]++] ??= 123;\na[++b[c++]] ??= 123;\na[++b[++c]] ??= 123;\na[b(c++, d++)] ??= 123;", ":[0]\npushi.e -6\npush.v builtin.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [2]\n\n:[1]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.b\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.b\nconv.v.i\npop.v.v [array]self.a\nb [3]\n\n:[2]\npopz.v\npush.v builtin.b\npush.e 1\nadd.i.v\npop.v.v builtin.b\n\n:[3]\npushi.e -6\npush.v builtin.b\npushi.e 1\nadd.i.v\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [5]\n\n:[4]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.b\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.b\nconv.v.i\npop.v.v [array]self.a\nb [6]\n\n:[5]\npopz.v\npush.v builtin.b\npush.e 1\nadd.i.v\npop.v.v builtin.b\n\n:[6]\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [8]\n\n:[7]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [9]\n\n:[8]\npopz.v\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[9]\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\npush.v [array]self.b\npushi.e 1\nadd.i.v\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [11]\n\n:[10]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [12]\n\n:[11]\npopz.v\npushi.e -6\npush.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[12]\npushi.e -6\npushi.e -6\npush.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [14]\n\n:[13]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [15]\n\n:[14]\npopz.v\npush.v builtin.c\npush.e 1\nadd.i.v\npop.v.v builtin.c\n\n:[15]\npushi.e -6\npushi.e -6\npush.v builtin.c\npushi.e 1\nadd.i.v\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [17]\n\n:[16]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [18]\n\n:[17]\npopz.v\npush.v builtin.c\npush.e 1\nadd.i.v\npop.v.v builtin.c\n\n:[18]\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [20]\n\n:[19]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [21]\n\n:[20]\npopz.v\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[21]\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [23]\n\n:[22]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\ndup.v 0\ndup.i 4 6\npush.e 1\nadd.i.v\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [24]\n\n:[23]\npopz.v\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[24]\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\npushi.e 1\nadd.i.v\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [26]\n\n:[25]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [27]\n\n:[26]\npopz.v\npushi.e -6\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[27]\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\npush.v [array]self.b\npushi.e 1\nadd.i.v\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [29]\n\n:[28]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\ndup.v 0\ndup.i 4 6\npop.i.v [array]self.b\nconv.v.i\npop.v.v [array]self.a\nb [30]\n\n:[29]\npopz.v\npushi.e -6\npush.v builtin.c\npush.e 1\nadd.i.v\ndup.v 0\npop.v.v builtin.c\nconv.v.i\ndup.i 1\npush.v [array]self.b\npush.e 1\nadd.i.v\npop.i.v [array]self.b\n\n:[30]\npushi.e -6\npush.v builtin.d\npush.v builtin.c\ncall.i @@This@@ 0\npush.v builtin.b\ncallv.v 2\nconv.v.i\npush.v [array]self.a\nisnullish.e\nbf [32]\n\n:[31]\npopz.v\npushi.e 123\nconv.i.v\npushi.e -6\npush.v builtin.d\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.d\npush.v builtin.c\ndup.v 0\npush.e 1\nadd.i.v\npop.v.v builtin.c\ncall.i @@This@@ 0\npush.v builtin.b\ncallv.v 2\nconv.v.i\npop.v.v [array]self.a\nb [33]\n\n:[32]\npopz.v\npush.v builtin.c\npush.e 1\nadd.i.v\npop.v.v builtin.c\npush.v builtin.d\npush.e 1\nadd.i.v\npop.v.v builtin.d\n\n:[33]", isGlobalScript: false, new GameContextMock
        {
            UsingSelfToBuiltin = true,
            UsingNewNullishAssignSideEffects = true
        });
    }
}
