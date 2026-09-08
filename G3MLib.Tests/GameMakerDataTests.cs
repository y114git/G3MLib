using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;
using Xunit;

namespace G3MLib.Tests;

public sealed class GameMakerDataTests
{
    [Fact("GameMakerDataTests.cs", 9)]
    public void ResourceListIndexerReturnsTheMatchingResourceList()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        Assert.Same(data.Sprites, data[typeof(GameMakerSprite)]);
    }

    [Fact("GameMakerDataTests.cs", 17)]
    public void ResourceListIndexerRejectsNonResourceTypes()
    {
        GameMakerData data = GameMakerData.CreateNew2026LTS();
        try
        {
            Assert.Throws<NotSupportedException>(() => data[typeof(string)]);
        }
        finally
        {
            if (data != null)
            {
                ((IDisposable)data).Dispose();
            }
        }
    }

    [Fact("GameMakerDataTests.cs", 25)]
    public void ResourceListIndexersReplaceListsAtomically()
    {
        GameMakerData data = GameMakerData.CreateNew2026LTS();
        try
        {
            IList<GameMakerSprite> sprites = data.Sprites;
            GameMakerSprite sprite = new()
            {
                Name = data.Strings.MakeString("sprite")
            };
            Assert.Same(sprites, data["Sprites"]);
            data["Sprites"] = new List<GameMakerSprite> { sprite };
            Assert.Same(sprites, data[typeof(GameMakerSprite)]);
            GameMakerSprite onlySprite = Assert.Single(data.Sprites);
            Assert.Same(sprite, onlySprite);
            Assert.Throws<ArgumentException>(delegate
            {
                GameMakerData gameMakerData = data;
                List<GameMakerRoom> obj =
                [
                    new GameMakerRoom()
                ];
                IList result = obj;
                gameMakerData["Sprites"] = obj;
                return result;
            });
            Assert.Single(data.Sprites);
            Assert.Throws<MissingMemberException>(() => data["Missing"]);
            Assert.Throws<ArgumentNullException>(() => data[(Type)null]);
        }
        finally
        {
            if (data != null)
            {
                ((IDisposable)data).Dispose();
            }
        }
    }

    [Fact("GameMakerDataTests.cs", 44)]
    public void ResourceLookupFindsTheExpectedRoom()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerRoom room = data.Rooms.ByName("room0");
        Assert.Same(room, data.ByName("room0"));
        Assert.Equal(0, data.IndexOf(room));
        Assert.Equal(0, data.IndexOfByName("room0"));
        Assert.Equal(-1, data.IndexOfByName("missing"));
    }

    [Fact("GameMakerDataTests.cs", 56)]
    public void NamedResourceAndStringListExtensionsHandleLookupAndCreation()
    {
        GameMakerRoom room = new()
        {
            Name = new GameMakerString("Room")
        };
        int num = 1;
        List<GameMakerRoom> list = new(num);
        CollectionsMarshal.SetCount(list, num);
        CollectionsMarshal.AsSpan(list)[0] = room;
        List<GameMakerRoom> rooms = list;
        num = 1;
        List<GameMakerString> list2 = new(num);
        CollectionsMarshal.SetCount(list2, num);
        CollectionsMarshal.AsSpan(list2)[0] = new GameMakerString("existing");
        List<GameMakerString> strings = list2;
        Assert.Null(rooms.ByName("room"));
        Assert.Same(room, rooms.ByName("room".AsSpan(), ignoreCase: true));
        Assert.Equal(0, rooms.IndexOfName("room", ignoreCase: true));
        Assert.Equal(-1, rooms.IndexOfName("missing".AsSpan()));
        Assert.Same(strings[0], strings.MakeString("existing", out var existingIndex));
        Assert.Equal(0, existingIndex);
        GameMakerString expected = strings.MakeString("created", out int createdIndex);
        Assert.Equal(1, createdIndex);
        Assert.Same(expected, strings.MakeString("created"));
        Assert.NotSame(expected, strings.MakeString("created", createNew: true));
        Assert.Throws<ArgumentNullException>(() => strings.MakeString(null));
    }

    [Fact("GameMakerDataTests.cs", 77)]
    public void CompilerListExtensionsDefineAndReuseEntries()
    {
        GameMakerData data = GameMakerData.CreateNew2026LTS();
        try
        {
            List<GameMakerString> strings = [];
            List<GameMakerFunction> functions = [];
            List<GameMakerVariable> variables = [];
            GameMakerString name = strings.MakeString("value", out int nameIndex);
            GameMakerFunction function = functions.EnsureDefined("value", strings);
            Assert.Same(function, functions.EnsureDefined("value", strings));
            Assert.Equal(0, function.NameStringID);
            GameMakerVariable variable = variables.EnsureDefined(name, nameIndex, GameMakerInstruction.InstanceType.Global, isBuiltin: false, data);
            Assert.Same(variable, variables.EnsureDefined(name, nameIndex, GameMakerInstruction.InstanceType.Global, isBuiltin: false, data));
            Assert.Equal(nameIndex, variable.VarID);
            Assert.Equal(GameMakerInstruction.InstanceType.Self, variables.CalculateInstType(GameMakerInstruction.InstanceType.Global, isBuiltin: true, data));
            Assert.Throws<InvalidOperationException>(() => variables.CalculateInstType(GameMakerInstruction.InstanceType.Local, isBuiltin: false, data));
            data.GeneralInfo.BytecodeVersion = 14;
            List<GameMakerVariable> legacyVariables = [];
            GameMakerVariable local = legacyVariables.DefineLocal(data, 5, name, nameIndex);
            Assert.Same(local, legacyVariables.DefineLocal(data, 6, name, nameIndex));
            Assert.Equal(GameMakerInstruction.InstanceType.Undefined, local.InstanceType);
            GameMakerCode code = new()
            {
                Name = name
            };
            GameMakerCodeLocals codeLocals = new()
            {
                Name = new GameMakerString("value")
            };
            Assert.Same(codeLocals, new List<GameMakerCodeLocals> { codeLocals }.For(code));
            GameMakerExtensionFunction extensionFunction = new List<GameMakerExtensionFunction>().DefineExtensionFunction(functions, strings, 1u, 2u, "external", GameMakerExtensionVarType.Double, "external_name", GameMakerExtensionVarType.String);
            Assert.Equal("external", extensionFunction.Name.Content);
            Assert.Equal("external_name", extensionFunction.ExtName.Content);
            Assert.Equal(GameMakerExtensionVarType.String, Assert.Single((IEnumerable<GameMakerExtensionFunctionArg>)extensionFunction.Arguments).Type);
            Assert.NotNull(functions.ByName("external"));
        }
        finally
        {
            if (data != null)
            {
                ((IDisposable)data).Dispose();
            }
        }
    }

    [Fact("GameMakerDataTests.cs", 115)]
    public void AssetReferenceTypeConvertersRejectUnknownValues()
    {
        GameMakerData data = GameMakerData.CreateNew2026LTS();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AssetReferenceTypes.ConvertToRefType(data, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => AssetReferenceTypes.ConvertFromRefType(data, (AssetReferenceTypes.RefType)(-1)));
        }
        finally
        {
            if (data != null)
            {
                ((IDisposable)data).Dispose();
            }
        }
    }

    [Fact("GameMakerDataTests.cs", 124)]
    public void AssetReferenceTypeConvertersUseTheCorrectVersionedMappings()
    {
        using GameMakerData oldData = GameMakerData.CreateNew2026LTS();
        oldData.SetGMS2Version(2023u, 11u);
        Assert.Equal(AssetReferenceTypes.RefType.Background, AssetReferenceTypes.ConvertToRefType(oldData, 4));
        Assert.Equal(AssetReferenceTypes.RefType.ParticleSystem, AssetReferenceTypes.ConvertToRefType(oldData, 13));
        Assert.Equal(4, AssetReferenceTypes.ConvertFromRefType(oldData, AssetReferenceTypes.RefType.Background));
        Assert.Equal(13, AssetReferenceTypes.ConvertFromRefType(oldData, AssetReferenceTypes.RefType.ParticleSystem));
        using GameMakerData currentData = GameMakerData.CreateNew2026LTS();
        Assert.Equal(AssetReferenceTypes.RefType.Path, AssetReferenceTypes.ConvertToRefType(currentData, 4));
        Assert.Equal(AssetReferenceTypes.RefType.Background, AssetReferenceTypes.ConvertToRefType(currentData, 13));
        Assert.Equal(4, AssetReferenceTypes.ConvertFromRefType(currentData, AssetReferenceTypes.RefType.Path));
        Assert.Equal(13, AssetReferenceTypes.ConvertFromRefType(currentData, AssetReferenceTypes.RefType.Background));
    }

    [Fact("GameMakerDataTests.cs", 148)]
    public void AddedExtensionWritesRequiredProductData()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.Extensions.Add(new GameMakerExtension
        {
            FolderName = data.Strings.MakeString(""),
            Name = data.Strings.MakeString("test_extension"),
            Version = data.Strings.MakeString(""),
            ClassName = data.Strings.MakeString(""),
            Files = [],
            Options = []
        });

        using MemoryStream stream = new();
        GameMakerIO.Write(stream, data);
        stream.Position = 0;
        using GameMakerData reread = GameMakerIO.Read(stream);
        Assert.Equal("test_extension", Assert.Single(reread.Extensions).Name.Content);
        Assert.Single(reread.FORM.EXTN.productIdData);
        Assert.Equal(16, reread.FORM.EXTN.productIdData[0].Length);
    }
}
