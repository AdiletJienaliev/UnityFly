using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FlyBrain.Brain
{
    public enum Side : byte { Unknown = 0, Left = 1, Right = 2, Center = 3 }

    /// <summary>
    /// FlyWire annotations (flyconnectome/flywire_annotations) for every neuron of the model plus
    /// the neuron groups used in the original paper. Written by BrainModel/tools/export_brain.py.
    /// </summary>
    public sealed class NeuronCatalog
    {
        public static readonly string[] SuperClassNames =
        {
            "", "optic", "central", "sensory", "visual_projection", "ascending", "descending",
            "sensory_ascending", "visual_centrifugal", "motor", "endocrine"
        };
        public static readonly string[] TransmitterNames = { "", "acetylcholine", "glutamate", "gaba", "dopamine", "serotonin", "octopamine" };

        public int Count { get; private set; }
        public long[] RootId;
        public float[] Position;   // x,y,z in micrometers (FlyWire space), NaN if unknown
        public byte[] SuperClass;
        public Side[] Sides;
        public byte[] Transmitter;
        public int[] CellClass, CellSubClass, CellType, HemibrainType;
        public string[] Strings;
        public readonly Dictionary<string, NeuronGroup> Groups = new Dictionary<string, NeuronGroup>();

        Dictionary<long, int> _rootToIndex;

        public string TypeOf(int i) => Strings[CellType[i]];
        public string HemibrainTypeOf(int i) => Strings[HemibrainType[i]];
        public string ClassOf(int i) => Strings[CellClass[i]];
        public string SubClassOf(int i) => Strings[CellSubClass[i]];
        public string SuperClassOf(int i) => SuperClassNames[SuperClass[i]];

        public string Describe(int i)
        {
            var t = TypeOf(i);
            var hb = HemibrainTypeOf(i);
            var name = string.IsNullOrEmpty(t) ? "(untyped)" : t;
            if (!string.IsNullOrEmpty(hb) && hb != t) name += " / " + hb;
            return $"{name} [{SuperClassOf(i)}, {Sides[i].ToString().ToLowerInvariant()}]";
        }

        public int IndexOfRoot(long rootId)
        {
            if (_rootToIndex == null)
            {
                _rootToIndex = new Dictionary<long, int>(Count);
                for (int i = 0; i < Count; i++) _rootToIndex[RootId[i]] = i;
            }
            return _rootToIndex.TryGetValue(rootId, out var idx) ? idx : -1;
        }

        public static NeuronCatalog Load(string neuronsPath, string groupsPath)
        {
            var c = new NeuronCatalog();
            using (var fs = new FileStream(neuronsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var br = new BinaryReader(fs))
            {
                if (new string(br.ReadChars(4)) != "FLYN") throw new InvalidDataException("Not a neuron catalog: " + neuronsPath);
                if (br.ReadInt32() != 1) throw new InvalidDataException("Unsupported catalog version");
                int n = br.ReadInt32();
                br.ReadInt32(); // string count
                c.Count = n;
                c.RootId = ReadArray<long>(br, n);
                c.Position = ReadArray<float>(br, n * 3);
                c.SuperClass = br.ReadBytes(n);
                c.Sides = br.ReadBytes(n).Select(b => (Side)b).ToArray();
                c.Transmitter = br.ReadBytes(n);
                c.CellClass = ReadArray<int>(br, n);
                c.CellSubClass = ReadArray<int>(br, n);
                c.CellType = ReadArray<int>(br, n);
                c.HemibrainType = ReadArray<int>(br, n);
                int blobLen = br.ReadInt32();
                c.Strings = Encoding.UTF8.GetString(br.ReadBytes(blobLen)).Split('\n');
            }

            if (groupsPath != null && File.Exists(groupsPath))
            {
                foreach (var line in File.ReadAllLines(groupsPath))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    var idx = parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                    c.Groups[parts[0]] = new NeuronGroup(parts[0], parts[1], idx);
                }
            }
            return c;
        }

        static unsafe T[] ReadArray<T>(BinaryReader br, int count) where T : unmanaged
        {
            var arr = new T[count];
            var bytes = br.ReadBytes(count * sizeof(T));
            fixed (byte* src = bytes)
            fixed (T* dst = arr)
                Buffer.MemoryCopy(src, dst, bytes.Length, bytes.Length);
            return arr;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// Neurons whose cell_type or hemibrain_type matches one of the patterns.
        /// A pattern ending in '*' is a prefix match, otherwise the match is exact.
        /// </summary>
        public int[] FindByType(Side side, params string[] patterns)
        {
            var result = new List<int>();
            var matchCache = new Dictionary<int, bool>();
            bool Match(int stringId)
            {
                if (stringId == 0) return false;
                if (matchCache.TryGetValue(stringId, out var m)) return m;
                var s = Strings[stringId];
                m = patterns.Any(p => p.EndsWith("*") ? s.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal) : s == p);
                matchCache[stringId] = m;
                return m;
            }
            for (int i = 0; i < Count; i++)
            {
                if (side != Side.Unknown && Sides[i] != side) continue;
                if (Match(CellType[i]) || Match(HemibrainType[i])) result.Add(i);
            }
            return result.ToArray();
        }

        public int[] FindBySubClass(Side side, string subClass)
        {
            var result = new List<int>();
            for (int i = 0; i < Count; i++)
                if ((side == Side.Unknown || Sides[i] == side) && Strings[CellSubClass[i]] == subClass) result.Add(i);
            return result.ToArray();
        }

        public int[] Group(string name) => Groups.TryGetValue(name, out var g) ? g.Indices : Array.Empty<int>();

        public int[] Filter(IEnumerable<int> indices, Side side) =>
            side == Side.Unknown ? indices.ToArray() : indices.Where(i => Sides[i] == side).ToArray();

        /// <summary>All distinct cell types with their neuron count, sorted by name.</summary>
        public List<KeyValuePair<string, int>> AllCellTypes()
        {
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < Count; i++)
            {
                int t = CellType[i];
                if (t == 0) continue;
                counts.TryGetValue(t, out var k);
                counts[t] = k + 1;
            }
            return counts.Select(kv => new KeyValuePair<string, int>(Strings[kv.Key], kv.Value))
                         .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public sealed class NeuronGroup
    {
        public readonly string Name;
        public readonly string Description;
        public readonly int[] Indices;

        public NeuronGroup(string name, string description, int[] indices)
        {
            Name = name;
            Description = description;
            Indices = indices;
        }
    }
}
