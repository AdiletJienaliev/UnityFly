"""
Export the FlyWire connectome used by philshiu/Drosophila_brain_model into compact
binary files that the Unity/C# port of the LIF model can load quickly.

Outputs (for --version 783, written to Assets/StreamingAssets/FlyBrain):
  connectome_783.bin   CSR connectivity (presynaptic rows), signed synapse counts
  neurons_783.bin      root ids, positions and FlyWire annotations for every model neuron
  groups_783.tsv       neuron groups used in the paper (sugar/water/bitter GRNs, JONs, MN9, aDN1 ...)

For --version 630 only the connectome is written (used to validate the port against
the Brian2 results stored in Drosophila_brain_model/results/example).

Usage:
  .venv/Scripts/python tools/export_brain.py --version 783
  .venv/Scripts/python tools/export_brain.py --version 630 --out BrainSim/data
"""
import argparse
import json
import re
import struct
from pathlib import Path

import numpy as np
import pandas as pd

ROOT = Path(__file__).resolve().parent.parent          # BrainModel/
REPO = ROOT / "Drosophila_brain_model"
ANNOT = ROOT / "flywire_annotations" / "Supplemental_file1_neuron_annotations.tsv"
UNITY_OUT = ROOT.parent / "Assets" / "StreamingAssets" / "FlyBrain"

FILES = {
    "783": ("Completeness_783.csv", "Connectivity_783.parquet"),
    "630": ("2023_03_23_completeness_630_final.csv", "2023_03_23_connectivity_630_final.parquet"),
}

SUPER_CLASSES = ["", "optic", "central", "sensory", "visual_projection", "ascending", "descending",
                 "sensory_ascending", "visual_centrifugal", "motor", "endocrine"]
SIDES = {"left": 1, "right": 2, "center": 3}
NTS = ["", "acetylcholine", "glutamate", "gaba", "dopamine", "serotonin", "octopamine"]


def write_connectome(df_comp, df_con, path):
    n = len(df_comp)
    pre = df_con["Presynaptic_Index"].to_numpy(np.int64)
    post = df_con["Postsynaptic_Index"].to_numpy(np.int32)
    w = df_con["Excitatory x Connectivity"].to_numpy(np.int64)
    assert np.all(np.diff(pre) >= 0), "connectivity must be sorted by presynaptic index"
    assert np.abs(w).max() < 32768
    row_ptr = np.zeros(n + 1, np.int32)
    np.add.at(row_ptr, pre + 1, 1)
    row_ptr = np.cumsum(row_ptr, dtype=np.int64).astype(np.int32)
    with open(path, "wb") as f:
        f.write(b"FLYC")
        f.write(struct.pack("<iii", 1, n, len(post)))
        f.write(row_ptr.tobytes())
        f.write(post.tobytes())
        f.write(w.astype(np.int16).tobytes())
    print(f"  {path.name}: {n} neurons, {len(post)} connections, {path.stat().st_size / 1e6:.1f} MB")


def write_neurons(ids, path):
    ann = pd.read_csv(ANNOT, sep="\t", low_memory=False).drop_duplicates("root_id").set_index("root_id")
    ann = ann.reindex(ids)
    n = len(ids)

    strings = [""]
    lookup = {"": 0}

    def sid(v):
        s = "" if pd.isna(v) else str(v)
        if s not in lookup:
            lookup[s] = len(strings)
            strings.append(s)
        return lookup[s]

    # FlyWire voxel size is 4 x 4 x 40 nm -> positions in micrometers
    pos = np.stack([ann.pos_x * 0.004, ann.pos_y * 0.004, ann.pos_z * 0.040], axis=1).astype(np.float32)
    pos = np.nan_to_num(pos, nan=np.float32("nan"))
    sup = np.array([SUPER_CLASSES.index(s) if isinstance(s, str) and s in SUPER_CLASSES else 0
                    for s in ann.super_class], np.uint8)
    side = np.array([SIDES.get(s, 0) for s in ann.side], np.uint8)
    nt = np.array([NTS.index(s) if isinstance(s, str) and s in NTS else 0 for s in ann.top_nt], np.uint8)
    cls = np.array([sid(v) for v in ann.cell_class], np.int32)
    sub = np.array([sid(v) for v in ann.cell_sub_class], np.int32)
    typ = np.array([sid(v) for v in ann.cell_type], np.int32)
    hb = np.array([sid(v) for v in ann.hemibrain_type], np.int32)

    blob = "\n".join(s.replace("\n", " ") for s in strings).encode("utf-8")
    with open(path, "wb") as f:
        f.write(b"FLYN")
        f.write(struct.pack("<iii", 1, n, len(strings)))
        f.write(np.asarray(ids, np.int64).tobytes())
        f.write(pos.tobytes())
        f.write(sup.tobytes())
        f.write(side.tobytes())
        f.write(nt.tobytes())
        for arr in (cls, sub, typ, hb):
            f.write(arr.tobytes())
        f.write(struct.pack("<i", len(blob)))
        f.write(blob)
    print(f"  {path.name}: {n} neurons, {len(strings)} strings, {path.stat().st_size / 1e6:.1f} MB")
    return ann


def paper_ids(name):
    """Neuron id lists exactly as used in figures.ipynb of the original repository."""
    nb = json.loads((REPO / "figures.ipynb").read_text(encoding="utf-8"))
    src = "".join("".join(c["source"]) for c in nb["cells"])
    m = re.search(re.escape(name) + r"\s*=\s*\[(.*?)\]", src, re.S)
    return [int(x) for x in re.findall(r"\d{18}", m.group(1))]


def write_groups(ids, ann, path):
    idx = {int(r): i for i, r in enumerate(ids)}

    def by_ids(root_ids):
        return sorted(idx[r] for r in root_ids if r in idx)

    groups = []

    def add(name, indices, desc):
        groups.append({"name": name, "description": desc, "indices": [int(i) for i in indices]})

    add("paper_sugar_GRN_a", by_ids(paper_ids("neu_sugar")), "Labellar sugar GRNs, figures.ipynb neu_sugar (Fig 1)")
    add("paper_sugar_GRN_b", by_ids(paper_ids("neu_sugar_left")), "Labellar sugar GRNs, other hemisphere (Fig S1)")
    add("paper_water_GRN", by_ids(paper_ids("neu_water")), "Labellar water GRNs (Fig 4)")
    add("paper_bitter_GRN", by_ids(paper_ids("neu_bitter")), "Labellar bitter GRNs (Fig 3)")
    add("paper_ir94e_GRN", by_ids(paper_ids("neu_ir94e")), "Labellar Ir94e GRNs (Fig 3)")
    add("paper_JON_CE", by_ids(paper_ids("neu_JON_CE")), "Johnston's organ neurons C/E (Fig 5)")
    add("paper_JON_F", by_ids(paper_ids("neu_JON_F")), "Johnston's organ neurons F (Fig 5)")
    add("paper_JON_D_m", by_ids(paper_ids("neu_JON_D_m")), "Johnston's organ neurons D/m (Fig 5)")
    add("paper_MN9", by_ids([720575940660219265]), "MN9, proboscis motor neuron (Fig 1)")
    add("paper_aDN1", by_ids([720575940616185531]), "aDN1, antennal grooming descending neuron (Fig 5)")
    add("paper_aDN2", by_ids([720575940629806974]), "aDN2, antennal grooming descending neuron (Fig S4)")
    add("paper_aBN1", by_ids([720575940630907434]), "aBN1, antennal grooming brain neuron (Fig 5)")

    lines = []
    for g in groups:
        print(f"  group {g['name']}: {len(g['indices'])}")
        lines.append("\t".join([g["name"], g["description"], ",".join(str(i) for i in g["indices"])]))
    path.write_text("# name\tdescription\tneuron indices\n" + "\n".join(lines) + "\n", encoding="utf-8")


def reference_rates(version, out):
    """Per-neuron rates from the Brian2 runs stored in the repository (630 data, 30 x 1 s trials)."""
    df_comp = pd.read_csv(REPO / FILES[version][0], index_col=0)
    flyid2i = {j: i for i, j in enumerate(df_comp.index)}
    for exp in ["sugarR", "sugarR_100Hz"]:
        p = REPO / "results" / "example" / f"{exp}.parquet"
        d = pd.read_parquet(p)
        r = d.groupby("flywire_id").size() / (d.trial.nunique() * 1.0)
        pd.DataFrame({"index": [flyid2i[i] for i in r.index], "flywire_id": r.index, "rate": r.values}) \
            .to_csv(out / f"brian2_{exp}_rates.csv", index=False)
    (out / "paper_sugar_630.txt").write_text("\n".join(str(flyid2i[i]) for i in paper_ids("neu_sugar")))
    print(f"  reference rates written to {out}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", default="783", choices=FILES.keys())
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    out = Path(args.out) if args.out else UNITY_OUT
    if not out.is_absolute():
        out = ROOT / out
    out.mkdir(parents=True, exist_ok=True)

    comp_file, con_file = FILES[args.version]
    print(f"Loading FlyWire v{args.version} ...")
    df_comp = pd.read_csv(REPO / comp_file, index_col=0)
    df_con = pd.read_parquet(REPO / con_file)
    ids = df_comp.index.to_numpy(np.int64)

    write_connectome(df_comp, df_con, out / f"connectome_{args.version}.bin")
    if args.version == "783":
        ann = write_neurons(ids, out / f"neurons_{args.version}.bin")
        write_groups(ids, ann, out / f"groups_{args.version}.tsv")
    else:
        reference_rates(args.version, out)


if __name__ == "__main__":
    main()
