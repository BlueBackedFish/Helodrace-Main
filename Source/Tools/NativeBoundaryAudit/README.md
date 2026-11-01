# Native boundary IL audit

This standalone .NET 10 tool reads a product PE and optional matching portable PDB without loading the product, UnityStubs, Waylith or game assemblies. It supports the stage 3 source/boundary review.

```powershell
dotnet run --project Source/Tools/NativeBoundaryAudit/NativeBoundaryAudit.csproj -- Assemblies/Helodrace.dll .codex-staging/migration17/native-boundary-audit.json Assemblies/Helodrace.pdb
```

The output's parent directory must exist. Omit the third argument to audit without source locations. The PDB must match the product's CodeView identity; a mismatched PDB is rejected before any report is written. It is still the caller's responsibility to use source files from that build.

The tool walks each method's complete IL instruction stream, resolving method specifications/member references, field/type operands and local/method/field signatures. It records calls/field uses/type tokens with UnityEngine targets or signatures and a geometry/Verse-vector-utility subset, plus candidate declarations. Offsets, caller metadata rows and file/line sequence points make each finding reviewable. These lists overlap and contain constructors, current callback/API/XML contracts, delegates and generic containers. They do not automatically label anything forbidden, native-ready or complete.

The tool does not infer ownership/control flow or follow reflection, strings, dynamic calls or external/native code. It does not validate rendering, input, resources or performance. Local/source-signature locations are method-entry locations when available, not declarations' precise source spans. Call locations are the nearest preceding visible sequence point.

Verified against the 82f97ed7 product: decoded 10,930 methods, 10,869 bodies and 453,587 instructions with zero build warnings/errors. Matching-PDB and no-PDB runs produced identical IL findings/offsets/counts. A known actual mismatched engine bridge PDB was rejected without output. The installed-charge offset's source multiply/add pair and absence of arithmetic calls in the converted tactical quartet were cross-checked. Remaining work is recorded in Docs/1.7 remaining native calculations.md; these checks are not a claim of exhaustive dynamic analysis.
