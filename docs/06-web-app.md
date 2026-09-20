# 06 Offering the mapper as a web app

## In short

- One ASP.NET Core process serves page, bundle and API on one origin; no CORS, no proxy needed (see §1).
- Reference the same conversion project and ship the same modeler bundle as the plugin (see §1, §6, §10).
- Endpoints: `to-<format>`, `update`, `to-aml`, `validate`, `library`, `health` (see §2).
- Saving an opened AML file goes through `update` with the original document text; `to-aml` builds a new document and loses everything else (see §2.1).
- SVG is exported in the page from the running modeler, never on the server (see §2.3).
- Cap the request body, rate-limit only `/api`, partition by the forwarded client address (see §3).
- Metadata in one small ASCII JSON response header, findings as JSON; one error wrapper maps input errors to 400 and logs real 500s; load AML through a helper that rejects a null `CAEXFile` (see §4).
- Relative URLs in the page so the app survives being put under a path by a proxy (see §5).
- Stage the bundle into `wwwroot/` with a `Copy` target, not `Link`; `no-cache`, `nosniff`, MIME types for your extensions (see §6).
- Downloads via an attached anchor with a late revoke; serialize documents to a stream (see §7).
- Deploy: build bundle, `dotnet publish`, zip the contents, zip deploy, health check and browser test against the live URL (see §8, §9).
- `starter/dotnet/Efl.Web` is the minimal version of this chapter and has a browser test; add the hardening of §3 and §5 before a public deployment (see §11).

Read fully when: you put the mapper on a public server or behind a proxy.
Skim when: you only need a local page to try the mapper; start `starter/dotnet/Efl.Web` and read §2.1 and §11.

This chapter covers putting the mapper behind HTTP so that people without the AutomationML Editor
can open an AML document, see and edit the diagram in a browser, and download the result. Read it
after the mapper works in tests ([04-aml-mapping.md](04-aml-mapping.md)) and the modeler bundle
builds for the plugin ([02-modeler-diagram-js.md](02-modeler-diagram-js.md),
[05-editor-plugin.md](05-editor-plugin.md)). Both source projects run such a web app. AMLPetriNet
(`dotnet/PtMapper.Web`) is the recommended template: one ASP.NET Core process serves the page, the
modeler bundle and the API. fpb-aml-mapper is the older shape: a text-in, text-out converter page
served by a Node reverse proxy that forwards to a separate .NET API. Both are covered because the
older one shows what goes wrong when page and API live on different origins.

The only hard hosting requirement: a server that runs .NET (ASP.NET Core 8 in both projects) with
Aml.Engine. Aml.Engine is a .NET library, so the conversion cannot run in the browser. The source
projects happen to use a managed .NET app service and, for the FPB side, a Node/Express reverse
proxy on a separate web host; treat that as one example, not as a requirement.

## 1. Architecture: one process, page and API on the same origin

**Do:** create a `<Lang>Mapper.Web` project with `Microsoft.NET.Sdk.Web`, reference the same
`<Lang>Mapper.Conversion` project the plugin references, serve the page from `wwwroot/`, and map
the conversion endpoints under `/api/`. Keep the server stateless: the browser holds the opened
document and sends it back with every request that needs it.

**Why:** page and API on one origin need no CORS and no proxy, and the app runs on any host that
runs ASP.NET Core. The header comment of the PT app states the design:

```csharp
// Web front end for the PT mapper: the same conversion library the AML Editor
// plugin uses, behind a handful of endpoints, with the modeler served from the
// same origin so the page needs no proxy and no CORS.
//
// Page and API are one app, so it runs on any host that runs ASP.NET Core, and
// a reverse proxy can put it under a path of another site.
```
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, header comment at the top of the file)

The FPB variant splits the work: `server.js` serves `public/` and proxies `POST /api/:direction`
to the .NET API (fpb-aml-mapper: `server.js`, the `express.static` call for the static frontend
and the `app.post('/api/:direction', ...)` route), and the .NET API carries its own CORS policy
(fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`, `AddCors`). Two deployables, a rate limiter
and a body limit that live only in the proxy while the API is reachable on its own, and a header
whitelist in the proxy that must be kept in step with the API (section 5). The newer project avoided all of that.

The web project references the conversion library exactly like the plugin:

| Consumer | File | Symbol |
|---|---|---|
| Web app | AMLPetriNet: `dotnet/PtMapper.Web/PtMapper.Web.csproj` | `ProjectReference` to `PtMapper.Conversion.csproj` |
| Plugin | AMLPetriNet: `Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` | `ProjectReference` to `PtMapper.Conversion.csproj` |
| Web API (FPB) | fpb-aml-mapper: `dotnet/FpbMapper.Web/FpbMapper.Web.csproj` | `ProjectReference` to `FpbMapper.Conversion.csproj` |
| Plugin (FPB) | AMLFPB.js: `Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` | `ProjectReference` to `FpbMapper.Conversion.csproj` |

## 2. The endpoint set

A web app for your language needs these endpoints. Names follow the PT app; replace `pnml` with
your exchange format (FPB uses `json`).

| Endpoint | In | Out | PT reference |
|---|---|---|---|
| `POST /api/to-<format>` | AML document; `?hierarchy=` optional | exchange format; metadata header | `MapPost("/api/to-pnml", ...)` |
| `POST /api/update` | JSON `{aml, <format>, hierarchy}` | the *same* AML document, diagram written back | `MapPost("/api/update", ...)` |
| `POST /api/to-aml` | exchange format | a new AML document | `MapPost("/api/to-aml", ...)` |
| `POST /api/validate` | AML or exchange format | JSON findings | `MapPost("/api/validate", ...)` |
| `GET /api/library` | nothing | the domain library as a download | `MapGet("/api/library", ...)` |
| `GET /api/health` | nothing | status, library version, time, client address | `MapGet("/api/health", ...)` |

(all AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`)

### 2.1 `update` is the endpoint that matters

**Do:** when the user opened an AML file, save through `update`, not through `to-aml`. The page
keeps the original AML text and posts it together with the edited diagram; the server loads it,
runs the same `UpdateInPlace` the plugin uses, and returns the whole document.

**Why:** AML to exchange format to AML via `to-aml` produces a document that holds the diagram and
nothing else. Foreign attributes, links into other hierarchies, externally referenced libraries
and the document's own IDs are gone. The PT app says so explicitly (AMLPetriNet:
`dotnet/PtMapper.Web/Program.cs`, header comment on `/api/update`), and its browser test asserts
that the downloaded file is the opened one, not a fresh one (AMLPetriNet:
`web/tools/verify-webapp.mjs`, checks "the saved document still carries its libraries" and "the
saved document is the one that was opened"). The FPB web app has only `to-aml` and `to-json`
(fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`, `MapPost("/api/to-aml", ...)` and
`MapPost("/api/to-json", ...)`), so a round trip through it is lossy by construction.

```csharp
app.MapPost("/api/update", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var payload = await JsonSerializer.DeserializeAsync<UpdateRequest>(
        ctx.Request.Body, Json.Options) ?? throw new BadRequest("Empty request.");

    if (string.IsNullOrWhiteSpace(payload.Aml)) throw new BadRequest("No AML document was sent.");
    if (string.IsNullOrWhiteSpace(payload.Pnml)) throw new BadRequest("No net was sent.");

    var doc = CAEXDocument.LoadFromString(payload.Aml);
    var hierarchy = string.IsNullOrWhiteSpace(payload.Hierarchy)
        ? doc.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToPtNet.ContainsNet)
        : doc.CAEXFile.InstanceHierarchy[payload.Hierarchy];

    if (hierarchy == null) throw new BadRequest("The document no longer holds that hierarchy.");

    var summary = PtNetUpdater.UpdateInPlace(doc, hierarchy, Pnml.Read(payload.Pnml));
    ...
    await Text(ctx, "application/xml", Xml(doc));
})).RequireRateLimiting(ApiPolicy);
```
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `MapPost("/api/update", ...)`)

The JSON options set `PropertyNameCaseInsensitive = true` because the page sends camelCase and the
request record is PascalCase (AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `Json.Options`,
`UpdateRequest`).

The page side keeps `source = { aml, name, hierarchy }` after opening and chooses `update` when a
source exists, `to-aml` otherwise (AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html`, `source`,
`saveAml`). A file opened directly in the exchange format has no source document, and the status
line says that saving will create a new document (same file, `openText`).

### 2.2 Hierarchy selection

**Do:** let the reading endpoint take an optional hierarchy name; without one, pick the first
hierarchy that holds a diagram of your language and report which one was taken, plus the names of
all candidate hierarchies, so the page can offer a selector.

**Why:** real documents hold several instance hierarchies (plant, other views). Guessing silently
leads to saves into the wrong one. See AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`,
`MapPost("/api/to-pnml", ...)` (the `hierarchy` query lookup and the `hierarchies` field of
`X-Pt-Info`); the page fills a `<select>` only when there are two or more
(`dotnet/PtMapper.Web/wwwroot/index.html`, `fillHierarchies`) and restores the previous value if
switching fails (same file, the `change` listener on `hierarchy`).

### 2.3 SVG: export in the browser, not on the server

**Do:** produce the SVG in the page from the running modeler (`saveSVG` plus your post-processing),
with the same function the plugin bridge uses.

**Why:** the server has no renderer; only the modeler knows how shapes are drawn. The PT bundle
exports `exportSvg` (AMLPetriNet: `web/src/index.js`, the `export { exportSvg }` line), which pads
diagram-js' tight bounding box so strokes and labels are not clipped (AMLPetriNet: `web/src/svg.js`,
`exportSvg`). The plugin bridge answers an SVG request with the same function (AMLPetriNet:
`web/src/bridge.js`, `connectBridge`, the `requestSvg` message), and the web page calls it
directly (AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html`, `saveSvg`). There is no
`/api/svg`, and there should not be.

### 2.4 Library download

Serve the domain library built from code, not a copy that can drift: the PT app builds a
fresh document, calls `EnsureLibraries`, and sets `Content-Disposition: attachment` (AMLPetriNet:
`dotnet/PtMapper.Web/Program.cs`, `MapGet("/api/library", ...)`). The FPB page instead links static
`.aml` copies from `public/` (fpb-aml-mapper: `public/index.html`, the "Download FPD Library" and
"Download DI Library" links in the page header), which must be replaced by hand at every library
version bump.

## 3. Request size limits and rate limiting

**Do:** set Kestrel's `MaxRequestBodySize` to a value that fits your largest realistic document
with inlined libraries; limit only the `/api` endpoints; partition the limiter by the real client
address; return a JSON body and `Retry-After` on 429.

```csharp
const int MaxBodyBytes = 8 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxBodyBytes);
```
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `MaxBodyBytes`, `ConfigureKestrel`)

**Why, with evidence** (all in AMLPetriNet: `dotnet/PtMapper.Web/Program.cs` unless named otherwise):

- Without a cap a stray upload fills worker memory (comment above `MaxBodyBytes`).
- A body over the limit surfaces as `BadHttpRequestException` when the body is read; the PT error
  wrapper maps it to 400 with a readable message (`Guarded`).
- The limiter first partitioned on `RemoteIpAddress`, which behind a hosting front end is the front
  end's address. Everyone shared one bucket, and "fifteen page loads by anyone locked the site for
  the rest of the minute" (comment above `Configure<ForwardedHeadersOptions>`). The fix is
  `UseForwardedHeaders` with `X-Forwarded-For` and `X-Forwarded-Proto` (`ForwardedHeadersOptions`,
  `app.UseForwardedHeaders`), and the limit applies only to API routes via
  `.RequireRateLimiting(ApiPolicy)`, because static files "were burning permits on every load"
  (comment above `ApiPolicy`).
- `KnownNetworks` and `KnownProxies` are cleared because the front end's addresses are not fixed
  (`ForwardedHeadersOptions`, `KnownNetworks.Clear`, `KnownProxies.Clear`). That trusts
  `X-Forwarded-For` from any caller. It is acceptable only when the app is reachable exclusively
  through a front end that overwrites the header. If your app is directly reachable, list the proxy
  addresses instead.
- A reverse proxy on another host is one caller to the front end, so every visitor coming through
  it shares one bucket, and a client address header from the proxy cannot simply be trusted, or
  anyone picks their own bucket. The app therefore takes `X-Client-Address` as the partition only
  when the request also carries `X-Proxy-Key` equal to the `PT_PROXY_KEY` setting both sides hold,
  compared in constant time, and ignores the header when no key is configured (`ClientAddress`,
  comment above `proxyKey`; AMLPetriNet: `README.md`, section "Web application").
- The health endpoint echoes the forwarded client address, so the forwarded-header setup can be
  checked after every deploy (`MapGet("/api/health", ...)`, the `client` field). It shows
  `RemoteIpAddress`, not the proxy-supplied address `ClientAddress` partitions on for keyed
  requests.
- The rejection handler sets `Retry-After: 60` and writes `{ error }`, so the page can show a
  sentence instead of "429" (`AddRateLimiter`, `options.OnRejected`).

When a proxy fronts the app, give the proxy the same body limit as the app, and tie the two numbers
together with a comment. The FPB proxy counts the body itself and answers 413 before the app sees
the request: for its own .NET API above `MAX_BODY_BYTES` of 2 MB, and for the PT app it passes
through above `PT_MAX_BODY_BYTES` of 8 MB, commented as the PT app's own limit (fpb-aml-mapper:
`server.js`, `MAX_BODY_BYTES`, `PT_MAX_BODY_BYTES` and the body loops in the
`app.post('/api/:direction', ...)` route and the `app.use('/pt', ...)` middleware). A proxy limit
lower than the app's produces 413s that the app's logs never show.

## 4. Returning warnings, summaries and findings

A conversion has two outputs: the document, and what the user needs to know about it (elements
that cannot be drawn, IDs that had to be adjusted, nodes that were auto-arranged, update counts).

**Do:**

- For endpoints whose body is a file (AML, exchange format), put a *small* JSON object into one
  custom response header (`X-<Lang>-Info`). Cap the lists: the PT app sends `warningCount` plus at
  most five `warnings` (AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `MapPost("/api/to-pnml", ...)`,
  the `warningCount` and `warnings` fields), because "a header has to stay small".
- For validation, return a plain JSON body with one object per finding: `rule`, `severity`
  (lowercase string), `message`, `element` (the diagram ID, so the page can select it).
- Serialize header values with `System.Text.Json` defaults. The default encoder escapes non-ASCII
  characters as `\uXXXX`, which keeps the header ASCII; Kestrel refuses non-ASCII response header
  values unless an encoding selector is configured. Do not switch to a relaxed encoder for header
  values, since element names in your users' documents will contain umlauts.
- Report failures as `{ "error": "..." }` with 400 for the caller's input and 500 for everything
  else, and log the 500s.

```csharp
ctx.Response.Headers["X-Pt-Info"] = JsonSerializer.Serialize(new
{
    hierarchy = hierarchy.Name,
    hierarchies = doc.CAEXFile.InstanceHierarchy
        .Where(CaexToPtNet.ContainsNet).Select(h => h.Name).ToArray(),
    nets = nets.Count,
    places = net.Places.Count,
    transitions = net.Transitions.Count,
    arcs = net.Arcs.Count,
    adjustedIds = adjusted,
    arrangedNodes = arranged,
    warningCount = net.Warnings.Count,
    warnings = net.Warnings.Take(5).ToArray(),
});
```
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `MapPost("/api/to-pnml", ...)`, the `X-Pt-Info`
header, comments omitted)

`update` reports `added`, `updated`, `removed` and the updater's `notes` the same way
(`MapPost("/api/update", ...)`), and the page turns them into the status line
(`dotnet/PtMapper.Web/wwwroot/index.html`, `saveAml`). Show warnings about elements that are not on
the canvas explicitly: they remain in the document, and without a message the user believes they
were lost (same file, `notShown`).

`validate` accepts either format and sniffs it (`LooksLikeAml` checks for `CAEXFile`; Program.cs,
`MapPost("/api/validate", ...)`, `LooksLikeAml`). The page renders findings as a table whose rows
select and scroll to the element (index.html, `renderFindings`, `select`), and escapes every value
before putting it into `innerHTML` (index.html, `escape`); finding messages contain element names
from user files.

**Error wrapper.** The PT app funnels every endpoint through one `Guarded` function
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `Guarded`): a `BadRequest` exception type for
messages that are safe to show; `XmlException`, `InvalidOperationException`, `ArgumentException`,
`FormatException`, `JsonException` and `BadHttpRequestException` become 400 "This file could not
be read"; anything else is logged and returned as 500. `Fail` checks `Response.HasStarted` (same
file, `Fail`). Compare the FPB API, which maps every exception, including server bugs, to 400 and
logs nothing (fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`, the `catch (Exception ex)` blocks
of `MapPost("/api/to-aml", ...)` and `MapPost("/api/to-json", ...)`): a real defect then looks like
bad user input and leaves no trace on the server.

**Load AML through one helper.** The PT app calls `CAEXDocument.LoadFromString` directly
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, in `MapPost("/api/to-pnml", ...)`,
`MapPost("/api/update", ...)` and `MapPost("/api/validate", ...)`). For some non-AML input
Aml.Engine returns a document whose `CAEXFile` is null instead of throwing; the
NullReferenceException that follows is not in the wrapper's list of input errors, so a web API
reported the caller's broken file as a 500. The starter's `EflDocuments.Load` turns that into a
`FormatException` (`starter/dotnet/Efl.Conversion/EflDocuments.cs`, `Load`), every endpoint of its
web app loads through it (`starter/dotnet/Efl.Web/Program.cs`, `MapPost("/api/to-json", ...)`,
`MapPost("/api/update", ...)`, `MapPost("/api/validate", ...)`), and its browser test asserts that
a broken file is a 400 with a message (`starter/web/tools/verify-webapp.mjs`, check "a broken file
is a 400 with a message, not a 500").

**Metadata headers across origins.** A custom header is invisible to `fetch` on a cross-origin call
unless the API lists it in `Access-Control-Expose-Headers`; the FPB API does so with
`WithExposedHeaders("X-Conversion-Warnings")` (fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`,
`AddCors`, `WithExposedHeaders`). A proxy drops it unless it copies it: see section 5.

## 5. CORS, same origin, and running behind a reverse proxy

**Rule:** if the page and the API share an origin, configure no CORS at all. Add CORS only when a
page on a different origin must call the API directly.

The FPB API has a CORS policy that allows the project's domains and `localhost`
(fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`, `AddCors`, `SetIsOriginAllowed`), yet the FPB
page calls the relative path `/api/${direction}` on its own origin (fpb-aml-mapper:
`public/index.html`, `convert`), and the proxy calls the API server to server, where CORS does not
apply. The policy therefore protects nothing in the deployed setup and only matters for direct
browser calls. If you do write an origin predicate, use `Uri.TryCreate`: `new Uri(origin)` throws
on the literal origin `null` that sandboxed frames and `file:` pages send.

When a proxy puts the app under a path of another site, four things have to be handled. The FPB
proxy is the worked example: it serves the PT app under `/pt` next to its own pages (fpb-aml-mapper:
`server.js`, `PT_APP` and the `app.use('/pt', ...)` middleware), and both pages link to each other
(fpb-aml-mapper: `public/index.html`, the `nav` in the page header; AMLPetriNet:
`dotnet/PtMapper.Web/wwwroot/index.html`, `nav.languages`, shown only when the page is served under
a path).

1. **Relative URLs in the page.** An absolute `fetch('/api/...')` resolves against the root of the
   proxy host, not the app. The PT page uses `./api/...` throughout, with the reason in a comment
   (AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html`, the comment on `call`, and the requests
   in `openAml`, `saveAml` and `validate`), and loads its bundle as `./ptnjs.esm.js` (same file,
   the `import` in the module script). The library link is `./api/library` (same file, the link in
   the `<footer>`).
2. **Trailing slash redirect.** `/pt` without a slash makes the browser resolve `./ptnjs.esm.js`
   against the root. Redirect `/pt` to `/pt/` inside the prefix middleware, not as a separate
   route: Express treats `/pt` and `/pt/` as the same path without strict routing, and a second
   route loops (fpb-aml-mapper: `server.js`, the `redirect(301, '/pt/')` at the start of the
   `app.use('/pt', ...)` middleware, with that reason in its comment).
3. **Forwarded client address.** Pass the caller's address and protocol on, so the app's limiter
   partitions on the caller (section 3). A plain `x-forwarded-for` is not enough when a hosting
   front end sits between proxy and app: the front end puts the proxy's address last. The FPB
   proxy sends `x-forwarded-proto`, plus `x-client-address` with the shared `x-proxy-key` when
   `PT_PROXY_KEY` is set (fpb-aml-mapper: `server.js`, the `headers` built in the
   `app.use('/pt', ...)` middleware), and trusts one proxy hop itself so that its own limiter sees
   the visitor, not the local web server (same file, `app.set('trust proxy', 1)` and `rateLimit`).
   Its own API still gets only a `Content-Type` header, so the API sees the proxy's address for
   every caller (same file, the `fetch` call in `app.post('/api/:direction', ...)`); that API is
   rate limited in the proxy instead (`rateLimit`).
4. **Header whitelist.** A proxy copies only the response headers it names. For its own API the
   FPB proxy copies `X-Conversion-Warnings` and the content type, nothing else (fpb-aml-mapper:
   `server.js`, the `res.set` and `.type` calls in `app.post('/api/:direction', ...)`). Every new
   metadata header in the app must be added to such a list, or the page silently loses warnings.
   For a whole-app pass-through, copy `content-type`, `cache-control`, `content-disposition`, the
   app's info headers and `retry-after`, pass redirects through with `redirect: 'manual'` and a
   `location` rewritten onto the proxy's path, and answer 502 with `{ error }` when the app is down.
   The FPB proxy's `/pt` middleware does exactly that (same file, the header list with `x-pt-info`,
   the `location` rewrite and the `catch` block in `app.use('/pt', ...)`).

Pass a whole app through unchanged rather than rewriting parts of it: rewriting would break the
relative paths in its HTML (fpb-aml-mapper: `server.js`, comment above `app.use('/pt', ...)`). The
FPB API proxy, in contrast, whitelists the two known directions and rejects anything else
(`server.js`, `app.post('/api/:direction', ...)`); with a whole-app pass-through that list does not
need maintenance.

## 6. Static files: the page, the bundle, example files

**Do:**

1. Ship the *same* modeler bundle the plugin ships. Do not maintain a second build for the web.
2. Stage the bundle into `wwwroot/` with an MSBuild `Copy` target that runs before build and fails
   with a clear message when the bundle is missing. Gitignore the staged copies.
3. Register MIME types for your file extensions (`.aml` and your exchange format).
4. Send `Cache-Control: no-cache` for the page and the bundle, and `X-Content-Type-Options: nosniff`
   for everything.
5. Offer one example document that the page can load with one click.

```xml
<Target Name="StageWebAssets" BeforeTargets="BeforeBuild">
  <Error Condition="!Exists('$(PtnJsDistDir)\ptnjs.esm.js')"
         Text="The modeler bundle is missing. Run 'npm install &amp;&amp; npm run build' in web\." />

  <Copy SourceFiles="$(PtnJsDistDir)\ptnjs.esm.js;$(PtnJsDistDir)\ptnjs.css"
        DestinationFolder="$(MSBuildThisFileDirectory)wwwroot"
        SkipUnchangedFiles="true" />
  ...
</Target>
```
(AMLPetriNet: `dotnet/PtMapper.Web/PtMapper.Web.csproj`, target `StageWebAssets`)

**Why:**

- **Copy, not Link.** Linked content items end up in a publish but not in the content root that
  `dotnet run` serves, so the local page "would silently lose the modeler" (PtMapper.Web.csproj,
  comment above `StageWebAssets`). The plugin, which loads from its output folder, uses `Link`
  instead (AMLPetriNet: `Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj`, the `None`
  item with `Link="ptnjs-assets\..."`). Same source directory (`web/dist`, property `PtnJsDistDir`
  in both project files), different MSBuild item style.
- **Staged copies gitignored** (AMLPetriNet: `.gitignore`, section "Staged into the web project by
  its build"), so the repository never holds a stale bundle.
- **MIME types.** `.aml` and `.pnml` are not in the default table; StaticFiles does not serve
  unknown extensions, so the example document came back as 404 (AMLPetriNet:
  `dotnet/PtMapper.Web/Program.cs`, `FileExtensionContentTypeProvider`, `contentTypes.Mappings`).
- **no-cache.** Bundle and page keep their names across deploys; a cached copy leaves a visitor on
  the old modeler with no way to tell (Program.cs, `UseStaticFiles`, `OnPrepareResponse`). If you
  add content hashes to file names, you can cache the bundle and keep `no-cache` only on the HTML.
- **nosniff.** The app returns user-supplied XML as downloads; content sniffing is the one way a
  file could be interpreted as script (Program.cs, the `app.Use` middleware that sets
  `X-Content-Type-Options`).
- **HSTS only outside Development**, so a developer's browser is not pinned to https on localhost
  (Program.cs, `UseHsts`).

**The bundle must boot without the host.** The web page imports the bundle as an ES module and
creates the modeler directly, without `connectBridge` (AMLPetriNet:
`dotnet/PtMapper.Web/wwwroot/index.html`, the module script's `import` and the
`createPetriNetModeler` call). This works because every call into the WebView2 host is guarded
with optional chaining (`window.chrome?.webview?.postMessage`, AMLPetriNet: `web/src/bridge.js`,
`post`). Write your bridge the same way, and keep the web page off the bridge so that web changes
carry no risk for the plugin. The bundle must also not fetch anything from a CDN at runtime
(AMLPetriNet: `web/build.mjs`, header comment); the plugin runs offline and the web page benefits
from the same property. The page exposes `window.ptn` and `window.ptnReady` for browser tests
(index.html, after `createPetriNetModeler`) and shows boot failures in a visible element via
`showFatal` (index.html, the `boot-error` element and the `catch` around `createPetriNetModeler`)
instead of leaving an empty canvas.

For FPB, the modeler web app itself is a separate deployment that serves the released npm
package's `dist/` (FPB.JS: `package.json`, `files`), published from CI on a version tag (FPB.JS:
`.github/workflows/release.yml`, the `on.push.tags` trigger and the `npm publish --access public`
step). Serving the tarball's build rather than a local build matters: a local build with a
different Node version yields a different bundle hash, and at one point a hand-copied development
build stayed live for months.

## 7. Downloads from the browser

The server returns text; the page turns it into a file.

```js
function download(text, name, type) {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  // Revoked late: Safari reads the blob after the click returns.
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
```
(AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html`, `download`)

**Do:** append the anchor before clicking, revoke the object URL later, and derive the file name
from the opened file (`source.name` with the extension replaced; index.html, `saveAml`, `savePnml`,
`saveSvg`). The FPB page revokes the URL immediately after `click()` and never attaches the anchor
(fpb-aml-mapper: `public/index.html`, `downloadOutput`), which is the pattern the PT comment warns
about.

**Server-side names.** When the API itself names a file, sanitize: take `Path.GetFileName`, fall
back when empty or longer than 120 characters (AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`,
`FileName`). Never build a path from a query parameter.

**Serialize to a stream, not a temp file.** The PT app writes the document with
`doc.SaveToStream(prettyPrint: true)` (Program.cs, `Xml`). The FPB API saves to
`Path.GetTempFileName()` and reads the file back (fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs`,
`MapPost("/api/to-aml", ...)`). That costs disk I/O per request and, since Aml.Engine places the
CAEX schema file next to any file it saves (AMLPetriNet: `.gitignore`, the
`**/CAEX_ClassModel_V.3.0.xsd` entry), leaves schema copies in the server's temp directory. The
starter keeps both directions in one place: `EflDocuments.ToXml` writes through `SaveToStream`, and
`LoadFile`/`SaveFile` go through text for the same reason
(`starter/dotnet/Efl.Conversion/EflDocuments.cs`, `ToXml`, `LoadFile`, `SaveFile`).

**Concurrency in the page.** Disable buttons while a request runs *and* guard non-button inputs
(file picker, drag and drop, hierarchy select) with a `working` flag; otherwise a second file can
be opened while the first is converting and the late answer overwrites the newer canvas
(AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html`, `working`, `busy`, `idle`, and the `change`
listener on `file` and the `drop` listener on `window`).

**Testing downloads.** Headless Chromium reports blob downloads as canceled. The PT browser test
wraps `URL.createObjectURL`, records the entry synchronously, and reads the blob's text, then
asserts on content (AMLPetriNet: `web/tools/verify-webapp.mjs`, the `page.addInitScript` call and
`saves`). See [07-testing-and-ci.md](07-testing-and-ci.md).

## 8. Health check

**Do:** expose a cheap `GET /api/health` that does no Aml.Engine work and is not rate limited (the
PT app maps it without `RequireRateLimiting`, so host probes cannot exhaust a visitor's permits).
Return status, the domain library version, the server time, and
the client address as the app sees it. Add the mapper assembly's informational version (and commit
if you stamp one) so that web and plugin versions can be compared.

```csharp
app.MapGet("/api/health", (HttpContext ctx) => Results.Ok(new
{
    status = "ok",
    library = PtNames.LibraryVersion,
    utc = DateTime.UtcNow,
    client = ctx.Connection.RemoteIpAddress?.ToString(),
}));
```
(AMLPetriNet: `dotnet/PtMapper.Web/Program.cs`, `MapGet("/api/health", ...)`, comment omitted)

**Why:** the library version comes from the same constant the mapper stamps into the libraries it
writes (AMLPetriNet: `dotnet/PtMapper.Conversion/PtNames.cs`, `PtNames.LibraryVersion`), so it
cannot disagree with what the endpoints produce. The FPB API has no health endpoint; after a deploy
the only check was a real conversion request with curl against `/api/to-aml`. Use the health
endpoint for three things: the host's health probe, a post-deploy smoke test, and warming the app
before a demo. Low-cost hosting plans unload idle apps, and the first request after that pays the
full .NET and Aml.Engine start-up.

## 9. Deployment as a generic pattern

The source projects deploy the same way; the commands below contain no provider specifics.

```bash
# 1. Build the modeler bundle (the web project's build refuses to run without it)
cd web && npm ci && npm run build && cd ..

# 2. Publish the web project
dotnet publish dotnet/<Lang>Mapper.Web -c Release -o publish

# 3. Zip the *contents* of publish/ (files at the zip root, not a publish/ folder)
cd publish && zip -r ../deploy.zip . && cd ..
#    PowerShell: Compress-Archive publish/* deploy.zip -Force

# 4. Hand deploy.zip to your host's zip deployment (CLI, REST endpoint or panel)

# 5. Smoke test
curl https://<your-host>/api/health
node web/tools/verify-webapp.mjs https://<your-host>/
```

References: fpb-aml-mapper: `README.md`, section "Deploy to Azure" (publish, zip, zip deploy);
AMLPetriNet: `README.md`, sections "Build" (bundle build first) and "Web application"
(`dotnet run`); the browser
test takes a base URL (AMLPetriNet: `web/tools/verify-webapp.mjs`, the usage comment and `base`)
and was run against the live instance.

Notes from the source projects:

- **Gitignore the artefacts.** `publish/` and `deploy.zip` are ignored in both repositories
  (AMLPetriNet: `.gitignore`, section "Made by the deploy commands in the README"; fpb-aml-mapper:
  `.gitignore`, entries `dotnet/publish/` and `dotnet/deploy.zip`).
- **Check the worker's bitness.** The PT web project keeps `PlatformTarget` at `AnyCPU` because the
  host plan runs a 32-bit worker (AMLPetriNet: `dotnet/PtMapper.Web/PtMapper.Web.csproj`,
  `PlatformTarget` and the comment above it). A project forced to x64 fails to start on such a
  worker.
- **Aml.Engine is managed code.** Framework-dependent publish on a host with the ASP.NET Core
  runtime is enough; no native dependency needs installing.
- **Build the web project in CI** even if you deploy by hand. The PT solution contains
  `PtMapper.Web` and CI builds the bundle before the solution, so a web project broken by a mapper
  API change fails the build (AMLPetriNet: `.github/workflows/ci.yml`, steps "Build the modeler
  bundle" and "Build"). The FPB mapper solution also contains its web project (fpb-aml-mapper:
  `dotnet/FpbMapper.sln`, project entry `FpbMapper.Web`) and its CI builds the whole `dotnet/`
  folder.
- **Use HTTPS only** at the host, and keep HSTS in the app for non-development environments.

## 10. Keeping web app and plugin on the same mapper version

Users will open a file in the web app and later in the editor, or the other way round. If the two
run different mapper versions, IDs, attribute conventions or library versions can differ, and an
update-in-place in one tool can duplicate elements written by the other.

**Do:**

1. **One conversion project, referenced by both** (table in section 1). Never copy mapper code into
   the web project. The PT app keeps plugin, mapper, CLI, tests and web in one solution and one
   repository; the FPB plugin references the mapper project from a sibling checkout
   (AMLFPB.js: `Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj`, `ProjectReference` to
   `FpbMapper.Conversion.csproj`), which works but means the plugin release and the web deploy are
   two independent actions on two repositories.
2. **One modeler bundle, consumed by both** (section 6). The PT plugin and web both read `web/dist`
   (AMLPetriNet: `Aml.Editor.Plugin.PetriNet/Aml.Editor.Plugin.PetriNet.csproj` and
   `dotnet/PtMapper.Web/PtMapper.Web.csproj`, property `PtnJsDistDir` in each). The FPB plugin reads
   FPB.JS's `dist` (AMLFPB.js: `Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj`, property
   `FpbJsDistDir`, the `None` items linked into `fpbjs-assets`, target `VerifyFpbJsDist`).
3. **Redeploy the web app whenever you release the plugin**, from the same commit. Make it a step
   of the release checklist; in the source projects, web deploys happened by hand after mapper
   commits and were tracked in notes, which is how drift goes unnoticed.
4. **Make the version observable on both sides.** Return library and mapper versions from
   `/api/health`; log the same values in the plugin at start-up. Stamp written documents with
   the library version (the PT mapper sets `OriginVersion` from `PtNames.LibraryVersion`,
   AMLPetriNet: `dotnet/PtMapper.Conversion/PtNetToCaex.cs`, `Convert`).
5. **Run the same round-trip checks against the deployed web app** that you run against the mapper
   in tests (browser test with base URL, section 9).

## 11. The starter web app

`starter/dotnet/Efl.Web` is the smallest form of this chapter for EFL, in the same solution as the
mapper (`starter/dotnet/Efl.sln`), so a mapper API change breaks its build.

| Part | Starter file | Symbols |
|---|---|---|
| Endpoints `to-json`, `to-aml` (with `?style=link` or `?style=element`), `update`, `validate` (with the list of rule ids), `library`, `library/layout`, `health` | `starter/dotnet/Efl.Web/Program.cs` | `MapPost("/api/to-json", ...)`, `MapPost("/api/to-aml", ...)`, `MapPost("/api/update", ...)`, `MapPost("/api/validate", ...)`, `MapGet("/api/library", ...)`, `MapGet("/api/library/layout", ...)`, `MapGet("/api/health", ...)` |
| Body cap, `nosniff`, `no-cache` on static files | `starter/dotnet/Efl.Web/Program.cs` | `MaxBodyBytes`, the `app.Use` middleware, `UseStaticFiles` (`OnPrepareResponse`) |
| Hierarchy lookup and `X-Efl-Info` header with the candidate hierarchies | `starter/dotnet/Efl.Web/Program.cs` | `FindHierarchy`, `MapPost("/api/to-json", ...)` |
| Error wrapper: 400 for input errors, logged 500 otherwise | `starter/dotnet/Efl.Web/Program.cs` | `Guarded`, `Fail` |
| Bundle and example staged into `wwwroot/` by a `Copy` target that fails without the bundle; the staged examples folder is removed first, so a renamed or deleted example is not served on | `starter/dotnet/Efl.Web/Efl.Web.csproj` | target `StageModeler` |
| Page: open AML or JSON, keep the opened AML text, Update writes into it and keeps the result as the new source, downloads with a late revoke, SVG from `saveSVG` in the page, findings select their element | `starter/dotnet/Efl.Web/wwwroot/index.html` | module script: `openedAml`, `call`, `download`, `show`, `guarded`, the `update`, `svg` and `validate` click handlers |
| Browser and API test (7 checks, including both library downloads) | `starter/web/tools/verify-webapp.mjs` | `check` calls |

`verify-webapp.mjs` builds the project and then starts the built `Efl.Web.dll` directly with `--urls`
and the project folder as working directory (the content root, where `wwwroot` is found), because
`dotnet run` starts the app as a child process that survives killing `dotnet run`, keeps the port and
locks the build output for the next run (`starter/web/tools/verify-webapp.mjs`, `build` and
`server`, with the comment above them). Its checks: the page boots and loads the example, JSON to
AML and back is lossless in both connection encodings, an update keeps a foreign hierarchy, a broken
file is a 400, and validation names rule and element (same file, the `check` calls from "the page
loads the modeler and the example" to "validation names the rule and the element"). It only runs
locally; add a base URL argument as in the PT test if you want to point it at a deployment.

What the starter leaves out, and what to add from this chapter before a public deployment: rate
limiting and forwarded headers (§3), HSTS and MIME types for `.aml` and your exchange format (§6; the
starter serves only a `.json` example), the client address and mapper version in `health` (§8), a
`working` flag and a hierarchy selector in the page (§2.2, §7). Its page uses relative URLs
(`api/...`) already, so it survives a path prefix (§5).

## Checklist

- [ ] `<Lang>Mapper.Web` references the same conversion project as the plugin; no copied mapper code.
- [ ] Page, bundle and API are served by one ASP.NET Core process on one origin; no CORS configured
      unless a cross-origin client really exists.
- [ ] Endpoints: `to-<format>`, `update`, `to-aml`, `validate`, `library`, `health`.
- [ ] Saving an opened AML file goes through `update` with the original document; test asserts the
      downloaded file still contains the libraries and foreign content.
- [ ] Optional `hierarchy` parameter; the response names the chosen hierarchy and lists candidates.
- [ ] SVG export happens in the page with the bundle's export function; no server SVG endpoint.
- [ ] Library download built from code, not a static copy.
- [ ] `MaxRequestBodySize` set; any proxy in front uses the same limit.
- [ ] Rate limiter only on `/api`, partitioned by forwarded client address, 429 with JSON and
      `Retry-After`; forwarded-header trust matches how the app is reachable.
- [ ] Metadata in one small, capped JSON header serialized with default (ASCII-escaping) encoding;
      exposed via CORS or copied by the proxy if either exists.
- [ ] Findings as JSON with rule, severity, message, element ID; the page escapes and selects.
- [ ] One error wrapper: 400 with safe message for input errors, logged 500 for the rest.
- [ ] Page uses relative URLs only (`./api/...`, `./bundle.js`); proxy redirects `/prefix` to
      `/prefix/`.
- [ ] Bundle staged into `wwwroot/` by a `Copy` target with a missing-bundle error; staged files
      gitignored.
- [ ] MIME mappings for `.aml` and the exchange format; `no-cache` on page and bundle; `nosniff`;
      HSTS outside Development.
- [ ] Bundle boots without the WebView2 host (all host calls guarded); page does not use the bridge.
- [ ] Downloads: anchor attached, object URL revoked late, file name derived from the opened file.
- [ ] Documents serialized via `SaveToStream`, not temp files.
- [ ] A `working` flag guards file picker, drop and selectors during requests.
- [ ] `/api/health` returns status, library and mapper version, time, client address.
- [ ] Deploy: build bundle, `dotnet publish -c Release`, zip the contents, zip deploy, then health
      check and browser test against the live URL.
- [ ] `publish/` and `deploy.zip` gitignored; `PlatformTarget` matches the host worker.
- [ ] CI builds the web project; web redeploy is part of every plugin release.
- [ ] AML loaded through one helper that rejects a null `CAEXFile`; a test asserts a broken file is a 400.
- [ ] Browser test starts the built app directly (not through `dotnet run`) and stops it in `finally`.

## Where to look

| Topic | File | Symbols |
|---|---|---|
| Minimal web app to start from | `starter/dotnet/Efl.Web/Program.cs`, `Efl.Web.csproj`, `wwwroot/index.html` | `Guarded`, `FindHierarchy`, target `StageModeler` |
| Web app test that starts the built app | `starter/web/tools/verify-webapp.mjs` | `build`, `server`, `check` |
| Load helper against null `CAEXFile` | `starter/dotnet/Efl.Conversion/EflDocuments.cs` | `Load` |
| Complete single-process web app (endpoints, limits, errors) | AMLPetriNet: `dotnet/PtMapper.Web/Program.cs` | `MapPost`/`MapGet` endpoints, `AddRateLimiter`, `ClientAddress`, `Guarded` |
| Bundle staging, 32-bit note | AMLPetriNet: `dotnet/PtMapper.Web/PtMapper.Web.csproj` | `StageWebAssets`, `PlatformTarget` |
| Page: open, update, validate, SVG, download, concurrency | AMLPetriNet: `dotnet/PtMapper.Web/wwwroot/index.html` | `openAml`, `saveAml`, `validate`, `saveSvg`, `download`, `working` |
| Browser test of the page, local or live | AMLPetriNet: `web/tools/verify-webapp.mjs` | `base`, `saves` |
| Host calls guarded so the bundle boots in a plain browser | AMLPetriNet: `web/src/bridge.js` | `post` |
| SVG export shared by plugin and page | AMLPetriNet: `web/src/svg.js` | `exportSvg` |
| Single-file bundle build, no CDN | AMLPetriNet: `web/build.mjs` | header comment |
| CI building bundle then solution including the web project | AMLPetriNet: `.github/workflows/ci.yml` | steps "Build the modeler bundle", "Build" |
| Endpoint table, limits, proxy key, run instructions | AMLPetriNet: `README.md` | section "Web application" |
| Separate .NET API with CORS and warnings header | fpb-aml-mapper: `dotnet/FpbMapper.Web/Program.cs` | `AddCors`, `WithExposedHeaders` |
| Node reverse proxy: body limit, rate limit, API route whitelist, copied warnings header, whole-app pass-through under `/pt` | fpb-aml-mapper: `server.js` | `MAX_BODY_BYTES`, `rateLimit`, `app.post('/api/:direction', ...)`, `app.use('/pt', ...)`, `PT_PROXY_KEY` |
| Converter page with absolute API path and immediate revoke | fpb-aml-mapper: `public/index.html` | `convert`, `downloadOutput` |
| Publish and zip deploy commands | fpb-aml-mapper: `README.md` | section "Deploy to Azure" |
| Plugin referencing mapper and bundle | AMLFPB.js: `Aml.Editor.Plugin.FPB/Aml.Editor.Plugin.FPB.csproj` | `ProjectReference`, `FpbJsDistDir`, `VerifyFpbJsDist` |
| Modeler web app shipped in the npm package | FPB.JS: `package.json`, `.github/workflows/release.yml` | `files`; the `npm publish` step |
