<div align="center">

<img src="docs/banner.svg" alt="FlyBrain" width="100%">

# FlyBrain

**A living fruit fly in Unity, whose reflexes are computed by a full connectome model of the *Drosophila melanogaster* brain.**

[![Unity 6000.4](https://img.shields.io/badge/Unity-6000.4-000000?logo=unity&logoColor=white)](https://unity.com/releases/editor/archive)
[![C#](https://img.shields.io/badge/C%23-Burst%20jobs-239120?logo=csharp&logoColor=white)](Assets/FlyBrain/Scripts)
[![Connectome FlyWire v783](https://img.shields.io/badge/connectome-FlyWire%20v783-7c3aed)](https://flywire.ai/)
[![Neurons 138,639](https://img.shields.io/badge/neurons-138%2C639-0ea5e9)](#what-the-brain-decides-and-what-the-body-does)
[![Synapses 15,091,983](https://img.shields.io/badge/synapses-15%2C091%2C983-0ea5e9)](#what-the-brain-decides-and-what-the-body-does)
[![Validation r = 0.9997](https://img.shields.io/badge/vs%20Brian2-r%20%3D%200.9997-16a34a)](#porting-the-model-and-checking-it)

[**Русская версия →**](README.ru.md)

</div>

---

## What is this?

A single fly lives in a garden. Nobody scripted her behaviour: every reflex marked **BRAIN** in the interface is
decoded from the spikes of a leaky integrate-and-fire model of the *whole* fly brain —
138,639 neurons and 15,091,983 synapses of the FlyWire v783 connectome, ported to C# from
[philshiu/Drosophila_brain_model](https://github.com/philshiu/Drosophila_brain_model)
(Shiu et al., *"A Drosophila computational brain model reveals sensorimotor processing"*, **Nature** 2024).

She lives where a real fly would: on the ground of a garden under an apple tree, among fallen fermenting fruit.
She decides for herself what to do — she gets hungry, thirsty and sleepy, follows odour plumes to food, flies,
lands on any surface, grooms, lays eggs, and escapes from a jumping spider or a thrush overhead.
Every body movement — legs, proboscis, antennae, head, wings, halteres, abdomen — is solved with inverse kinematics.

> **In one sentence:** touch the fly's leg to a drop of sugar and the spike that reaches motor neuron MN9 is the
> same one a real connectome predicts — the proboscis extends because the model said so, not because of an `if`.

<!--
  📸 Screenshots go here. Drop PNG/GIF files into docs/ and uncomment:

  <div align="center">
    <img src="docs/screenshot-garden.png" width="49%">
    <img src="docs/screenshot-neurons.png" width="49%">
  </div>
-->

### Table of contents

- [Getting started](#getting-started)
- [Controls](#controls)
- [The natural environment](#the-natural-environment)
- [What the brain decides, and what the body does](#what-the-brain-decides-and-what-the-body-does)
- [How it is built](#how-it-is-built)
- [Porting the model and checking it](#porting-the-model-and-checking-it)
- [Limitations](#limitations)
- [Automated test run](#automated-test-run)
- [Regenerating the data](#regenerating-the-data)
- [Credits](#credits)

---

## Getting started

**Requirements:** Unity **6000.4**. The connectome binaries are already in the repository
(`Assets/StreamingAssets/FlyBrain/`), so nothing has to be downloaded or generated to press Play.

1. Open the project in Unity 6000.4. On the first import, `FlyBrainSetup` creates and opens the scene
   `Assets/Scenes/FlyBrainDemo.unity` by itself (or use the menu **FlyBrain → Open demo scene**).
2. Press **Play**. The world is generated procedurally in a couple of seconds; the brain loads in ~1–3 s.
3. To build a standalone executable: **FlyBrain → Build Windows player**.
4. For the classic lab setup (a Petri dish), use the *Environment* button in the UI or the launch flag `-flybrainLab`.

**Performance** (i5-12400F): 5–15× real time during ordinary fly life, 2–4× under heavy stimulation
(flight, dust, optogenetics). If the brain cannot keep up, the world slows down automatically so that the
body–brain loop stays honest.

## Controls

| Key | Action |
|---|---|
| RMB / wheel / MMB | orbit, zoom, pan the camera; **F** — follow the fly; **C** — cinematic camera |
| **W** | freedom on/off (with it off, only the brain's reflexes remain) |
| **[** / **]** / **K** | slower / faster day, skip an hour |
| 1 / 2 / 3 + LMB | drop of sugar / water / bitter on any surface |
| 4 + LMB | puff of air into the antennae |
| 5 + LMB on the head | dust on the eyes |
| 6 + LMB | looming object |
| 7 + LMB | small moving object |
| 8 / 9 + LMB | summon a jumping spider / a thrush fly-by |
| N / B / Tab | sensory and DN neurons / cloud of all neurons / optogenetics on any FlyWire cell type |
| M / Space / R / Del / H / F1 | sound / pause / reset brain / clear objects / hide UI / help |

## The natural environment

The scene is at a scale of 1 unit = 1 mm — roughly 3 × 3 m of garden with a distant backdrop.

- **Fallen fruit:** rotting and pecked apples, a burst plum, a rotten pear, cherries, a mouldy apple.
  Fermenting juice (sugar) with yeast seeps from the rot and wounds and slowly wells up again; the mould is bitter.
- **The smell of fermentation** is carried by the wind: every source has a diffuse cloud and a plume that widens
  downwind and breaks into filaments (intermittency), as it does in real air.
- **Water:** a puddle by a stone, and dew that condenses at dawn on leaves, stones and fruit and dries by noon.
- **Ground, leaves, stones, twigs, bark, grass:** everything except the blades of grass can be walked on —
  upside down included.
- **The day cycle** (24 minutes by default): sun, moon, stars, clouds, dawn fog, sun flecks under the canopy,
  temperature; gusty wind that weakens at night and near the ground (boundary layer).
- **Neighbours** (no brain model, simple rules): wild flies (a male courts and sings with his wing), an ant trail,
  a jumping spider *Salticus scenicus* (it stalks and pounces), and a thrush that occasionally passes over the fruit.
- **Sound** is synthesised: wing tone at ~220 Hz, wind, birds (the dawn chorus), crickets at night.

## What the brain decides, and what the body does

The interface labels every current decision: **BRAIN** — decoded from the spikes of the model;
**OUTSIDE THE MODEL** — produced by the motivation layer and the "virtual VNC".

### The brain (FlyWire 783 connectome)

| Situation in the world | Sensory neurons (Poisson) | Model response | Behaviour |
|---|---|---|---|
| legs/proboscis in juice | sugar GRNs (lists from the paper) | MN9 (CB0701), swallowing motor neurons | proboscis to the juice, swallowing, crop fills |
| gust of wind, air | JO-C/E, JO-F | aDN1 (DNg62), aDN2 (DNge078) | antennal grooming |
| dust, pollen, spores on the eyes | eye bristles | aDN1/aDN2 | eye cleaning; legs brush the specks off |
| a spider's leap, a diving bird | LC4 + LPLC2 | Giant Fiber (DNp01), DNp02/04/06/11 | escape take-off, freezing |
| surface looming during landing | LPLC2 (expansion in flight) | DNp103, DNg40, DNp70 | legs extended for landing* |
| flies and ants nearby | LC10a | DNa02/DNa04 on the object's side | turn towards the moving object |
| looming in flight | LC4/LPLC2 on one side | contralateral DNa01/DNa02/DNa04, DNb01 | banked turn in flight |
| frontal looming / LC16 optogenetics | LC16 | MDN | backward walking |

\* DNp07/DNp10, known from the literature as "landing" neurons, barely respond to LPLC2/LC4 in this model,
so landing is decoded from the descending neurons that LPLC2 actually excites; this is a hypothesis,
not a result from the paper.

### Body and motivation (not in the connectome)

- **Physiology** on a compressed biological clock: energy, crop, water, sleep pressure, fear, egg maturation.
  Hunger and satiety act on the brain as neuromodulation — they change the sensitivity of the sugar GRNs
  (in a hungry fly the MN9 threshold is reached by a leg touch, in a fed one it is not), so **feeding ends inside
  the brain model by itself**. Sleep raises the arousal threshold (it lowers the input from eyes and antennae).
- **Choosing what to do:** exploring, resting, looking for food, looking for water, drinking, local search around
  food that has been eaten (the fly's "dance"), grooming with the front or hind legs, laying eggs on fermenting
  fruit, rejecting a courting male, taking shelter at dusk, sleeping at night and during the siesta, escaping.
  Activity peaks at dawn and at dusk.
- **Navigation:** upwind along the odour plume, casting across the wind when the odour is lost, gradient following
  near the source, humidity gradient towards water, memory of places where she ate and drank, negative geotaxis.
- **Movement:** walking on any surface (normal, transitions onto walls, edge negotiation, never squeezing into gaps),
  tripod gait, voluntary and escape take-off, flight with wind drift, obstacle avoidance and landing-site choice,
  touching any surface.
- **Drinking** is driven by the body: in this model the water GRNs do not excite MN9.

## How it is built

```
BrainModel/
  Drosophila_brain_model/    clone of the model repository (Brian2) and the connectome data
  flywire_annotations/       FlyWire annotations (cell types, sides, coordinates)
  tools/export_brain.py      export parquet/csv → binaries for Unity
  BrainSim/                  .NET bench: verification of the port against Brian2, profiling, pathway probes
Assets/StreamingAssets/FlyBrain/   connectome_783.bin, neurons_783.bin, groups_783.tsv
Assets/FlyBrain/Scripts/
  Brain/Core/         C# port of the LIF model (Burst in Unity, the same code in BrainSim)
  Runtime/Brain/      BrainService: background simulation thread, syncing world time and brain time
  Runtime/Body/       procedural fly, IKChain (FABRIK with pole targets), FlyMotor (surfaces, gait, grooming, flight)
  Runtime/Interface/  FlySensors (world → sensory neuron rates), FlyNervousSystem (DN/MN → commands)
  Runtime/Mind/       FlyPhysiology (hunger, thirst, sleep, fear, eggs), FlyMind (motivation and navigation)
  Runtime/World/      NatureEnvironment (garden), LabEnvironment (Petri dish), DayCycle, WindField, OdorField,
                      WorldObjects (drops, stimuli), Wildlife (flies, ants, spider, thrush), FlyEgg
  Runtime/UI/         interface, camera, neuron cloud, sound synthesis
  Runtime/Util/       procedural meshes and textures for the nature scene
```

## Porting the model and checking it

The equations and parameters match `model.py`: `dv/dt = (v0 − v + g)/20 ms`, `dg/dt = −g/5 ms`,
threshold −45 mV, reset −52 mV, refractory period 2.2 ms, delay 1.8 ms, weight 0.275 mV per synapse,
Poisson activation as a 68.75 mV kick, time step 0.1 ms, exact integration. One important Brian2 detail:
the `(unless refractory)` flag makes writes to `v` and `g` conditional, so synaptic input arriving during the
refractory period (including the spike step itself) is discarded.

Comparison against the Brian2 results shipped in that repository (`results/example`, FlyWire 630, 30 × 1 s):

| Experiment | Pearson r | mean difference | C# speed |
|---|---|---|---|
| sugar GRNs at 200 Hz | 0.9997 | 0.72 Hz | ~24× real time |
| sugar GRNs at 100 Hz | 0.9996 | 0.50 Hz | ~36× real time |

Plus an exact match on a control network run through the original `model.py`.

Reproduce it yourself:

```bash
cd BrainModel/BrainSim
dotnet run -c Release -- validate          # also: mini, prof, stim, probe, persist
dotnet run -c Release -- measure 150 600 type:LPLC2:L DNp01,DNa02,DNa04   # probe any pathway
```

## Limitations

- **Olfaction is not fed into the brain.** In this LIF model, stimulating the olfactory receptors triggers
  self-sustaining activity of ~475k spikes/s. Every "fruit" glomerulus was tested: DM1, DM2, DM3, DM4, DL1, DC1,
  DP1m, DP1l, VA2, VA6, DA1 blow up at 2–10 Hz already; DM5, DM6, DC2, VM2, VM7, VM3, DA2, VC1, DL3, DL5, V are
  safe on their own, but together (VM2+VM7+DM5+DM6 at 20 Hz) they blow up as well. Odour-guided food search is
  therefore implemented outside the model (the preset "(!) ORN DM1" demonstrates the runaway).
- The gustatory neurons of the legs reach the brain through the VNC, which is not part of the connectome,
  so leg contact is fed to the same labellar GRNs at a lower rate.
- The DN → movement decoding (thresholds, speeds), the sensory rates and the physiology parameters were tuned by
  hand from probes in `BrainSim`. The other animals have no brain model.

## Automated test run

`FlyBrain.exe -flybrainAutotest <folder>` lives through a morning with the fly — hunger, dust, a spider attack,
a bird fly-by, thirst, dusk, night and dawn — and writes a log of neurons, physiology and behaviour plus screenshots.

- `-flybrainShots` — environment screenshots at different times of day only
- `-flybrainLab` — the classic experiments in a Petri dish

## Regenerating the data

```bash
BrainModel/.venv/Scripts/python BrainModel/tools/export_brain.py --version 783
BrainModel/.venv/Scripts/python BrainModel/tools/export_brain.py --version 630 --out BrainSim/data
```

## Credits

This project stands on other people's work:

- **Brain model** — [philshiu/Drosophila_brain_model](https://github.com/philshiu/Drosophila_brain_model);
  Shiu, Sterne, Spiller et al., *"A Drosophila computational brain model reveals sensorimotor processing"*,
  Nature 634, 210–219 (2024).
- **Connectome** — [FlyWire](https://flywire.ai/) v783 (Dorkenwald et al., Schlegel et al., Nature 2024)
  and the FlyWire neuron annotations.
- **Simulator** — the model is a port of [Brian2](https://briansimulator.org/) code to C#.

The connectome data and the original model come with their own licences (FlyWire data is CC BY-NC);
check them before using this material beyond personal research.
