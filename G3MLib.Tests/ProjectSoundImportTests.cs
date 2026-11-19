using System.Text.Json.Nodes;
using G3MLib.DataFile;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;

namespace G3MLib.Tests;

public sealed class ProjectSoundImportTests
{
    [Fact]
    public void NewSoundImportRequestsTheAudioGroupFromTheJsonGroupId()
    {
        var directory = Directory.CreateTempSubdirectory("G3MLib-snd-").FullName;
        try
        {
            using var source = GameMakerData.CreateNew2026LTS();
            source.Sounds.Add(new GameMakerSound
            {
                Name = source.Strings.MakeString("snd_new"),
                File = source.Strings.MakeString("snd_new.ogg"),
                Type = source.Strings.MakeString(".ogg")
            });
            var mainFile = Path.Combine(directory, "project.json");
            ProjectContext export = new(source, "input.win", "output.win", mainFile, "Test");
            export.MarkAssetForExport(source.Sounds[0]);
            export.Export(clearMarkedAssets: true);

            var soundJson = Directory.EnumerateFiles(directory, "snd_new.json", SearchOption.AllDirectories).Single();
            var json = JsonNode.Parse(File.ReadAllText(soundJson))!;
            json["GroupID"] = 2;
            File.WriteAllText(soundJson, json.ToJsonString());
            File.WriteAllBytes(Path.ChangeExtension(soundJson, ".ogg"), [1, 2, 3]);
            using (var group = GameMakerData.CreateNew2026LTS())
            using (var stream = File.Create(Path.Combine(directory, "audiogroup2.dat")))
            {
                GameMakerIO.Write(stream, group);
            }

            using var target = GameMakerData.CreateNew2026LTS();
            ProjectContext.CreateWithDirectories(directory, directory, mainFile).Import(target, new GameFileNoOpBackup());
            var sound = target.Sounds.ByName("snd_new");
            Assert.Equal(2, sound.GroupID);
            Assert.Equal(0, sound.AudioID);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ExportMapsEachInvalidNameCharacterAndStaysInsideTheProjectFolder()
    {
        var outer = Directory.CreateTempSubdirectory("G3MLib-name-").FullName;
        try
        {
            var directory = Path.Combine(outer, "project");
            using var source = GameMakerData.CreateNew2026LTS();
            string[] names = ["spr-a", "a/../../x"];
            foreach (var name in names)
            {
                source.Sounds.Add(new GameMakerSound
                {
                    Name = source.Strings.MakeString(name),
                    File = source.Strings.MakeString("s.ogg"),
                    Type = source.Strings.MakeString(".ogg")
                });
            }
            ProjectContext export = new(source, "input.win", "output.win", Path.Combine(directory, "project.json"), "Test");
            foreach (var sound in source.Sounds)
            {
                export.MarkAssetForExport(sound);
            }
            export.Export(clearMarkedAssets: true);

            var files = Directory.EnumerateFiles(outer, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(directory, f).Replace(Path.DirectorySeparatorChar, '/')).Order().ToArray();
            Assert.Contains("sounds/spr_a/spr_a.json", files);
            Assert.Contains("sounds/a_______x/a_______x.json", files);
            Assert.All(files.Where(f => f != "project.json"), f => Assert.StartsWith("sounds/", f, StringComparison.Ordinal));
        }
        finally { Directory.Delete(outer, true); }
    }
}
