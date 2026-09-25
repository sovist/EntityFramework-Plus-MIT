# EntityFrameworkPlus.EFCore.MIT

[![build](https://img.shields.io/github/actions/workflow/status/sovist/EntityFramework-Plus-MIT/build.yml?branch=master-MIT&style=flat-square&logo=github)](https://github.com/sovist/EntityFramework-Plus-MIT/actions/workflows/build.yml)
[![nuget](https://img.shields.io/nuget/vpre/EntityFrameworkPlus.EFCore.MIT?logo=nuget&style=flat-square)](https://www.nuget.org/packages/EntityFrameworkPlus.EFCore.MIT)
[![downloads](https://img.shields.io/nuget/dt/EntityFrameworkPlus.EFCore.MIT?logo=nuget&style=flat-square)](https://www.nuget.org/packages/EntityFrameworkPlus.EFCore.MIT)
[![license](https://img.shields.io/github/license/sovist/EntityFramework-Plus-MIT?style=flat-square)](https://github.com/sovist/EntityFramework-Plus-MIT/blob/master-MIT/LICENSE)

An MIT build of [Entity Framework Plus](https://github.com/zzzprojects/EntityFramework-Plus) for EF Core 10
with **no proprietary dependencies**.

Upstream's `Z.EntityFramework.Plus.EFCore` package is MIT, but it depends on two proprietary ZZZ Projects
packages, `Z.EntityFramework.Extensions.EFCore` and `Z.Expressions.Eval`, so every application that
references it ships both DLLs whether it uses them or not. This build compiles the same upstream source
without them, and reimplements Batch Update / Batch Delete on EF Core's own `ExecuteUpdate` / `ExecuteDelete`.
Its only dependency is `Microsoft.EntityFrameworkCore.Relational`.

This project is not affiliated with or endorsed by ZZZ Projects.

## Install

```
dotnet add package EntityFrameworkPlus.EFCore.MIT
```

Coming from `Z.EntityFramework.Plus.EFCore`: change the package reference and nothing else — the namespaces
are unchanged (`using Z.EntityFramework.Plus;`). Then remove any `Z.EntityFramework.Extensions.EFCore` or
`Z.Expressions.Eval` references you only had because of Plus.

Targets `net10.0` / EF Core 10, as versions `10.x`.
Targets `net11.0` / EF Core 11, as versions `11.x`.

## What is included

| Feature | Included | Notes |
|---|---|---|
| Query Future (`Future()`, `FutureValue()`, `DeferredCount().FutureValue()`, …) | yes | Several queries, one round trip. |
| Query Deferred | yes | |
| Query Cache | yes | |
| Query Filter | yes | |
| Query IncludeFilter, Query IncludeOptimized | yes | |
| Audit | yes | As upstream; upstream's EF Core test suite does not cover it. |
| Set Dynamic, Set Identity | yes | |
| Batch Update / Batch Delete (`query.Update(x => new Entity { … })`, `query.Delete()`, `UpdateAsync`, `DeleteAsync`) | yes | Same signatures as upstream, reimplemented on EF Core's `ExecuteUpdate` / `ExecuteDelete`. Object-initializer update factory only: not the `ExpandoObject`, dictionary or anonymous-object forms, and not the `BatchUpdate` / `BatchDelete` options (`BatchSize`, `BatchDelayInterval`, `Executing`). |
| Query Hook (`WithHint`, temporal tables, command interception) | **no** | Implemented by `Z.EntityFramework.Extensions`. |
| Bulk operations (`BulkInsert`, `BulkSaveChanges`, …) | **no** | Never part of Entity Framework Plus; they are `Z.EntityFramework.Extensions` features. |

## Usage

```csharp
using Z.EntityFramework.Plus;

var blogs = context.Blogs.OrderBy(b => b.Name).Future();
var posts = context.Posts.Where(p => p.Score > 3).Future();
var count = context.Blogs.DeferredCount().FutureValue();

var blogList = await blogs.ToListAsync();   // one round trip executes all three
var postList = await posts.ToListAsync();   // already loaded
var blogCount = count.Value;                // already loaded

// Batch Update / Delete: one statement, no rows loaded, no SaveChanges
var archived = await context.Posts.Where(p => p.Score < 0).UpdateAsync(p => new Post { Archived = true, Score = p.Score - 1 });
var deleted  = await context.Posts.Where(p => p.Archived && p.Score < -10).DeleteAsync();
```

For everything else, upstream's documentation applies unchanged: https://entityframework-plus.net/

## Versions

The package version is the upstream tag it is built from, so the version alone tells you which Entity Framework Plus release you are on.
As in upstream's own scheme, the first component is the EF Core major version.
A `-preview.N` suffix marks this build's own iterations on one upstream tag; the version without a
suffix follows once a real application has run on the last preview. Every published version has a
`mit/<version>` tag in this repository, and the package's version history on nuget.org lists what is available.

## Differences from upstream

- `DeferredFirst().FutureValue()` on an empty result returns `default` instead of throwing.
  `FirstOrDefault`, `Count`, `Sum` and the rest behave as upstream.
- Queries wrapped by LinqKit's `AsExpandable()` are not unwrapped.
- Batch Update / Batch Delete are translated by EF Core (`ExecuteUpdate` / `ExecuteDelete`), so what a
  query may contain is what EF Core translates for your provider, not what `Z.EntityFramework.Extensions`
  accepted. The update factory has to be an object initializer; anything else throws `ArgumentException`.
  `UpdateAsync` and `DeleteAsync` are truly asynchronous.
- On the InMemory provider, which has no `ExecuteUpdate` / `ExecuteDelete`, Batch Update / Delete read the
  matching rows and save them through a second context over the same database, so your context is left as
  a statement would leave it: tracked instances keep their values, pending changes stay pending, nothing new
  is tracked. That second context is built from your context's type and options. If your context needs
  more than its options, set `Z.EntityFramework.Extensions.EntityFrameworkManager.ContextFactory`, the same
  hook EF Extensions used. The context you return stays yours: it is not disposed, and handing out the same
  instance on every call is fine.
- As upstream: only Query Future is truly asynchronous. The async methods of Query Cache, Query Deferred
  (`ExecuteAsync`), IncludeFilter and IncludeOptimized run the synchronous code on a thread-pool thread.
- Like upstream, the library relies on EF Core internals. Every release is verified against upstream's own
  test suite (270 tests on EF Core, including its Batch Update / Delete tests) and a smoke test (Query  Future round trips;
  Batch Update / Delete on InMemory and SQLite) before publishing;

## Building and maintaining

See [FORK.md](https://github.com/sovist/EntityFramework-Plus-MIT/blob/master-MIT/FORK.md).

## License

MIT. Entity Framework Plus is Copyright © ZZZ Projects Inc., MIT License. The fork's query compilation step
mirrors EF Core, Copyright © .NET Foundation and Contributors, MIT License. "Entity Framework Plus" and
"ZZZ Projects" are names of their respective owners.
