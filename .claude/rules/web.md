---
paths:
  - "src/VroksNet.Web/**"
---

# VroksNet.Web

Blazor **WebAssembly** Admin UI (standalone, `Microsoft.NET.Sdk.BlazorWebAssembly`). Shared Presentation rules: `.claude/rules/presentation.md`.

## Rules

- **Never make Web a composition root.** It has no `Application`/`Infrastructure`/`Domain` references, and as WASM it can't reference server-side project types anyway.
- **Call the API only through typed `HttpClient`s** (e.g. `SpecificationApiClient`).
- **Take `HttpClient.BaseAddress` from `wwwroot/appsettings.{Environment}.json` (`ApiService:BaseAddress`).** Don't use Aspire's `https+http://` service-discovery scheme. It only resolves server-side and does nothing in browser-executed code.
- **If you change the dev `ApiService:BaseAddress` port (`7352` in `wwwroot/appsettings.Development.json`), change the pinned port in `AppHost.cs` too.** See `.claude/rules/apphost.md`.
- **Never list connection types in Web.** A connection's `ServiceType` is a plain string; the type list, value labels/placeholders and which connections fit an operation or a Listen come from `SystemApiClient.GetConnectionTypesAsync()` (`GET /api/system/connection-types`), so a type the server adds shows up without a Web change. Broker options are the same: `Shared/BrokerOptionsEditor` renders the inputs the selected type's adapter declares (`ConnectionTypeInfo.Options`), and forms send `brokerOptions`, never the deprecated `exchange`. Which connections fit a spec's servers comes from the server too (`SpecificationDetails.ConnectionTypes`): `Shared/SpecConnectionHint` puts those first and says when none fits.
- **Give inputs real `<label for>`/`id` pairs and ARIA roles.** E2E tests locate elements by role/label. See `.claude/rules/e2e-tests.md`.

## Hosting

- **Dev:** runs as its own process under AppHost (hot reload) and calls ApiService's fixed dev URL, with CORS enabled on ApiService for it.
- **Production:** build output only. ApiService serves it from the same origin, so no CORS is needed.
- **Environment name is build-time only in .NET 10** (MSBuild `WasmApplicationEnvironmentName`). Setting `ASPNETCORE_ENVIRONMENT` on the dev-server process has no runtime effect.
