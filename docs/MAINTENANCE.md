# Documentation Maintenance Guide

This guide explains how to maintain and sync Boutique's end-user documentation.

## Documentation Location

- **Git Repository**: `D:\var\Boutique\docs\` (submodule: boutique.wiki)
- **Published Location**: Nexus Mods articles
- **Main Document**: `README.md`

## Workflow Overview

Documentation is maintained in this git repository and manually synced to Nexus Mods. Images are hosted on Nexus and
referenced by URL in the markdown.

## Editing Documentation

### Local Editing

1. **Navigate to docs directory**:
   ```bash
   cd D:\var\Boutique\docs
   ```

2. **Edit README.md** using your preferred markdown editor
  - VS Code recommended for markdown preview
  - Follow the existing structure and style
  - Use placeholder text for new images: `![Placeholder: Description]()`

3. **Commit changes**:
   ```bash
   git add README.md
   git commit -m "docs: update [section name]"
   git push
   ```

4. **Update parent repo** (if needed):
   ```bash
   cd D:\var\Boutique
   git add docs
   git commit -m "docs: update submodule reference"
   git push
   ```

## Syncing to Nexus

### Prerequisites

- Access to Boutique Nexus Mods page with article editing permissions
- Screenshots prepared and ready for upload
- Updated README.md with content changes

### Step-by-Step Sync Process

1. **Capture New Screenshots** (if needed):
  - Resolution: 1920x1080 or higher
  - Format: PNG
  - Save temporarily to `docs/images/` (gitignored)
  - Follow naming: `boutique-{section}-{number}.png`

2. **Upload Images to Nexus**:
  - Log in to Nexus Mods
  - Navigate to Boutique mod page → Articles → Edit
  - Use Nexus image uploader to upload screenshots
  - Copy the Nexus-generated image URLs

3. **Record Image URLs**:
  - Open `docs/images/image-urls.md`
  - Add entries for each uploaded image:
    ```
    [Section Name] Description - https://nexus-url.com/image.png - 2026-02-06
    ```
  - Commit this file to git

4. **Update README.md**:
  - Replace image placeholders with actual Nexus URLs:
    ```markdown
    ![Description](https://nexus-url.com/image.png)
    *Caption text*
    ```
  - Commit changes to git

5. **Convert Markdown to Nexus Format**:
  - Nexus articles support limited HTML/BB Code
  - Open README.md in editor
  - Copy sections into Nexus article editor
  - Format adjustments:
    - Headers: Use Nexus heading styles
    - Code blocks: Use Nexus code formatting
    - Links: Convert to Nexus link format if needed
    - Lists: Should work as-is
  - Verify images display correctly in preview

6. **Publish on Nexus**:
  - Review entire article in Nexus preview
  - Test all internal links (table of contents)
  - Test all image links
  - Click "Publish" or "Update"

7. **Tag Release** (for major updates):
   ```bash
   cd D:\var\Boutique\docs
   git tag v1.0.0-docs
   git push --tags
   ```

8. **Clean Up**:
  - Delete local screenshot copies from `docs/images/`
  - Images are now hosted on Nexus only

## When to Update Documentation

Update documentation when:

- **New Features**: Any new UI or functionality
- **Workflow Changes**: Process changes that affect users
- **Bug Fixes**: If the fix changes user-facing behavior
- **Common Questions**: Add to FAQ based on user feedback
- **Clarity Improvements**: Better explanations or examples
- **Screenshot Updates**: When UI changes significantly

## Version Tracking

Use git tags to track documentation versions:

- **Major Updates**: `v1.0.0-docs` (complete rewrites, major additions)
- **Minor Updates**: `v1.1.0-docs` (new sections, significant content)
- **Patches**: `v1.0.1-docs` (typo fixes, minor clarifications)

## Style Guidelines

### Writing Style

- **Target Audience**: Average modders, assume no coding knowledge
- **Tone**: Friendly, helpful, encouraging
- **Language**: Clear, concise, avoid jargon
- **Structure**: Scannable with headers, lists, short paragraphs

### Formatting

- **Headers**: Use descriptive, action-oriented headers
- **Lists**: Use bulleted lists for options, numbered lists for steps
- **Code**: Use code blocks for file paths, syntax examples
- **Emphasis**: Bold for UI elements, italics for emphasis
- **Links**: Use descriptive link text, not "click here"

### Screenshots

- **Consistency**: Same theme (light or dark) across all screenshots
- **Annotations**: Use arrows/boxes to highlight important elements
- **Quality**: High resolution, clear text, no sensitive information
- **Cropping**: Crop to relevant area, keep enough context

## Troubleshooting

### Broken Image Links

If images don't display on Nexus:

1. Check `docs/images/image-urls.md` for correct URL
2. Verify image is still hosted on Nexus
3. Re-upload image if necessary
4. Update URL in README.md and re-sync

### Formatting Issues on Nexus

If formatting looks wrong:

1. Compare markdown preview to Nexus preview
2. Adjust markdown for Nexus compatibility
3. Use Nexus-specific formatting if needed
4. Keep markdown version as source of truth

### Merge Conflicts

If submodule has conflicts:

1. Pull latest from boutique.wiki repo
2. Resolve conflicts locally
3. Commit and push
4. Update parent repo submodule reference

## Questions?

For questions about documentation maintenance:

- Check this guide first
- Review existing documentation structure
- Refer to CONTRIBUTING.md for style guidance
- Open an issue on GitHub if stuck
