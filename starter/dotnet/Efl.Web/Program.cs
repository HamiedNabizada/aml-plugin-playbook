// The mapper as a web app: the same conversion library the plugin uses, behind
// a few endpoints, with the modeler page served from the same origin. One
// process, no CORS, no proxy needed; any host that runs ASP.NET Core will do.
//
// /api/update is the endpoint that matters. Converting a document to JSON and
// back through /api/to-aml builds a new document that holds the diagram and
// nothing else. /api/update writes the edited diagram into the document it came
// from, which keeps everything else in that document.

using System.Text;
using System.Text.Json;
using Aml.Engine.CAEX;
using Efl.Conversion;

var builder = WebApplication.CreateBuilder(args);

// Diagrams are small. The cap keeps a stray upload from filling the memory.
const int MaxBodyBytes = 8 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxBodyBytes);

var app = builder.Build();

app.Use(async (context, next) =>
{
    // User supplied XML goes back out as a download; the browser must not guess
    // a content type for it.
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // Page and bundle keep their names across deploys; without revalidation a
    // visitor stays on the old modeler and cannot tell.
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

/// AML in, the first diagram as JSON out. The response header says which
/// hierarchies hold diagrams, so the page can offer a choice.
app.MapPost("/api/to-json", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var document = EflDocuments.Load(await Body(ctx));
    var hierarchy = FindHierarchy(document, ctx.Request.Query["hierarchy"]);

    var model = CaexToEfl.Read(hierarchy).First();
    var arranged = EflLayout.ArrangeMissing(model);

    // Headers must stay ASCII; System.Text.Json escapes everything else.
    ctx.Response.Headers["X-Efl-Info"] = JsonSerializer.Serialize(new
    {
        hierarchy = hierarchy.Name,
        hierarchies = document.CAEXFile.InstanceHierarchy
            .Where(CaexToEfl.ContainsDiagram).Select(h => h.Name).ToArray(),
        arrangedNodes = arranged,
        warnings = model.Warnings.Take(5).ToArray(),
        warningCount = model.Warnings.Count,
    });

    await Text(ctx, "application/json", EflJson.Write(model));
}));

/// JSON in, a new AML document out. ?style=element picks the reified encoding.
app.MapPost("/api/to-aml", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var model = EflJson.Read(await Body(ctx));
    EflLayout.ArrangeMissing(model);

    var style = ctx.Request.Query["style"] == "element" ? EflConnectionStyle.Element : EflConnectionStyle.Link;
    var document = EflToCaex.Convert(model, null, style);

    ctx.Response.Headers["X-Efl-Info"] = JsonSerializer.Serialize(new
    {
        style = style.ToString(),
        warnings = model.Warnings.Take(5).ToArray(),
        warningCount = model.Warnings.Count,
    });

    await Text(ctx, "application/xml", Xml(document));
}));

/// The lossless direction: the edited diagram, written into its own document.
app.MapPost("/api/update", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var request = await JsonSerializer.DeserializeAsync<UpdateRequest>(ctx.Request.Body, JsonOptions.CaseInsensitive)
        ?? throw new BadRequest("Empty request.");
    if (string.IsNullOrWhiteSpace(request.Aml)) throw new BadRequest("No AML document was sent.");
    if (string.IsNullOrWhiteSpace(request.Model)) throw new BadRequest("No diagram was sent.");

    var document = EflDocuments.Load(request.Aml);
    var hierarchy = FindHierarchy(document, request.Hierarchy);
    var summary = EflUpdater.UpdateInPlace(document, hierarchy, EflJson.Read(request.Model));

    ctx.Response.Headers["X-Efl-Info"] = JsonSerializer.Serialize(new
    {
        hierarchy = hierarchy.Name,
        added = summary.Added,
        updated = summary.Updated,
        removed = summary.Removed,
        notes = summary.Notes,
    });

    await Text(ctx, "application/xml", Xml(document));
}));

/// The language's rules, on JSON or AML.
app.MapPost("/api/validate", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var body = await Body(ctx);
    var model = body.Contains("CAEXFile", StringComparison.Ordinal)
        ? CaexToEfl.ReadAll(EflDocuments.Load(body)).FirstOrDefault()
          ?? throw new BadRequest("This document holds no EFL diagram.")
        : EflJson.Read(body);

    await ctx.Response.WriteAsJsonAsync(new
    {
        rules = EflValidator.RuleIds,
        findings = EflValidator.Validate(model).Select(f => new
        {
            rule = f.Rule,
            severity = f.Severity.ToString().ToLowerInvariant(),
            message = f.Message,
            element = f.ElementId,
        }),
    });
}));

/// The domain library as a file of its own.
app.MapGet("/api/library", async (HttpContext ctx) => await Guarded(ctx, async () =>
{
    var document = EflLibraries.CreateArtefact();
    ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{document.CAEXFile.FileName}\"";
    await Text(ctx, "application/xml", Xml(document));
}));

/// The shared layout library the domain library references by file name.
app.MapGet("/api/library/layout", (HttpContext ctx) =>
{
    ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{EflDiagramInterchange.FileName}\"";
    return Results.Text(EflDiagramInterchange.Xml, "application/xml");
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", library = EflNames.LibraryVersion }));

app.Run();

static InstanceHierarchyType FindHierarchy(CAEXDocument document, string? wanted)
{
    var hierarchy = string.IsNullOrWhiteSpace(wanted)
        ? document.CAEXFile.InstanceHierarchy.FirstOrDefault(CaexToEfl.ContainsDiagram)
        : document.CAEXFile.InstanceHierarchy[wanted];

    return hierarchy ?? throw new BadRequest(string.IsNullOrWhiteSpace(wanted)
        ? "This document holds no EFL diagram."
        : $"This document has no InstanceHierarchy '{wanted}'.");
}

static async Task<string> Body(HttpContext ctx)
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var text = await reader.ReadToEndAsync();
    return string.IsNullOrWhiteSpace(text) ? throw new BadRequest("The request body is empty.") : text;
}

static async Task Text(HttpContext ctx, string contentType, string body)
{
    ctx.Response.ContentType = contentType + "; charset=utf-8";
    await ctx.Response.WriteAsync(body);
}

static string Xml(CAEXDocument document) => EflDocuments.ToXml(document);

/// Caller errors are 400 with a readable message; everything else is a 500 that
/// is also logged, so a server fault never looks like a bad file.
static async Task Guarded(HttpContext ctx, Func<Task> action)
{
    try
    {
        await action();
    }
    catch (BadRequest bad)
    {
        await Fail(ctx, StatusCodes.Status400BadRequest, bad.Message);
    }
    catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or JsonException
                                  or InvalidOperationException or ArgumentException or BadHttpRequestException)
    {
        await Fail(ctx, StatusCodes.Status400BadRequest, "This file could not be read: " + ex.Message);
    }
    catch (Exception ex)
    {
        ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Efl.Web")
            .LogError(ex, "Unhandled failure in {Path}", ctx.Request.Path);
        await Fail(ctx, StatusCodes.Status500InternalServerError, "Conversion failed: " + ex.Message);
    }
}

static async Task Fail(HttpContext ctx, int status, string message)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}

file static class JsonOptions
{
    public static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };
}

file sealed class BadRequest(string message) : Exception(message);

file sealed record UpdateRequest(string? Aml, string? Model, string? Hierarchy);

/// <summary>Visible to WebApplicationFactory in tests.</summary>
public partial class Program;
