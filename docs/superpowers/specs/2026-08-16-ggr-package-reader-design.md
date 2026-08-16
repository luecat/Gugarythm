# GGR Package Reader Design

## Goal

Replace Gugarythm's user-facing SCP, ZIP, SUS, USC, and Sonolus LevelData import paths with a single `.ggr` package import path. Legacy importer classes remain in the source tree but are not registered or invoked.

## Scope

- Accept only `.ggr` through the Editor picker and Android native picker.
- Do not load a built-in default song. The initial menu waits for the user to import a GGR package.
- Read only the version-1 canonical GGR layout: `manifest.json`, `chart.usc`, exactly one permitted `audio.<extension>`, and optional `metadata.json` / `cover.<extension>`.
- Use the existing USC parser for chart conversion and validation.
- Preserve the current local-library and runtime-audio paths after a package has been validated.
- Do not delete legacy importer source files in this change. They remain unused compatibility code.

## Architecture

### `GgrPackageReader`

This new plain C# reader owns all archive-boundary security checks. It first parses ZIP central-directory metadata without extracting data, then checks entry count (maximum 8), encryption flags, compression method (stored or Deflate), flat UTF-8 names, duplicate names, declared compressed and uncompressed sizes (maximum 256 MiB each), and the 100:1-plus-1-MiB expansion-ratio limit. ZIP header sizes are treated only as claims.

Only canonical filenames are allowed. This prevents arbitrary payloads, including executable files, from being retained or interpreted. The reader rejects directory entries, nested paths, backslashes, `..`, unknown files, duplicate files, and unsupported extensions before allocation.

After manifest validation identifies the required entries, extraction uses bounded copy loops. Each copy rechecks the per-entry and package output caps and fails as soon as actual output exceeds the declared or specification limit. `ZipArchive` is used only after the raw metadata preflight succeeds.

### `GgrChartImporter`

This importer owns the package contract. It parses `manifest.json` as strict UTF-8 JSON; requires `format = "gugarythm-package"`, `version = 1`, `chart = "chart.usc"`, and an existing canonical audio path; and ignores unknown manifest fields.

It passes the original `chart.usc` bytes to `UscChartImporter`. An unsuccessful USC import maps to the specified GGR chart error and never produces an empty `RuntimeChart`. On success it supplies the selected audio bytes and extension, applies `USC offset + finite manifest.offset`, and sets title, artist, author, and rating display data where supported by the runtime model.

`metadata.json` is parsed only after the chart and audio are valid. Invalid UTF-8 or JSON becomes a chart warning. Cover decoding remains non-fatal: undecodable cover bytes produce a warning and retain the default cover.

### Integration

`SonolusLandscapePrototype` registers only `GgrChartImporter`, labels the import action as GGR, filters the Editor picker to `ggr`, accepts only `.ggr` paths from the Android bridge, and starts at an import-ready menu with no built-in chart. It creates/saves a local-song record only after archive validation, manifest validation, USC import, and audio decoding all succeed.

`ScpChartImporter`, `SusChartImporter`, `UscChartImporter`, `LevelDataImporter`, and `ChartPackageReader` remain compiled but are no longer exposed as import choices or called by the runtime import path. `UscChartImporter` remains an internal dependency of the GGR importer.

## Error Contract

Fatal failures use exactly one of the requested messages:

- `不是有效的 GGR ZIP 封包。`
- `GGR 缺少 manifest.json。`
- `不支援的 GGR 格式或版本。`
- `GGR 缺少 USC 譜面或音樂。`
- `GGR 的 USC 譜面無效。`
- `GGR 音樂格式不支援或無法解碼。`
- `GGR 包含不安全的檔案路徑。`
- `GGR 封包過大或壓縮資料異常。`

Optional metadata or cover problems add a warning and must not stop the song from being added.

## Validation

The existing `Gugarythm > Validate Runtime` harness will cover a valid stored GGR package and every fatal error category. It will also verify that unknown/executeable-like entries, nested names, duplicate names, encrypted-method flags, unsupported compression methods, declared size/ratio violations, and extraction-time expansion violations never reach the USC or audio stages. Separate tests verify metadata and cover failures stay warnings, manifest offset combines correctly with USC offset, and legacy extensions are rejected by the UI-path filter.

Unity compilation and the runtime validation command are the handoff gate. Android must also be checked through the normal system picker with a valid package and an invalid package.
