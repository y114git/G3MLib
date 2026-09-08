using System.Linq;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Integrity;
using Xunit;

namespace G3MLib.Tests;

public sealed class DataIntegrityServiceTests
{
    [Fact("DataIntegrityServiceTests.cs", 11)]
    public void ValidateOnlyReportsBrokenFunctionReferenceWithoutMutatingIt()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerCode code = GameMakerCode.CreateEmptyEntry(data, "gml_Script_probe");
        GameMakerFunction orphan = new() { Name = data.Strings.MakeString("gml_Script_probe") };
        GameMakerInstruction instruction = new() { ValueFunction = orphan };
        code.Instructions.Add(instruction);
        data.Code.Add(code);

        DataIntegrityResult result = DataIntegrityService.ValidateOnly(data);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("missing Function", System.StringComparison.Ordinal));
        Assert.Same(orphan, instruction.ValueFunction);
        Assert.Empty(data.Functions);
    }

    [Fact("DataIntegrityServiceTests.cs", 31)]
    public void RepairAndValidateRestoresFunctionAndSoundReferences()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        GameMakerCode code = GameMakerCode.CreateEmptyEntry(data, "gml_Script_probe");
        GameMakerInstruction instruction = new()
        {
            ValueFunction = new GameMakerFunction { Name = data.Strings.MakeString("gml_Script_probe") }
        };
        code.Instructions.Add(instruction);
        data.Code.Add(code);

        GameMakerEmbeddedAudio audio = new() { Name = data.Strings.MakeString("audio_0000") };
        data.EmbeddedAudio.Add(audio);
        GameMakerSound sound = new()
        {
            Name = data.Strings.MakeString("snd_probe"),
            Type = data.Strings.MakeString(string.Empty),
            GroupID = 0,
            AudioID = 0
        };
        data.Sounds.Add(sound);

        DataIntegrityResult result = DataIntegrityService.RepairAndValidate(data);

        Assert.True(result.Success);
        Assert.Contains(data.Functions, function => ReferenceEquals(function, instruction.ValueFunction));
        Assert.Same(audio, sound.AudioFile);
        Assert.Null(sound.Type);
        Assert.Contains(result.Repairs, repair => repair.Contains("Function", System.StringComparison.Ordinal));
        Assert.Contains(result.Repairs, repair => repair.Contains("Sound", System.StringComparison.Ordinal));
    }

    [Fact("DataIntegrityServiceTests.cs", 66)]
    public void RepairAndValidateRestoresKnown2023Branch()
    {
        using GameMakerData data = GameMakerData.CreateNew2026LTS();
        data.GeneralInfo.BytecodeVersion = 17;
        data.GeneralInfo.Major = 2023;
        data.GeneralInfo.Minor = 6;
        data.GeneralInfo.Release = 0;
        data.GeneralInfo.Build = 0;
        data.GeneralInfo.Branch = GameMakerGeneralInfo.BranchType.Post2022_0;

        DataIntegrityResult result = DataIntegrityService.RepairAndValidate(data);

        Assert.Equal(GameMakerGeneralInfo.BranchType.LTS2022_0, data.GeneralInfo.Branch);
        Assert.Contains(result.Repairs, repair => repair.Contains("GeneralInfo branch", System.StringComparison.Ordinal));
    }
}
