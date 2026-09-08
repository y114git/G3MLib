using System.Collections.Generic;
using System.Collections.ObjectModel;
using G3MLib.DataFile;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Models;
using Xunit;

namespace G3MLib.Tests;

public sealed class CodeImportGroupTests
{
    [Fact("CodeImportGroupTests.cs", 9)]
    public void QueueReplaceCreatesAndLinksScriptCode()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup codeImportGroup = new(data);
        codeImportGroup.QueueReplace("gml_Script_probe", "return 1;");
        codeImportGroup.Import();
        GameMakerScript gameMakerScript = data.Scripts.ByName("probe");
        Assert.NotNull(gameMakerScript.Code);
        Assert.Same(gameMakerScript.Code, data.Code.ByName("gml_Script_probe"));
    }

    [Fact("CodeImportGroupTests.cs", 23)]
    public void QueueReplaceWithoutAssetCreationCreatesOnlyCode()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup codeImportGroup = new(data)
        {
            AutoCreateAssets = false
        };
        codeImportGroup.QueueReplace("gml_Script_probe", "return 1;");
        codeImportGroup.Import();
        Assert.NotNull(data.Code.ByName("gml_Script_probe"));
        Assert.Null(data.Scripts.ByName("probe"));
    }

    [Fact("CodeImportGroupTests.cs", 36)]
    public void QueueAppendAndPrependModifyExistingCode()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup initial = new(data);
        initial.QueueReplace("gml_Script_probe", "return 1;");
        initial.Import();

        CodeImportGroup edits = new(data);
        edits.QueuePrepend("gml_Script_probe", "var before = 0;");
        edits.QueueAppend("gml_Script_probe", "var after = 2;");
        edits.Import();

        GameMakerCode code = data.Code.ByName("gml_Script_probe");
        Assert.NotEmpty(code.Instructions);
        Assert.NotNull(data.Scripts.ByName("probe").Code);
    }

    [Fact("CodeImportGroupTests.cs", 57)]
    public void QueueFindReplaceModifiesExistingCode()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup initial = new(data);
        initial.QueueReplace("gml_Script_probe", "return 1;");
        initial.Import();

        CodeImportGroup edits = new(data);
        edits.QueueFindReplace("gml_Script_probe", "return 1;", "return 2;");
        edits.Import();

        Assert.Equal("pushi.e 2", data.Code.ByName("gml_Script_probe").Instructions[0].ToString());
    }

    [Fact("CodeImportGroupTests.cs", 36)]
    public void QueueReplaceCreatesGlobalInitAndObjectEvent()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        CodeImportGroup codeImportGroup = new(data);
        codeImportGroup.QueueReplace("gml_GlobalScript_boot", "return 1;");
        codeImportGroup.QueueReplace("gml_Object_obj_probe_Create_0", "return 2;");
        codeImportGroup.Import();
        Assert.Equal("gml_GlobalScript_boot", Assert.Single(data.GlobalInitScripts).Code.Name.Content);
        GameMakerGameObject.EventAction action = Assert.Single((IEnumerable<GameMakerGameObject.EventAction>)Assert.Single((IEnumerable<GameMakerGameObject.Event>)((Collection<GameMakerPointerList<GameMakerGameObject.Event>>)(object)data.GameObjects.ByName("obj_probe").Events)[0]).Actions);
        Assert.NotNull(action.CodeId);
        Assert.Equal("gml_Object_obj_probe_Create_0", action.CodeId.Name.Content);
    }

    [Fact("CodeImportGroupTests.cs", 54)]
    public void QueueReplaceCreatesCollisionEvent()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.GameObjects.Add(new GameMakerGameObject
        {
            Name = data.Strings.MakeString("obj_target")
        });
        CodeImportGroup codeImportGroup = new(data);
        codeImportGroup.QueueReplace("gml_Object_obj_probe_Collision_obj_target", "return 3;");
        codeImportGroup.Import();
        GameMakerGameObject.Event @event = Assert.Single((IEnumerable<GameMakerGameObject.Event>)((Collection<GameMakerPointerList<GameMakerGameObject.Event>>)(object)data.GameObjects.ByName("obj_probe").Events)[4]);
        Assert.Equal(0u, @event.EventSubtype);
    }
}
