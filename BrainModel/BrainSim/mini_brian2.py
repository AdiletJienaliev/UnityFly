"""Tiny network run with the ORIGINAL model.py (Brian2) to check the C# port semantics exactly."""
import sys, time
import numpy as np, pandas as pd
sys.path.insert(0, "Drosophila_brain_model")
sys.path.insert(0, "tools")
from brian2 import prefs, Hz
prefs.codegen.target = sys.argv[1] if len(sys.argv) > 1 else "numpy"
import model
from export_brain import write_connectome

out = "BrainSim/data/mini/"
n_in = 21
rows = []
def syn(pre, post, w):
    rows.append((pre, post, abs(w), 1 if w > 0 else -1))
for i in range(n_in):
    syn(i, 21, 8); syn(i, 22, 20); syn(i, 23, 40); syn(i, 26, 3)
for t in (21, 22, 23):
    syn(t, 24, 60)
syn(24, 25, 150); syn(22, 25, -80); syn(25, 22, -30)
for i in range(n_in):
    syn(26, i, -5)
n = 27
ids = np.arange(n) + 1000
df_comp = pd.DataFrame({"Completed": True}, index=pd.Index(ids, name="root"))
df_con = pd.DataFrame(rows, columns=["Presynaptic_Index", "Postsynaptic_Index", "Connectivity", "Excitatory"]).sort_values(["Presynaptic_Index", "Postsynaptic_Index"])
df_con["Presynaptic_ID"] = ids[df_con.Presynaptic_Index]
df_con["Postsynaptic_ID"] = ids[df_con.Postsynaptic_Index]
df_con["Excitatory x Connectivity"] = df_con.Connectivity * df_con.Excitatory
df_comp.to_csv(out + "comp.csv")
df_con.to_parquet(out + "con.parquet")
write_connectome(df_comp, df_con.reset_index(drop=True), __import__("pathlib").Path(out + "connectome_mini.bin"))

params = dict(model.default_params)
params["r_poi"] = 100 * Hz
trials = int(sys.argv[2]) if len(sys.argv) > 2 else 40
counts = np.zeros(n)
t0 = time.time()
for t in range(trials):
    spk = model.run_trial(list(range(n_in)), [], [], out + "comp.csv", out + "con.parquet", params)
    for k, v in spk.items():
        counts[k] += len(v)
print(f"brian2 {prefs.codegen.target}: {trials} trials in {time.time()-t0:.1f}s")
rates = counts / trials
print("rates:", " ".join(f"{i}:{r:.1f}" for i, r in enumerate(rates)))
np.savetxt(out + f"brian2_rates.txt", rates)
