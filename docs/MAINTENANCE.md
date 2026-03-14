# Documentation Maintenance Guide

## Documentation Structure

```
docs/
  wiki/                  # Source of truth for all user-facing docs
    Home.md              # Wiki landing page
    Installation-and-Setup.md
    Quick-Start.md
    Outfit-Creation.md
    Distribution-Overview.md
    Distribution-Creating-Entries.md
    Distribution-NPC-Browser.md
    Distribution-Outfit-Browser.md
    Armor-Patching.md
    Common-Workflows.md
    Troubleshooting-and-FAQ.md
  scripts/
    sync-wiki.sh         # Pushes docs/wiki/* to GitHub wiki
  images/
    image-urls.md        # Nexus image URL registry
```

Wiki filenames become page titles on GitHub (hyphens become spaces).

## Editing Documentation

1. Edit files in `docs/wiki/` as part of normal commits
2. Commit and push to `main`
3. Run `bash docs/scripts/sync-wiki.sh` to push changes to the GitHub wiki

## Syncing to GitHub Wiki

```bash
bash docs/scripts/sync-wiki.sh
```

This clones the wiki repo, copies all `docs/wiki/*.md` files into it, commits, and pushes. If there are no changes, it exits cleanly.

## Syncing to Nexus

The main Nexus mod description (BBCode) is a sales pitch / overview that links out to wiki pages for detailed guides. When updating Nexus articles:

1. Edit the source markdown in `docs/wiki/`
2. Convert to BBCode (use any markdown-to-BBCode tool)
3. Paste into the Nexus article editor
4. Verify formatting in Nexus preview

## Adding Images

1. Capture screenshots (PNG, 1920x1080+)
2. Upload to Nexus or Imgur
3. Record the URL in `docs/images/image-urls.md`
4. Reference in markdown: `![Description](https://url)`

## Adding a New Wiki Page

1. Create `docs/wiki/Page-Name.md` (use hyphens for spaces)
2. Add a link to it from `docs/wiki/Home.md`
3. Commit, push, run sync script

## Style Guidelines

- **Audience**: Average modders, no coding knowledge assumed
- **Tone**: Friendly, helpful, encouraging (match the Nexus description voice)
- **Structure**: Scannable headers, short paragraphs, bulleted lists
- **Bold** for UI elements, code blocks for file paths and syntax examples
