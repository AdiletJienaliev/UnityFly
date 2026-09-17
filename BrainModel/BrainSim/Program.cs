using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using FlyBrain.Brain;

// Offline harness for the C# port of the Drosophila LIF brain model.
//   dotnet run -c Release -- validate   compare with the Brian2 results shipped in the original repository
//   dotnet run -c Release -- bench      simulation speed on FlyWire 783
//   dotnet run -c Release -- probe      which descending / motor neurons respond to sensory groups
static unsafe class Program
{
    static readonly string Root = FindRoot();
    static string Data(string f) => Path.Combine(Root, "BrainModel", "BrainSim", "data", f);
    static string Streaming(string f) => Path.Combine(Root, "Assets", "StreamingAssets", "FlyBrain", f);

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("Unity project root not found");
    }

    static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        var cmd = args.Length > 0 ? args[0] : "validate";
        switch (cmd)
        {
            case "validate": Validate(args.Skip(1).ToArray()); break;
            case "bench": Bench(); break;
            case "probe": Probe(args.Skip(1).ToArray()); break;
            case "mini": Mini(args.Skip(1).ToArray()); break;
            case "stim": Stim(args.Skip(1).ToArray()); break;
            case "persist": Persist(args.Skip(1).ToArray()); break;
            case "prof": Prof(args.Skip(1).ToArray()); break;
            case "measure": Measure(args.Skip(1).ToArray()); break;
            default: Console.WriteLine("commands: validate | bench | probe"); return 1;
        }
        return 0;
    }

    // ------------------------------------------------------------------ validation against Brian2

    static void Validate(string[] args)
    {
        using var con = Connectome.Load(Data("connectome_630.bin"));
        var sugar = File.ReadAllLines(Data("paper_sugar_630.txt")).Select(int.Parse).ToArray();
        int trials = args.Length > 0 ? int.Parse(args[0]) : 30;
        var prm = new LifParameters();
        if (args.Length > 1) prm.Delay = double.Parse(args[1]);
        if (args.Length > 2) prm.Refractory = double.Parse(args[2]);
        var snaps = args.Length > 3 ? new[] { float.Parse(args[3]) } : new[] { 0f, 1e-3f };
        Console.WriteLine($"delay {prm.DelaySteps} steps, refractory {prm.RefractorySteps} steps");
        Console.WriteLine($"FlyWire 630: {con.NeuronCount} neurons, {con.ConnectionCount} connections; {trials} trials x 1 s");

        foreach (var (exp, hz) in new[] { ("sugarR", 200f), ("sugarR_100Hz", 100f) })
        {
            var reference = File.ReadAllLines(Data($"brian2_{exp}_rates.csv")).Skip(1)
                .Select(l => l.Split(',')).ToDictionary(p => int.Parse(p[0]), p => double.Parse(p[2]));
            foreach (var snap in snaps)
            {
                using var brain = new LifBrain(con, prm, seed: 777);
                brain.SnapTolerance = snap;
                foreach (var i in sugar) brain.SetPoissonRate(i, hz);
                var sw = Stopwatch.StartNew();
                var total = new double[con.NeuronCount];
                for (int t = 0; t < trials; t++)
                {
                    brain.RequestReset();
                    brain.ClearStatistics();
                    brain.RunMs(1000);
                    for (int i = 0; i < con.NeuronCount; i++) total[i] += brain.SpikeCount[i];
                }
                double wall = sw.Elapsed.TotalSeconds;
                var rates = total.Select(x => x / trials).ToArray();
                Compare(exp, snap, rates, reference, wall, trials);
                File.WriteAllLines(Data($"csharp_{exp}_rates.csv"), new[] { "index,rate" }.Concat(
                    Enumerable.Range(0, rates.Length).Where(i => rates[i] > 0).Select(i => $"{i},{rates[i]}")));
            }
        }
    }

    static void Compare(string exp, float snap, double[] rates, Dictionary<int, double> reference, double wall, int trials)
    {
        var union = reference.Keys.Union(Enumerable.Range(0, rates.Length).Where(i => rates[i] > 0)).ToArray();
        var x = union.Select(i => reference.TryGetValue(i, out var r) ? r : 0).ToArray();
        var y = union.Select(i => rates[i]).ToArray();
        // restrict "responding" comparisons to neurons that are not Poisson-driven and fire > 1 Hz in either model
        var strong = union.Where(i => Math.Max(reference.GetValueOrDefault(i), rates[i]) > 1).ToArray();
        Console.WriteLine();
        Console.WriteLine($"== {exp}  snap={snap}  ({wall:F1} s wall for {trials} s simulated, {trials / wall:F2}x real time)");
        Console.WriteLine($"   active neurons: Brian2 {reference.Count}, C# {rates.Count(r => r > 0)};  >1 Hz: Brian2 {reference.Values.Count(r => r > 1)}, C# {rates.Count(r => r > 1)}");
        Console.WriteLine($"   Pearson r (all active) = {Pearson(x, y):F4};  r (>1 Hz) = {Pearson(strong.Select(i => reference.GetValueOrDefault(i)).ToArray(), strong.Select(i => rates[i]).ToArray()):F4}");
        var resp = strong.Where(i => reference.GetValueOrDefault(i) < 190 && rates[i] < 190 && reference.GetValueOrDefault(i) > 20).ToArray();
        Console.WriteLine($"   downstream gain (sum C# / sum Brian2 over 20..190 Hz neurons) = {resp.Sum(i => rates[i]) / resp.Sum(i => reference.GetValueOrDefault(i)):F3}");
        Console.WriteLine($"   mean |diff| over >1 Hz neurons = {strong.Average(i => Math.Abs(reference.GetValueOrDefault(i) - rates[i])):F2} Hz");
        Console.WriteLine("   top responders (index: Brian2 Hz / C# Hz)");
        foreach (var i in reference.OrderByDescending(kv => kv.Value).Skip(21).Take(15).Select(kv => kv.Key))
            Console.WriteLine($"     {i,7}: {reference[i],7:F1} / {rates[i],7:F1}");
    }

    static double Pearson(double[] a, double[] b)
    {
        double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
        for (int i = 0; i < a.Length; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
        return sab / Math.Sqrt(saa * sbb);
    }

    // stim <hz> <ms> <spec> [<spec>...]   spec = type:A,B*[:L|R]  |  sub:<cell_sub_class>[:L|R]  |  group:<name>
    static void Stim(string[] args)
    {
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        float hz = float.Parse(args[0]);
        double ms = double.Parse(args[1]);
        using var brain = new LifBrain(con);
        foreach (var spec in args.Skip(2))
        {
            var idx = Resolve(cat, spec);
            foreach (var i in idx) brain.SetPoissonRate(i, hz);
            brain.RequestReset();
            brain.ClearStatistics();
            brain.RunMs(ms);
            double sec = ms / 1000;
            var top = Enumerable.Range(0, brain.N)
                .Where(i => (cat.SuperClass[i] == 6 || cat.SuperClass[i] == 9) && brain.SpikeCount[i] > 0)
                .OrderByDescending(i => brain.SpikeCount[i]).Take(16)
                .Select(i => $"{cat.TypeOf(i)}{(cat.Sides[i] == Side.Left ? "L" : cat.Sides[i] == Side.Right ? "R" : "")}={brain.SpikeCount[i] / sec:F0}");
            Console.WriteLine($"## {spec} @ {hz} Hz: {idx.Length} stimulated, {brain.TotalSpikes / sec:F0} spikes/s, {Enumerable.Range(0, brain.N).Count(i => brain.SpikeCount[i] > 0)} active");
            Console.WriteLine("   " + string.Join("  ", top));
            foreach (var i in idx) brain.SetPoissonRate(i, 0);
        }
    }

    // measure <hz> <ms> <stim spec>[+<stim spec>...] <type,type,...>: mean rate of each listed cell type per side
    static void Measure(string[] args)
    {
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        float hz = float.Parse(args[0]);
        double ms = double.Parse(args[1]);
        using var brain = new LifBrain(con);
        var types = args[args.Length - 1].Split(',');
        for (int a = 2; a < args.Length - 1; a++)
        {
            var idx = args[a].Split('+').SelectMany(s => Resolve(cat, s)).Distinct().ToArray();
            foreach (var i in idx) brain.SetPoissonRate(i, hz);
            brain.RequestReset();
            brain.ClearStatistics();
            brain.RunMs(ms);
            double sec = ms / 1000;
            var line = new List<string>();
            foreach (var t in types)
                foreach (var side in new[] { Side.Left, Side.Right, Side.Unknown })
                {
                    var o = cat.FindByType(side, t);
                    if (side == Side.Unknown && (cat.FindByType(Side.Left, t).Length > 0 || cat.FindByType(Side.Right, t).Length > 0)) continue;
                    if (o.Length == 0) continue;
                    line.Add($"{t}{(side == Side.Left ? "L" : side == Side.Right ? "R" : "")}={o.Average(i => brain.SpikeCount[i]) / sec:F0}");
                }
            Console.WriteLine($"## {args[a]} @ {hz} Hz ({idx.Length} neurons, {brain.TotalSpikes / sec:F0} spikes/s): " + string.Join("  ", line));
            foreach (var i in idx) brain.SetPoissonRate(i, 0);
        }
    }

    // persist <hz> <spec>: stimulate 500 ms, then 1500 ms without input; spikes/s in 250 ms bins
    static void Persist(string[] args)
    {
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        float hz = float.Parse(args[0]);
        var idx = args[1].Split('+').SelectMany(s => Resolve(cat, s)).Distinct().ToArray();
        using var brain = new LifBrain(con);
        foreach (var i in idx) brain.SetPoissonRate(i, hz);
        var bins = new List<string>();
        for (int b = 0; b < 8; b++)
        {
            if (b == 2) foreach (var i in idx) brain.SetPoissonRate(i, 0);
            long before = brain.TotalSpikes;
            brain.RunMs(250);
            bins.Add($"{(brain.TotalSpikes - before) * 4}/{brain.ActiveNeurons}");
        }
        Console.WriteLine($"{args[1]} @ {hz} Hz  spikes/s / active per 250 ms bin (input off after bin 2): " + string.Join("  ", bins));
    }

    // prof <threads> <hz> <spec>...: 2 s of simulation, time split between neuron kernels and spike scatter
    static void Prof(string[] args)
    {
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        int threads = int.Parse(args[0]);
        float hz = float.Parse(args[1]);
        using var brain = new LifBrain(con, threads: threads);
        foreach (var spec in args.Skip(2)) foreach (var i in Resolve(cat, spec)) brain.SetPoissonRate(i, hz);
        brain.RunMs(300);
        brain.NeuronMs = brain.ScatterMs = 0;
        long spikes0 = brain.TotalSpikes;
        var sw = Stopwatch.StartNew();
        brain.RunMs(2000);
        double wall = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"threads={threads} {string.Join(" ", args.Skip(2))} @ {hz}: {2000 / wall:F2}x real time, spikes/s={(brain.TotalSpikes - spikes0) / 2.0:F0}, active={brain.ActiveNeurons}, stepped={brain.SteppedNeurons}, neuron={brain.NeuronMs / wall * 100:F0}% scatter={brain.ScatterMs / wall * 100:F0}% other={(wall - brain.NeuronMs - brain.ScatterMs) / wall * 100:F0}%");
    }

    static int[] Resolve(NeuronCatalog cat, string spec)
    {
        var parts = spec.Split(':');
        var side = parts.Length > 2 ? (parts[2] == "L" ? Side.Left : parts[2] == "R" ? Side.Right : Side.Unknown) : Side.Unknown;
        return parts[0] switch
        {
            "type" => cat.FindByType(side, parts[1].Split(',')),
            "sub" => cat.FindBySubClass(side, parts[1]),
            "group" => cat.Group(parts[1]),
            "subn" => cat.FindBySubClass(parts.Length > 3 ? (parts[3] == "L" ? Side.Left : Side.Right) : Side.Unknown, parts[1]).Take(int.Parse(parts[2])).ToArray(),
            _ => throw new ArgumentException(spec),
        };
    }

    static void Mini(string[] args)
    {
        using var con = Connectome.Load(Data("mini/connectome_mini.bin"));
        int trials = args.Length > 0 ? int.Parse(args[0]) : 400;
        var reference = File.ReadAllLines(Data("mini/brian2_rates.txt")).Select(double.Parse).ToArray();
        using var brain = new LifBrain(con, new LifParameters(), threads: 1, seed: 99);
        for (int i = 0; i < 21; i++) brain.SetPoissonRate(i, 100);
        var total = new double[con.NeuronCount];
        for (int t = 0; t < trials; t++)
        {
            brain.RequestReset(); brain.ClearStatistics(); brain.RunMs(1000);
            for (int i = 0; i < con.NeuronCount; i++) total[i] += brain.SpikeCount[i];
        }
        Console.WriteLine("neuron  brian2   C#");
        for (int i = 0; i < con.NeuronCount; i++)
            if (i >= 19) Console.WriteLine($"{i,6} {reference[i],7:F1} {total[i] / trials,7:F1}");
        Console.WriteLine($"inputs mean {reference.Take(21).Average():F1} {total.Take(21).Average() / trials:F1}");
    }

    // ------------------------------------------------------------------ benchmark

    static void Bench()
    {
        var sw = Stopwatch.StartNew();
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        Console.WriteLine($"load {sw.ElapsedMilliseconds} ms: {con.NeuronCount} neurons, {con.ConnectionCount} connections");
        foreach (var threads in new[] { 1, 4, 8, 11 })
        {
            using var brain = new LifBrain(con, threads: threads);
            foreach (var i in cat.Group("paper_sugar_GRN_a")) brain.SetPoissonRate(i, 150);
            foreach (var i in cat.Group("paper_JON_CE")) brain.SetPoissonRate(i, 150);
            brain.RunMs(200);
            sw.Restart();
            brain.RunMs(2000);
            Console.WriteLine($"threads {threads,2}: {2.0 / sw.Elapsed.TotalSeconds:F2}x real time, {brain.TotalSpikes / brain.TimeMs * 1000:F0} spikes/s");
        }
    }

    // ------------------------------------------------------------------ probing sensorimotor pathways

    static void Probe(string[] args)
    {
        using var con = Connectome.Load(Streaming("connectome_783.bin"));
        var cat = NeuronCatalog.Load(Streaming("neurons_783.bin"), Streaming("groups_783.tsv"));
        float hz = args.Length > 0 ? float.Parse(args[0]) : 150f;
        double ms = args.Length > 1 ? double.Parse(args[1]) : 1000;
        using var brain = new LifBrain(con);

        var L = Side.Left; var R = Side.Right; var A = Side.Unknown;
        var inputs = new List<(string, int[])>
        {
            ("sugar GRN (paper)", cat.Group("paper_sugar_GRN_a").Concat(cat.Group("paper_sugar_GRN_b")).ToArray()),
            ("water GRN (paper)", cat.Group("paper_water_GRN")),
            ("bitter GRN (LB1a-d)", cat.FindByType(A, "LB1a,LB1d", "LB1b", "LB1c")),
            ("JO-CE L", cat.FindByType(L, "JO-C*", "JO-E*")),
            ("JO-CE R", cat.FindByType(R, "JO-C*", "JO-E*")),
            ("JO-F L", cat.FindByType(L, "JO-F*")),
            ("JON all (paper)", cat.Group("paper_JON_CE").Concat(cat.Group("paper_JON_F")).Concat(cat.Group("paper_JON_D_m")).ToArray()),
            ("LC4 L", cat.FindByType(L, "LC4")),
            ("LPLC2 L", cat.FindByType(L, "LPLC2")),
            ("LC4+LPLC2 both", cat.FindByType(A, "LC4", "LPLC2")),
            ("LPLC1 L", cat.FindByType(L, "LPLC1")),
            ("LC16 both", cat.FindByType(A, "LC16")),
            ("ORN DM1+DM4+VA2+DM2 L", cat.FindByType(L, "ORN_DM1", "ORN_DM4", "ORN_VA2", "ORN_DM2")),
            ("ORN V (CO2) L", cat.FindByType(L, "ORN_V")),
            ("ORN DA2 (geosmin) L", cat.FindByType(L, "ORN_DA2")),
            ("head bristle L", cat.FindBySubClass(L, "head bristle")),
            ("eye bristle L", cat.FindBySubClass(L, "eye bristle")),
            ("ocellar", cat.FindBySubClass(A, "ocellar")),
            ("taste peg mech", cat.FindBySubClass(A, "taste peg")),
        };

        var outputs = new List<(string, int[])>
        {
            ("GF L", cat.FindByType(L, "DNp01")), ("GF R", cat.FindByType(R, "DNp01")),
            ("DNp02", cat.FindByType(A, "DNp02")), ("DNp04", cat.FindByType(A, "DNp04")), ("DNp06", cat.FindByType(A, "DNp06")), ("DNp11", cat.FindByType(A, "DNp11")),
            ("MDN", cat.FindByType(A, "MDN")),
            ("P9 L", cat.FindByType(L, "DNp09")), ("P9 R", cat.FindByType(R, "DNp09")),
            ("DNa01 L", cat.FindByType(L, "DNa01")), ("DNa01 R", cat.FindByType(R, "DNa01")),
            ("DNa02 L", cat.FindByType(L, "DNa02")), ("DNa02 R", cat.FindByType(R, "DNa02")),
            ("DNb05", cat.FindByType(A, "DNb05")), ("DNb06", cat.FindByType(A, "DNb06")),
            ("aDN1 (DNg62)", cat.FindByType(A, "DNg62")), ("aDN2 (DNge078)", cat.FindByType(A, "DNge078")),
            ("DNg12", cat.FindByType(A, "DNg12*")),
            ("MN9 (CB0701)", cat.FindByType(A, "CB0701")),
            ("proboscis MNs", cat.FindBySubClass(A, "proboscis_motor_neuron")),
            ("haustellum MNs", cat.FindBySubClass(A, "haustellum_motor_neuron")),
            ("ingestion MNs", cat.FindBySubClass(A, "ingestion_motor_neuron")),
            ("antennal MNs", cat.FindBySubClass(A, "antennal_motor_neuron")),
            ("neck MNs L", cat.FindBySubClass(L, "neck_motor_neuron")), ("neck MNs R", cat.FindBySubClass(R, "neck_motor_neuron")),
        };
        if (args.Length > 2) inputs = inputs.Where(i => i.Item1.Contains(args[2], StringComparison.OrdinalIgnoreCase)).ToList();

        Console.WriteLine($"Probe: {hz} Hz Poisson activation for {ms} ms (FlyWire 783)");
        foreach (var (name, idx) in inputs)
        {
            foreach (var i in idx) brain.SetPoissonRate(i, hz);
            brain.RequestReset();
            brain.ClearStatistics();
            var sw = Stopwatch.StartNew();
            brain.RunMs(ms);
            double sec = ms / 1000;
            Console.WriteLine();
            Console.WriteLine($"## {name}: {idx.Length} neurons stimulated, {brain.TotalSpikes / sec:F0} spikes/s total, {Enumerable.Range(0, brain.N).Count(i => brain.SpikeCount[i] > 0)} active ({sw.ElapsedMilliseconds} ms)");
            var line = new List<string>();
            foreach (var (oname, oidx) in outputs)
            {
                if (oidx.Length == 0) continue;
                double r = oidx.Average(i => brain.SpikeCount[i]) / sec;
                if (r > 0.5) line.Add($"{oname}={r:F0}");
            }
            Console.WriteLine("   outputs: " + (line.Count > 0 ? string.Join("  ", line) : "(none)"));
            var top = Enumerable.Range(0, brain.N)
                .Where(i => (cat.SuperClass[i] == 6 || cat.SuperClass[i] == 9) && brain.SpikeCount[i] > 0)
                .OrderByDescending(i => brain.SpikeCount[i]).Take(12)
                .Select(i => $"{cat.TypeOf(i)}{(cat.Sides[i] == Side.Left ? "L" : cat.Sides[i] == Side.Right ? "R" : "")}={brain.SpikeCount[i] / sec:F0}");
            Console.WriteLine("   top DN/MN: " + string.Join("  ", top));
            foreach (var i in idx) brain.SetPoissonRate(i, 0);
        }
    }
}
