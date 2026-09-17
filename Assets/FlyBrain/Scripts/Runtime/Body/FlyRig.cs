using System.Collections.Generic;
using UnityEngine;

namespace FlyBrain
{
    public sealed class LegRig
    {
        public string Name;
        public int Side;          // -1 left, +1 right
        public int Pair;          // 0 front (prothoracic), 1 middle, 2 hind
        public IKChain Chain;     // coxa, femur, tibia -> ankle
        public IKChain Tarsus;    // aim: ankle -> claw
        public float TarsusLength;
        public Vector3 HomeLocal; // resting claw position in fly-root space (y = 0 is the ground)
        public Transform[] Visuals;
    }

    /// <summary>All bones of the procedural fly. Every movable part is posed exclusively through its IK chain.</summary>
    public sealed class FlyRig
    {
        public Transform Root;      // on the ground, yaw only
        public Transform Body;      // thorax center: height, pitch, roll
        public IKChain Head;        // neck -> head (aim)
        public IKChain Abdomen;     // 2 bones
        public IKChain Proboscis;   // rostrum, haustellum, labellum
        public IKChain[] LabellumLobes = new IKChain[2];
        public IKChain[] Antennae = new IKChain[2];   // 0 left, 1 right
        public Transform[] Aristae = new Transform[2];
        public IKChain[] Wings = new IKChain[2];
        public Transform[] WingBlur = new Transform[2]; // translucent stroke fan shown while the wings beat
        public IKChain[] Halteres = new IKChain[2];
        public LegRig[] Legs;       // L1, L2, L3, R1, R2, R3
        public Transform HeadVisual;
        public readonly List<Collider> Colliders = new List<Collider>();

        public const float StandHeight = 0.66f;

        public static int LegIndex(int side, int pair) => (side < 0 ? 0 : 3) + pair;

        public IEnumerable<IKChain> AllChains()
        {
            yield return Head;
            foreach (var a in Antennae) yield return a;
            yield return Proboscis;
            foreach (var l in LabellumLobes) yield return l;
            foreach (var leg in Legs) { yield return leg.Chain; yield return leg.Tarsus; }
            foreach (var w in Wings) yield return w;
            foreach (var h in Halteres) yield return h;
            yield return Abdomen;
        }
    }
}
