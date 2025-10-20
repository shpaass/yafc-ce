# Bugs
This often generates negative resource densities (e.g. tree-01, big-rock, big-fulgora-rock) or incorrectly non-zero (e.g. vanilla coal on Pyanodon maps) or zero (e.g. iron-stromatolite) densities.
Some of this is definitely cause by not supporting the various voronoi noise functions.

To match 1.1, the desired result is approximately $2^{-20}$ times the "totalRichness" value given by
```
factorio --generate-map-preview= --map-preview-offset=3000,3000 --map-preview-size=4096 --report-quantities=...
```

# Refactoring
The tokenization, compiling, and parsing code needs to be merged with MathExpression.cs.

# Time to live
This should probably be abandoned if a modpack cache is introduced that can save computed data across Yafc executions.
Running Factorio is slower, especially if there are multiple planets and Factorio decides not to obey the `cache-prototype-data` option, but it is more straightforward.
