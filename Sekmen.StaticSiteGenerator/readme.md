# Sekmen.StaticSiteGenerator

High‑level, flexible static site export utility for .NET and Umbraco.

This library (NuGet package: `Sekmen.StaticSiteGenerator`) crawls a dynamic Umbraco (or any standard HTML) website, follows internal links, downloads referenced assets, rewrites domain references, and produces a portable static snapshot you can deploy to any static host (Azure Static Web Apps, GitHub Pages, Netlify, S3, Cloudflare Pages, etc.).

It is also shipped together with an Umbraco backoffice plugin (NuGet: `Umbraco.Community.HtmlExporter`) that adds a dashboard UI & API endpoints so editors can trigger an export without touching code.

---
## Packages
| Package | Purpose |
|---------|---------|
| `Sekmen.StaticSiteGenerator` | Core export logic (crawler + downloader + rewriting). |
| `Umbraco.Community.HtmlExporter` | Umbraco backoffice dashboard + API wrapper around the core. |

---
## Key Features
- Sitemap + on‑the‑fly discovery (loads `sitemap.xml` then continues via internal `<a>` links)
- Recursive asset collection (CSS, JS, images, inline style `background-image: url(...)` references)
- Intelligent resource re-download check via HEAD (skips unchanged assets where size matches)
- Domain & path rewriting (replaces the original origin with a target/static domain you specify)
- Additional manual URL injection (export pages not linked or not in sitemap)
- Automatic `index.html` generation for folder style URLs
- Custom ordered string replacements (filenames + HTML content) via `StringReplacements[]`
- Basic normalization for Umbraco specific paths (`umbraco-cms` → `umbraco`)
- MIT licensed & simple, dependency-light (only HtmlAgilityPack)

---
## How It Works (Core Flow)
1. Build a queue: seed with `sitemap.xml` entries + any `AdditionalUrls` you provide.
2. Pop a URL, download HTML, rewrite & save.
3. Parse internal `<a href>` links, enqueue new same‑host URLs.
4. Extract asset URLs (link/script/img/inline style url()).
5. Download assets (only if new or changed size).
6. Repeat until queue is empty.

---
## Installation
### Core Library
```
dotnet add package Sekmen.StaticSiteGenerator
```
### Umbraco Plugin (Dashboard)
```
dotnet add package Umbraco.Community.HtmlExporter
```
(Requires an Umbraco 16+ style project with backoffice extensions; ensure you restore & rebuild so client assets are generated.)

---
## Public API (Core)
### ExportCommand
| Property | Type | Description |
|----------|------|-------------|
| `SiteUrl` | `string` | Host *without* protocol (e.g. `example.com`). `https://` is automatically prepended in current implementation. |
| `AdditionalUrls` | `string[]` | Extra absolute or relative URLs/paths to ensure export even if not linked/sitemap. |
| `TargetUrl` | `string` | Replacement origin (e.g. `https://static.example.com/`). Used for rewriting references in saved HTML. Include trailing slash for consistency. |
| `OutputFolder` | `string` | Local folder where the static site will be generated. Created if missing. |
| `StringReplacements` | `StringReplacements[]` | Ordered list of additional string replacements applied to: (1) the computed output file path, then (2) the final HTML/content after built‑in rewrites. Each pair is simple `OldValue` → `NewValue` replacement (case sensitive). |

Supporting record:
```
public record StringReplacements(string OldValue, string NewValue);
```

### Method
`await Functions.ExportWebsite(HttpClient client, ExportCommand command);`

#### Minimal Example (Console)
```csharp
using Sekmen.StaticSiteGenerator;

var http = new HttpClient();
var cmd = new ExportCommand(
    SiteUrl: "myumbracosite.com",          // no protocol
    AdditionalUrls: new[] { "/404", "/health" },
    TargetUrl: "https://static.myumbracosite.com/", // will replace absolute refs
    OutputFolder: Path.Combine(Environment.CurrentDirectory, "export"),
    StringReplacements: new []
    {
        new StringReplacements("umbraco-cms", "umbraco"),
        new StringReplacements("Umbraco CMS", "Umbraco")
    }
);

await Functions.ExportWebsite(http, cmd);
Console.WriteLine("Export complete.");
```

---
### Custom String Replacements
Use `StringReplacements` to perform deterministic, ordered find/replace operations after the default rewrite logic. Typical use cases:
- Normalizing CMS specific path or branding strings
- Injecting a CDN hostname into residual inline references missed by primary rewrite
- Renaming generated folder/file segments (e.g. remove `umbraco-cms` from paths)

Rules & Behavior:
- Applied to output path first (affects where file is written)
- Then applied to full HTML/content string
- Executed in the order supplied (later replacements see earlier changes)
- Pure string operations (no regex)

Example – add analytics snippet placeholder replacement:
```csharp
var cmd = new ExportCommand(
    SiteUrl: "example.com",
    AdditionalUrls: Array.Empty<string>(),
    TargetUrl: "https://static.example.com/",
    OutputFolder: "./out",
    StringReplacements: new[]
    {
        new StringReplacements("{{ANALYTICS}}", "<script src=\"/analytics.js\"></script>"),
        new StringReplacements("umbraco-cms", "umbraco")
    }
);
```

If you have no custom replacements, pass `Array.Empty<StringReplacements>()` or `new StringReplacements[0]`.

---
## Umbraco Plugin
After installing `Umbraco.Community.HtmlExporter`, the package adds:
- Backoffice dashboard (App_Plugins) bundling a UI allowing you to enter export parameters.
- API endpoints (secured by backoffice auth & content section policy):
  - `GET /umbracocommunityhtmlexporter/api/v1.0/get-data` → dashboard config & domains
  - `POST /umbracocommunityhtmlexporter/api/v1.0/export-website` (multipart/form-data for the `ExportCommand` fields)

### Sample cURL
```bash
curl -X POST \
  -H "Cookie: auth cookie here" \
  -F SiteUrl=mysite.local \
  -F AdditionalUrls=/custom-page \
  -F AdditionalUrls=/another-page \
  -F TargetUrl=https://static.mysite.local/ \
  -F OutputFolder=C:\\exports\\mysite \
  -F StringReplacements[0].OldValue=umbraco-cms \
  -F StringReplacements[0].NewValue=umbraco \
  https://mysite.local/umbracocommunityhtmlexporter/api/v1.0/export-website
```
(You must be an authenticated backoffice user; obtain cookies via normal login.)

---
## Rewriting & Replacement Order Details
1. Built‑in replacements:
   - Prefix root‑relative `"/` and `'/` references with `TargetUrl`
   - Replace full `sourceUrl` (`https://{SiteUrl}/`) with `TargetUrl`
2. Apply each `StringReplacements` pair in sequence to the output path (directory/file name)
3. Apply each pair again to the full HTML/content payload

Implication: you can purposefully chain transformations (e.g. first collapse a verbose path segment, then swap a remaining token produced by the first step).

---
## Limitations / Known Gaps
- Assumes sitemap lives at `https://{SiteUrl}/sitemap.xml` and is reachable.
- Currently forces HTTPS when constructing source URL.
- No parallel throttling controls (a burst of sequential requests; could be slower for large sites).
- Does not parse JS-generated navigation or SPA routes.
- Asset change detection only by Content-Length (size) not hash.
- No exclusion / allow lists yet.

---
## Roadmap Ideas
- [ ] Protocol support in `SiteUrl` (respect http/https as provided)
- [ ] Configurable concurrency (degree of parallelism)
- [ ] Exclude / include glob patterns
- [ ] Hash-based unchanged asset detection
- [ ] Export manifest JSON (list of pages & assets)
- [ ] CLI tool wrapper (`dotnet tool install`)
- [ ] Stronger URL normalization & canonicalization
- [ ] Structured HTML rewrite (DOM aware) for safer replacements

Feel free to open issues or PRs to discuss and contribute.

---
## Development (Repo)
1. Clone & restore
```
git clone https://github.com/sekmenhuseyin/Sekmen.StaticSiteGenerator.git
cd Sekmen.StaticSiteGenerator
 dotnet restore
```
2. Build solution
```
dotnet build
```
3. (Plugin only) Build client assets – the MSBuild targets automatically run `npm i` + `npm run build` in `Umbraco.Community.HtmlExporter/Client` during pack/build. To trigger manually:
```
cd Umbraco.Community.HtmlExporter/Client
npm install
npm run build
```
4. Run test Umbraco site: open `UmbracoTestProject` (configure connection strings etc.)

---
## Packaging
```
dotnet pack -c Release
```
Outputs `.nupkg` for both packages (with embedded README & icon). Ensure the signing key path is valid or update `AssemblyOriginatorKeyFile` properties if forking.

---
## Extensibility Ideas
You can wrap `Functions.ExportWebsite` to:
- Inject custom headers (User-Agent, auth tokens)
- Add retry logic / resilience policies (Polly)
- Preprocess/postprocess HTML (minification, analytics injection)
- Generate a search index or RSS/JSON feed

---
## Troubleshooting
| Symptom | Possible Cause | Action |
|---------|----------------|-------|
| Empty export folder | Exception early (check console output) | Run under debugger / add logging |
| Some pages missing | Not linked internally nor in sitemap | Add to `AdditionalUrls` |
| Broken relative links | Missing trailing slash in `TargetUrl` | Ensure `TargetUrl` ends with `/` |
| Assets 404 on host | Rewritten to wrong domain | Verify `TargetUrl` correctness |
| Slow export | Large asset count, sequential fetch | Future: add parallelism (or fork & add tasks) |
| Replacement not applied | Order conflict or missing pair | Inspect `StringReplacements` order |

---
## Security Notes
- Do not expose the export POST endpoint publicly without auth; it can be abused for bandwidth & disk usage.
- Validate / sanitize the `OutputFolder` if exposing in multi-tenant scenarios to avoid path traversal.

---
## Contributing
1. Fork & create a feature branch
2. Make changes (+ optional unit / integration tests)
3. Run build & manual smoke export
4. Open PR with clear description

---
## License
MIT © Hüseyin Sekmenoğlu

---
## Quick Start TL;DR
```csharp
await Functions.ExportWebsite(new HttpClient(), new ExportCommand(
    SiteUrl: "example.com",
    AdditionalUrls: Array.Empty<string>(),
    TargetUrl: "https://static.example.com/",
    OutputFolder: "./out",
    StringReplacements: new [] { new StringReplacements("umbraco-cms", "umbraco") }
));
```
Deploy the ./out folder to any static host – done.
