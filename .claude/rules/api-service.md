---
paths:
  - "src/VroksNet.ApiService/**"
  - "Dockerfile"
---

# VroksNet.ApiService

The only server process and the **only composition root**, in both dev and prod. It references `VroksNet.Application` and `VroksNet.Infrastructure`. Shared Presentation rules: `.claude/rules/presentation.md`.

## Rules

- **Keep it the single composition root.** No other project wires DI for the app.
- **Serve Web's WASM output with `UseStaticFiles` + `MapFallbackToFile("index.html")` in Production.** Do **not** use `MapStaticAssets()`/`UseBlazorFrameworkFiles()`. See the root `Dockerfile`.
- **One process, one Docker image.** The root `Dockerfile` is multi-stage: it publishes `VroksNet.Web` first, then copies its `wwwroot` into the final image next to ApiService's own publish output. Keep it that way.
- **Never reference `Microsoft.AspNetCore.OpenApi`.** Don't re-add it. It brings back a `NU1107` version conflict.
- **Keep the Development CORS predicate as `IsLoopback || Host.EndsWith(".localhost")`.** Don't narrow it to `IsLoopback` alone.
- **Add extra dev origins through config, not by widening the CORS policy.** Use the `Cors:AdditionalDevOrigins` key (env var `Cors__AdditionalDevOrigins=https://foo.example,https://bar.example`) for hosts-file custom hostnames, tunnel domains and the like.

## Gotchas

- **Why no `Microsoft.AspNetCore.OpenApi`:** even its latest version hard-pins `Microsoft.OpenApi < 3.0.0`, while spec parsing needs the 3.x line (`Microsoft.OpenApi.YamlReader` only exists for 3.x). If ApiService's own `/openapi/v1.json` self-doc is wanted later, use a mechanism that doesn't pull in `Microsoft.OpenApi` 2.x, such as Scalar or a hand-rolled document.
- **Why the CORS predicate allows any loopback origin:** `webfrontend`'s dev-server port is deliberately left unpinned, and a predicate (`SetIsOriginAllowed`) makes Web's random port irrelevant.
- **Why `Uri.IsLoopback` isn't enough:** Aspire's per-resource dev-domain hostnames (`<resource>-<appname>.dev.localhost`, e.g. `https://webfrontend-vroksnet.dev.localhost:7043`) are real origins the browser can end up on. `Uri.IsLoopback` returns `false` for any `*.localhost` subdomain (verified: `[Uri]::new("https://foo.localhost").IsLoopback` → `False`), even though RFC 6761 reserves the whole `.localhost` TLD for loopback.
- **EF tooling:** ApiService must reference `Microsoft.EntityFrameworkCore.Design` because it is the `--startup-project` for `dotnet ef`. See `.claude/rules/infrastructure.md`.
