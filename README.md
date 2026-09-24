# EntityFramework.Plus.EFCore.MIT

An MIT build of [Entity Framework Plus](https://github.com/zzzprojects/EntityFramework-Plus) for EF Core 10
with **no proprietary dependencies**.

Upstream's `Z.EntityFramework.Plus.EFCore` package is MIT, but it depends on two proprietary ZZZ Projects
packages, `Z.EntityFramework.Extensions.EFCore` and `Z.Expressions.Eval`, so every application that
references it ships both DLLs whether it uses them or not. This build compiles the same upstream source
without them: its only dependency is `Microsoft.EntityFrameworkCore.Relational`.

This project is not affiliated with or endorsed by ZZZ Projects.

## Install

```
dotnet add package EntityFramework.Plus.EFCore.MIT
```

Coming from `Z.EntityFramework.Plus.EFCore`: change the package reference and nothing else — the namespaces
are unchanged (`using Z.EntityFramework.Plus;`). Then remove any `Z.EntityFramework.Extensions.EFCore` or
`Z.Expressions.Eval` references you only had because of Plus.

Targets `net10.0` / EF Core 10.

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
| Batch Update / Batch Delete (`query.Update(…)`, `query.Delete()`) | **no** | On EF Core these are implemented by `Z.EntityFramework.Extensions`. Use EF Core's `ExecuteUpdate` / `ExecuteDelete`. |
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
```

For everything else, upstream's documentation applies unchanged: https://entityframework-plus.net/

## Versions

The package version mirrors the upstream tag it is built from: `10.105.8.1` is upstream `10.105.8.1`
compiled for EF Core 10. As in upstream's own scheme, the first component is the EF Core major version.

## Differences from upstream

- `DeferredFirst().FutureValue()` on an empty result returns `default` instead of throwing.
  `FirstOrDefault`, `Count`, `Sum` and the rest behave as upstream.
- Queries wrapped by LinqKit's `AsExpandable()` are not unwrapped.
- As upstream: only Query Future is truly asynchronous. The async methods of Query Cache, Query Deferred
  (`ExecuteAsync`), IncludeFilter and IncludeOptimized run the synchronous code on a thread-pool thread.
- Like upstream, the library relies on EF Core internals. Every release is verified against upstream's own
  test suite (219 tests on EF Core) and a round-trip smoke test before publishing; treat a new EF Core
  major version as unsupported until a matching release exists.

## Building and maintaining

See [FORK.md](https://github.com/sovist/EntityFramework-Plus-MIT/blob/master-MIT/FORK.md).

## License

MIT. Entity Framework Plus is Copyright © ZZZ Projects Inc., MIT License. The fork's query compilation step
mirrors EF Core, Copyright © .NET Foundation and Contributors, MIT License. "Entity Framework Plus" and
"ZZZ Projects" are names of their respective owners.
