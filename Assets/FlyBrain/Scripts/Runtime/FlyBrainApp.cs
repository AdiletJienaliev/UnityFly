using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlyBrain
{
    /// <summary>
    /// Entry point of the demo: builds the world (the orchard floor by default, or the laboratory), the IK fly,
    /// the whole-brain simulation, the other animals and the interface.
    /// Put this component on an empty GameObject in a scene (FlyBrain → Открыть демо-сцену does it for you).
    /// </summary>
    public sealed class FlyBrainApp : MonoBehaviour
    {
        /// <summary>Start in the petri dish laboratory instead of nature (also: command line -flybrainLab).</summary>
        public static bool UseLab;

        public FlyEnvironment World { get; private set; }
        public BrainService Brain { get; private set; }
        public FlyRig Rig { get; private set; }
        public FlyMotor Motor { get; private set; }
        public FlyNervousSystem Nervous { get; private set; }
        public FlyCamera Cam { get; private set; }
        public BrainActivityView BrainView { get; private set; }
        public InteractionTools Tools { get; private set; }
        public FlyBrainHUD Hud { get; private set; }
        public Wildlife Wildlife { get; private set; }
        public FlyAudio Audio { get; private set; }

        void Awake()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;
            Time.maximumDeltaTime = 0.05f;

            // the scene may contain a default camera / light: the demo brings its own
            foreach (var cam in FindObjectsByType<Camera>()) cam.gameObject.SetActive(false);
            foreach (var light in FindObjectsByType<Light>()) light.gameObject.SetActive(false);

            if (CommandLineValue("-flybrainLab") != null) UseLab = true;
            World = UseLab ? FlyEnvironment.Create<LabEnvironment>() : FlyEnvironment.Create<NatureEnvironment>();
            Physics.SyncTransforms();

            var pose = World.SpawnPose();
            Rig = FlyBuilder.Build(null, pose.position, 0f);
            Rig.Root.rotation = pose.rotation;
            Motor = Rig.Root.gameObject.AddComponent<FlyMotor>();
            Motor.BoundsCenter = World.Center;
            Motor.BoundsRadius = World.Radius;
            Motor.Init(Rig);
            Motor.PlaceAt(pose.position, pose.rotation);
            SeenObject.Attach(Rig.Body.gameObject, 1.1f, "муха").Owner = Rig.Root;

            Brain = gameObject.AddComponent<BrainService>();
            Brain.BeginLoad();

            Nervous = Rig.Root.gameObject.AddComponent<FlyNervousSystem>();
            Nervous.Init(Brain, Motor, Rig);

            World.SpawnDefaultFood();

            Cam = FlyCamera.Create(Rig.Body, Motor);
            BrainView = gameObject.AddComponent<BrainActivityView>();
            BrainView.Init(Brain);
            Tools = gameObject.AddComponent<InteractionTools>();
            Tools.Init(this);
            Hud = gameObject.AddComponent<FlyBrainHUD>();
            Hud.Init(this);
            Cam.IsOverUI = Hud.IsOverUI;

            if (World.IsNature)
            {
                Wildlife = gameObject.AddComponent<Wildlife>();
                Wildlife.Init(this);
            }
            Audio = gameObject.AddComponent<FlyAudio>();
            Audio.Init(this);

            string autotest = CommandLineValue("-flybrainAutotest");
            if (autotest != null) gameObject.AddComponent<FlyAutotest>().Init(this, autotest);
        }

        /// <summary>Reloads the scene in the other environment (the brain is loaded again).</summary>
        public void SwitchEnvironment(bool lab)
        {
            UseLab = lab;
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        public static string CommandLineValue(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : "";
            return null;
        }
    }
}
