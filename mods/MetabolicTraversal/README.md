# Metabolic Traversal (port)

Port of *Metabolic Traversal* (workshop 3709852838; no source published, rebuilt from its description). The vanilla hunger drain
(`MedicalState.Hunger` falling with an empty stomach) is scaled by `floor + (peak - floor) x effort^exponent`, effort = horizontal speed /
`MaxSpeed` (current top speed, encumbrance included). Defaults floor 0.5, peak 12, exponent 2.5: still or slow halves the drain, a walk at
about a third of top speed is near vanilla, a sprint about 12x. Every 15th tick in the serial slow phase; player characters only by default.
The original's own curve and `config.json` keys are **Unknown**; these defaults are ours.
