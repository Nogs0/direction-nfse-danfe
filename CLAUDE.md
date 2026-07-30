# CLAUDE.md

Guidance for Claude Code (or any agent) working in this repository.

## What this is

`Direction.NFSe.Danfe` (packaged as `Nogueira.NFSe.Danfe.FixLinux`) — a .NET library that
deserializes NFS-e Nacional XML (DPS/NFSe) into `NFSeSchema`, renders it to HTML via placeholder
substitution, and converts the HTML to PDF (the DANFSe) using a headless Chromium
(PuppeteerSharp). No external service dependency. Consumed by other Questor projects (notably
the `nfse_nacional_api` service) and by the small `src/GeraDanfe` CLI sample in this repo.

## Layout compliance

The DANFSe layout follows **Nota Técnica nº 008/2026 (SE/CGNFS-e) — DANFSe v2.0** (Anexo I,
page 25 of the NT, has the visual model). When adding/changing fields, verify the XML path and
field name against the NT's field table (§2.4.5) rather than guessing — this schema has been
bitten before by invented/mistyped field names (see "Known follow-ups" below).

## Architecture

- `src/Danfe/Public/DanfeService.cs` — entry point: XML → `NFSeSchema` → HTML → PDF.
- `src/Danfe/Rendering/DanfeHtmlRenderer.cs` — maps `NFSeSchema` to `{{PLACEHOLDER}}` tokens in
  `Assets/Templates/Danfe.html`. Blocks that must be fully suppressed/substituted when data is
  absent (Tomador, Destinatário, Intermediário, Tributação Municipal ISSQN, IBS/CBS, Canhoto)
  are built as complete HTML strings in C# and injected as a single placeholder — extend this
  pattern for new suppressible blocks rather than introducing a templating engine. All other
  fields stay as plain per-field `{{PLACEHOLDER}}` tokens directly in the HTML template.
- `src/Danfe/Pdf/DanfePdfGenerator.cs` — HTML→PDF via a pooled headless Chromium. Failed pages
  are discarded, never recycled; the pool is drained before any browser relaunch; pool size is
  capped. Don't reintroduce `--single-process` (it couples a renderer crash to the whole browser
  dying).
- `src/Danfe/Schemas/NFSeSchema.cs` — POCO XML-serialization model of the national NFSe/DPS
  schema. Property names must match XML element names exactly unless annotated with
  `[XmlElement("...")]`.
- `src/Danfe/Diagnostics/` — structured warnings (`NFSE_FIELD_MISSING`, `MUNICIPIO_NOT_FOUND`,
  `TEMPLATE_PLACEHOLDER_EMPTY`) instead of throwing on incomplete XML.

## Build / test

```bash
dotnet build Danfe.sln
dotnet test tests/Danfe.Tests/Danfe.Tests.csproj
```

Tests are split into two speeds:
- Default run: fast, pure-render golden HTML tests (`GoldenHtmlTests.cs`) + unit tests. No
  browser needed.
- `[Trait("Category", "Integration")]`: needs a real headless Chromium (auto-downloaded by
  PuppeteerSharp on first run). Run explicitly with `--filter "Category=Integration"`, or
  exclude with `--filter "Category!=Integration"` for a fast loop.

### Golden HTML tests

`tests/Danfe.Tests/Fixtures/*.xml` are synthetic (no real customer data — never commit a real
NFS-e XML as a fixture) covering the key acceptance scenarios (regular, cancelada, substituída,
produção restrita, participantes ausentes, destinatário == tomador, IBS/CBS presente/ausente).
`tests/Danfe.Tests/Approved/*.approved.html` are the committed baseline snapshots.

If a test fails because you intentionally changed rendering behavior: delete the stale
`.approved.html` file(s), re-run the tests (they self-write a new baseline on first run), review
the generated HTML in `bin/Debug/*/Approved/` for correctness, then copy it back into
`tests/Danfe.Tests/Approved/` and commit.

`HtmlNormalization.Normalize` collapses **all** whitespace runs (including newlines) to a single
space before comparing — this is intentional, so `npm run format` (Prettier) reformatting the
template's line breaks doesn't produce false snapshot failures. Don't narrow this back to
tag-adjacent whitespace only.

## Formatting

```bash
dotnet format
npm install   # first time only
npm run format         # Prettier on the HTML template
npm run format:check   # CI-style check
```

Both must be clean before a PR (see `CONTRIBUTING.md`).

## Git conventions

- **Never** commit a real customer/production NFS-e XML or PDF as a test fixture or example —
  it contains PII (CNPJ/CPF, names, addresses). Use synthetic data.
- Commit message format, one commit per Redmine subtask:
  `#{subtask id} - {full subtask subject, verbatim}`
  e.g. `#2357225 - NFS-e Nacional - Geração de DANFSe - Estabilidade do Gerador Local (4.3)`.
  When a parent ticket splits work across subtasks, split the commits (and any shared file like
  `CHANGELOG.md`) to match — don't squash multiple subtasks into one commit.
- This repo's `main` currently has no protection requiring PRs; fast-forwarding
  `feature-*` branches into `main` directly is the established pattern here as of 2026-07,
  but confirm with the user before pushing to `main` — treat it as shared/production state.

## Known follow-ups (flagged, not yet fixed)

- `CServ.clongContrib` (in `NFSeSchema.cs`) looks like the same "int→long" find-replace
  corruption that broke `Intermediario`/`interm` (fixed 2026-07-30). Not currently wired to
  anything, so left alone rather than guess the real field name — check against the official
  DPS XSD before using it.
- `DanfeHtmlRenderer.GetDescricaoAmbienteGerador` (ambGer 1/2/3 descriptions) is best-effort from
  public docs, not the NT-008 itself. Confirm against the DPS XSD/manual before relying on the
  exact wording in production.
