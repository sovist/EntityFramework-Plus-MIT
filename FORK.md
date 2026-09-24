# EntityFrameworkPlus.EFCore.MIT — fork notes

Design and maintenance notes for this fork. `README.md` is for consumers; this file is for whoever maintains the fork.

## What this fork is

An MIT build of [Z.EntityFramework.Plus.EFCore](https://github.com/zzzprojects/EntityFramework-Plus)
for EF Core 10 that does **not** depend on `Z.EntityFramework.Extensions.EFCore` or `Z.Expressions.Eval`.

Upstream Entity Framework Plus is MIT, but its EF Core package declares both of those proprietary
ZZZ Projects packages as runtime dependencies. Every application that references it ships both DLLs,
whether or not it calls anything in them. This fork removes the dependencies so the licence closure
of a consuming application is MIT + EF Core, nothing else.

This fork is not affiliated with or endorsed by ZZZ Projects. Upstream code is compiled unchanged; the
fork adds a shim, one project, two test projects and packaging.

## Ground rules

1. **Never edit upstream source files.** Everything the fork needs lives in files upstream does not
   have. This is what keeps upstream merges conflict-free. The one exception is
   `src/Z.EntityFramework.Plus.sln`, which gains the fork's three projects — 20 additive lines: the
   `Project` entries, `Debug|Any CPU` / `Release|Any CPU` mappings, and nesting of the two test projects
   under the `test` folder. If a merge conflicts there, take upstream's file and re-add the three
   projects from the IDE. `dotnet sln add` also works, but it invents `x64` and `x86` solution platforms
   and writes mappings for *every* project in the solution (200 lines); strip those before committing —
   upstream's solution is `Any CPU` only.
2. **`master` mirrors upstream exactly.** Never commit to it; only `git pull upstream master`.
3. **`master-MIT` is the release branch and the GitHub default branch.** Merge `master` into it.
   Never rebase it — published packages point at it.
4. **Work happens on short-lived branches off `master-MIT`** (`features/<slug>`), merged back with a
   pull request. `master-MIT` itself only receives merges: from `master` (upstream sync) or from a
   feature branch.
5. **Package version = the upstream tag the release is built from.**

## What ships

| Feature | In `.MIT`? | Notes |
|---|---|---|
| Query Future (`Future()`, `FutureValue()`, `DeferredXxx().FutureValue()`) | yes | Truly async (`ExecuteReaderAsync`). Its compile step is reimplemented in the shim — see below. |
| Query Deferred | yes | `ExecuteAsync()` is `Task.Run` over the sync call, as upstream. Prefer `.FutureValue()`. |
| Query Cache | yes | `FromCacheAsync` is `Task.Run`, as upstream. Its cache key uses the shim's compile step. |
| Query Filter | yes | |
| Query IncludeFilter (Core), Query IncludeOptimized | yes | Async paths are `Task.Run`, as upstream. |
| Audit | yes | |
| Query Extensions, Set Dynamic, Set Identity | yes | Set Identity needs a 15-line metadata shim (`ToZInfo`). |
| **Batch Update / Batch Delete** (`Update`, `Delete`, `UpdateAsync`, `DeleteAsync`) | **no** | On EF Core these are facades over `Z.EntityFramework.Extensions` (`UpdateFromQuery` / `DeleteFromQuery`, including the InMemory hook). Plus's own `BatchUpdate.cs` is commented out upstream. Use EF Core's `ExecuteUpdate` / `ExecuteDelete`. |
| **Query Hook** (`WithHint`, temporal-table and command-executing extensions) | **no** | Pure facade over `Z.EntityFramework.Extensions.PublicMethodForEFPlus`; no Plus implementation behind it. |
| Bulk operations (`BulkInsert`, `BulkSaveChanges`, `WhereBulkContains`, …) | **no** | Never were in Plus; they are `Z.EntityFramework.Extensions` features. |

Non-goals: no API changes, no fixes to upstream behaviour (report those upstream), no
reimplementation of batch or bulk operations.

## The shim

Upstream's EF Core shared code reaches the two paid assemblies in two very different ways, and the
shim (`src/Z.EntityFramework.Plus.EFCore10x.NET10/Shim/`) answers both. Everything is `internal`,
compiled into the Plus assembly, in the namespaces upstream code imports — `Shim/Extensions/` holds
`Z.EntityFramework.Extensions`, `Shim/EvalManager.cs` holds `Z.Expressions`, and
`Shim/EntityTypeInfoExtensions.cs` sits in `Z.EntityFramework.Plus` because its one caller imports
nothing else.

### Seven trivial members

Call-site counts are for the folders the EF Core project imports, at `10.105.8.1`.

| Member | Call sites | Shim behaviour |
|---|---|---|
| `EntityFrameworkManager.IsEntityFrameworkPlus` (`bool`, written) | 6 static constructors: `AuditManager`, `QueryCacheManager`, `QueryFilterManager`, `QueryFutureManager`, `QueryIncludeFilterManager`, `QueryIncludeOptimizedManager` | Plain static property. Never read by Plus. |
| `EntityFrameworkManager.IsCommunity` (`bool`, written) | `EntityFrameworkPlusManager.IsCommunity` setter | Plain static property. |
| `EvalManager.IsCommunity` (`bool`, written) | same setter | Plain static property. |
| `PublicMethods.GetQueryContextFactory(object) : object` | 8: `BaseQueryFuture`, `DbContext.MapReader`, `CreateCommand`, `GetDbContext` ×2, `GetInMemoryContext`, `IQueryable.GetCommand`, `IsInMemoryQueryContext` | `QueryCompiler._queryContextFactory` by reflection — the inline code upstream replaced with the EFE call in `48e460d`. |
| `PublicMethods.GetDatabase(object) : object` | 3: `DbContext.MapReader`, `CreateCommand`, `IQueryable.GetCommand` | `QueryCompiler._database`, same commit. |
| `PublicExtensions.GetInnerForLinqKit` (generic and non-generic) | 5: `GetDbContext` ×2, `GetInMemoryContext`, `IsInMemoryQueryContext` | Identity. Upstream unwraps LinqKit's `ExpandableQuery<T>`; the fork does not (see caveats). |
| `PublicExtensions.GetParameterName(IRelationalParameter) : string` | 1: `QueryFutureBatch` (renames parameters to `Z_{n}_…` when combining queries) | The logic upstream replaced in `e734b42`: for a `TypeMappedRelationalParameter` whose `Name` starts with `@_` and whose `Name.Substring(1)` differs from `InvariantName`, return `Name.Substring(1)`; otherwise `InvariantName`. |

`ToZInfo(IEntityType)` for Set Identity is an eighth trivial member: schema and table name via EF
Core's `GetSchema()` / `GetTableName()`.

### The compile step (`Shim/Extensions/QueryCommandExtensions.cs`)

On EF Core 3+, **Plus has no command-creation code of its own**: `CreateCommand.cs`'s EF Core 3+
branch is empty and `GetDbCommand<T>` is `#if EFCORE_2X`. Upstream moved the whole step into EFE in
February 2020 (`dbb2b69`). Two EFE extension methods do it, both on untyped `IQueryable`:

- `EFPlusCreateCommand(query, beforeCompile, out RelationalQueryContext, out object compiledQuery) : IRelationalCommand`
  — Query Future. Plus reads `CommandText` and `Parameters` off the command to build the combined
  SQL, indexes `queryContext.Parameters` by invariant name, and later calls `GetEnumerator()` on
  `compiledQuery` against its own fake reader, first nulling the enumerator's `_readerColumns`.
- `CreateCommand(query, out RelationalQueryContext) : IRelationalCommand` — Query Cache's key.

The shim reimplements them as EF Core's own `QueryCompiler.ExecuteCore` minus the execution, using
public API for every step: `EntityQueryProvider._queryCompiler` → `IQueryContextFactory.Create()` →
`QueryCompiler.ExtractParameters(expression, queryContext.Parameters, logger)` → compile → invoke the
compiled delegate to get the lazy `SingleQueryingEnumerable<T>` → its `_relationalCommandResolver`
delegate gives the `IRelationalCommandTemplate` (a `RelationalCommand`) for the parameter values.

**Why the compile step is not just `IDatabase.CompileQuery`.** A scalar query such as `Count()` has
`ResultCardinality.Single`; EF compiles it to a `Func<QueryContext, int>` — nothing to enumerate.
Wrapping that scalar in a one-element enumerable would be wrong under buffering: with a retrying
execution strategy (`EnableRetryOnFailure`) EF wraps readers in `BufferedDataReader`, whose
`Initialize` consumes *every* remaining result set, and Plus's fake reader delegates `NextResult()` to
the real one. Plus guards against this by nulling `_readerColumns` on the *outer* enumerator; a wrapped
inner scalar query would escape that guard and silently swallow the following futures. So the shim
runs EF's `QueryCompilationContext.CreateQueryExecutorExpression` pipeline itself (preprocess →
translate → postprocess → shaper-compile → runtime parameters → lambda → `InlineConstants` → compile),
mirrored line for line from `release/10.0`, with one insertion after translate:
`shaped.UpdateResultCardinality(ResultCardinality.Enumerable)`. `SELECT COUNT(*) …` becomes a genuine
one-row `SingleQueryingEnumerable<int>` with identical SQL, and Plus's existing handling applies.

Compiled executors are cached in a size-limited `MemoryCache` keyed by EF's own
`ICompiledQueryCacheKeyGenerator` key plus the element type. EF's `ICompiledQueryCache` cannot be
shared: the same key would hold a scalar delegate there and an enumerable one here.

### Reflected EF Core internals

| Field | Owner | Used by |
|---|---|---|
| `EntityQueryProvider._queryCompiler` | upstream already | everything |
| `QueryCompiler._queryContextFactory`, `QueryCompiler._database` | shim | `PublicMethods` |
| `QueryCompilationContext._runtimeParameters` | shim | `InsertRuntimeParameters` (global query filters that read context members) |
| `*QueryingEnumerable<T>._relationalCommandResolver` | shim | command text and parameters |
| `RelationalConnection._connection`, `Enumerator._readerColumns`, … | upstream already | Query Future's connection swap and reader handling |

Two `[Experimental]` members are used with `EF9100` suppressed, because EF's own `CreateQueryExecutor`
calls exactly these two: `ILiftableConstantProcessor.InlineConstants` and
`QueryCompilationContext.SupportsPrecompiledQuery`.

All fields exist in EF Core 10.0.x. A renamed field surfaces as a `TypeInitializationException`
wrapping `MissingFieldException` on the first use — not at compile time. The test projects are what
catch it on an EF Core bump.

### Contract check — after every upstream merge

The compiler is the real check: a new EFE *type* (as `SqlServerTableHintFlags` was) or extension
method escapes any grep. So: build the 10x project, then both test projects. The grep below is the
quick pre-read that tells you what changed before the compiler does:

```bash
git grep -n -E "EntityFrameworkManager\.|PublicMethods\.|PublicExtensions\.|EvalManager\.|PublicMethodForEFPlus|Z\.Expressions\.\w" -- src/shared \
  | grep -v -E ":\s*//" \
  | grep -v -E "Batch(Update|Delete)\.Shared|QueryHook|QueryFilterInterceptor|QueryIncludeFilter\.Shared"
```

(`QueryFilterInterceptor.Shared` and `QueryIncludeFilter.Shared` are not imported by the EF Core
project; the batch and hook folders are excluded by this fork.) A new member means: extend the shim if
it is trivially replaceable, otherwise stop and look at what upstream started depending on.

### Provenance

Nothing in `Shim/` derives from `Z.EntityFramework.Extensions` or `Z.Expressions.Eval`. Neither
assembly's source is available, and neither was decompiled for this fork. The only inspection of an
EFE binary was a string search confirming which package version first exports `GetParameterName`,
done to explain why upstream's own 9x project does not build. Each piece comes from:

| Shim piece | Origin |
|---|---|
| `EntityFrameworkManager`, `EvalManager` | Nothing to copy: flags the MIT callers write and nothing reads. |
| `PublicMethods.GetQueryContextFactory` / `GetDatabase` | Upstream Plus's own inline reflection, removed in `48e460d` (MIT, this repository). |
| `PublicExtensions.GetParameterName` | Upstream Plus's own logic, removed in `e734b42` (MIT, this repository). |
| `GetInnerForLinqKit`, `ToZInfo` | Identity function; two calls to EF Core public API. |
| `QueryCommandExtensions` | EF Core's `QueryCompiler.ExecuteCore` and `QueryCompilationContext.CreateQueryExecutorExpression`, read from `dotnet/efcore` `release/10.0` and mirrored (MIT, © .NET Foundation and Contributors; notice at the top of the file). |
| Member names, namespaces and signatures | Dictated by the MIT call sites in upstream's shared code, which this fork compiles unchanged. |

Recorded 2026-09-24, when the shim was written.

## Project layout

```
.github/workflows/
  build.yml                                         # build + test + pack on every push/PR
  publish.yml                                       # tag mit/<version> → nuget.org
src/
  Z.EntityFramework.Plus.sln                        # upstream's solution + the three fork projects (see ground rule 1)
  EntityFrameworkPlus.EFCore.MIT.slnf              # solution filter: only the fork's projects — open this one
  Z.EntityFramework.Plus.EFCore10x.NET10/           # the library — mirrors EFCore9x.NET8
    Z.EntityFramework.Plus.EFCore10x.NET10.csproj
    Shim/
      Extensions/                                   # namespace Z.EntityFramework.Extensions
        EntityFrameworkManager.cs
        PublicMethods.cs
        PublicExtensions.cs
        QueryCommandExtensions.cs                   # the compile step
      EvalManager.cs                                # namespace Z.Expressions
      EntityTypeInfoExtensions.cs                   # namespace Z.EntityFramework.Plus (ToZInfo)
  test/
    Z.Test.EntityFramework.Plus.EFCore100/          # upstream's shared test suite against this build — mirrors EFCore90
    EntityFrameworkPlus.EFCore.MIT.Smoke/          # fork-owned xunit smoke test
FORK.md                                             # this file
README.md                                           # the fork's readme; packed as the nuget.org readme too
```

The library csproj mirrors `Z.EntityFramework.Plus.EFCore9x.NET8.csproj` with these differences:

| | 9x (upstream) | 10x (`.MIT`) |
|---|---|---|
| `TargetFramework` | `net8.0` | `net10.0` |
| `AssemblyName` / `RootNamespace` | `Z.EntityFramework.Plus.EFCore` | `EntityFrameworkPlus.EFCore.MIT` / `Z.EntityFramework.Plus` (no `.resx` anywhere, so the change is safe) |
| `DefineConstants` | `… EFCORE_8X EFCORE_9X` | same + `EFCORE_10X` (upstream already guards with it) |
| `<Import>` list | 15 projitems | 12 — without `BatchDelete`, `BatchUpdate`, `QueryHook` |
| `PackageReference` | EF Relational 9.0.0, `Z.EntityFramework.Extensions.EFCore`, `Z.Expressions.Eval` | `Microsoft.EntityFrameworkCore.Relational` 10.0.0 only |
| Shim sources | — | `Shim\**\*.cs`, via the SDK's default globbing |
| `NoWarn` | — | `CS1591` (upstream XML docs are incomplete), `EF1001` (upstream reflects EF internals by design) |
| `SignAssembly` | `False` | `False` (no key needed) |

Package metadata (in the csproj): `PackageId` `EntityFrameworkPlus.EFCore.MIT` (two prefixes are reserved on nuget.org: `Z.EntityFramework.*` by ZZZ Projects and
`EntityFramework.*` by Microsoft — the first publish attempt, as `EntityFramework.Plus.EFCore.MIT`, was rejected
for the latter; a 404 from the package index only proves no package exists, reservation shows only on the
Upload page or in the push response); `Version` = upstream tag; `Authors` upstream + fork maintainer;
`Copyright` upstream's; `PackageLicenseExpression` `MIT` with upstream's `LICENSE` packed;
`PackageProjectUrl` / `RepositoryUrl` this repository; `Description` names what is and is not included.

The test project follows upstream's per-version layout exactly: the csproj is
`Z.Test.EntityFramework.Plus.EFCore90.csproj` with the target, package versions and project reference
changed (current MSTest instead of upstream's 1.1.18, which does not run on .NET 10). Fork-specific
additions sit at the end of the csproj: `Compile Remove` for the `BatchDelete` and `BatchUpdate` test
folders and four repro files that call EFE's batch API directly; a `Compile Include` of the EFCore90
project's own `QueryIncludeOptimized/` tests, compiled from where they live rather than copied (upstream
keeps a copy per test project, and the older copies have drifted); and a fork-owned `DeleteFromQuery` →
`ExecuteDelete()` helper so one IncludeOptimized-with-Future repro test keeps compiling. No `App.config`:
it is EF6-era configuration and the shared tests hard-code their connection string.

## Build, verify, publish

Prerequisites: .NET SDK 10.0.x; a local trusted SQL Server (`localhost`). Both test projects create
their own databases (`Z.Test.EntityFramework.Plus.EFCore`, `EFPlusMitSmoke`); the smoke test's
connection string can be overridden with `EFPLUS_MIT_SMOKE_CONNECTION`.

Upstream's other projects in the solution still need the paid packages — and upstream `master` does
not even build against the EFE version its 9x project pins (`GetParameterName` arrived in EFE after
`9.104.0.1`). So work through the solution filter `src/EntityFrameworkPlus.EFCore.MIT.slnf`, which
selects only the fork's three projects; open the `.slnf` in Rider or Visual Studio instead of the `.sln`.

```bash
dotnet build src/EntityFrameworkPlus.EFCore.MIT.slnf -c Release
dotnet test  src/EntityFrameworkPlus.EFCore.MIT.slnf -c Release
dotnet pack  src/Z.EntityFramework.Plus.EFCore10x.NET10 -c Release   # → src/Z.EntityFramework.Plus.EFCore10x.NET10/bin/Release/*.nupkg
```

What the two test projects prove, at `10.105.8.1` on EF Core 10.0.3:

- **Upstream suite** (219 tests): QueryFilter 79, QueryIncludeOptimized 44, QueryIncludeFilter 38,
  QueryCache 25, QueryFuture 7, QueryDeferred 2, upstream repro cases 24. Audit contributes nothing on
  EF Core in upstream's suite either (106 of its 126 test files are `#if EF5 || EF6`). Batch tests (76)
  are excluded with the feature.
- **Smoke** (8 tests): two entity futures + `DeferredCount` + `DeferredFirstOrDefault` in **one server
  round trip** (`SqlConnection.RetrieveStatistics`), with and without `EnableRetryOnFailure` — the
  buffering case is the one that justifies the compile step; an `Include` graph; two queries whose EF
  parameters have the same name but different values (exercises `GetParameterName`); a global query
  filter reading a context property (runtime parameters); the sync path; `FromCache` hitting the cache;
  and the InMemory provider (non-batched path).

Package check after `dotnet pack`: the nuspec's only dependency is `Microsoft.EntityFrameworkCore.Relational`,
`lib/net10.0/` holds `EntityFrameworkPlus.EFCore.MIT.dll` + `.xml`, `LICENSE` is at the root, and the
`<repository>` element records the upstream commit the build came from.

### CI

Two GitHub Actions workflows in `.github/workflows/`, both on `windows-latest` with SQL Server 2022
installed by `Potatoqualitee/mssqlsuite` (upstream's suite needs `localhost` + Windows authentication):

- **`build.yml`** — on pushes to `master-MIT` and `features/**`, and PRs to `master-MIT`: build and test
  through the solution filter, pack, upload the `.nupkg` as an artifact.
- **`publish.yml`** — on a pushed tag `mit/<version>` (or `workflow_dispatch` with a version): build,
  test, pack as `<version>`, push to nuget.org. The version must equal the csproj `<Version>` or be a
  prerelease of it (`10.105.8.1-preview.1`), which keeps the "package version = upstream tag" rule
  mechanical. A 409 fails the run: nuget.org uses it both for "version already exists" and for "package ID is reserved", and `--skip-duplicate` would report either as success. Re-running an already published version therefore fails with a clear message, which is the right outcome.

  Authentication is nuget.org **Trusted Publishing**: a policy on the package owner's nuget.org account
  bound to repository `sovist/EntityFramework-Plus-MIT`, workflow file `publish.yml`, scope "push new
  packages and package versions", package `EntityFrameworkPlus.EFCore.MIT`. The job requests an OIDC
  token (`id-token: write`) and `NuGet/login` exchanges it for a short-lived API key. No secret is stored
  in the repository; if the policy is deleted, publishing stops until it is recreated.

Publishing therefore is: merge to `master-MIT`, then `git tag mit/<version> && git push origin mit/<version>`.
Publish a `-preview.N` first when the release has not yet been exercised by a real consumer; nuget.org
versions are immutable and the four-component scheme leaves no room for a fork-only fix.

## Upstream sync runbook

```bash
git checkout master     && git pull upstream master && git push origin master
git checkout master-MIT && git merge master          # or a features/ branch off master-MIT, then PR
```

Then, in order: contract-check grep → read the upstream diff for the folders we import
(`git diff <previous-upstream-tag>..master -- src/shared/`) → build the library → run both test
projects (a new upstream test that calls EFE directly shows up as a compile error: add it to the
`Compile Remove` list) → update `<Version>` in the csproj to the new upstream tag → merge → tag
`mit/<version>` and let CI publish. If upstream ever adds its own `EFCore10x` project, diff it against
ours and prefer theirs.

## Versioning

- The package version mirrors the upstream tag. Consumers can read "which upstream is this" off the
  version alone.
- Upstream uses four components (`10.105.8.1`), which leaves no room for a fork-only patch number.
  Policy: a fork-only fix waits for the next upstream release. If one cannot wait, bump the fourth
  component and record the mapping in the release table below.
- Git tags are `mit/<version>`, so they never collide with upstream's bare `<version>` tags (those
  are fetched into local clones from `upstream` but never pushed to this repository).

| `.MIT` version | Upstream tag | Notes |
|---|---|---|
| — | — | no release yet |

## Known caveats

1. **Async.** Only Query Future is truly asynchronous. Query Deferred's `ExecuteAsync`, Query Cache's
   `FromCacheAsync` and the IncludeFilter/IncludeOptimized async paths are `Task.Run` over synchronous
   code — exactly as upstream. Not a fork regression, and not something the fork fixes.
2. **LinqKit.** `GetInnerForLinqKit` is the identity function, so a query wrapped by LinqKit's
   `AsExpandable()` is not unwrapped and `GetDbContext()` on it fails. If that is ever needed,
   implement the unwrap via reflection on `ExpandableQuery<T>.InnerQuery`.
3. **Precompiled queries / NativeAOT** are not supported: the compile step compiles at runtime, like
   upstream does.
4. **Reflection on EF Core internals** — as upstream, plus the fields listed above. Every EF Core
   bump needs both test projects.
5. **Not for** applications that reference `Z.EntityFramework.Extensions.EFCore` directly for its own
   features; they have the dependency anyway and should stay on upstream.

## Decisions

- **`AssemblyName` renamed** to `EntityFrameworkPlus.EFCore.MIT`: provenance visible in `bin/`, and
  no two DLLs with the same name if both packages ever meet. Namespaces stay `Z.EntityFramework.Plus`
  because upstream code is untouched, so consumer code changes only its `PackageReference`.
- **Shim lives in the library project** (`Shim/`, sub-folders mirror namespaces); promote to a shared
  projitems only if a second EF Core target is ever added.
- **Upstream's solution is the entry point**; the fork's three projects are added to it (ground rule 1).
- **Tests = upstream's own suite + a fork-owned smoke test.** Upstream's suite proves the features
  still behave; the smoke test proves the one thing upstream's suite cannot (single round trip, and
  scalar futures under buffering).

- **README replaced.** Upstream's `README.md` was marketing for ZZZ Projects' products (including the
  dependency this fork removes); ours describes the package and is also packed as `PackageReadmeFile`.
  If an upstream merge conflicts on it, take ours.

Open:

1. **nuget.org co-owner** for the package, so it does not depend on one account.
2. **Additional targets** (EF Core 8/9): not in the first release.

## History

- 2026-09-24 — forked from `zzzprojects/EntityFramework-Plus` at tag `10.105.8.1` (`0397735`);
  `master-MIT` branched from `master`; this document written before any code.
- 2026-09-24 — library project and shim built. Found that on EF Core 3+ the Query Future / Query Cache
  compile step lives entirely in EFE (`dbb2b69`, Feb 2020), and that Query Hook is a pure EFE facade;
  reimplemented the compile step on EF Core's public pipeline with the enumerable-cardinality override.
  Upstream suite 219/219 and smoke 8/8 green on SQL Server 2022 + InMemory; package packs with EF Core
  Relational as its only dependency.
- 2026-09-24 — first publish (`mit/10.105.8.1-preview.1`, as `EntityFramework.Plus.EFCore.MIT`) was
  rejected by nuget.org: `EntityFramework.*` is a Microsoft-reserved prefix. The run still went green
  because `--skip-duplicate` reports every 409 as "already exists". Renamed the package to
  `EntityFrameworkPlus.EFCore.MIT`, removed `--skip-duplicate`, deleted the tag; nothing was published.
