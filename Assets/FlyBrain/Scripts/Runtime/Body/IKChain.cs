using UnityEngine;

namespace FlyBrain
{
    /// <summary>
    /// FABRIK inverse kinematics for a chain of bones. Every bone is a Transform whose local +Z axis
    /// points along the bone; the next bone sits at local (0, 0, length) of its parent bone.
    /// The solver moves the joint positions, rotates every inner joint into the plane of its pole (bend)
    /// direction and then writes back world rotations root to tip, so bone lengths never change.
    /// A chain with a single bone is an aim constraint.
    /// </summary>
    public sealed class IKChain
    {
        public readonly string Name;
        public readonly Transform[] Bones;
        public readonly float[] Lengths;
        readonly Vector3[] _p;
        readonly Vector3[] _poles;
        readonly float _totalLength;

        /// <summary>World-space position the chain tip should reach.</summary>
        public Vector3 Target;

        /// <summary>World-space "up" used for the roll of single-bone chains (defines the plane of flat parts like wings).</summary>
        public Vector3 RollUp = Vector3.up;

        public int Iterations = 10;
        public float Tolerance = 0.002f;

        /// <summary>Distance between the tip and the target after the last solve (mm).</summary>
        public float Error { get; private set; }

        public IKChain(string name, Transform[] bones, float[] lengths)
        {
            Name = name;
            Bones = bones;
            Lengths = lengths;
            _p = new Vector3[bones.Length + 1];
            _poles = new Vector3[bones.Length + 1];
            for (int i = 0; i < lengths.Length; i++) _totalLength += lengths[i];
            for (int i = 0; i < _poles.Length; i++) _poles[i] = Vector3.up;
        }

        public Vector3 Root => Bones[0].position;
        public Vector3 Tip => Bones[Bones.Length - 1].position + Bones[Bones.Length - 1].forward * Lengths[Lengths.Length - 1];
        public float Reach => _totalLength;

        /// <summary>Preferred bend direction (world space) of the joint at the start of bone <paramref name="joint"/> (1..n-1).</summary>
        public void SetPole(int joint, Vector3 worldDirection) => _poles[joint] = worldDirection;

        public void Solve()
        {
            int n = Bones.Length;
            _p[0] = Bones[0].position;
            for (int i = 1; i <= n; i++) _p[i] = _p[i - 1] + Bones[i - 1].forward * Lengths[i - 1];

            Vector3 root = _p[0];
            Vector3 toTarget = Target - root;
            float dist = toTarget.magnitude;

            if (n == 1 || dist >= _totalLength * 0.999f)
            {
                // out of reach (or a single bone): stretch towards the target
                Vector3 dir = dist > 1e-6f ? toTarget / dist : Bones[0].forward;
                for (int i = 1; i <= n; i++) _p[i] = _p[i - 1] + dir * Lengths[i - 1];
            }
            else
            {
                ApplyPoles();
                for (int it = 0; it < Iterations; it++)
                {
                    // backward pass: tip to root
                    _p[n] = Target;
                    for (int i = n - 1; i >= 0; i--)
                        _p[i] = _p[i + 1] + SafeDir(_p[i] - _p[i + 1], -Bones[i].forward) * Lengths[i];
                    // forward pass: root to tip
                    _p[0] = root;
                    for (int i = 1; i <= n; i++)
                        _p[i] = _p[i - 1] + SafeDir(_p[i] - _p[i - 1], Bones[i - 1].forward) * Lengths[i - 1];
                    if ((_p[n] - Target).sqrMagnitude < Tolerance * Tolerance) break;
                }
                ApplyPoles();
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 dir = _p[i + 1] - _p[i];
                if (dir.sqrMagnitude < 1e-12f) continue;
                Vector3 up = n == 1 ? RollUp : (i + 1 < n ? _poles[i + 1] : _poles[Mathf.Max(1, i)]);
                up = Vector3.ProjectOnPlane(up, dir);
                if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Bones[i].up, dir);
                if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.forward, dir);
                Bones[i].rotation = Quaternion.LookRotation(dir, up);
            }
            Error = (Tip - Target).magnitude;
        }

        /// <summary>Rotates each inner joint about the line through its neighbours so that it bends towards its pole.
        /// The neighbours (and therefore the tip) do not move.</summary>
        void ApplyPoles()
        {
            int n = Bones.Length;
            for (int j = 1; j < n; j++)
            {
                Vector3 a = _p[j - 1], c = _p[j + 1];
                Vector3 axis = c - a;
                float len = axis.magnitude;
                if (len < 1e-6f) continue;
                axis /= len;
                Vector3 pole = _poles[j] - axis * Vector3.Dot(_poles[j], axis);
                if (pole.sqrMagnitude < 1e-10f) continue;
                Vector3 v = _p[j] - a;
                Vector3 vProj = v - axis * Vector3.Dot(v, axis);
                if (vProj.sqrMagnitude < 1e-10f)
                {
                    // straight joint: nudge it towards the pole so the next iteration bends the right way
                    _p[j] += pole.normalized * Lengths[j - 1] * 0.05f;
                    continue;
                }
                float angle = Vector3.SignedAngle(vProj, pole, axis);
                _p[j] = a + Quaternion.AngleAxis(angle, axis) * v;
            }
        }

        static Vector3 SafeDir(Vector3 v, Vector3 fallback)
        {
            float m = v.magnitude;
            return m > 1e-7f ? v / m : fallback.normalized;
        }
    }
}
