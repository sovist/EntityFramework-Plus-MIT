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
fork adds a shim, a Batch Update / Batch Delete implementation on EF Core, one project, two test projects
and packaging.

## Ground rules

1. **Never edit upstream source files.** Everything the fork needs lives in files upstream does not
   have. This is what keeps upstream merges conflict-free. The one exception is
   `src/Z.EntityFramework.Plus.sln`, which gains the fork's projects — about 30 additive lines: `Project`
   entries and `Debug|Any CPU` / `Release|Any CPU` mappings for the three csproj, `.shproj` entries for the
   two shared projects, nesting under the `shared` and `test` folders, and the `SharedMSBuildProjectFiles`
   lines that tie each `.projitems` to the projects importing it. If a merge conflicts there, take
   upstream's file and re-add the projects from the IDE. `dotnet sln add` also works, but it invents `x64` and `x86` solution platforms
   and writes mappings for *every* project in the solution (200 lines); strip those before committing —
   upstream's solution is `Any CPU` only.
2. **`master` — read-only, Sync fork only.** It mirrors upstream exactly.
3. **`master-MIT` is the release branch and the GitHub default branch.** Merge `master` into it.
   Never rebase it — published packages point at it.
4. **`master-MIT` — no direct writes, pull requests only.** Work happens on short-lived branches off it
   (`features/<slug>`); it receives merges from `master` (upstream sync) or from a feature branch,
   through a pull request either way.
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
| Batch Update / Batch Delete (`Update`, `Delete`, `UpdateAsync`, `DeleteAsync`) | yes | Upstream's are facades over `Z.EntityFramework.Extensions` (`UpdateFromQuery` / `DeleteFromQuery`, including the InMemory hook); Plus's own `BatchUpdate.cs` is commented out upstream. The fork reimplements the four methods on EF Core's `ExecuteUpdate` / `ExecuteDelete` with upstream's signatures — see [Batch Update / Batch Delete](#batch-update--batch-delete-batch). Object-initializer factory only; no `BatchUpdate` / `BatchDelete` options. |
| **Query Hook** (`WithHint`, temporal-table and command-executing extensions) | **no** | Pure facade over `Z.EntityFramework.Extensions.PublicMethodForEFPlus`; no Plus implementation behind it. |
| Bulk operations (`BulkInsert`, `BulkSaveChanges`, `WhereBulkContains`, …) | **no** | Never were in Plus; they are `Z.EntityFramework.Extensions` features. |

Non-goals: no API changes, no fixes to upstream behaviour (report those upstream), no
reimplementation of Query Hook or bulk operations.

## The shim

Upstream's EF Core shared code reaches the two paid assemblies in two very different ways, and the
shim (`src/shared/Z.EF.Plus.MIT.Shared/Shim/`) answers both. Everything is `internal` —
except `EntityFrameworkManager`, public because its `ContextFactory` is a hook consumers set (see the
Batch section) — compiled into the Plus assembly, in the namespaces upstream code imports — `Shim/Extensions/` holds
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

`EntityFrameworkManager.ContextFactory` stands in for nothing upstream calls: it is the fork's InMemory
hook for Batch Update / Delete, kept under EF Extensions' name and signature so a consumer's existing
wiring survives the switch.

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
project; the batch folders are replaced by the fork's `Batch/`, and the hook folder is excluded.) A new
member means: extend the shim if it is trivially replaceable, otherwise stop and look at what upstream
started depending on.

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

## Batch Update / Batch Delete (`Batch/`)

On EF Core 3+ upstream's `Update` / `Delete` extension methods are one-liners over EFE's
`UpdateFromQuery` / `DeleteFromQuery`; Plus's own `BatchUpdate.cs` is commented out. Dropping the feature
would have made every consumer rewrite its call sites against `ExecuteUpdate`'s `SetProperty` builder
— and, on the InMemory provider, pass the `DbContext` explicitly, because InMemory has no `ExecuteUpdate`
and the context behind an `IQueryable` is not public API. So the fork provides the four public methods
with upstream's exact signatures (`namespace Z.EntityFramework.Plus`, no context parameter). They live in
`Batch/`, not `Shim/`: this is fork-owned public API, not a stand-in for a paid member.

- **Relational providers.** `Update(factory)` becomes `ExecuteUpdate(setters => …)` with one
  `SetProperty` per `MemberAssignment` of the factory's object initializer; `Delete()` is
  `ExecuteDelete()`. A value that reads the row (`t => new T { Order = t.Order + 1 }`) goes through the
  `SetProperty(Expression, Expression)` overload; a row-independent value (a constant, a captured local)
  is evaluated once and goes through `SetProperty(Expression, TProperty)`, which EF parameterises. The
  split is not cosmetic: on EF Core 10.0.0–10.0.6 a constant assigned to a nullable member through the
  expression overload fails to translate ("No coercion operator is defined", dotnet/efcore#37974).
- **Where the context comes from.** Upstream's own internal `IsInMemoryQueryContext()` /
  `GetInMemoryContext()` (`_Core.Shared`), which read it off the query's `QueryContextFactory` through
  the shim's `GetQueryContextFactory`. That is what lets the signatures stay parameterless.
- **InMemory provider.** No `ExecuteUpdate` / `ExecuteDelete` — the provider throws rather than falling
  back — so the rows are read with `AsNoTracking()` and saved through a *second* context over the same
  database (`InMemoryContext`): each row is attached there alone (`Entry(row).State`, which unlike
  `Attach` / `Remove` does not walk navigations), the factory's members are applied and marked modified
  or the row is marked deleted, and that context saves. What happens to it then depends on who owns it: a
  context the fork built from options is disposed; one the factory returned is left to its owner, with
  the rows this call attached detached so it goes back as clean as it came. A container typically hands
  out the same instance for a whole scope, and the next batch call gets it again — disposing it broke a
  consumer whose cleanup job makes five batch calls per unit of work, the second of which found a disposed
  context. The query's own context is never touched, which is the statement contract in full — a statement changes rows, not objects: tracked
  instances keep their values, the unit of work's other pending changes stay pending, nothing new is
  tracked, and later queries see the new rows. Saving through the query's context cannot give all of
  that: with a save, every pending change in the unit of work goes to the store early (a consumer's
  history diff right after a batch call found nothing left to diff); without one, later reads miss the
  rows. The second context comes from `EntityFrameworkManager.ContextFactory` (`Func<DbContext, DbContext>`,
  the hook EF Extensions had, so a consumer's existing wiring keeps working) or, when that is unset or
  returns null, from constructing the context's own type with its own `IDbContextOptions` — the
  constructor an EF Core context has by convention. A context that needs more, one resolved from a
  container for instance, sets the hook.
- **Not provided.** The `ExpandoObject`, `IDictionary` and anonymous-object update factories, and the
  `BatchUpdate` / `BatchDelete` options builders (`BatchSize`, `BatchDelayInterval`, `Executing`,
  `InMemoryDbContextFactory`, `UseTableLock`). Those were EFE features with EFE semantics (`BatchSize`
  chunks the statement) that `ExecuteUpdate` cannot honour; upstream's tests for them are the
  `Compile Remove` list in the EFCore100 test project. A factory that is not an object initializer
  throws `ArgumentException`.
- **Translation limits are EF Core's.** Whatever `ExecuteUpdate` / `ExecuteDelete` refuse for a provider
  (`Skip`, some `Include` / `Select` shapes), these refuse too. EFE generated its own SQL and accepted more.

Nothing here reflects EF Core internals. The `UpdateSettersBuilder<T>.SetProperty` overloads are picked by
reflection over public API and cached per (entity type, member type, overload).

## Project layout

```
.github/workflows/
  build.yml                                         # build + test + pack on every push/PR
  publish.yml                                       # tag mit/<version> → nuget.org
src/
  Z.EntityFramework.Plus.sln                        # upstream's solution + the fork's projects (see ground rule 1)
  EntityFrameworkPlus.EFCore.MIT.slnf              # solution filter: only the fork's buildable projects — open this one
  Z.EntityFramework.Plus.EFCore10x.NET10/           # the library for EF Core 10 — mirrors EFCore9x.NET8; the csproj alone
    Z.EntityFramework.Plus.EFCore10x.NET10.csproj
  shared/Z.EF.Plus.MIT.Shared/                      # the fork's own code, imported by every EFCore1Nx csproj like upstream's feature folders
    Z.EF.Plus.MIT.Shared.projitems                  # the file list; .shproj beside it is for the IDE
    Shim/
      Extensions/                                   # namespace Z.EntityFramework.Extensions
        EntityFrameworkManager.cs                   # + ContextFactory, the InMemory hook (public)
        PublicMethods.cs
        PublicExtensions.cs
        QueryCommandExtensions.cs                   # the compile step
      EvalManager.cs                                # namespace Z.Expressions
      EntityTypeInfoExtensions.cs                   # namespace Z.EntityFramework.Plus (ToZInfo)
    Batch/                                          # Batch Update / Delete on ExecuteUpdate / ExecuteDelete, upstream's signatures
      BatchUpdateExtensions.cs                      # the ExecuteUpdate proxy: object initializer → SetProperty calls
      BatchUpdateExtensions.InMemory.cs             # InMemory fallback: read untracked, apply, save through the second context
      BatchDeleteExtensions.cs                      # the ExecuteDelete proxy
      BatchDeleteExtensions.InMemory.cs             # InMemory fallback: read untracked, mark deleted, save through the second context
      InMemoryContext.cs                            # the second context both fallbacks save through: ContextFactory, or the context's type from its options
  test/
    Z.Test.EntityFramework.Plus.EFCore100/          # upstream's shared test suite against this build — mirrors EFCore90
    EntityFrameworkPlus.EFCore.MIT.Smoke.Shared/    # fork-owned xunit + Shouldly smoke tests, one folder per feature; .projitems + .shproj
      ShouldExtensions.cs                           # ShouldBeInOrder: sequence assertions as "these elements, in this order"
      QueryFuture/                                  # round trips on SQL Server + InMemory: QueryFutureTests.SqlServer / .InMemory and their fixtures
      Batch/                                        # Batch Update / Delete on InMemory + SQLite: one test class per extension class, one part per method
    EntityFrameworkPlus.EFCore.MIT.Smoke.EFCore100/ # the smoke tests against the EF Core 10 build: target framework, package pins, project reference
FORK.md                                             # this file
README.md                                           # the fork's readme; packed as the nuget.org readme too
```

The library csproj mirrors `Z.EntityFramework.Plus.EFCore9x.NET8.csproj` with these differences:

| | 9x (upstream) | 10x (`.MIT`) |
|---|---|---|
| `TargetFramework` | `net8.0` | `net10.0` |
| `AssemblyName` / `RootNamespace` | `Z.EntityFramework.Plus.EFCore` | `EntityFrameworkPlus.EFCore.MIT` / `Z.EntityFramework.Plus` (no `.resx` anywhere, so the change is safe) |
| `DefineConstants` | `… EFCORE_8X EFCORE_9X` | same + `EFCORE_10X` (upstream already guards with it) |
| `<Import>` list | 15 projitems | 13 — upstream's without `BatchDelete` and `BatchUpdate` (replaced by the fork's `Batch/`) and `QueryHook`, plus `Z.EF.Plus.MIT.Shared` |
| `PackageReference` | EF Relational 9.0.0, `Z.EntityFramework.Extensions.EFCore`, `Z.Expressions.Eval` | `Microsoft.EntityFrameworkCore.Relational` 10.0.0 only |
| Fork sources | — | through the `Z.EF.Plus.MIT.Shared` import; the project folder holds only the csproj |
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
additions sit at the end of the csproj: `Compile Remove` for the 25 batch test files that pass a
`BatchUpdate` / `BatchDelete` options lambda (`BatchSize`, `BatchDelayInterval`, `Executing`,
`InMemoryDbContextFactory`, the `Skip` / `Take` visitor tests) and three repro files that call EFE
directly (`UpdateFromQuery`, `BulkInsert`) — every other upstream batch test runs against the fork's
implementation; a `Compile Include` of the EFCore90 project's own `QueryIncludeOptimized/` tests,
compiled from where they live rather than copied (upstream keeps a copy per test project, and the older
copies have drifted); and a fork-owned `DeleteFromQuery` → `ExecuteDelete()` helper so one
IncludeOptimized-with-Future repro test keeps compiling. No `App.config`: it is EF6-era configuration and
the shared tests hard-code their connection string.

The smoke tests follow the same split: the sources are `EntityFrameworkPlus.EFCore.MIT.Smoke.Shared`, a
`.projitems` like upstream's, and `EntityFrameworkPlus.EFCore.MIT.Smoke.EFCore100` is a csproj holding only
the target framework, the package pins and the project reference. An EF Core 11 target adds
`…Smoke.EFCore110` and nothing else. The SQL Server database name `EFPlusMitSmoke` is shared by every
version, so two versions must not run against one server at once; suffix it per version when the second
arrives.

## Build, verify, publish

Prerequisites: .NET SDK 10.0.x; a local trusted SQL Server (`localhost`). Both test projects create
their own databases (`Z.Test.EntityFramework.Plus.EFCore`, `EFPlusMitSmoke`); the smoke test's
connection string can be overridden with `EFPLUS_MIT_SMOKE_CONNECTION`. The smoke test's batch theories
run on InMemory and on SQLite in-memory, which need nothing installed.

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

- **Upstream suite** (270 tests): QueryFilter 79, QueryIncludeOptimized 44, QueryIncludeFilter 38,
  BatchUpdate 31, QueryCache 25, BatchDelete 18, QueryFuture 7, QueryDeferred 2, upstream repro cases 26.
  The 49 batch tests are upstream's own (`Value`, `WhereValue`, `Action`, `ActionAsync`, `PrimaryKey`,
  `SqlSchema`, `Transaction`, `Keyword`, one `Visitor` case each), on SQL Server, against the fork's
  `ExecuteUpdate` / `ExecuteDelete` implementation; the 25 files bound to EFE options are excluded.
  Audit contributes nothing on EF Core in upstream's suite either (106 of its 126 test files are
  `#if EF5 || EF6`).
- **Smoke** (47 tests). Query Future (8): two entity futures + `DeferredCount` + `DeferredFirstOrDefault`
  in **one server round trip** (`SqlConnection.RetrieveStatistics`), with and without
  `EnableRetryOnFailure` — the buffering case is the one that justifies the compile step; an `Include`
  graph; two queries whose EF parameters have the same name but different values (exercises
  `GetParameterName`); a global query filter reading a context property (runtime parameters); the sync
  path; `FromCache` hitting the cache; and the InMemory provider (non-batched path). Batch (39: 17
  theories × InMemory and SQLite, so the statement path and the fallback path answer the same
  assertions, plus five InMemory facts): assigned members set; a constant into a nullable member (the
  efcore#37974 case); a value reading its own row; rows reached through a join; zero matches; persisted
  without `SaveChanges` while loading nothing into the context; a tracked instance left unchanged when
  its row is updated or deleted; the unit of work's other pending changes left unsaved; the sync
  overloads; a non-initializer factory rejected; the `ContextFactory` hook being the context saved
  through; both ways the second context cannot be had — the hook returning the query's own context,
  and a context type the default cannot construct with no hook set; and a hook handing out the same
  context on every call, which two updates of the same rows and two deletes leave undisposed, usable
  and tracking nothing.

Line coverage of the fork-owned code (`Batch\`, `Shim\`), both suites merged, is 93%: 278 of 299 lines.
Uncovered: the two `IsCommunity` setters nothing calls, `PublicMethods.GetDatabase` and the `@_` branch of
`GetParameterName` (paths upstream no longer reaches on EF Core 10), the `FieldInfo` branches of the
update factory (no test entity maps a field), and `ToZInfo` for Set Identity, which neither suite
exercises. Collect it with `dotnet test … --collect "Code Coverage;Format=Cobertura"`; no extra package is
needed, `Microsoft.NET.Test.Sdk` carries the collector.

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

1. On GitHub, **Sync fork → Update branch** on `master` (or `gh repo sync sovist/EntityFramework-Plus-MIT --branch master`).
   A `git push origin master` is rejected; this is the only way `master` moves.
2. Locally:

   ```bash
   git fetch origin && git fetch upstream --tags                     # upstream's tags name the release
   git checkout master && git merge --ff-only origin/master          # local mirror catches up
   git checkout -b features/sync-<upstream-tag> master-MIT && git merge master
   ```

3. On that branch, in order: contract-check grep → read the upstream diff for the folders we import
   (`git diff <previous-upstream-tag>..master -- src/shared/`) → build the library → run both test
   projects (a new upstream test that calls EFE directly shows up as a compile error: add it to the
   `Compile Remove` list) → update `<Version>` in the csproj to the new upstream tag.
4. Pull request into `master-MIT`, merge commit (ground rule 4) → tag `mit/<version>` and let CI publish.

If upstream ever adds its own `EFCore10x` project, diff it against ours and prefer theirs.

## Versioning

- The package version mirrors the upstream tag. Consumers can read "which upstream is this" off the
  version alone.
- Upstream uses four components (`10.105.8.1`), which leaves no room for a fork-only patch number.
  Policy: a fork-only fix waits for the next upstream release. If one cannot wait, bump the fourth
  component and record the mapping in this section.
- Git tags are `mit/<version>`, so they never collide with upstream's bare `<version>` tags (those
  are fetched into local clones from `upstream` but never pushed to this repository).

The record of published versions is the `mit/*` tags here and the version history on nuget.org; each
tag points at the `master-MIT` commit the package was built from, and the History section below says
what changed. A fork-only bump of the fourth component, should one ever happen, gets a note in this
section with the upstream tag it maps to.

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
6. **Batch Update / Delete are EF Core translations**, not EFE's SQL generation: a query EFE accepted may
   not translate, the options builders are gone, and the InMemory fallback loads the matching rows (which
   is what InMemory is for). Details in the Batch section above.

## Decisions

- **`AssemblyName` renamed** to `EntityFrameworkPlus.EFCore.MIT`: provenance visible in `bin/`, and
  no two DLLs with the same name if both packages ever meet. Namespaces stay `Z.EntityFramework.Plus`
  because upstream code is untouched, so consumer code changes only its `PackageReference`.
- **Fork-owned code is a shared project**, `src/shared/Z.EF.Plus.MIT.Shared` (`Shim/` and `Batch/`),
  imported by each versioned csproj exactly like upstream's feature folders, so an EF Core 11 project is a
  csproj plus `#if EFCORE_11X` branches wherever EF's internals moved. It started inside the 10x project
  and was promoted once EF Core 11 was two months out: a rename-only change then, rather than a rename
  buried inside the port.
- **Upstream's solution is the entry point**; the fork's projects are added to it (ground rule 1).
- **Tests = upstream's own suite + a fork-owned smoke test.** Upstream's suite proves the features
  still behave; the smoke test proves the one thing upstream's suite cannot (single round trip, and
  scalar futures under buffering).

- **README replaced.** Upstream's `README.md` was marketing for ZZZ Projects' products (including the
  dependency this fork removes); ours describes the package and is also packed as `PackageReadmeFile`.
  If an upstream merge conflicts on it, take ours.
- **Batch Update / Delete reimplemented, not dropped.** The first release excluded them and pointed at
  `ExecuteUpdate` / `ExecuteDelete`. Migrating a real consumer showed what that costs: a wrapper
  extension method in every consumer *with a `DbContext` parameter*, because InMemory has no
  `ExecuteUpdate` and the context behind a query is not reachable through public API. Upstream's internal
  `GetInMemoryContext()` already solves that inside the library, so the four methods keep upstream's
  signatures and a migrating consumer changes only its `PackageReference`. Object-initializer factory
  only; the other factory forms and the options builders were EFE features with EFE semantics.
- **The InMemory fallback saves through a second context**, as EF Extensions did, not through the
  query's own. The first version saved through the query's context and restored its tracked instances
  afterwards; a consumer's test caught what that cannot restore — the unit of work's other pending
  changes, which a statement leaves alone and which the consumer diffed for history right after the
  batch call. `EntityFrameworkManager.ContextFactory` keeps EF Extensions' name and signature so the
  consumer's existing hook needs no change; the options-constructor default covers contexts that need
  nothing else.

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
- 2026-09-25 — Batch Update / Batch Delete reimplemented on `ExecuteUpdate` / `ExecuteDelete` with
  upstream's signatures (`Batch/`), after migrating a consumer showed that dropping them forces a
  context-taking wrapper into every consumer. Upstream's batch tests re-included except the 25 files bound
  to EFE options: upstream suite 270/270, smoke 38/38 (batch theories on InMemory and SQLite). Found
  dotnet/efcore#37974 on the way (constant into a nullable member fails to translate on EF Core
  10.0.0–10.0.6) and routed row-independent values through the `SetProperty` value overload.
- 2026-09-25 — InMemory fallback reworked to save through a second context, after a consumer test showed
  the shared-context version flushing the unit of work's pending changes: `EntityFrameworkManager.ContextFactory`
  (EF Extensions' hook, now public in the shim) or the context's own type from its options;
  `TrackedInstances` removed. Smoke 43/43, upstream 270/270.
- 2026-09-25 — `mit/10.105.8.1-preview.2` published from `master-MIT` after PR #3 (batch) and PR #4 (both
  `ContextFactory` failure paths covered). Fork-owned code promoted to shared projects,
  `Z.EF.Plus.MIT.Shared` for the library and `EntityFrameworkPlus.EFCore.MIT.Smoke.Shared` for the tests,
  with one smoke csproj per EF Core version (`…Smoke.EFCore100`). Package content unchanged.
- 2026-09-25 — A context returned by `ContextFactory` is no longer disposed by the fallback: it is its
  owner's, and a container hands out the same instance per scope, so a consumer's cleanup job making five
  batch calls per unit of work hit `ObjectDisposedException` on the second. The fallback now detaches the
  rows it attached instead, and disposes only a context it built itself. Smoke 45/45, upstream 270/270.
