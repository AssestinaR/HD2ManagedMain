# Patch Operation Pipeline

Every user-invoked patch transformation follows this boundary:

```text
Manager request -> Core planner -> Adaptation operation workspace -> discrete jobs -> shared packager and validator
```

The planner chooses rebuilt, replaced, hidden, retained, and copied AssetKeys. The workspace owns operation-scoped staging, records output descriptors in `manifest.json`, and converts each job output to disk-backed `CanonicalPatchSessionEntry` payload paths. Jobs may allocate while executing, but must not retain large byte-array outputs after staging. The shared writer packages descriptors and copies untouched source ranges without fully extracting them.

Planning consumes cached asset inventories, cached source-unit preparation facts, TOC descriptors, and Game Data asset-index mappings. It must not decode full source or target Unit payloads, vertices, or GPU buffers to precompute mesh mappings. A discrete Unit job reads those payloads once, derives its mapping, rebuilds or minifies, stages its result, and releases its working model before the next job.

Game Data bundled archives are read by requested resource range. A batch tool must not reconstruct and retain one full archive byte array for every archive it touches; only compact package/TOC/chunk metadata may remain cached across Unit jobs.

Complete hidden Canonical Units may be persisted under `data/hidden-unit-cache` and reused only for a Unit with no replacement mappings. The cache key includes archive and AssetKey; its manifest is bound to the current Game Data index fingerprint and is discarded when that index is stale or changes. A partially replaced Unit must always read its current target shell and rebuild it; a complete hidden cache entry is never a partial-replacement baseline.

`HD2ModAdaptation` owns payload staging and binary patch operations. `HD2ModCore` owns planning, scheduling, progress, and failure policy. `HD2ModManager` initiates and presents operations only.

All new user-invoked patch tools must create one `IPatchOperationWorkspace` and use the shared writer. Private per-orchestrator staging helpers are prohibited. Validators must compare source/output Units one at a time and must not retain a full-operation Unit-model cache. The workspace is normally removed after package validation; resumable workspaces and manifests are a future explicit extension.

Current consumers: `CanonicalSameKeyReconstructionService` and `CanonicalCrossArmorOrchestrator`.
