using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.Modding.Resources;

namespace G3MLib.Tests;

public sealed class DuplicateEmitterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"G3MLib-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static GameMakerData DataWithDuplicates(string fixture)
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        var data = GameMakerIO.Read(input);
        data.ParticleSystemEmitters.Add(new GameMakerParticleSystemEmitter { Name = data.Strings.MakeString("em_dup"), EmitCount = 1 });
        data.ParticleSystemEmitters.Add(new GameMakerParticleSystemEmitter { Name = data.Strings.MakeString("em_dup"), EmitCount = 7 });
        GameMakerParticleSystem system = new() { Name = data.Strings.MakeString("ps_dup"), Emitters = [] };
        foreach (var e in data.ParticleSystemEmitters.Where(e => e.Name.Content == "em_dup").Reverse())
        {
            system.Emitters.Add(new(e));
        }
        data.ParticleSystems.Add(system);
        return data;
    }

    private void Export(GameMakerData data) =>
        ResourceExportService.ExportTypes(data, _root, "", ["ParticleSystemEmitters", "ParticleSystems"]);

    private static void Import(GameMakerData data, string root)
    {
        ResourceImportService.SetPatchFileSystem(null);
        ResourceImportService.SetDataPaths("", "");
        ResourceImportService.Import("ParticleSystemEmitters", data, Path.Combine(root, "ParticleSystemEmitters"));
        ResourceImportService.Import("ParticleSystems", data, Path.Combine(root, "ParticleSystems"));
    }

    [Fact]
    public void DuplicateNamesExportToSeparateStableFolders()
    {
        using var data = DataWithDuplicates("data-2026.win");
        Export(data);
        string[] folders = [.. Directory.GetDirectories(Path.Combine(_root, "ParticleSystemEmitters")).Select(Path.GetFileName).Where(n => n.StartsWith("em_dup", StringComparison.Ordinal))];
        Assert.Equal(2, folders.Length);
        Assert.Equal(2, folders.Distinct().Count());
    }

    [Fact]
    public void ImportMapsDuplicateEmittersAndSystemReferencesBack()
    {
        using var data = DataWithDuplicates("data-2026.win");
        Export(data);
        Import(data, _root);
        GameMakerParticleSystemEmitter[] dups = [.. data.ParticleSystemEmitters.Where(e => e.Name.Content == "em_dup")];
        Assert.Equal([1, 7], dups.Select(e => e.EmitCount));
        Assert.Equal([dups[1], dups[0]], data.ParticleSystems.ByName("ps_dup").Emitters.Select(r => r.Resource));
    }

    [Fact]
    public void EditingTheSecondDuplicateChangesOnlyTheSecond()
    {
        using var data = DataWithDuplicates("data-2026.win");
        Export(data);
        var second = Directory.GetDirectories(Path.Combine(_root, "ParticleSystemEmitters"))
            .Single(d => File.ReadAllText(Path.Combine(d, Path.GetFileName(d) + ".json")).Contains("\"EmitCount\": 7", StringComparison.Ordinal));
        var file = Path.Combine(second, Path.GetFileName(second) + ".json");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"EmitCount\": 7", "\"EmitCount\": 9", StringComparison.Ordinal));
        Import(data, _root);
        Assert.Equal([1, 9], data.ParticleSystemEmitters.Where(e => e.Name.Content == "em_dup").Select(e => e.EmitCount));
    }
}
