using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace G3MLib.Modding.Hashing;

public static class FileHash
{
	private const int BufferSize = 1024 * 1024;

	public static async Task<string> ComputeMd5Async(string filePath, CancellationToken cancellationToken = default)
	{
		using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
	}

	public static string ComputeMd5(string filePath)
	{
		using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
			FileOptions.SequentialScan);
		return Convert.ToHexStringLower(MD5.HashData(stream));
	}

	public static bool HaveSameSize(string firstPath, string secondPath)
	{
		return new FileInfo(firstPath).Length == new FileInfo(secondPath).Length;
	}
}
