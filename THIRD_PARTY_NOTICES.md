# Third-party notices

Pinned production dependencies and their licenses (verify upstream at upgrade time).

## Backend (.NET)

| Component | Package / version | License |
|---|---|---|
| .NET / ASP.NET Core | net10.0 | MIT |
| Markdig | 1.3.2 | BSD-2-Clause |
| Microsoft.Data.Sqlite | 10.0.10 | MIT |
| Microsoft.Extensions.* | 10.0.10 | MIT |
| Microsoft.PowerShell.SDK | 7.5.4 | MIT |
| PSScriptAnalyzer (bundled module, optional) | 1.25.x | MIT |

## Frontend (npm)

| Component | Version | License |
|---|---|---|
| React / react-dom | 19.2.x | MIT |
| Vite | 8.2.x | MIT |
| TipTap (+ extensions) | 3.29.2 | MIT |
| @tiptap/markdown | 3.29.2 | MIT |
| DOMPurify | 3.4.x | Apache-2.0 / MPL-2.0 |
| lowlight | 3.3.x | MIT |
| CodeMirror 6 (@codemirror/*, codemirror) | 6.x | MIT |
| @codemirror/legacy-modes | 6.x | MIT |
| @lezer/highlight | 1.x | MIT |
| remark / remark-parse / remark-lint (+ rules) | 15.x / 11.x / 10.x | MIT |
| TypeScript | 6.0.x | Apache-2.0 |

Pin exact versions in lockfiles (`package-lock.json`, NuGet restore) before production release.
