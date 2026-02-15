# Deployment Script Enhancements

Analysis of `scripts/deploy.sh`, `scripts/workshop-upload.sh`, and `scripts/modio-upload.sh` with suggested improvements.

## Current State

The three scripts cover local testing, Steam Workshop, and mod.io deployment. They share a consistent structure (load `.env`, read version from `ModInfo.xml`, stage content, upload) and version has a single source of truth in `ModInfo.xml`. The mod.io script auto-creates a mod on first run and saves the ID back to `.env`.

## Suggested Enhancements

### 1. Extract shared logic into a common library

Version extraction, `.env` loading, changelog parsing, and content staging are copy-pasted across `workshop-upload.sh` and `modio-upload.sh`. If a new file is added to the mod, three separate `cp` blocks need updating.

**Proposal:** Create `scripts/lib.sh` with shared functions:
- `load_env` — load and validate `.env`
- `read_version` — extract version from `ModInfo.xml`
- `read_changelog` — parse `CHANGELOG.md` or accept CLI argument
- `stage_content <target_dir>` — copy mod files to a staging directory

Each script would `source scripts/lib.sh` and call these functions instead of duplicating the logic.

### 2. Add pre-flight validation

None of the scripts verify mod content before shipping. Given that a missing UTF-8 BOM on text files silently breaks the mod in-game, a validation step would catch real bugs.

**Checks to add:**
- Text files (`text-*-add.xml`) have UTF-8 BOM (`ef bb bf` at byte 0)
- All expected files exist in `Infos/`
- XML files are well-formed (`xmllint --noout`)

This could live in `scripts/lib.sh` as a `validate_content` function called by all three scripts, or as a standalone `scripts/validate.sh`.

### 3. Create CHANGELOG.md

Both upload scripts try to parse `CHANGELOG.md` for changenotes, but the file doesn't exist. Every upload gets just `v1.0.0` as the changenote unless a CLI argument is passed.

Either create and maintain `CHANGELOG.md`, or make the scripts warn more clearly when it's missing.

### 4. Add --dry-run support

There's no way to preview what would be uploaded without actually uploading. Add a `--dry-run` flag that stages content, shows what would be sent (version, changelog, file list), then exits without calling SteamCMD or the mod.io API.

### 5. Add missing .gitignore entries

The mod.io script creates `modio_content/` and `modio_upload.zip` as temporary artifacts. If the script fails mid-way, these stick around and could be accidentally committed. Add to `.gitignore`:

```
modio_content/
modio_upload.zip
workshop_upload.vdf
```

### 6. Unified release script

Releasing to both platforms requires running two separate scripts manually. A `scripts/release.sh` could orchestrate the full flow:

```
./scripts/release.sh [--local] [--steam] [--modio] [--all] [changelog]
```

Default to `--all`. Run validation first, then deploy to each selected target.

### 7. Version bump workflow

Version lives in `ModInfo.xml` but must be edited manually. A `scripts/bump-version.sh` (or a `--bump` flag on the release script) would:
- Accept `major`, `minor`, or `patch`
- Update the `<modversion>` tag in `ModInfo.xml`
- Optionally update `CHANGELOG.md` with a new section header

### 8. Platform portability note

`modio-upload.sh` line 102 uses `sed -i ''` which is macOS-specific (`sed -i` on Linux expects no argument). Not a problem for local use today, but would break in CI or on Linux. If portability is ever needed, use a temp file pattern or `perl -pi -e` instead.

## Priority

Highest impact, roughly ordered:
1. **Shared lib** — eliminates duplication, single place to maintain file list
2. **BOM/XML validation** — catches the exact class of bug that has already cost debugging time
3. **CHANGELOG.md + .gitignore fixes** — low effort, immediate value
4. **Unified release script** — streamlines the publish workflow
5. **Dry-run and version bump** — nice quality-of-life improvements
