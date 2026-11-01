using System.IO;
using System.Text;
using G3MLib.DataFile.Project;

namespace G3MLib.Modding.Resources;

/// <summary>
/// Text file writes of the resource exporters.
/// </summary>
/// <remarks>
/// A project export normalizes every json, gml and asm file (see <see cref="ProjectContext"/>). While
/// <see cref="NormalizeForProject"/> is set, those files are written in that form at once instead of being written
/// and rewritten, which halves the writes on a file system that scans every new file.
/// </remarks>
internal static class ExportText
{
	private static readonly string[] NormalizedExtensions = [".json", ".gml", ".asm"];

	private static readonly AsyncLocal<bool> normalizeForProject = new();

	/// <summary>Set by the project export for the current execution context and the tasks it starts.</summary>
	internal static bool NormalizeForProject
	{
		get => normalizeForProject.Value;
		set => normalizeForProject.Value = value;
	}

	internal static void WriteAllText(string path, string contents, Encoding? encoding = null)
	{
		if (NormalizeForProject && Array.Exists(NormalizedExtensions, e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
		{
			File.WriteAllBytes(path, ProjectContext.NormalizeTextBytes(new UTF8Encoding(false).GetBytes(contents)));
		}
		else if (encoding is null)
		{
			File.WriteAllText(path, contents);
		}
		else
		{
			File.WriteAllText(path, contents, encoding);
		}
	}
}
