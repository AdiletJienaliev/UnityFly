using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FlyBrain.Brain
{
    /// <summary>
    /// FlyWire connectivity in compressed sparse row form (rows = presynaptic neuron),
    /// loaded from the binary written by BrainModel/tools/export_brain.py.
    /// Weights are signed synapse counts ("Excitatory x Connectivity" in the original data).
    /// </summary>
    public sealed unsafe class Connectome : IDisposable
    {
        public int NeuronCount { get; private set; }
        public int ConnectionCount { get; private set; }
        public int* RowPtr { get; private set; }
        public int* Post { get; private set; }
        public short* Weight { get; private set; }

        public static Connectome Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            using (var br = new BinaryReader(fs))
            {
                if (new string(br.ReadChars(4)) != "FLYC") throw new InvalidDataException("Not a connectome file: " + path);
                int version = br.ReadInt32();
                if (version != 1) throw new InvalidDataException("Unsupported connectome version " + version);
                var c = new Connectome { NeuronCount = br.ReadInt32(), ConnectionCount = br.ReadInt32() };
                c.RowPtr = (int*)Alloc(fs, sizeof(int) * (long)(c.NeuronCount + 1));
                c.Post = (int*)Alloc(fs, sizeof(int) * (long)c.ConnectionCount);
                c.Weight = (short*)Alloc(fs, sizeof(short) * (long)c.ConnectionCount);
                return c;
            }
        }

        static void* Alloc(Stream s, long bytes)
        {
            var p = (byte*)Marshal.AllocHGlobal((IntPtr)bytes);
            long done = 0;
            while (done < bytes)
            {
                int chunk = (int)Math.Min(bytes - done, 1 << 26);
                int n = s.Read(new Span<byte>(p + done, chunk));
                if (n <= 0) throw new EndOfStreamException();
                done += n;
            }
            return p;
        }

        public int OutDegree(int neuron) => RowPtr[neuron + 1] - RowPtr[neuron];

        public void Dispose()
        {
            if (RowPtr != null) Marshal.FreeHGlobal((IntPtr)RowPtr);
            if (Post != null) Marshal.FreeHGlobal((IntPtr)Post);
            if (Weight != null) Marshal.FreeHGlobal((IntPtr)Weight);
            RowPtr = null; Post = null; Weight = null;
        }
    }
}
