using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using G3MLib.DataFile.Compiler;
using G3MLib.DataFile.Models;
using G3MLib.DataFile.Util;

namespace G3MLib.DataFile
{
    public interface GameMakerResourceRef : GameMakerObject
    {
        object Resource { get; set; }
        void PostUnserialize(GameMakerReader reader);
        int SerializeById(GameMakerWriter writer);
    }

    public class GameMakerResourceById<T, ChunkT> : GameMakerResourceRef, IStaticChildObjectsSize, IDisposable where T : GameMakerResource, new() where ChunkT : GameMakerListChunk<T>
    {
        /// <inheritdoc cref="IStaticChildObjectsSize.ChildObjectsSize" />
        public static readonly uint ChildObjectsSize = 4;

        public int CachedId { get; set; }
        public T Resource { get; set; }

        object GameMakerResourceRef.Resource { get => Resource; set => Resource = (T)value; }

        public GameMakerResourceById()
        {
            this.CachedId = -1;
        }

        public GameMakerResourceById(int id)
        {
            this.CachedId = id;
        }

        public GameMakerResourceById(T res) : this(res, -1)
        {
        }

        public GameMakerResourceById(T res, int id = -1)
        {
            this.Resource = res;
            this.CachedId = id;
        }

        private static ChunkT FindListChunk(GameMakerData data)
        {
            if (data.FORM.ChunksTypeDict.TryGetValue(typeof(ChunkT), out GameMakerChunk chunk))
            {
                return (ChunkT)chunk;
            }
            return null;
        }

        public int SerializeById(GameMakerWriter writer)
        {
            ChunkT chunk = FindListChunk(writer.gameMakerData);
            if (chunk != null)
            {
                if (!EqualityComparer<T>.Default.Equals(Resource, default))
                {
                    CachedId = chunk.IndexDict[Resource];
                    if (CachedId < 0)
                        throw new IOException("Unregistered object");
                }
                else
                {
                    int newCachedId;
                    if (typeof(ChunkT) == typeof(GameMakerChunkAGRP))
                        newCachedId = 0;
                    else
                        newCachedId = -1;
                    if (CachedId > 0 || (typeof(ChunkT) != typeof(GameMakerChunkAGRP) && CachedId == 0))
                    {
                        if (chunk.List.Count > CachedId && chunk.List[CachedId] is not null)
                        {
                            int firstNullOccurrence = chunk.List.IndexOf(default);
                            if (firstNullOccurrence != -1)
                                newCachedId = firstNullOccurrence;
                        }
                        else
                        {
                            newCachedId = CachedId;
                        }
                    }
                    CachedId = newCachedId;
                }
            }
            return CachedId;
        }

        public void UnserializeById(GameMakerReader reader, int id)
        {
            if (id < -1)
                throw new IOException("Invalid value for resource ID (" + typeof(ChunkT).Name + "): " + id);
            CachedId = id;
            Resource = default;
            reader.RequestResourceUpdate(this);
        }

        public void PostUnserialize(GameMakerReader reader)
        {
            GameMakerPointerList<T> list = FindListChunk(reader.gameMakerData)?.List;
            if (list != null)
            {
                if (typeof(ChunkT) == typeof(GameMakerChunkAGRP) && CachedId == reader.gameMakerData.GetBuiltinSoundGroupID() && list.Count == 0) // I won't even ask why this works like that
                {
                    Resource = default;
                    return;
                }
                if (CachedId >= list.Count)
                {
                    reader.SubmitWarning("Invalid value for resource ID of type " + typeof(ChunkT).Name + ": " + CachedId + " (there are only " + list.Count + ")");
                    return;
                }
                Resource = CachedId >= 0 ? list[CachedId] : default;
                if (EqualityComparer<T>.Default.Equals(Resource, default) && CachedId >= 0)
                {
                    // Null slots were introduced for resources removed by GMAC in 2024.11.
                    if (reader.gameMakerData.IsGameMaker2())
                    {
                        if (!reader.gameMakerData.IsVersionAtLeast(2024, 11))
                            reader.gameMakerData.SetGMS2Version(2024, 11);
                    }
                    else
                    {
                        reader.SubmitWarning("ID reference to null object found on file built with GMS pre-2!");
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return (Resource?.ToString() ?? "(null)") + GetMarkerSuffix();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            GC.SuppressFinalize(this);

            Resource = default;
        }

        public string GetMarkerSuffix()
        {
            return "@" + CachedId;
        }

        public void Serialize(GameMakerWriter writer)
        {
            writer.Write(SerializeById(writer));
        }

        public void Unserialize(GameMakerReader reader)
        {
            UnserializeById(reader, reader.ReadInt32());
        }
    }

    public class GameMakerReader : AdaptiveBinaryReader
    {
        /// <summary>
        /// Function to delegate warning messages to.
        /// </summary>
        /// <param name="warning">Warning message.</param>
        /// <param name="isImportant">Whether the warning is deemed important (that is, may risk data loss when re-saving).</param>
        public delegate void WarningHandlerDelegate(string warning, bool isImportant);

        /// <summary>
        /// Function to delegate informational messages to.
        /// </summary>
        /// <param name="message">Informational message.</param>
        public delegate void MessageHandlerDelegate(string message);

        private readonly WarningHandlerDelegate _warningHandler;
        private readonly MessageHandlerDelegate _messageHandler;

        public bool ReadOnlyGEN8 { get; set; }

        /// <summary>
        /// The detected absolute path of the data file, if a FileStream is passed in, or null otherwise (by default).
        /// Can also be manually changed.
        /// </summary>
        public string FilePath { get; set; } = null;

        /// <summary>
        /// The detected absolute path of the directory containing the data file, if a FileStream is passed in, or null otherwise (by default).
        /// Can also be manually changed.
        /// </summary>
        public string Directory { get; set; } = null;

        internal readonly record struct BytecodeInformation(uint InstructionCount, GameMakerCode RootEntry);

        internal Dictionary<uint, BytecodeInformation> BytecodeAddresses;
        internal ArrayPool<uint> ListPtrsPool = ArrayPool<uint>.Create(100000, 17);
        internal string LastChunkName;
        internal List<string> AllChunkNames;
        internal bool Bytecode14OrLower = false;

        public GameMakerReader(Stream input,
                               WarningHandlerDelegate warningHandler = null, MessageHandlerDelegate messageHandler = null,
                               bool onlyGeneralInfo = false) : base(input)
        {
            _warningHandler = warningHandler;
            _messageHandler = messageHandler;
            ReadOnlyGEN8 = onlyGeneralInfo;
            if (input is FileStream fs)
            {
                FilePath = fs.Name;
                Directory = Path.GetDirectoryName(FilePath);
            }

            ReaderMetadata metadata = ReaderMetadataCache.Value;
            countUnserializeExc = metadata.Exception;
            unserializeFuncDict = new(metadata.UnserializeFunctions);
            staticObjCountDict = new(metadata.StaticObjectCounts);
            staticObjSizeDict = new(metadata.StaticObjectSizes);
        }

        public void SubmitWarning(string warning, bool isImportant = true)
        {
            warning += "\nat " + AbsPosition.ToString("X8");
            if (_warningHandler != null)
                _warningHandler.Invoke(warning, isImportant);
            else if (isImportant)
                throw new IOException(warning);
        }

        public void SubmitMessage(string message)
        {
            if (_messageHandler != null)
                _messageHandler.Invoke(message);
            else
                Debug.WriteLine(message);
        }

        public GameMakerChunk ReadGameMakerChunk()
        {
            return GameMakerChunk.Unserialize(this);
        }
        public (uint, GameMakerChunk) CountChunkChildObjects()
        {
            return GameMakerChunk.CountChunkChildObjects(this);
        }

        private readonly List<GameMakerResourceRef> _resourceRefsToResolve = new(256);
        internal GameMakerData gameMakerData;

        public GameMakerData ReadGameMakerData()
        {
            // Create new data context
            GameMakerData data = new();
            gameMakerData = data;

            // Ensure root chunk is called "FORM"
            string name = ReadChars(4);
            if (name != "FORM")
                throw new IOException($"Root chunk is \"{name}\", not FORM");
            uint length = ReadUInt32();
            if ((long)length > Length - Position)
                throw new IOException("Root chunk exceeds the available input.");
            data.FORM = new GameMakerChunkFORM
            {
                Length = length
            };
            DebugUtil.Assert(data.FORM.Name == name);

            // Perform object counting pass on file
            long startPos = Position;
            uint poolSize = 0;
            if (!ProcessObjectCountingErrors()) // process an exception from "FillUnserializeCountDictionaries()"
            {
                try
                {
                    if (!ReadOnlyGEN8)
                    {
                        poolSize = data.FORM.UnserializeObjectCount(this);
                    }
                }
                catch (Exception e)
                {
                    countUnserializeExc = e;
                    Debug.WriteLine(e);

                    ResetToFilePosition(startPos);
                }
            }
            ListPtrsPool = null;

            // Initialize object pools
            InitializePools(poolSize);

            // Read all of the main data
            Position = startPos;
            EnsureLengthOperation lenReader = EnsureLengthFromHere(data.FORM.Length);
            data.FORM.UnserializeChunk(this);
            lenReader.ToHere();

            // Resolve resource IDs
            SubmitMessage("Resolving resource IDs...");
            foreach (GameMakerResourceRef res in _resourceRefsToResolve)
            {
                res.PostUnserialize(this);
            }
            _resourceRefsToResolve.Clear();

            // Skip extra processing if it's not a regular data file (e.g., audio group files)
            if (data.FORM.GEN8 is not null)
            {
                // Initialize GML builtins
                data.BuiltinList = new BuiltinList(data);

                // Initialize game-specific data resolver
                try
                {
                    Decompiler.GameSpecificResolver.Initialize(data);
                }
                catch (Exception e)
                {
                    SubmitWarning($"Error initializing game-specific data resolver:\n{e.GetType().FullName}: {e.Message}", false);
                    data.GameSpecificRegistry = new(); // Use a blank registry...
                }

                // Link texture group information, if relevant
                GameMakerEmbeddedTexture.FindAllTextureInfo(data);

                // Iterate over function names to see if 2023.11+ naming process was used (if necessary)
                if (data.Functions is not null && data.IsVersionAtLeast(2023, 8) && !data.IsVersionAtLeast(2023, 11))
                {
                    foreach (GameMakerFunction function in data.Functions)
                    {
                        // If name starts with "gml_Script" and contains a @ character, it should be from 2023.11
                        if (function?.Name?.Content is string functionName &&
                            functionName.StartsWith("gml_Script_", StringComparison.Ordinal) &&
                            functionName.Contains('@'))
                        {
                            data.SetGMS2Version(2023, 11);
                            break;
                        }
                    }
                }
            }

            // Process any errors that may have occurred during object counting
            ProcessObjectCountingErrors(poolSize);

            return data;
        }

        public GameMakerDebugData ReadGameMakerDebugData()
        {
            gameMakerData = GameMakerData.CreateNew();
            gameMakerData.SetGMS2Version(2024, 11);

            string name = ReadChars(4);
            if (name != "FORM")
                throw new IOException($"Root chunk is \"{name}\", not FORM");
            uint length = ReadUInt32();
            if ((long)length > Length - Position)
                throw new IOException("Root chunk exceeds the available input.");

            GameMakerDebugData data = new()
            {
                FORM = new GameMakerDebugFORM { Length = length }
            };

            long startPos = Position;
            uint poolSize = data.FORM.UnserializeObjectCount(this);
            InitializePools(poolSize);

            Position = startPos;
            EnsureLengthOperation lengthReader = EnsureLengthFromHere(data.FORM.Length);
            data.FORM.UnserializeChunk(this);
            lengthReader.ToHere();
            return data;
        }

        internal void RequestResourceUpdate(GameMakerResourceRef res)
        {
            _resourceRefsToResolve.Add(res);
        }

        /// <summary>
        /// Reads a boolean as 32 bits (0 or 1), maintaining alignment.
        /// </summary>
        public override bool ReadBoolean()
        {
            uint val = ReadUInt32();
            return val switch
            {
                0 => false,
                1 => true,
                _ => throw new IOException($"Invalid boolean value: {val}")
            };
        }

        private Dictionary<uint, GameMakerObject> objectPool;
        private Dictionary<GameMakerObject, uint> objectPoolRev;
        private readonly HashSet<uint> unreadObjects = new();

        private Exception countUnserializeExc;
        private readonly Dictionary<Type, Func<GameMakerReader, uint>> unserializeFuncDict;
        private readonly Dictionary<Type, uint> staticObjCountDict;
        private readonly Dictionary<Type, uint> staticObjSizeDict;

        private static readonly BindingFlags PublicStaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        private static readonly Type[] ReaderArgType = [typeof(GameMakerReader)];
        private static readonly Type DelegateType = typeof(Func<GameMakerReader, uint>);
        private static readonly Func<GameMakerReader, uint> BlankCountFunc = _ => 0;
        private static readonly Lazy<ReaderMetadata> ReaderMetadataCache = new(CreateReaderMetadata);

        private sealed class ReaderMetadata(
            Dictionary<Type, Func<GameMakerReader, uint>> unserializeFunctions,
            Dictionary<Type, uint> staticObjectCounts,
            Dictionary<Type, uint> staticObjectSizes,
            Exception exception)
        {
            public Dictionary<Type, Func<GameMakerReader, uint>> UnserializeFunctions { get; } = unserializeFunctions;
            public Dictionary<Type, uint> StaticObjectCounts { get; } = staticObjectCounts;
            public Dictionary<Type, uint> StaticObjectSizes { get; } = staticObjectSizes;
            public Exception Exception { get; } = exception;
        }

        private bool ProcessObjectCountingErrors(uint poolSize = 0)
        {
            if (countUnserializeExc is not null)
            {
                string logMessage = string.Empty;
                try
                {
                    string fileDir = Path.GetDirectoryName(Environment.ProcessPath);
                    File.WriteAllText(Path.Join(fileDir, "unserializeCountError.txt"),
                                      countUnserializeExc + "\n"
                                      + countUnserializeExc.Message + "\n"
                                      + countUnserializeExc.StackTrace);
                    logMessage = " The error log is saved to \"unserializeCountError.txt\".";
                }
                catch
                {
                    // The error log is optional; keep the original warning when it cannot be written.
                }

                SubmitWarning("Warning - there was an error while trying to unserialize total object count.\n" +
                              logMessage + " Please report that error to the G3MLib maintainers.");

                countUnserializeExc = null;

                return true;
            }

            if (poolSize != 0 && poolSize != objectPool.Count)
            {
                SubmitWarning("Warning - the estimated object pool size differs from the actual size.\n" +
                             $"Estimated: {poolSize}, Actual: {objectPool.Count}\n" +
                              "Please report this to the G3MLib maintainers.", false);
            }

            return false;
        }
        private static ReaderMetadata CreateReaderMetadata()
        {
            Dictionary<Type, Func<GameMakerReader, uint>> unserializeFunctions = new();
            Dictionary<Type, uint> staticObjectCounts = new();
            Dictionary<Type, uint> staticObjectSizes = new();
            try
            {
                Assembly currAssem = Assembly.GetExecutingAssembly();
                Type[] allTypes = currAssem.GetTypes();

                Type utObjectType = typeof(GameMakerObject);
                Type staticObjCountType = typeof(IStaticChildObjCount);
                Type staticObjSizeType = typeof(IStaticChildObjectsSize);

                allTypes = allTypes.Where(t => t.IsAssignableTo(utObjectType)).ToArray();
                foreach (Type t in allTypes)
                {
                    // It's not possible to call a static method of generic classes without present type argument.
                    if (t.ContainsGenericParameters)
                        continue;

                    MethodInfo mi = t.GetMethod("UnserializeChildObjectCount", PublicStaticFlags, ReaderArgType);
                    if (mi is null)
                        continue;

                    var func = Delegate.CreateDelegate(DelegateType, mi) as Func<GameMakerReader, uint>;
                    if (func is null)
                    {
                        Debug.WriteLine($"Can't create a delegate from MethodInfo of type \"{t.FullName}\"");
                        continue;
                    }

                    unserializeFunctions[t] = func;
                }

                for (int i = 0; i < allTypes.Length; i++)
                {
                    Type t = allTypes[i];
                    FieldInfo fi;
                    object res;

                    // It's not supported to get a static field from generic classes without present type argument.
                    if (t.ContainsGenericParameters)
                        continue;

                    if (t.IsAssignableTo(staticObjCountType))
                    {
                        fi = t.GetField("ChildObjectCount", PublicStaticFlags);
                        if (fi is null)
                        {
                            Debug.WriteLine($"Can't get \"ChildObjectCount\" field of \"{t.FullName}\"");
                            continue;
                        }

                        res = fi.GetValue(null);
                        if (res is null)
                        {
                            Debug.WriteLine($"Can't get value of \"ChildObjectCount\" of \"{t.FullName}\"");
                            continue;
                        }

                        staticObjectCounts[t] = (uint)res;
                    }

                    if (t.IsAssignableTo(staticObjSizeType))
                    {
                        fi = t.GetField("ChildObjectsSize", PublicStaticFlags);
                        if (fi is null)
                        {
                            Debug.WriteLine($"Can't get \"ChildObjectsSize\" field of \"{t.FullName}\"");
                            continue;
                        }

                        res = fi.GetValue(null);
                        if (res is null)
                        {
                            Debug.WriteLine($"Can't get value of \"ChildObjectsSize\" of \"{t.FullName}\"");
                            continue;
                        }

                        staticObjectSizes[t] = (uint)res;
                    }
                }
                return new ReaderMetadata(unserializeFunctions, staticObjectCounts, staticObjectSizes, null);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
                return new ReaderMetadata(unserializeFunctions, staticObjectCounts, staticObjectSizes, exception);
            }
        }
        public Func<GameMakerReader, uint> GetUnserializeCountFunc(Type objType)
        {
            if (!unserializeFuncDict.TryGetValue(objType, out var res))
            {
                MethodInfo mi = objType.GetMethod("UnserializeChildObjectCount", PublicStaticFlags, ReaderArgType);
                if (mi is null)
                {
                    Debug.WriteLine($"\"GameMakerReader.unserializeFuncDict\" doesn't contain a method for \"{objType.FullName}\".");
                    return BlankCountFunc;
                }

                var func = Delegate.CreateDelegate(DelegateType, mi) as Func<GameMakerReader, uint>;
                if (func is null)
                {
                    Debug.WriteLine($"Can't create a delegate from MethodInfo of type \"{objType.FullName}\"");
                    return BlankCountFunc;
                }

                unserializeFuncDict[objType] = func;

                res = func;
            }

            return res;
        }
        public uint GetStaticChildCount(Type objType)
        {
            if (!staticObjCountDict.TryGetValue(objType, out uint res))
            {
                Debug.WriteLine($"\"GameMakerReader.staticObjCountDict\" doesn't contain type \"{objType.FullName}\".");
                return 0;
            }

            return res;
        }
        public uint GetStaticChildObjectsSize(Type objType)
        {
            if (!staticObjSizeDict.TryGetValue(objType, out uint res))
            {
                Debug.WriteLine($"\"GameMakerReader.staticObjSizeDict\" doesn't contain type \"{objType.FullName}\".");
                return 0;
            }

            return res;
        }
        public void SetStaticChildCount(Type objType, uint count)
        {
            staticObjCountDict[objType] = count;
        }
        public void SetStaticChildObjectsSize(Type objType, uint size)
        {
            staticObjSizeDict[objType] = size;
        }

        public Dictionary<uint, GameMakerObject> GetOffsetMap()
        {
            return objectPool;
        }

        public Dictionary<GameMakerObject, uint> GetOffsetMapRev()
        {
            return objectPoolRev;
        }

        public void InitializePools(uint objCount = 0)
        {
            if (objCount == 0)
            {
                objectPool = new();
                objectPoolRev = new();
            }
            else
            {
                int objCountInt = checked((int)objCount);
                objectPool = new(objCountInt);
                objectPoolRev = new(objCountInt);
            }
        }

        public uint GetChildObjectCount(Type t)
        {
            if (!unserializeFuncDict.TryGetValue(t, out var func))
            {
                if (staticObjSizeDict.TryGetValue(t, out uint size))
                {
                    Position += size;

                    staticObjCountDict.TryGetValue(t, out uint subCount);

                    return subCount;
                }

                throw new GameMakerSerializationException(
                    $"\"GameMakerReader.unserializeFuncDict\" doesn't contain a method for \"{t.FullName}\".");
            }

            return func(this);
        }
        public uint GetChildObjectCount<T>() where T : GameMakerObject
        {
            Type t = typeof(T);

            return GetChildObjectCount(t);
        }


        public T GetGameMakerObjectAtAddress<T>(uint address) where T : GameMakerObject, new()
        {
            if (address == 0)
                return default;
            GameMakerObject obj;
            if (!objectPool.TryGetValue(address, out obj))
            {
                obj = new T();
                objectPool.Add(address, obj);
                objectPoolRev.Add(obj, address);
                unreadObjects.Add(address);
            }
            return (T)obj;
        }

        public uint GetAddressForGameMakerObject(GameMakerObject obj)
        {
            if (obj == null)
                return 0;
            return objectPoolRev[obj];
        }

        public void ReadGameMakerObject<T>(T obj) where T : GameMakerObject, new()
        {
            try
            {
                uint expectedAddress = GetAddressForGameMakerObject(obj);
                if (expectedAddress == 0)
                    return;
                if (expectedAddress != AbsPosition)
                {
                    SubmitWarning($"Reading misaligned at {AbsPosition:X8}, realigning back to {expectedAddress:X8}\nHIGH RISK OF DATA LOSS! The file is probably corrupted, or uses unsupported features\nProceed at your own risk");
                    AbsPosition = expectedAddress;
                }
                unreadObjects.Remove((uint)AbsPosition);
                obj.Unserialize(this);
            }
            catch (Exception e)
            {
                throw new GameMakerSerializationException(e.Message + "\nat " + AbsPosition.ToString("X8") + " while reading object " + typeof(T).FullName, e);
            }
        }

        public T ReadGameMakerObject<T>() where T : GameMakerObject, new()
        {
            uint address = (uint)AbsPosition;

            T result;
            if (objectPool.TryGetValue(address, out GameMakerObject obj))
            {
                result = (T)obj;
                unreadObjects.Remove(address);
            }
            else
            {
                result = new T();
                objectPool.Add(address, result);
                objectPoolRev.Add(result, address);
            }

            result.Unserialize(this);
            return result;
        }

        public T ReadGameMakerObjectNoPool<T>() where T : GameMakerObject, new()
        {
            T o = new();
            o.Unserialize(this);
            return o;
        }

        public T ReadGameMakerObjectPointer<T>() where T : GameMakerObject, new()
        {
            return GetGameMakerObjectAtAddress<T>(ReadUInt32());
        }

        public GameMakerString ReadGameMakerString()
        {
            uint addr = ReadUInt32();

            if (addr == 0)
                return null;

            // Normally, the strings point directly to the string content
            // This may be done that way because it's faster when the game accesses them, but for our purposes it's better to access the whole string resource object
            return GetGameMakerObjectAtAddress<GameMakerString>(addr - 4);
        }

        public void ThrowIfUnreadObjects()
        {
            if (ReadOnlyGEN8)
                return;

            if (unreadObjects.Count > 0)
            {
                throw new IOException("Found pointer targets that were never read:\n" + String.Join("\n", unreadObjects.Take(10).Select((x) => "0x" + x.ToString("X8") + " (" + objectPool[x].GetType().Name + ")")) + (unreadObjects.Count > 10 ? "\n(and more, " + unreadObjects.Count + " total)" : ""));
            }
        }

        public class EnsureLengthOperation
        {
            private readonly GameMakerReader reader;
            private readonly long startPos;
            private readonly uint expectedLength;
            internal EnsureLengthOperation(GameMakerReader reader, uint expectedLength)
            {
                this.reader = reader;
                this.startPos = reader.Position;
                this.expectedLength = expectedLength;
            }
            public void ToHere()
            {
                long length = reader.Position - startPos;
                if (length != expectedLength)
                {
                    long diff = (long)expectedLength - length;
                    if (diff > 0)
                    {
                        // Some platform ports pad known chunk payloads to the declared
                        // chunk length. Skip that padding without treating it as data loss.
                        reader.Position += (uint)diff;
                    }
                    else
                    {
                        reader.SubmitWarning("WARNING: File specified length " + expectedLength + ", but read " + length + " bytes.");
                        throw new IOException("Read underflow");
                    }
                }
            }
        }

        public void Align(int alignment)
        {
            while ((AbsPosition & (alignment - 1)) != 0)
            {
                if (ReadByte() != 0)
                {
                    throw new IOException("Invalid alignment padding");
                }
            }
        }

        public EnsureLengthOperation EnsureLengthFromHere(uint expectedLength)
        {
            return new EnsureLengthOperation(this, expectedLength);
        }
    }

    public class GameMakerWriter : FileBinaryWriter
    {
        internal GameMakerData gameMakerData;

        public string LastChunkName { get; set; }
        internal bool IsDebugData;
        public uint LastBytecodeAddress { get; set; }
        public bool Bytecode14OrLower { get; set; }

        public delegate void MessageHandlerDelegate(string message);
        private readonly MessageHandlerDelegate MessageHandler;

        public GameMakerWriter(Stream output, MessageHandlerDelegate messageHandler = null) : base(output)
        {
            MessageHandler = messageHandler;
        }

        public void SubmitMessage(string message)
        {
            if (MessageHandler != null)
                MessageHandler.Invoke(message);
            else
                Debug.WriteLine(message);
        }

        public void Write(GameMakerChunk obj)
        {
            obj.Serialize(this);
        }

        /// <summary>
        /// Writes a boolean using 32 bits (0 or 1), maintaining alignment.
        /// </summary>
        public override void Write(bool value)
        {
            Write(value ? (uint)1 : (uint)0);
        }

        public void WriteGameMakerData(GameMakerData data)
        {
            gameMakerData = data;
            Bytecode14OrLower = data?.GeneralInfo?.BytecodeVersion <= 14;

            // Figure out the last chunk by iterating identically as it does when serializing,
            // and generate the object index dictionaries for acceleration of "GameMakerResourceById.SerializeById()"
            foreach (var chunk in data.FORM.Chunks)
            {
                LastChunkName = chunk.Key;

                if (chunk.Value is IGameMakerListChunk listChunk)
                {
                    listChunk.GenerateIndexDict();
                }
            }

            Write(data.FORM);
        }

        private readonly Dictionary<GameMakerObject, uint> objectPool = new();
        private readonly Dictionary<GameMakerObject, List<uint>> pendingWrites = new();
        private readonly Dictionary<GameMakerObject, List<uint>> pendingStringWrites = new();

        public void Flush(GameMakerData data)
        {
            // Clear out index dictionaries (no longer needed)
            foreach (var chunk in data.FORM.Chunks.Values)
            {
                if (chunk is IGameMakerListChunk listChunk)
                {
                    listChunk.ClearIndexDict();
                }
            }

            SubmitMessage("Flushing remaining buffer data...");
            base.Flush();
        }

        public Dictionary<GameMakerObject, uint> GetObjectPool()
        {
            return objectPool;
        }

        public uint GetAddressForGameMakerObject(GameMakerObject obj)
        {
            if (obj == null)
                return 0;
            if (!objectPool.TryGetValue(obj, out uint res))
                throw new KeyNotFoundException();
            return res;
        }

        public void WriteGameMakerObject<T>(T obj) where T : GameMakerObject
        {
            if (obj is null)
            {
                // We simply shouldn't write anything.
                // Pointers to this "object" are simply written as 0, and we don't need to
                // put it in the pool
                return;
            }

            try
            {
                // Store object address before writing it
                uint objectAddr = Position;

                if (typeof(T) == typeof(GameMakerString))
                {
                    // Can skip adding strings to object pool (nothing later in the file references them).
                    obj.Serialize(this);

                    // Patch all pointers to this string, if applicable
                    if (pendingStringWrites.TryGetValue(obj, out List<uint> patches))
                    {
                        uint returnTo = Position;
                        objectAddr += 4; // Destination is where the string starts, not its length
                        foreach (uint pointerAddr in patches)
                        {
                            Position = pointerAddr;
                            Write(objectAddr);
                        }
                        Position = returnTo;

                        // Remove pending write
                        pendingStringWrites.Remove(obj);
                    }
                }
                else
                {
                    // Add object to pool
                    objectPool.Add(obj, objectAddr);

                    // Serialize object
                    obj.Serialize(this);

                    // Patch all pointers to this object, if applicable
                    if (pendingWrites.TryGetValue(obj, out List<uint> patches))
                    {
                        uint returnTo = Position;
                        foreach (uint pointerAddr in patches)
                        {
                            Position = pointerAddr;
                            Write(objectAddr);
                        }
                        Position = returnTo;

                        // Remove pending write
                        pendingWrites.Remove(obj);
                    }
                }
            }
            catch (Exception e)
            {
                throw new GameMakerSerializationException($"{e.Message}\nat {Position:X8} while writing object {typeof(T).FullName}", e);
            }
        }

        public void WriteGameMakerObjectPointer<T>(T obj) where T : GameMakerObject
        {
            if (EqualityComparer<T>.Default.Equals(obj, default))
            {
                Write(0x00000000u);
                return;
            }

            if (objectPool.TryGetValue(obj, out uint objectAddress))
            {
                Write(objectAddress);
            }
            else
            {
                if (!pendingWrites.TryGetValue(obj, out List<uint> list))
                {
                    list = new List<uint>();
                    pendingWrites.Add(obj, list);
                }
                list.Add(Position);
                Write(0xDEADC0DEu);
            }
        }

        public void WriteGameMakerString(GameMakerString obj)
        {
            if (obj == null)
            {
                Write(0x0000000u);
                return;
            }

            if (!pendingStringWrites.TryGetValue(obj, out List<uint> list))
            {
                list = new List<uint>();
                pendingStringWrites.Add(obj, list);
            }
            list.Add(Position);
            Write(0xDEADC0DEu);
        }

        public void ThrowIfUnwrittenObjects()
        {
            if ((pendingWrites.Count + pendingStringWrites.Count) != 0)
            {
                var unwrittenObjects = pendingWrites.Concat(pendingStringWrites);
                throw new IOException("Found pointer targets that were never written:\n"
                                      + String.Join("\n", unwrittenObjects.Take(10).Select((x) => x.Key + " at " + String.Join(", ", x.Value.Select((y) => "0x" + y.ToString("X8")))))
                                      + (unwrittenObjects.Count() > 10
                                         ? "\n(and more, " + unwrittenObjects.Count() + " total)"
                                         : ""));
            }
        }

        public void Align(int alignment)
        {
            while ((Position & (alignment - 1)) != 0)
            {
                Write((byte)0);
            }
        }

        public class WriteLengthOperation
        {
            private readonly GameMakerWriter writer;
            private readonly uint writePos;
            private uint? startPos = null;
            internal WriteLengthOperation(GameMakerWriter writer)
            {
                this.writer = writer;
                this.writePos = writer.Position;
                writer.Write(0xDEADC0DEu);
            }
            public void FromHere()
            {
                this.startPos = writer.Position;
            }
            public uint ToHere()
            {
                if (!startPos.HasValue)
                    throw new InvalidOperationException("Forgot to call FromHere()");

                uint endPos = writer.Position;
                writer.Position = writePos;
                uint valueToWrite = endPos - startPos.Value;
                writer.Write(valueToWrite);
                writer.Position = endPos;
                return valueToWrite;
            }
        }

        public WriteLengthOperation WriteLengthHere()
        {
            return new WriteLengthOperation(this);
        }
    }

    public static class GameMakerIO
    {
        public static GameMakerData Read(Stream stream, GameMakerReader.WarningHandlerDelegate warningHandler = null,
                                                        GameMakerReader.MessageHandlerDelegate messageHandler = null,
                                                        bool onlyGeneralInfo = false)
        {
            ArgumentNullException.ThrowIfNull(stream);
            GameMakerReader reader = new(stream, warningHandler, messageHandler, onlyGeneralInfo);
            GameMakerData data = reader.ReadGameMakerData();
            reader.ThrowIfUnreadObjects();
            return data;
        }

        public static void Write(Stream stream, GameMakerData data, GameMakerWriter.MessageHandlerDelegate messageHandler = null)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(data);
            ArgumentNullException.ThrowIfNull(data.FORM);
            GameMakerWriter writer = new(stream, messageHandler);
            writer.WriteGameMakerData(data);
            writer.ThrowIfUnwrittenObjects();
            writer.Flush(data);
        }

        public static Dictionary<uint, GameMakerObject> GenerateOffsetMap(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            GameMakerReader reader = new(stream);
            reader.ReadGameMakerData();
            reader.ThrowIfUnreadObjects();
            return reader.GetOffsetMap();
        }
    }
}
