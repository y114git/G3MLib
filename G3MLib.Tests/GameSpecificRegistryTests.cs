using System;
using System.IO;
using System.Linq;
using G3MLib.DataFile.Decompiler;
using G3MLib.Analyzer.Decompiler;
using G3MLib.Analyzer.Decompiler.AST;
using G3MLib.Analyzer.Decompiler.GameSpecific;
using G3MLib.Analyzer.Mock;
using Xunit;

namespace G3MLib.Tests;

public class GameSpecificRegistryTests
{
    [Fact("GameSpecificRegistryTests.cs", 7)]
    public void DeserializeFromJsonReadsUnionAndIntersectTypes()
    {
        GameSpecificRegistry gameSpecificRegistry = new();
        gameSpecificRegistry.DeserializeFromJson("{\n  \"Types\": {\n    \"RegisterBasic\": true,\n    \"General\": {\n      \"Union\": { \"MacroType\": \"Union\", \"Macros\": [\"Bool\", \"Constant.Color\"] },\n      \"Intersect\": { \"MacroType\": \"Intersect\", \"Macros\": [\"Bool\", \"Constant.Color\"] }\n    }\n  }\n}".AsSpan());
        Assert.IsType<UnionMacroType>(gameSpecificRegistry.FindType("Union"));
        Assert.IsType<IntersectMacroType>(gameSpecificRegistry.FindType("Intersect"));
    }

    [Fact("GameSpecificRegistryTests.cs", 20)]
    public void DeserializeEveryBundledDefinition()
    {
        string[] definitions = [.. typeof(GameSpecificResolver).Assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".GameSpecificData.Analyzer.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)];
        Assert.NotEmpty(definitions);
        string template = definitions.Single(name => name.EndsWith(".template.json", StringComparison.Ordinal));
        string gameMaker = definitions.Single(name => name.EndsWith(".gamemaker.json", StringComparison.Ordinal));

        foreach (string definition in definitions)
        {
            GameSpecificRegistry registry = new();
            registry.DeserializeFromJson(ReadEmbeddedResource(template).AsSpan());
            registry.DeserializeFromJson(ReadEmbeddedResource(gameMaker).AsSpan());
            if (StringComparer.OrdinalIgnoreCase.Equals(definition, template) ||
                StringComparer.OrdinalIgnoreCase.Equals(definition, gameMaker))
            {
                continue;
            }
            registry.DeserializeFromJson(ReadEmbeddedResource(definition).AsSpan());
        }
    }

    private static string ReadEmbeddedResource(string resourceName)
    {
        using Stream stream = typeof(GameSpecificResolver).Assembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    [Fact("GameSpecificRegistryTests.cs", 35)]
    public void BuiltInMacroTypesResolveKnownAndFallbackValues()
    {
        GameContextMock gameContext = new()
        {
            UsingTypedBooleans = false
        };
        ASTCleaner cleaner = CreateCleaner(gameContext, useCssColors: true);

        BooleanMacroType boolean = new();
        Assert.Equal("false", Assert.IsType<MacroValueNode>(boolean.Resolve(cleaner, new Int32Node(0), 0)).ValueName);
        Assert.Equal("true", Assert.IsType<MacroValueNode>(boolean.Resolve(cleaner, new Int32Node(1), 1)).ValueName);
        Assert.Null(boolean.Resolve(cleaner, new Int32Node(2), 2));
        gameContext.UsingTypedBooleans = true;
        Assert.Null(boolean.Resolve(cleaner, new Int32Node(0), 0));

        ColorMacroType color = new();
        Assert.Equal("c_black", Assert.IsType<MacroValueNode>(color.Resolve(cleaner, new Int32Node(0), 0)).ValueName);
        Assert.Equal("#332211", Assert.IsType<MacroValueNode>(color.Resolve(cleaner, new Int32Node(0x112233), 0x112233)).ValueName);
        Int32Node negative = new(-1);
        Assert.Same(negative, color.Resolve(cleaner, negative, -1));

        VirtualKeyMacroType virtualKey = new();
        Assert.Equal("vk_enter", Assert.IsType<MacroValueNode>(virtualKey.Resolve(cleaner, new Int32Node(13), 13)).ValueName);
        Assert.Equal("ord(\"A\")", Assert.IsType<MacroValueNode>(virtualKey.Resolve(cleaner, new Int32Node('A'), 'A')).ValueName);
        Assert.Null(virtualKey.Resolve(cleaner, new Int32Node(255), 255));
    }

    private static ASTCleaner CreateCleaner(GameContextMock gameContext, bool useCssColors)
    {
        DecompileContext context = new(gameContext, new GMCode("test", []), new DecompileSettings
        {
            UseCSSColors = useCssColors
        });
        return new ASTCleaner(context);
    }
}
