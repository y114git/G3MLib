using System.Globalization;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using G3MLib.DataFile;
using G3MLib.DataFile.Decompiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Project;
using G3MLib.DataFile.Util;

namespace G3MLib.Modding.Resources;

public static class ResourceHashService
{

	public static Dictionary<string, Dictionary<string, string>> HashAll(GameMakerData data, string? dataFilePath = null)
	{
		ArgumentNullException.ThrowIfNull(data);
		return new Dictionary<string, Dictionary<string, string>>
		{
			["GeneralInfo"] = HashGeneralInfo(data),
			["Options"] = HashOptions(data),
			["GlobalScripts"] = HashGlobalScripts(data),
			["Scripts"] = HashScripts(data),
			["Language"] = HashLanguage(data),
			["FeatureFlags"] = HashFeatureFlags(data),
			["Tags"] = HashTags(data),
			["FilterEffects"] = HashFilterEffects(data),
			["AudioGroups"] = HashAudioGroups(data),
			["EmbeddedAudio"] = HashEmbeddedAudio(data),
			["TextureGroupInfo"] = HashTextureGroupInfo(data),
			["EmbeddedTextures"] = HashEmbeddedTextures(data),
			["TexturePageItems"] = HashTexturePageItems(data),
			["EmbeddedImages"] = HashEmbeddedImages(data),
			["Sprites"] = HashSprites(data),
			["Backgrounds"] = HashBackgrounds(data),
			["Fonts"] = HashFonts(data),
			["Sounds"] = HashSounds(data, dataFilePath),
			["Paths"] = HashPaths(data),
			["Tilesets"] = HashTilesets(data),
			["Shaders"] = HashShaders(data),
			["Timelines"] = HashTimelines(data),
			["GameObjects"] = HashGameObjects(data),
			["Rooms"] = HashRooms(data),
			["AnimationCurves"] = HashAnimationCurves(data),
			["ParticleSystemEmitters"] = HashParticleSystemEmitters(data),
			["ParticleSystems"] = HashParticleSystems(data),
			["Sequences"] = HashSequences(data),
			["CodeEntries"] = HashCodeEntries(data),
			["Extensions"] = HashExtensions(data)
		};
	}

	private static string HexHash(byte[] data)
	{
		return Convert.ToHexStringLower(SHA256.HashData(data));
	}

	private static string HexHash(ReadOnlySpan<byte> data)
	{
		return Convert.ToHexStringLower(SHA256.HashData(data));
	}

	private static string JsonHash(Action<Utf8JsonWriter> writeAction)
	{
		using var ms = new MemoryStream();
		using (var w = new Utf8JsonWriter(ms))
		{
			writeAction(w);
		}
		return HexHash(ms.GetBuffer().AsSpan(0, (int)ms.Length));
	}

	private static string CanonicalJsonHash<T>(T value)
	{
		using var document = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value, ProjectContext.JsonOptions));
		return JsonHash(writer => WriteCanonicalJson(writer, document.RootElement));
	}

	private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				foreach (var property in element.EnumerateObject().OrderBy(static property => property.Name, StringComparer.Ordinal))
				{
					writer.WritePropertyName(property.Name);
					WriteCanonicalJson(writer, property.Value);
				}
				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (var item in element.EnumerateArray())
				{
					WriteCanonicalJson(writer, item);
				}
				writer.WriteEndArray();
				break;
			default:
				element.WriteTo(writer);
				break;
		}
	}

	private static Dictionary<string, string> HashEmbeddedAudio(GameMakerData data)
	{
		if (data.EmbeddedAudio == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.For(0, data.EmbeddedAudio.Count, delegate (int i)
		{
			var gameMakerEmbeddedAudio = data.EmbeddedAudio[i];
			var key = $"audio_{i:D4}";
			var data2 = gameMakerEmbeddedAudio?.Data ?? [];
			result[key] = HexHash(data2);
		});
		return new Dictionary<string, string>(result, StringComparer.OrdinalIgnoreCase);
	}

	private static Dictionary<string, string> HashEmbeddedTextures(GameMakerData data)
	{
		if (data.EmbeddedTextures == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.For(0, data.EmbeddedTextures.Count, delegate (int i)
		{
			var gameMakerEmbeddedTexture = data.EmbeddedTextures[i];
			if (gameMakerEmbeddedTexture != null)
			{
				result[$"texture_{i:D4}"] = HashEmbeddedTexture(gameMakerEmbeddedTexture);
			}
		});
		return new Dictionary<string, string>(result, StringComparer.OrdinalIgnoreCase);
	}

	private static string HashEmbeddedTexture(GameMakerEmbeddedTexture texture)
	{
		var name = Encoding.UTF8.GetBytes(texture.Name?.Content ?? string.Empty);
		var format = Encoding.UTF8.GetBytes(texture.TextureData?.Image?.Format.ToString() ?? string.Empty);
		var image = texture.TextureData?.Image?.GetData() ?? [];
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		Span<byte> intBuffer = stackalloc byte[4];
		Span<byte> uintBuffer = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, name.Length);
		hash.AppendData(intBuffer);
		hash.AppendData(name);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, texture.Scaled);
		hash.AppendData(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, texture.GeneratedMips);
		hash.AppendData(uintBuffer);
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, format.Length);
		hash.AppendData(intBuffer);
		hash.AppendData(format);
		BinaryPrimitives.WriteInt32LittleEndian(intBuffer, image.Length);
		hash.AppendData(intBuffer);
		hash.AppendData(image);
		return Convert.ToHexStringLower(hash.GetHashAndReset());
	}

	private static Dictionary<string, string> HashGeneralInfo(GameMakerData data)
	{
		if (data.GeneralInfo == null)
		{
			return [];
		}
		var gi = data.GeneralInfo;
		var hash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartObject();
			w.WriteBoolean("isDebuggerDisabled", gi.IsDebuggerDisabled);
			w.WriteNumber("bytecodeVersion", gi.BytecodeVersion);
			w.WriteString("fileName", gi.FileName?.Content ?? "");
			w.WriteString("config", gi.Config?.Content ?? "");
			w.WriteNumber("lastObj", gi.LastObj);
			w.WriteNumber("lastTile", gi.LastTile);
			w.WriteNumber("gameID", gi.GameID);
			w.WriteString("name", gi.Name?.Content ?? "");
			w.WriteNumber("major", gi.Major);
			w.WriteNumber("minor", gi.Minor);
			w.WriteNumber("release", gi.Release);
			w.WriteNumber("build", gi.Build);
			w.WriteNumber("defaultWindowWidth", gi.DefaultWindowWidth);
			w.WriteNumber("defaultWindowHeight", gi.DefaultWindowHeight);
			w.WriteNumber("infoFlags", (uint)gi.Info);
			w.WriteNumber("licenseCRC32", gi.LicenseCRC32);
			w.WriteNumber("timestamp", gi.Timestamp);
			w.WriteString("displayName", gi.DisplayName?.Content ?? "");
			w.WriteNumber("activeTargets", gi.ActiveTargets);
			w.WriteNumber("steamAppID", gi.SteamAppID);
			w.WritePropertyName("roomOrder");
			w.WriteStartArray();
			foreach (var item in gi.RoomOrder)
			{
				w.WriteStringValue(item?.Resource?.Name?.Content ?? "");
			}
			w.WriteEndArray();
			if (gi.Major >= 2)
			{
				w.WriteNumber("gms2FPS", gi.GMS2FPS);
			}
			w.WriteEndObject();
		});
		return new Dictionary<string, string> { ["GeneralInfo"] = hash };
	}

	private static Dictionary<string, string> HashAudioGroups(GameMakerData data)
	{
		if (data.AudioGroups == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.AudioGroups, delegate (GameMakerAudioGroup ag)
		{
			if (ag?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", ag.Name.Content);
					if (ag.Path != null)
					{
						w.WriteString("path", ag.Path.Content ?? "");
					}
					w.WriteEndObject();
				});
				result[ag.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashTextureGroupInfo(GameMakerData data)
	{
		if (data.TextureGroupInfo == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.TextureGroupInfo, delegate (GameMakerTextureGroupInfo tg)
		{
			if (tg?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", tg.Name.Content);
					if (data.IsVersionAtLeast(2022u, 9u))
					{
						if (tg.Directory != null)
						{
							w.WriteString("directory", tg.Directory.Content ?? "");
						}
						if (tg.Extension != null)
						{
							w.WriteString("extension", tg.Extension.Content ?? "");
						}
						w.WriteNumber("loadType", (int)tg.LoadType);
					}
					w.WriteStartArray("texturePages");
					if (tg.TexturePages != null)
					{
						foreach (var current in tg.TexturePages)
						{
							if (current.Resource?.Name?.Content != null)
							{
								w.WriteStringValue(current.Resource.Name.Content);
							}
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("sprites");
					if (tg.Sprites != null)
					{
						foreach (var current2 in tg.Sprites)
						{
							if (current2.Resource?.Name?.Content != null)
							{
								w.WriteStringValue(current2.Resource.Name.Content);
							}
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("fonts");
					if (tg.Fonts != null)
					{
						foreach (var current3 in tg.Fonts)
						{
							if (current3.Resource?.Name?.Content != null)
							{
								w.WriteStringValue(current3.Resource.Name.Content);
							}
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("tilesets");
					if (tg.Tilesets != null)
					{
						foreach (var current4 in tg.Tilesets)
						{
							if (current4.Resource?.Name?.Content != null)
							{
								w.WriteStringValue(current4.Resource.Name.Content);
							}
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
				result[tg.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashFeatureFlags(GameMakerData data)
	{
		if (data.FeatureFlags?.List == null)
		{
			return [];
		}
		var hash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartObject();
			w.WriteStartArray("flags");
			foreach (var current in data.FeatureFlags.List.Select(f => f?.Content ?? "").OrderBy(f => f, StringComparer.Ordinal))
			{
				w.WriteStringValue(current);
			}
			w.WriteEndArray();
			w.WriteEndObject();
		});
		return new Dictionary<string, string> { ["FeatureFlags"] = hash };
	}

	private static Dictionary<string, string> HashLanguage(GameMakerData data)
	{
		if (data.Language == null)
		{
			return [];
		}
		var hash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartObject();
			w.WriteNumber("unknown1", data.Language.Unknown1);
			w.WriteStartArray("entryIds");
			foreach (var item in data.Language.EntryIDs ?? [])
			{
				w.WriteStringValue(item?.Content ?? "");
			}
			w.WriteEndArray();
			w.WriteStartArray("languages");
			foreach (var current in data.Language.Languages ?? [])
			{
				if (current != null)
				{
					w.WriteStartObject();
					w.WriteString("name", current.Name?.Content ?? "");
					w.WriteString("region", current.Region?.Content ?? "");
					w.WriteStartArray("entries");
					foreach (var item2 in current.Entries ?? [])
					{
						w.WriteStringValue(item2?.Content ?? "");
					}
					w.WriteEndArray();
					w.WriteEndObject();
				}
			}
			w.WriteEndArray();
			w.WriteEndObject();
		});
		return new Dictionary<string, string> { ["Language"] = hash };
	}

	private static Dictionary<string, string> HashTags(GameMakerData data)
	{
		if (data.Tags == null)
		{
			return [];
		}
		var hash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartObject();
			w.WriteStartArray("tags");
			if (data.Tags.Tags != null)
			{
				foreach (var current in data.Tags.Tags.Select(t => t?.Content ?? "").OrderBy(t => t, StringComparer.Ordinal))
				{
					w.WriteStringValue(current);
				}
			}
			w.WriteEndArray();
			w.WriteStartArray("assetTags");
			if (data.Tags.AssetTags != null)
			{
				foreach (var (value, gameMakerSimpleListString, tuple2) in data.Tags.AssetTags.Select<KeyValuePair<int, GameMakerSimpleListString>, (int AssetId, GameMakerSimpleListString Tags, (string Type, string Name) Decoded)>(kvp => (kvp.Key, kvp.Value, DecodeAssetTagId(data, kvp.Key))).OrderBy(e => e.Decoded.Type, StringComparer.Ordinal).ThenBy(e => e.Decoded.Name, StringComparer.Ordinal)
					.ThenBy(e => e.AssetId))
				{
					w.WriteStartObject();
					w.WriteString("assetType", tuple2.Type);
					w.WriteString("assetName", tuple2.Name);
					if (string.IsNullOrEmpty(tuple2.Name))
					{
						w.WriteNumber("assetId", value);
					}
					w.WriteStartArray("tags");
					foreach (var current2 in (gameMakerSimpleListString ?? []).Select(t => t?.Content ?? "").OrderBy(t => t, StringComparer.Ordinal))
					{
						w.WriteStringValue(current2);
					}
					w.WriteEndArray();
					w.WriteEndObject();
				}
			}
			w.WriteEndArray();
			w.WriteEndObject();
		});
		return new Dictionary<string, string> { ["Tags"] = hash };
	}

	private static Dictionary<string, string> HashFilterEffects(GameMakerData data)
	{
		if (data.FilterEffects == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.FilterEffects, delegate (GameMakerFilterEffect effect)
		{
			if (effect?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", effect.Name.Content);
					w.WriteString("value", effect.Value?.Content ?? "");
					w.WriteEndObject();
				});
				result[effect.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static (string Type, string Name) DecodeAssetTagId(GameMakerData data, int assetId)
	{
		var type = (ResourceType)(assetId >> 24);
		var rawIndex = assetId & 0xFFFFFF;
		var index = (type == ResourceType.Script) ? (rawIndex - 100000) : rawIndex;
		return (Type: type.ToString(), Name: ResourceAssetUtil.GetAssetNameByTagType(data, type, index) ?? "");
	}

	private static Dictionary<string, string> HashTexturePageItems(GameMakerData data)
	{
		if (data.TexturePageItems == null)
		{
			return [];
		}
		Dictionary<GameMakerEmbeddedTexture, int> embeddedTextureIndices = [];
		if (data.EmbeddedTextures != null)
		{
			for (var i = 0; i < data.EmbeddedTextures.Count; i++)
			{
				embeddedTextureIndices[data.EmbeddedTextures[i]] = i;
			}
		}
		var hash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartObject();
			w.WriteStartArray("items");
			for (var j = 0; j < data.TexturePageItems.Count; j++)
			{
				var gameMakerTexturePageItem = data.TexturePageItems[j];
				if (gameMakerTexturePageItem == null)
				{
					w.WriteNullValue();
				}
				else
				{
					w.WriteStartObject();
					w.WriteNumber("index", j);
					w.WriteString("name", gameMakerTexturePageItem.Name?.Content ?? "");
					w.WriteNumber("texturePageIndex", (gameMakerTexturePageItem.TexturePage != null && embeddedTextureIndices.TryGetValue(gameMakerTexturePageItem.TexturePage, out var value)) ? value : (-1));
					w.WriteNumber("sourceX", gameMakerTexturePageItem.SourceX);
					w.WriteNumber("sourceY", gameMakerTexturePageItem.SourceY);
					w.WriteNumber("sourceWidth", gameMakerTexturePageItem.SourceWidth);
					w.WriteNumber("sourceHeight", gameMakerTexturePageItem.SourceHeight);
					w.WriteNumber("targetX", gameMakerTexturePageItem.TargetX);
					w.WriteNumber("targetY", gameMakerTexturePageItem.TargetY);
					w.WriteNumber("targetWidth", gameMakerTexturePageItem.TargetWidth);
					w.WriteNumber("targetHeight", gameMakerTexturePageItem.TargetHeight);
					w.WriteNumber("boundingWidth", gameMakerTexturePageItem.BoundingWidth);
					w.WriteNumber("boundingHeight", gameMakerTexturePageItem.BoundingHeight);
					w.WriteEndObject();
				}
			}
			w.WriteEndArray();
			w.WriteEndObject();
		});
		return new Dictionary<string, string> { ["TexturePageItems"] = hash };
	}

	private static Dictionary<string, string> HashSprites(GameMakerData data)
	{
		if (data.Sprites == null)
		{
			return [];
		}
		Dictionary<GameMakerEmbeddedTexture, int> pageIndexMap = [];
		if (data.EmbeddedTextures != null)
		{
			for (var i = 0; i < data.EmbeddedTextures.Count; i++)
			{
				pageIndexMap[data.EmbeddedTextures[i]] = i;
			}
		}
		Dictionary<int, HashSet<(ushort, ushort, ushort, ushort)>> regionsPerPage = [];
		foreach (var sprite in data.Sprites)
		{
			if (sprite?.Textures == null)
			{
				continue;
			}
			foreach (var texture in sprite.Textures)
			{
				var tpi = texture?.Texture;
				if (tpi?.TexturePage != null && pageIndexMap.TryGetValue(tpi.TexturePage, out var pageIdx))
				{
					if (!regionsPerPage.TryGetValue(pageIdx, out var regions))
					{
						regions = regionsPerPage[pageIdx] = [];
					}
					regions.Add((tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight));
				}
			}
		}
		var frameHashes = new ConcurrentDictionary<(int, ushort, ushort, ushort, ushort), string>();
		CancellableParallel.ForEach(regionsPerPage, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Min(16, Environment.ProcessorCount)
		}, delegate (KeyValuePair<int, HashSet<(ushort srcX, ushort srcY, ushort srcW, ushort srcH)>> kvp)
		{
			KeyValuePair<int, HashSet<(ushort, ushort, ushort, ushort)>> keyValuePair = kvp;
			keyValuePair.Deconstruct(out var key, out var value);
			var num = key;
			var hashSet2 = value;
			var gameMakerEmbeddedTexture = data.EmbeddedTextures[num];
			if (gameMakerEmbeddedTexture?.TextureData?.Image == null)
			{
				foreach (var (item, item2, item3, item4) in hashSet2)
				{
					frameHashes[(num, item, item2, item3, item4)] = "";
				}
				return;
			}
			var (width, height, array) = GetRgbaPixels(gameMakerEmbeddedTexture.TextureData.Image);
			foreach (var (num2, num3, num4, num5) in hashSet2)
			{
				TryHashTextureRegionPixels(array, num, width, height, num2, num3, num4, num5, out var hash);
				frameHashes[(num, num2, num3, num4, num5)] = hash;
			}
		});
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Sprites, delegate (GameMakerSprite gameMakerSprite)
		{
			if (gameMakerSprite?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", gameMakerSprite.Name.Content);
					w.WriteNumber("width", gameMakerSprite.Width);
					w.WriteNumber("height", gameMakerSprite.Height);
					w.WriteBoolean("transparent", gameMakerSprite.Transparent);
					w.WriteBoolean("smooth", gameMakerSprite.Smooth);
					w.WriteBoolean("preload", gameMakerSprite.Preload);
					w.WriteNumber("marginLeft", gameMakerSprite.MarginLeft);
					w.WriteNumber("marginRight", gameMakerSprite.MarginRight);
					w.WriteNumber("marginTop", gameMakerSprite.MarginTop);
					w.WriteNumber("marginBottom", gameMakerSprite.MarginBottom);
					w.WriteNumber("originX", gameMakerSprite.OriginX);
					w.WriteNumber("originY", gameMakerSprite.OriginY);
					w.WriteNumber("sepMasks", (int)gameMakerSprite.SepMasks);
					w.WriteNumber("bboxMode", gameMakerSprite.BBoxMode);
					w.WriteNumber("specialOrGMS2PlaybackSpeed", gameMakerSprite.GMS2PlaybackSpeed);
					w.WriteNumber("specialOrGMS2PlaybackSpeedType", (int)gameMakerSprite.GMS2PlaybackSpeedType);
					w.WriteBoolean("isSpecialType", gameMakerSprite.IsSpecialType);
					w.WriteNumber("spineVersion", gameMakerSprite.SpineVersion);
					if (gameMakerSprite.SpineJSON != null)
					{
						w.WriteString("spineJSON", gameMakerSprite.SpineJSON);
					}
					if (gameMakerSprite.SpineAtlas != null)
					{
						w.WriteString("spineAtlas", gameMakerSprite.SpineAtlas);
					}
					w.WriteNumber("sWFVersion", gameMakerSprite.SWFVersion);
					if (gameMakerSprite.V3NineSlice != null)
					{
						w.WriteStartObject("nineSlice");
						w.WriteNumber("left", gameMakerSprite.V3NineSlice.Left);
						w.WriteNumber("top", gameMakerSprite.V3NineSlice.Top);
						w.WriteNumber("right", gameMakerSprite.V3NineSlice.Right);
						w.WriteNumber("bottom", gameMakerSprite.V3NineSlice.Bottom);
						w.WriteBoolean("enabled", gameMakerSprite.V3NineSlice.Enabled);
						w.WriteStartArray("tileModes");
						var tileModes = gameMakerSprite.V3NineSlice.TileModes;
						foreach (var value2 in tileModes)
						{
							w.WriteNumberValue((int)value2);
						}
						w.WriteEndArray();
						w.WriteEndObject();
					}
					else
					{
						w.WriteNull("nineSlice");
					}
					w.WriteStartArray("textures");
					if (gameMakerSprite.Textures != null)
					{
						foreach (var texture2 in gameMakerSprite.Textures)
						{
							var gameMakerTexturePageItem = texture2?.Texture;
							if (gameMakerTexturePageItem != null)
							{
								w.WriteStartObject();
								w.WriteNumber("srcX", gameMakerTexturePageItem.SourceX);
								w.WriteNumber("srcY", gameMakerTexturePageItem.SourceY);
								w.WriteNumber("srcW", gameMakerTexturePageItem.SourceWidth);
								w.WriteNumber("srcH", gameMakerTexturePageItem.SourceHeight);
								w.WriteNumber("tgtX", gameMakerTexturePageItem.TargetX);
								w.WriteNumber("tgtY", gameMakerTexturePageItem.TargetY);
								w.WriteNumber("tgtW", gameMakerTexturePageItem.TargetWidth);
								w.WriteNumber("tgtH", gameMakerTexturePageItem.TargetHeight);
								w.WriteNumber("bndX", gameMakerTexturePageItem.BoundingWidth);
								w.WriteNumber("bndY", gameMakerTexturePageItem.BoundingHeight);
								var num = (gameMakerTexturePageItem.TexturePage != null && pageIndexMap.TryGetValue(gameMakerTexturePageItem.TexturePage, out var value3)) ? value3 : (-1);
								w.WriteNumber("texPage", num);
								var value4 = "";
								if (num >= 0)
								{
									frameHashes.TryGetValue((num, gameMakerTexturePageItem.SourceX, gameMakerTexturePageItem.SourceY, gameMakerTexturePageItem.SourceWidth, gameMakerTexturePageItem.SourceHeight), out value4);
								}
								w.WriteString("frameHash", value4 ?? "");
								w.WriteEndObject();
							}
							else
							{
								w.WriteNullValue();
							}
						}
					}
					w.WriteEndArray();
					if (gameMakerSprite.CollisionMasks != null)
					{
						w.WriteNumber("maskCount", gameMakerSprite.CollisionMasks.Count);
						w.WriteStartArray("masks");
						foreach (var current in gameMakerSprite.CollisionMasks)
						{
							if (current == null)
							{
								w.WriteNullValue();
							}
							else
							{
								w.WriteStartObject();
								w.WriteNumber("width", current.Width);
								w.WriteNumber("height", current.Height);
								var data2 = current.Data;
								w.WriteNumber("length", (data2 != null) ? data2.Length : 0);
								w.WriteString("dataHash", (current.Data != null) ? HexHash(current.Data) : "");
								w.WriteEndObject();
							}
						}
						w.WriteEndArray();
					}
					w.WriteEndObject();
				});
				result[gameMakerSprite.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static (int Width, int Height, byte[] Pixels) GetRgbaPixels(GMImage image)
	{
		var rawImage = image.ConvertToRawBgra();
		// Decoded images belong to this call; raw input pixels still belong to the caller.
		var rgba = ReferenceEquals(rawImage, image) ? [.. rawImage.GetData()] : rawImage.GetData();
		ReadOnlySpan<byte> bgra = rgba;
		var offset = 0;
		if (Vector.IsHardwareAccelerated)
		{
			var bgraPixels = MemoryMarshal.Cast<byte, uint>(bgra);
			var rgbaPixels = MemoryMarshal.Cast<byte, uint>(rgba.AsSpan());
			var vectorLength = Vector<uint>.Count;
			var vectorEnd = bgraPixels.Length - bgraPixels.Length % vectorLength;
			var preserve = new Vector<uint>(0xFF00FF00);
			var blue = new Vector<uint>(0x000000FF);
			var red = new Vector<uint>(0x00FF0000);
			for (var index = 0; index < vectorEnd; index += vectorLength)
			{
				var pixels = new Vector<uint>(bgraPixels.Slice(index, vectorLength));
				((pixels & preserve) | ((pixels & blue) << 16) | ((pixels & red) >> 16)).CopyTo(rgbaPixels.Slice(index, vectorLength));
			}
			offset = vectorEnd * 4;
		}
		for (; offset < bgra.Length; offset += 4)
		{
			(rgba[offset], rgba[offset + 2]) = (rgba[offset + 2], rgba[offset]);
		}
		return (rawImage.Width, rawImage.Height, rgba);
	}

	private static string HashTextureRegion(GameMakerTexturePageItem? tpi, ConcurrentDictionary<GameMakerEmbeddedTexture, Lazy<(int Width, int Height, byte[]? Pixels)>> pixelsByPage)
	{
		if (tpi?.TexturePage == null)
		{
			return "";
		}
		var pagePixels = pixelsByPage.GetOrAdd(tpi.TexturePage, texturePage => new Lazy<(int Width, int Height, byte[]? Pixels)>(delegate
		{
			if (texturePage.TextureData?.Image == null)
			{
				return default((int, int, byte[]));
			}
			return GetRgbaPixels(texturePage.TextureData.Image);
		})).Value;
		if (pagePixels.Pixels == null)
		{
			return "";
		}
		var (pageW, pageH, _) = pagePixels;
		if (tpi.SourceWidth == 0 || tpi.SourceHeight == 0 || tpi.SourceX >= pageW || tpi.SourceY >= pageH || tpi.SourceX + tpi.SourceWidth > pageW || tpi.SourceY + tpi.SourceHeight > pageH)
		{
			return $"invalid:{tpi.SourceX}:{tpi.SourceY}:{tpi.SourceWidth}:{tpi.SourceHeight}:{pageW}:{pageH}";
		}
		var allPixels = pagePixels.Pixels;
		if (allPixels == null)
		{
			return "";
		}
		TryHashTextureRegionPixels(allPixels, -1, pageW, pageH, tpi.SourceX, tpi.SourceY, tpi.SourceWidth, tpi.SourceHeight, out var regionHash);
		return regionHash;
	}

	private static void TryHashTextureRegionPixels(byte[] allPixels, int pageIdx, int pageW, int pageH, int srcX, int srcY, int srcW, int srcH, out string hash)
	{
		var invalidPrefix = (pageIdx >= 0) ? $"invalid:{pageIdx}" : "invalid";
		if (srcW <= 0 || srcH <= 0 || srcX < 0 || srcY < 0 || srcX >= pageW || srcY >= pageH || srcX > pageW - srcW || srcY > pageH - srcH)
		{
			hash = $"{invalidPrefix}:{srcX}:{srcY}:{srcW}:{srcH}:{pageW}:{pageH}:rect";
			return;
		}
		var expectedPixelsLength = pageW * (long)pageH * 4;
		if (allPixels.LongLength < expectedPixelsLength)
		{
			hash = $"{invalidPrefix}:{srcX}:{srcY}:{srcW}:{srcH}:{pageW}:{pageH}:buffer:{allPixels.LongLength}:{expectedPixelsLength}";
			return;
		}
		var regionSizeLong = srcW * (long)srcH * 4;
		if (regionSizeLong > int.MaxValue)
		{
			hash = $"{invalidPrefix}:{srcX}:{srcY}:{srcW}:{srcH}:{pageW}:{pageH}:region-too-large";
			return;
		}
		var rowBytesLong = srcW * 4L;
		if (rowBytesLong > int.MaxValue)
		{
			hash = $"{invalidPrefix}:{srcX}:{srcY}:{srcW}:{srcH}:{pageW}:{pageH}:row-too-large";
			return;
		}
		var rowBytes = (int)rowBytesLong;
		var regionSize = (int)regionSizeLong;
		var buffer = ArrayPool<byte>.Shared.Rent(regionSize);
		try
		{
			var dst = 0;
			for (var y = srcY; y < srcY + srcH; y++)
			{
				var sourceOffsetLong = (y * (long)pageW + srcX) * 4;
				if (sourceOffsetLong < 0 || sourceOffsetLong > allPixels.LongLength - rowBytes)
				{
					hash = $"{invalidPrefix}:{srcX}:{srcY}:{srcW}:{srcH}:{pageW}:{pageH}:offset:{sourceOffsetLong}:{allPixels.LongLength}:{rowBytes}";
					return;
				}
				Buffer.BlockCopy(allPixels, (int)sourceOffsetLong, buffer, dst, rowBytes);
				dst += rowBytes;
			}
			hash = HexHash(buffer.AsSpan(0, regionSize));
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	private static Dictionary<string, string> HashBackgrounds(GameMakerData data)
	{
		if (data.Backgrounds == null)
		{
			return [];
		}
		Dictionary<GameMakerEmbeddedTexture, int> pageIndexMap = [];
		if (data.EmbeddedTextures != null)
		{
			for (var i = 0; i < data.EmbeddedTextures.Count; i++)
			{
				pageIndexMap[data.EmbeddedTextures[i]] = i;
			}
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Backgrounds, delegate (GameMakerBackground bg)
		{
			if (bg?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", bg.Name.Content);
					w.WriteBoolean("transparent", bg.Transparent);
					w.WriteBoolean("smooth", bg.Smooth);
					w.WriteBoolean("preload", bg.Preload);
					w.WriteNumber("gmsWidth", bg.GMS2OutputBorderX);
					w.WriteNumber("gmsHeight", bg.GMS2OutputBorderY);
					w.WriteNumber("tileWidth", bg.GMS2TileWidth);
					w.WriteNumber("tileHeight", bg.GMS2TileHeight);
					w.WriteNumber("tileColumns", bg.GMS2TileColumns);
					w.WriteNumber("tileCount", bg.GMS2TileCount);
					w.WriteNumber("frameLength", bg.GMS2FrameLength);
					var texture = bg.Texture;
					if (texture != null)
					{
						w.WriteNumber("texSrcX", texture.SourceX);
						w.WriteNumber("texSrcY", texture.SourceY);
						w.WriteNumber("texSrcW", texture.SourceWidth);
						w.WriteNumber("texSrcH", texture.SourceHeight);
						w.WriteNumber("texPage", (texture.TexturePage != null && pageIndexMap.TryGetValue(texture.TexturePage, out var value2)) ? value2 : (-1));
					}
					w.WriteEndObject();
				});
				result[bg.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashFonts(GameMakerData data)
	{
		if (data.Fonts == null)
		{
			return [];
		}
		ConcurrentDictionary<GameMakerEmbeddedTexture, Lazy<(int Width, int Height, byte[]? Pixels)>> pixelsByPage = new ConcurrentDictionary<GameMakerEmbeddedTexture, Lazy<(int, int, byte[])>>(ReferenceEqualityComparer.Instance);
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Fonts, delegate (GameMakerFont font)
		{
			if (font?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", font.Name.Content);
					w.WriteString("displayName", font.DisplayName?.Content ?? "");
					w.WriteBoolean("emSizeIsFloat", font.EmSizeIsFloat);
					w.WriteNumber("emSize", font.EmSize);
					w.WriteBoolean("bold", font.Bold);
					w.WriteBoolean("italic", font.Italic);
					w.WriteNumber("charset", font.Charset);
					w.WriteNumber("antiAliasing", font.AntiAliasing);
					w.WriteNumber("rangeStart", font.RangeStart);
					w.WriteNumber("rangeEnd", font.RangeEnd);
					w.WriteNumber("scaleX", font.ScaleX);
					w.WriteNumber("scaleY", font.ScaleY);
					w.WriteNumber("ascenderOffset", font.AscenderOffset);
					w.WriteNumber("ascender", font.Ascender);
					w.WriteNumber("sdfSpread", font.SDFSpread);
					w.WriteNumber("lineHeight", font.LineHeight);
					w.WriteNumber("glyphCount", font.Glyphs?.Count ?? 0);
					var texture = font.Texture;
					if (texture != null)
					{
						w.WriteNumber("texSrcX", texture.SourceX);
						w.WriteNumber("texSrcY", texture.SourceY);
						w.WriteNumber("texSrcW", texture.SourceWidth);
						w.WriteNumber("texSrcH", texture.SourceHeight);
						w.WriteString("textureHash", HashTextureRegion(texture, pixelsByPage));
					}
					w.WriteStartArray("glyphs");
					if (font.Glyphs != null)
					{
						foreach (var current in font.Glyphs.OrderBy(g => g.Character))
						{
							if (current == null)
							{
								w.WriteNullValue();
							}
							else
							{
								w.WriteStartObject();
								w.WriteNumber("character", current.Character);
								w.WriteNumber("sourceX", current.SourceX);
								w.WriteNumber("sourceY", current.SourceY);
								w.WriteNumber("sourceWidth", current.SourceWidth);
								w.WriteNumber("sourceHeight", current.SourceHeight);
								w.WriteNumber("shift", current.Shift);
								w.WriteNumber("offset", current.Offset);
								w.WriteNumber("unknownAlwaysZero", current.UnknownAlwaysZero);
								w.WriteStartArray("kerning");
								if (current.Kerning != null)
								{
									foreach (var current2 in current.Kerning.OrderBy(k => k.Character))
									{
										w.WriteStartObject();
										w.WriteNumber("character", current2.Character);
										w.WriteNumber("shiftModifier", current2.ShiftModifier);
										w.WriteEndObject();
									}
								}
								w.WriteEndArray();
								w.WriteEndObject();
							}
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
				result[font.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashSounds(GameMakerData data, string? dataFilePath = null)
	{
		if (data.Sounds == null)
		{
			return [];
		}
		var externalAudioHashes = LoadExternalAudioHashes(data, dataFilePath);
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Sounds, delegate (GameMakerSound snd)
		{
			if (snd?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", snd.Name.Content);
					w.WriteNumber("flags", (uint)snd.Flags);
					w.WriteString("type", snd.Type?.Content ?? "");
					w.WriteString("file", snd.File?.Content ?? "");
					w.WriteNumber("effects", snd.Effects);
					w.WriteNumber("volume", snd.Volume);
					w.WriteBoolean("preload", snd.Preload);
					w.WriteNumber("pitch", snd.Pitch);
					w.WriteString("audioGroup", snd.AudioGroup?.Name?.Content ?? "");
					w.WriteNumber("audioID", snd.AudioID);
					w.WriteNumber("groupID", snd.GroupID);
					w.WriteNumber("audioLength", snd.AudioLength);
					w.WriteString("audioHash", GetSoundAudioHash(snd, externalAudioHashes));
					w.WriteEndObject();
				});
				result[snd.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static string GetSoundAudioHash(GameMakerSound sound, Dictionary<int, Dictionary<int, string>> externalAudioHashes)
	{
		if (sound.AudioFile?.Data != null)
		{
			return HexHash(sound.AudioFile.Data);
		}
		if (sound.GroupID > 0 && sound.AudioID >= 0 && externalAudioHashes.TryGetValue(sound.GroupID, out var groupHashes) && groupHashes.TryGetValue(sound.AudioID, out var hash))
		{
			return hash;
		}
		return "";
	}

	private static Dictionary<int, Dictionary<int, string>> LoadExternalAudioHashes(GameMakerData data, string? dataFilePath)
	{
		if (data.Sounds == null || string.IsNullOrWhiteSpace(dataFilePath))
		{
			return [];
		}
		var dataDir = Path.GetDirectoryName(dataFilePath);
		if (string.IsNullOrWhiteSpace(dataDir))
		{
			return [];
		}
		int[] array = [.. (from sound in data.Sounds
					   where sound != null && sound.GroupID > 0 && sound.AudioID >= 0
					   select sound.GroupID).Distinct()];
		Dictionary<int, Dictionary<int, string>> result = [];
		var array2 = array;
		foreach (var groupId in array2)
		{
			var audioGroupPath = ResourceAssetUtil.ResolveAudioGroupPath(data, dataDir, groupId);
			if (audioGroupPath == null || !File.Exists(audioGroupPath))
			{
				continue;
			}
			try
			{
				using var stream = new FileStream(audioGroupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var audioGroupData = GameMakerIO.Read(stream);
				if (audioGroupData.EmbeddedAudio == null)
				{
					continue;
				}
				Dictionary<int, string> hashes = [];
				for (var i = 0; i < audioGroupData.EmbeddedAudio.Count; i++)
				{
					var audio = audioGroupData.EmbeddedAudio[i];
					if (audio?.Data != null)
					{
						hashes[i] = HexHash(audio.Data);
					}
				}
				if (hashes.Count > 0)
				{
					result[groupId] = hashes;
				}
			}
			catch
			{
			}
		}
		return result;
	}

	private static Dictionary<string, string> HashOptions(GameMakerData data)
	{
		if (data.Options == null)
		{
			return [];
		}
		return new Dictionary<string, string>
		{
			["Options"] = JsonHash(delegate (Utf8JsonWriter w)
		{
			WriteOptionsJson(w, data);
		})
		};
	}

	private static Dictionary<string, string> HashGlobalScripts(GameMakerData data)
	{
		return new Dictionary<string, string>
		{
			["GlobalScripts"] = JsonHash(delegate (Utf8JsonWriter w)
		{
			WriteGlobalScriptsJson(w, data);
		})
		};
	}

	private static Dictionary<string, string> HashScripts(GameMakerData data)
	{
		if (data.Scripts == null)
		{
			return [];
		}
		var source = BuildUniqueResourceKeys([.. from script in data.Scripts
																				   where script?.Name?.Content != null
																				   select script], script => script.Name.Content);
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach<(GameMakerScript, string)>(source, delegate ((GameMakerScript Item, string Key) entry)
		{
			var (script, _) = entry;
			result[entry.Key] = JsonHash(delegate (Utf8JsonWriter w)
			{
				w.WriteStartObject();
				w.WriteString("name", script.Name.Content);
				w.WriteString("code", script.Code?.Name?.Content ?? "");
				w.WriteBoolean("isConstructor", script.IsConstructor);
				w.WriteEndObject();
			});
		});
		return new Dictionary<string, string>(result);
	}

	private static List<(T Item, string Key)> BuildUniqueResourceKeys<T>(List<T> items, Func<T, string> getName)
	{
		var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var result = new List<(T, string)>(items.Count);
		foreach (var item in items)
		{
			var baseName = getName(item);
			if (!used.TryGetValue(baseName, out var count))
			{
				used[baseName] = 1;
				result.Add((item, baseName));
				continue;
			}
			string uniqueName;
			do
			{
				count++;
				uniqueName = $"{baseName}__{count}";
			}
			while (used.ContainsKey(uniqueName));
			used[baseName] = count;
			used[uniqueName] = 1;
			result.Add((item, uniqueName));
		}
		return result;
	}

	private static Dictionary<string, string> HashEmbeddedImages(GameMakerData data)
	{
		if (data.EmbeddedImages == null)
		{
			return [];
		}
		var texturePageItemIndices = new Dictionary<GameMakerTexturePageItem, int>(ReferenceEqualityComparer.Instance);
		if (data.TexturePageItems != null)
		{
			for (var i = 0; i < data.TexturePageItems.Count; i++)
			{
				var texturePageItem = data.TexturePageItems[i];
				if (texturePageItem != null)
				{
					texturePageItemIndices.TryAdd(texturePageItem, i);
				}
			}
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.EmbeddedImages, delegate (GameMakerEmbeddedImage image)
		{
			if (image?.Name?.Content != null)
			{
				result[image.Name.Content] = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", image.Name.Content);
					WriteTexturePageItemRef(w, texturePageItemIndices, image.TextureEntry);
					w.WriteEndObject();
				});
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static void WriteTexturePageItemRef(Utf8JsonWriter w, Dictionary<GameMakerTexturePageItem, int> texturePageItemIndices, GameMakerTexturePageItem? tpi)
	{
		w.WriteNumber("texturePageItemIndex", (tpi != null && texturePageItemIndices.TryGetValue(tpi, out var index)) ? index : (-1));
		if (tpi != null)
		{
			w.WriteString("texturePageItemName", tpi.Name?.Content ?? "");
			w.WriteNumber("sourceX", tpi.SourceX);
			w.WriteNumber("sourceY", tpi.SourceY);
			w.WriteNumber("sourceWidth", tpi.SourceWidth);
			w.WriteNumber("sourceHeight", tpi.SourceHeight);
			w.WriteNumber("targetX", tpi.TargetX);
			w.WriteNumber("targetY", tpi.TargetY);
			w.WriteNumber("targetWidth", tpi.TargetWidth);
			w.WriteNumber("targetHeight", tpi.TargetHeight);
			w.WriteNumber("boundingWidth", tpi.BoundingWidth);
			w.WriteNumber("boundingHeight", tpi.BoundingHeight);
		}
	}

	private static void WriteOptionsJson(Utf8JsonWriter w, GameMakerData data)
	{
		var options = data.Options;
		w.WriteStartObject();
		if (options == null)
		{
			w.WriteEndObject();
			return;
		}
		w.WriteBoolean("newFormat", options.NewFormat);
		w.WriteNumber("shaderExtensionFlag", options.ShaderExtensionFlag);
		w.WriteNumber("shaderExtensionVersion", options.ShaderExtensionVersion);
		w.WriteNumber("info", (ulong)options.Info);
		w.WriteNumber("scale", options.Scale);
		w.WriteNumber("windowColor", options.WindowColor);
		w.WriteNumber("colorDepth", options.ColorDepth);
		w.WriteNumber("resolution", options.Resolution);
		w.WriteNumber("frequency", options.Frequency);
		w.WriteNumber("vertexSync", options.VertexSync);
		w.WriteNumber("priority", options.Priority);
		w.WriteNumber("loadAlpha", options.LoadAlpha);
		w.WriteStartArray("constants");
		if (options.Constants != null)
		{
			foreach (var constant in options.Constants)
			{
				w.WriteStartObject();
				w.WriteString("name", constant?.Name?.Content ?? "");
				w.WriteString("value", constant?.Value?.Content ?? "");
				w.WriteEndObject();
			}
		}
		w.WriteEndArray();
		w.WriteEndObject();
	}

	private static void WriteGlobalScriptsJson(Utf8JsonWriter w, GameMakerData data)
	{
		w.WriteStartObject();
		WriteCodeRefArray(w, "globalInitScripts", data.GlobalInitScripts);
		WriteCodeRefArray(w, "gameEndScripts", data.GameEndScripts);
		w.WriteEndObject();
	}

	private static void WriteCodeRefArray(Utf8JsonWriter w, string propertyName, IList<GameMakerGlobalInit>? scripts)
	{
		w.WriteStartArray(propertyName);
		if (scripts != null)
		{
			foreach (var script2 in scripts)
			{
				w.WriteStringValue(script2?.Code?.Name?.Content ?? "");
			}
		}
		w.WriteEndArray();
	}

	private static Dictionary<string, string> HashAnimationCurves(GameMakerData data)
	{
		if (data.AnimationCurves == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.AnimationCurves, delegate (GameMakerAnimationCurve curve)
		{
			if (curve?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					WriteAnimationCurveJson(w, data, curve);
					w.WriteEndObject();
				});
				result[curve.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashSequences(GameMakerData data)
	{
		if (data.Sequences == null)
		{
			return [];
		}
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		var tempRoot = Path.Combine(TempRoot.Path, "g3mlib_seq_hash", Guid.NewGuid().ToString("N"));
		try
		{
			var context = ResourceAssetUtil.CreateProjectContext(data, tempRoot);
			foreach (var sequence in data.Sequences)
			{
				if (sequence?.Name?.Content != null)
				{
					var serializable = sequence.GenerateSerializableProjectAsset(context);
					result[sequence.Name.Content] = CanonicalJsonHash(serializable);
				}
			}
		}
		finally
		{
			try
			{
				Directory.Delete(tempRoot, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				LogService.Warning("Could not delete temporary sequence hash directory '" + tempRoot + "': " + ex.Message);
			}
		}
		return result;
	}

	private static Dictionary<string, string> HashParticleSystems(GameMakerData data)
	{
		if (data.ParticleSystems == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.ParticleSystems, delegate (GameMakerParticleSystem ps)
		{
			if (ps?.Name?.Content != null)
			{
				result[ps.Name.Content] = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", ps.Name.Content);
					w.WriteNumber("originX", ps.OriginX);
					w.WriteNumber("originY", ps.OriginY);
					w.WriteNumber("drawOrder", (int)ps.DrawOrder);
					w.WriteBoolean("globalSpaceParticles", ps.GlobalSpaceParticles);
					w.WriteStartArray("emitters");
					foreach (var item in ps.Emitters ?? [])
					{
						w.WriteStringValue(item?.Resource?.Name?.Content ?? "");
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashParticleSystemEmitters(GameMakerData data)
	{
		if (data.ParticleSystemEmitters == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.ParticleSystemEmitters, delegate (GameMakerParticleSystemEmitter e)
		{
			if (e?.Name?.Content != null)
			{
				result[e.Name.Content] = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", e.Name.Content);
					w.WriteString("sprite", e.Sprite?.Name?.Content ?? "");
					w.WriteString("spawnOnDeath", e.SpawnOnDeath?.Name?.Content ?? "");
					w.WriteString("spawnOnUpdate", e.SpawnOnUpdate?.Name?.Content ?? "");
					foreach (var current in typeof(GameMakerParticleSystemEmitter).GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal))
					{
						if (current.CanRead && current.CanWrite)
						{
							var flag = current.Name switch
							{
								"SizeIncrease" or "SpawnOnDeath" or "SizeMax" or "SizeMin" or "Name" or "Sprite" or "SpawnOnUpdate" or "SizeWiggle" => true,
								_ => false,
							};
							if (!flag)
							{
								var type = Nullable.GetUnderlyingType(current.PropertyType) ?? current.PropertyType;
								if (type.IsEnum)
								{
									w.WriteNumber(current.Name, Convert.ToInt32(current.GetValue(e), CultureInfo.InvariantCulture));
								}
								else if (type == typeof(bool))
								{
									w.WriteBoolean(current.Name, (bool)(current.GetValue(e) ?? false));
								}
								else if (type == typeof(int))
								{
									w.WriteNumber(current.Name, (int)(current.GetValue(e) ?? 0));
								}
								else if (type == typeof(uint))
								{
									w.WriteNumber(current.Name, (uint)(current.GetValue(e) ?? 0u));
								}
								else if (type == typeof(float))
								{
									w.WriteNumber(current.Name, (float)(current.GetValue(e) ?? 0f));
								}
							}
						}
					}
					w.WriteEndObject();
				});
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static void WriteAnimationCurveJson(Utf8JsonWriter w, GameMakerData data, GameMakerAnimationCurve curve)
	{
		w.WriteString("name", curve.Name?.Content ?? "");
		w.WriteNumber("graphType", (uint)curve.GraphType);
		w.WriteStartArray("channels");
		if (curve.Channels != null)
		{
			foreach (var channel in curve.Channels)
			{
				if (channel == null)
				{
					continue;
				}
				w.WriteStartObject();
				w.WriteString("name", channel.Name?.Content ?? "");
				w.WriteNumber("curve", (uint)channel.Curve);
				w.WriteNumber("iterations", channel.Iterations);
				w.WriteStartArray("points");
				if (channel.Points != null)
				{
					foreach (var point in channel.Points)
					{
						if (point != null)
						{
							w.WriteStartObject();
							w.WriteNumber("x", point.X);
							w.WriteNumber("value", point.Value);
							if (data.IsVersionAtLeast(2u, 3u, 1u))
							{
								w.WriteNumber("bezierX0", point.BezierX0);
								w.WriteNumber("bezierY0", point.BezierY0);
								w.WriteNumber("bezierX1", point.BezierX1);
								w.WriteNumber("bezierY1", point.BezierY1);
							}
							w.WriteEndObject();
						}
					}
				}
				w.WriteEndArray();
				w.WriteEndObject();
			}
		}
		w.WriteEndArray();
	}

	private static Dictionary<string, string> HashPaths(GameMakerData data)
	{
		if (data.Paths == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Paths, delegate (GameMakerPath path)
		{
			if (path?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", path.Name.Content);
					w.WriteBoolean("isSmooth", path.IsSmooth);
					w.WriteBoolean("isClosed", path.IsClosed);
					w.WriteNumber("precision", path.Precision);
					w.WriteNumber("pointCount", path.Points?.Count ?? 0);
					w.WriteStartArray("points");
					if (path.Points != null)
					{
						foreach (var current in path.Points)
						{
							w.WriteStartObject();
							w.WriteNumber("x", current.X);
							w.WriteNumber("y", current.Y);
							w.WriteNumber("speed", current.Speed);
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
				result[path.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashTilesets(GameMakerData data)
	{
		if (data.Backgrounds == null)
		{
			return [];
		}
		List<GameMakerBackground> tilesets = [.. data.Backgrounds.Where(bg => bg != null && bg.Name?.Content != null && bg.GMS2TileWidth != 0 && bg.GMS2TileHeight != 0)];
		if (tilesets.Count == 0)
		{
			return [];
		}
		Dictionary<GameMakerEmbeddedTexture, int> tsPageIndexMap = [];
		if (data.EmbeddedTextures != null)
		{
			for (var i = 0; i < data.EmbeddedTextures.Count; i++)
			{
				tsPageIndexMap[data.EmbeddedTextures[i]] = i;
			}
		}
		var combinedHash = JsonHash(delegate (Utf8JsonWriter w)
		{
			w.WriteStartArray();
			foreach (var current in tilesets.OrderBy(t => t.Name.Content))
			{
				w.WriteStartObject();
				w.WriteString("name", current.Name.Content);
				w.WriteNumber("tileWidth", current.GMS2TileWidth);
				w.WriteNumber("tileHeight", current.GMS2TileHeight);
				w.WriteNumber("tileColumns", current.GMS2TileColumns);
				w.WriteNumber("tileCount", current.GMS2TileCount);
				w.WriteNumber("outputBorderX", current.GMS2OutputBorderX);
				w.WriteNumber("outputBorderY", current.GMS2OutputBorderY);
				w.WriteNumber("frameLength", current.GMS2FrameLength);
				var texture = current.Texture;
				if (texture != null)
				{
					w.WriteNumber("texSrcX", texture.SourceX);
					w.WriteNumber("texSrcY", texture.SourceY);
					w.WriteNumber("texSrcW", texture.SourceWidth);
					w.WriteNumber("texSrcH", texture.SourceHeight);
					w.WriteNumber("texPage", (texture.TexturePage != null && tsPageIndexMap.TryGetValue(texture.TexturePage, out var value)) ? value : (-1));
				}
				w.WriteEndObject();
			}
			w.WriteEndArray();
		});
		return new Dictionary<string, string> { ["Tilesets"] = combinedHash };
	}

	private static Dictionary<string, string> HashShaders(GameMakerData data)
	{
		if (data.Shaders == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Shaders, delegate (GameMakerShader shader)
		{
			if (shader?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", shader.Name.Content);
					w.WriteString("type", shader.Type.ToString());
					w.WriteString("glslES_Vertex", shader.GLSL_ES_Vertex?.Content ?? "");
					w.WriteString("glslES_Fragment", shader.GLSL_ES_Fragment?.Content ?? "");
					w.WriteString("glsl_Vertex", shader.GLSL_Vertex?.Content ?? "");
					w.WriteString("glsl_Fragment", shader.GLSL_Fragment?.Content ?? "");
					w.WriteString("hlsl9_Vertex", shader.HLSL9_Vertex?.Content ?? "");
					w.WriteString("hlsl9_Fragment", shader.HLSL9_Fragment?.Content ?? "");
					w.WriteEndObject();
				});
				result[shader.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashTimelines(GameMakerData data)
	{
		if (data.Timelines == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Timelines, delegate (GameMakerTimeline tl)
		{
			if (tl?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", tl.Name.Content);
					w.WriteStartArray("moments");
					if (tl.Moments != null)
					{
						foreach (var current in tl.Moments)
						{
							w.WriteStartObject();
							w.WriteNumber("step", current.Step);
							w.WriteStartArray("actions");
							if (current.Event != null)
							{
								foreach (var item in current.Event)
								{
									w.WriteStringValue(item?.CodeId?.Name?.Content ?? "");
								}
							}
							w.WriteEndArray();
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
				result[tl.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashGameObjects(GameMakerData data)
	{
		if (data.GameObjects == null)
		{
			return [];
		}
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var group in (from item in data.GameObjects.Select((obj, index) => (obj, index))
							   where item.obj?.Name?.Content != null
							   select item).GroupBy<(GameMakerGameObject, int), string>(((GameMakerGameObject obj, int index) item) => item.obj.Name.Content, StringComparer.Ordinal))
		{
			var hash = JsonHash(delegate (Utf8JsonWriter w)
			{
				w.WriteStartArray();
				foreach (var (gameMakerGameObject, value) in group.OrderBy(((GameMakerGameObject obj, int index) item) => item.index))
				{
					w.WriteStartObject();
					w.WriteNumber("index", value);
					w.WriteString("name", gameMakerGameObject.Name.Content);
					w.WriteString("spriteName", gameMakerGameObject.Sprite?.Name?.Content ?? "");
					w.WriteBoolean("visible", gameMakerGameObject.Visible);
					w.WriteBoolean("managed", gameMakerGameObject.Managed);
					w.WriteBoolean("solid", gameMakerGameObject.Solid);
					w.WriteNumber("depth", gameMakerGameObject.Depth);
					w.WriteBoolean("persistent", gameMakerGameObject.Persistent);
					w.WriteString("parentName", gameMakerGameObject.ParentId?.Name?.Content ?? "");
					w.WriteString("maskSpriteName", gameMakerGameObject.TextureMaskId?.Name?.Content ?? "");
					w.WriteBoolean("usesPhysics", gameMakerGameObject.UsesPhysics);
					w.WriteBoolean("isSensor", gameMakerGameObject.IsSensor);
					w.WriteNumber("collisionShape", (int)gameMakerGameObject.CollisionShape);
					w.WriteNumber("density", gameMakerGameObject.Density);
					w.WriteNumber("restitution", gameMakerGameObject.Restitution);
					w.WriteNumber("group", gameMakerGameObject.Group);
					w.WriteNumber("linearDamping", gameMakerGameObject.LinearDamping);
					w.WriteNumber("angularDamping", gameMakerGameObject.AngularDamping);
					w.WriteNumber("friction", gameMakerGameObject.Friction);
					w.WriteBoolean("kinematic", gameMakerGameObject.Kinematic);
					w.WriteStartArray("events");
					for (var num = 0; num < gameMakerGameObject.Events.Count; num++)
					{
						foreach (var current in gameMakerGameObject.Events[num])
						{
							w.WriteStartObject();
							w.WriteNumber("type", num);
							w.WriteNumber("subtype", current.EventSubtype);
							w.WriteStartArray("actions");
							foreach (var current2 in current.Actions)
							{
								w.WriteStringValue(current2.CodeId?.Name?.Content ?? "");
							}
							w.WriteEndArray();
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				}
				w.WriteEndArray();
			});
			result[group.Key] = hash;
		}
		return result;
	}

	private static Dictionary<string, string> HashRooms(GameMakerData data)
	{
		if (data.Rooms == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Rooms, delegate (GameMakerRoom room)
		{
			if (room?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", room.Name.Content);
					w.WriteString("caption", room.Caption?.Content ?? "");
					w.WriteNumber("width", room.Width);
					w.WriteNumber("height", room.Height);
					w.WriteNumber("speed", room.Speed);
					w.WriteBoolean("persistent", room.Persistent);
					w.WriteNumber("backgroundColor", room.BackgroundColor);
					w.WriteBoolean("drawBackgroundColor", room.DrawBackgroundColor);
					w.WriteString("creationCode", room.CreationCodeId?.Name?.Content ?? "");
					w.WriteNumber("flags", (uint)room.Flags);
					w.WriteBoolean("world", room.World);
					w.WriteNumber("top", room.Top);
					w.WriteNumber("left", room.Left);
					w.WriteNumber("right", room.Right);
					w.WriteNumber("bottom", room.Bottom);
					w.WriteNumber("gravityX", room.GravityX);
					w.WriteNumber("gravityY", room.GravityY);
					w.WriteNumber("metersPerPixel", room.MetersPerPixel);
					w.WriteStartArray("backgrounds");
					if (room.Backgrounds != null)
					{
						foreach (var current in room.Backgrounds)
						{
							w.WriteStartObject();
							w.WriteBoolean("enabled", current.Enabled);
							w.WriteBoolean("foreground", current.Foreground);
							w.WriteString("bgDef", current.BackgroundDefinition?.Name?.Content ?? "");
							w.WriteNumber("x", current.X);
							w.WriteNumber("y", current.Y);
							w.WriteBoolean("tiledH", current.TiledHorizontally);
							w.WriteBoolean("tiledV", current.TiledVertically);
							w.WriteNumber("speedX", current.SpeedX);
							w.WriteNumber("speedY", current.SpeedY);
							w.WriteBoolean("stretch", current.Stretch);
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("views");
					if (room.Views != null)
					{
						foreach (var current2 in room.Views)
						{
							w.WriteStartObject();
							w.WriteBoolean("enabled", current2.Enabled);
							w.WriteNumber("viewX", current2.ViewX);
							w.WriteNumber("viewY", current2.ViewY);
							w.WriteNumber("viewW", current2.ViewWidth);
							w.WriteNumber("viewH", current2.ViewHeight);
							w.WriteNumber("portX", current2.PortX);
							w.WriteNumber("portY", current2.PortY);
							w.WriteNumber("portW", current2.PortWidth);
							w.WriteNumber("portH", current2.PortHeight);
							w.WriteNumber("borderX", current2.BorderX);
							w.WriteNumber("borderY", current2.BorderY);
							w.WriteNumber("speedX", current2.SpeedX);
							w.WriteNumber("speedY", current2.SpeedY);
							w.WriteString("objectId", current2.ObjectId?.Name?.Content ?? "");
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("instances");
					if (room.GameObjects != null)
					{
						foreach (var current3 in room.GameObjects)
						{
							w.WriteStartObject();
							w.WriteNumber("id", current3.InstanceID);
							w.WriteString("obj", current3.ObjectDefinition?.Name?.Content ?? "");
							w.WriteNumber("x", current3.X);
							w.WriteNumber("y", current3.Y);
							w.WriteString("cc", current3.CreationCode?.Name?.Content ?? "");
							w.WriteNumber("scX", current3.ScaleX);
							w.WriteNumber("scY", current3.ScaleY);
							w.WriteNumber("rot", current3.Rotation);
							w.WriteNumber("col", current3.Color);
							w.WriteNumber("imgSpd", current3.ImageSpeed);
							w.WriteNumber("imgIdx", current3.ImageIndex);
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("tiles");
					if (room.Tiles != null)
					{
						foreach (var current4 in room.Tiles)
						{
							w.WriteStartObject();
							w.WriteNumber("x", current4.X);
							w.WriteNumber("y", current4.Y);
							w.WriteBoolean("sm", current4.spriteMode);
							if (current4.spriteMode)
							{
								w.WriteString("spr", current4.SpriteDefinition?.Name?.Content ?? "");
							}
							else
							{
								w.WriteString("bg", current4.BackgroundDefinition?.Name?.Content ?? "");
							}
							w.WriteNumber("sx", current4.SourceX);
							w.WriteNumber("sy", current4.SourceY);
							w.WriteNumber("w", current4.Width);
							w.WriteNumber("h", current4.Height);
							w.WriteNumber("d", current4.TileDepth);
							w.WriteNumber("id", current4.InstanceID);
							w.WriteNumber("scX", current4.ScaleX);
							w.WriteNumber("scY", current4.ScaleY);
							w.WriteNumber("col", current4.Color);
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					if (room.Layers != null)
					{
						w.WriteStartArray("layers");
						foreach (var current5 in room.Layers)
						{
							w.WriteStartObject();
							w.WriteString("name", current5.LayerName?.Content ?? "");
							w.WriteNumber("id", current5.LayerId);
							w.WriteNumber("type", (int)current5.LayerType);
							w.WriteNumber("depth", current5.LayerDepth);
							w.WriteNumber("xOff", current5.XOffset);
							w.WriteNumber("yOff", current5.YOffset);
							w.WriteNumber("hSpd", current5.HSpeed);
							w.WriteNumber("vSpd", current5.VSpeed);
							w.WriteBoolean("vis", current5.IsVisible);
							if (current5.LayerType == GameMakerRoom.LayerType.Instances && current5.InstancesData?.Instances != null)
							{
								w.WriteStartArray("instIds");
								foreach (var current6 in current5.InstancesData.Instances)
								{
									w.WriteNumberValue((int)current6.InstanceID);
								}
								w.WriteEndArray();
							}
							else if (current5.LayerType == GameMakerRoom.LayerType.Tiles && current5.TilesData != null)
							{
								w.WriteString("tilesBg", current5.TilesData.Background?.Name?.Content ?? "");
								w.WriteNumber("tilesX", current5.TilesData.TilesX);
								w.WriteNumber("tilesY", current5.TilesData.TilesY);
								if (current5.TilesData.TileData != null)
								{
									w.WriteStartArray("td");
									var tileData = current5.TilesData.TileData;
									foreach (var array in tileData)
									{
										w.WriteStartArray();
										if (array != null)
										{
											var array2 = array;
											foreach (var value2 in array2)
											{
												w.WriteNumberValue(value2);
											}
										}
										w.WriteEndArray();
									}
									w.WriteEndArray();
								}
							}
							else if (current5.LayerType == GameMakerRoom.LayerType.Background && current5.BackgroundData != null)
							{
								var backgroundData = current5.BackgroundData;
								w.WriteBoolean("bgVis", backgroundData.Visible);
								w.WriteString("bgSpr", backgroundData.Sprite?.Name?.Content ?? "");
								w.WriteBoolean("bgTH", backgroundData.TiledHorizontally);
								w.WriteBoolean("bgTV", backgroundData.TiledVertically);
								w.WriteBoolean("bgStr", backgroundData.Stretch);
								w.WriteNumber("bgCol", backgroundData.Color);
								w.WriteNumber("bgASpd", backgroundData.AnimationSpeed);
							}
							else if (current5.LayerType == GameMakerRoom.LayerType.Assets && current5.AssetsData != null)
							{
								var assetsData = current5.AssetsData;
								w.WriteNumber("ltCount", assetsData.LegacyTiles?.Count ?? 0);
								w.WriteNumber("sprCount", assetsData.Sprites?.Count ?? 0);
								if (assetsData.LegacyTiles != null)
								{
									w.WriteStartArray("lt");
									foreach (var current7 in assetsData.LegacyTiles)
									{
										w.WriteStartObject();
										w.WriteNumber("x", current7.X);
										w.WriteNumber("y", current7.Y);
										w.WriteNumber("sx", current7.SourceX);
										w.WriteNumber("sy", current7.SourceY);
										w.WriteNumber("w", current7.Width);
										w.WriteNumber("h", current7.Height);
										w.WriteNumber("d", current7.TileDepth);
										w.WriteNumber("id", current7.InstanceID);
										w.WriteString("bg", current7.BackgroundDefinition?.Name?.Content ?? "");
										w.WriteEndObject();
									}
									w.WriteEndArray();
								}
								if (assetsData.Sprites != null)
								{
									w.WriteStartArray("spr");
									foreach (var current8 in assetsData.Sprites)
									{
										w.WriteStartObject();
										w.WriteString("n", current8.Name?.Content ?? "");
										w.WriteString("s", current8.Sprite?.Name?.Content ?? "");
										w.WriteNumber("x", current8.X);
										w.WriteNumber("y", current8.Y);
										w.WriteNumber("scX", current8.ScaleX);
										w.WriteNumber("scY", current8.ScaleY);
										w.WriteNumber("col", current8.Color);
										w.WriteNumber("rot", current8.Rotation);
										w.WriteEndObject();
									}
									w.WriteEndArray();
								}
							}
							w.WriteEndObject();
						}
						w.WriteEndArray();
					}
					w.WriteEndObject();
				});
				result[room.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}

	private static Dictionary<string, string> HashCodeEntries(GameMakerData data)
	{
		if (data.Code == null)
		{
			return [];
		}
		var topLevel = new List<GameMakerCode>(data.Code.Count);
		foreach (var c in data.Code)
		{
			if (c != null && c.Name?.Content != null && c.ParentEntry == null)
			{
				topLevel.Add(c);
			}
		}
		var result = new Dictionary<string, string>(topLevel.Count, StringComparer.Ordinal);
		var localsByName = data.CodeLocals?.Where(locals => locals?.Name?.Content != null).GroupBy(locals => locals.Name.Content, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		var perName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
		var entryHashes = new string[topLevel.Count];
		CancellableParallel.For(0, topLevel.Count, index => entryHashes[index] = HashCodeEntry(topLevel[index], data.Variables, localsByName));
		for (var index = 0; index < topLevel.Count; index++)
		{
			var entry = topLevel[index];
			if (!perName.TryGetValue(entry.Name.Content, out var hashes))
			{
				hashes = [];
				perName[entry.Name.Content] = hashes;
			}
			hashes.Add(entryHashes[index]);
		}
		foreach (var (name, hashes3) in perName)
		{
			using var ms3 = new MemoryStream();
			using var bw2 = new BinaryWriter(ms3);
			bw2.Write(hashes3.Count);
			foreach (var hash in hashes3)
			{
				bw2.Write(hash);
			}
			bw2.Flush();
			result[name] = HexHash(ms3.GetBuffer().AsSpan(0, (int)ms3.Length));
		}
		return result;
	}

	private static string HashCodeEntry(GameMakerCode entry, IList<GameMakerVariable> variables, Dictionary<string, GameMakerCodeLocals>? localsByName)
	{
		try
		{
			using var stream = new MemoryStream();
			GameMakerCodeLocals? entryLocals = null;
			localsByName?.TryGetValue(entry.Name.Content, out entryLocals);
			var bytes = Encoding.UTF8.GetBytes(entry.Disassemble(variables, entryLocals));
			stream.Write(bytes, 0, bytes.Length);
			foreach (var child in entry.ChildEntries)
			{
				if (child?.Name?.Content == null)
				{
					continue;
				}
				try
				{
					stream.WriteByte(0);
					GameMakerCodeLocals? childLocals = null;
					localsByName?.TryGetValue(child.Name.Content, out childLocals);
					var childBytes = Encoding.UTF8.GetBytes(child.Disassemble(variables, childLocals));
					stream.Write(childBytes, 0, childBytes.Length);
				}
				catch
				{
				}
			}
			return HexHash(stream.GetBuffer().AsSpan(0, (int)stream.Length));
		}
		catch
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			writer.Write(entry.Length);
			writer.Write(entry.Instructions.Count);
			foreach (var instruction in entry.Instructions)
			{
				writer.Write((byte)instruction.Kind);
				writer.Write((byte)instruction.Type1);
				writer.Write((byte)instruction.Type2);
			}
			writer.Flush();
			return HexHash(stream.GetBuffer().AsSpan(0, (int)stream.Length));
		}
	}

	private static Dictionary<string, string> HashExtensions(GameMakerData data)
	{
		if (data.Extensions == null)
		{
			return [];
		}
		var result = new ConcurrentDictionary<string, string>();
		CancellableParallel.ForEach(data.Extensions, delegate (GameMakerExtension ext)
		{
			if (ext?.Name?.Content != null)
			{
				var value = JsonHash(delegate (Utf8JsonWriter w)
				{
					w.WriteStartObject();
					w.WriteString("name", ext.Name.Content);
					w.WriteString("className", ext.ClassName?.Content ?? "");
					w.WriteString("folderName", ext.FolderName?.Content ?? "");
					w.WriteString("version", ext.Version?.Content ?? "");
					w.WriteStartArray("files");
					if (ext.Files != null)
					{
						foreach (var current in ext.Files)
						{
							w.WriteStartObject();
							w.WriteString("filename", current?.Filename?.Content ?? "");
							w.WriteString("cleanupScript", current?.CleanupScript?.Content ?? "");
							w.WriteString("initScript", current?.InitScript?.Content ?? "");
							w.WriteNumber("kind", (int)(current?.Kind ?? 0u));
							w.WriteStartArray("functions");
							if (current?.Functions != null)
							{
								foreach (var current2 in current.Functions)
								{
									w.WriteStartObject();
									w.WriteString("name", current2?.Name?.Content ?? "");
									w.WriteString("extName", current2?.ExtName?.Content ?? "");
									w.WriteNumber("id", (int)(current2?.ID ?? 0));
									w.WriteNumber("kind", (int)(current2?.Kind ?? 0));
									w.WriteNumber("retType", (int)(current2?.RetType ?? 0u));
									w.WriteStartArray("arguments");
									if (current2?.Arguments != null)
									{
										foreach (var current3 in current2.Arguments)
										{
											w.WriteStartObject();
											w.WriteNumber("type", (int)(current3?.Type ?? 0u));
											w.WriteEndObject();
										}
									}
									w.WriteEndArray();
									w.WriteEndObject();
								}
							}
							w.WriteEndArray();
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteStartArray("options");
					if (ext.Options != null)
					{
						foreach (var current4 in ext.Options)
						{
							w.WriteStartObject();
							w.WriteString("name", current4?.Name?.Content ?? "");
							w.WriteString("value", current4?.Value?.Content ?? "");
							w.WriteNumber("kind", (int)(current4?.Kind ?? GameMakerExtensionOption.OptionKind.Boolean));
							w.WriteEndObject();
						}
					}
					w.WriteEndArray();
					w.WriteEndObject();
				});
				result[ext.Name.Content] = value;
			}
		});
		return new Dictionary<string, string>(result);
	}
}
