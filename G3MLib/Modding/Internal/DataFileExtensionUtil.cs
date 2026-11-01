using System.IO;

namespace G3MLib.Modding.Internal;

public static class DataFileExtensionUtil
{
	public static readonly string[] ValidExtensions = [".win", ".ios", ".droid", ".unx"];

	public static bool IsValidDataExtension(string extension)
	{
		ArgumentNullException.ThrowIfNull(extension);
		return ValidExtensions.Contains(extension.ToLowerInvariant());
	}

	public static bool IsDataFile(string filePath)
	{
		return IsValidDataExtension(Path.GetExtension(filePath));
	}

	public static string GetOutputExtension(string? inputPath)
	{
		if (string.IsNullOrEmpty(inputPath))
		{
			return ".win";
		}
		var ext = Path.GetExtension(inputPath).ToLowerInvariant();
		if (!IsValidDataExtension(ext))
		{
			return ".win";
		}
		return ext;
	}

	public static string GetValidExtensionsDisplay()
	{
		return string.Join(", ", ValidExtensions.Select(e => "`" + e + "`"));
	}
}
