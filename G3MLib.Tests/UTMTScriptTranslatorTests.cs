using System;
using G3MLib.DataFile.Legacy;
using Xunit;

namespace G3MLib.Tests;

public sealed class UTMTScriptTranslatorTests
{
    [Fact("UTMTScriptTranslatorTests.cs", 10)]
    public void TranslateRewritesLegacyIdentifiersOnly()
    {
        Assert.Equal("using G3MLib.DataFile;\nusing G3MLib.Analyzer;\nGameMakerData data = new GameMakerData();\nGameMakerChunkABCD chunk = null;\nvar text = \"UndertaleData\";\n// UndertaleData", UTMTScriptTranslator.Translate("using UndertaleModLib;\nusing Underanalyzer;\nUndertaleData data = new UndertaleData();\nUndertaleChunkABCD chunk = null;\nvar text = \"UndertaleData\";\n// UndertaleData"));
    }

    [Fact("UTMTScriptTranslatorTests.cs", 16)]
    public void TranslateReturnsCurrentScriptsUnchanged()
    {
        string source = "using G3MLib.DataFile; GameMakerData data = new();";
        Assert.Same(source, UTMTScriptTranslator.Translate(source));
    }

    [Fact("UTMTScriptTranslatorTests.cs", 23)]
    public void TranslateRewritesIdentifiersInsideNameofAndTypeofOnly()
    {
        Assert.Equal("var name = nameof(GameMakerData);\nvar type = typeof(GameMakerRoom);\nvar text = $\"UndertaleData {nameof(GameMakerRoom)}\";\nvar verbatim = @\"UndertaleData\";", UTMTScriptTranslator.Translate("var name = nameof(UndertaleData);\nvar type = typeof(UndertaleRoom);\nvar text = $\"UndertaleData {nameof(UndertaleRoom)}\";\nvar verbatim = @\"UndertaleData\";"));
    }

    [Fact("UTMTScriptTranslatorTests.cs", 29)]
    public void TranslateRejectsNullSource()
    {
        Assert.Throws<ArgumentNullException>(() => UTMTScriptTranslator.Translate(null));
    }
}
