# Contributing to Boutique Documentation

Thank you for your interest in improving Boutique's documentation! This guide explains how to contribute.

## Ways to Contribute

### Reporting Issues

If you find problems with the documentation:

- **Typos or Errors**: Open an issue on GitHub with the section name and correction
- **Missing Information**: Describe what's unclear or missing
- **Broken Links/Images**: Report the specific link or image
- **Confusing Explanations**: Suggest clearer wording

### Suggesting Improvements

We welcome suggestions for:

- Additional examples or workflows
- Better explanations of complex features
- More screenshots for clarity
- New FAQ entries based on common questions
- Updated syntax references

### Submitting Changes

For direct contributions:

1. Fork the boutique.wiki repository
2. Create a feature branch: `git checkout -b docs/improve-distribution-section`
3. Make your changes to README.md
4. Follow the style guide below
5. Commit with clear message: `docs: improve distribution filtering explanation`
6. Submit a pull request

## Style Guide

### Writing Principles

**Be Clear and Concise**
- Use simple language
- Avoid jargon without explanation
- Short paragraphs (2-4 sentences)
- Active voice preferred

**Be Beginner-Friendly**
- Assume no coding knowledge
- Explain acronyms on first use (e.g., "SPID (Spell Perk Item Distributor)")
- Provide context for technical terms
- Use examples to illustrate concepts

**Be Helpful**
- Anticipate common questions
- Provide step-by-step instructions
- Include troubleshooting tips
- Link to related sections

### Formatting Standards

**Headers**
```markdown
# Main Title (only one per document)
## Section Header
### Subsection Header
#### Minor Heading (use sparingly)
```

**Lists**

Numbered lists for sequential steps:
```markdown
1. First, do this
2. Then, do that
3. Finally, complete this
```

Bulleted lists for options or features:
```markdown
- Option one
- Option two
- Option three
```

**Emphasis**

- **Bold** for UI elements: Click the **Save** button
- *Italic* for emphasis: This is *very* important
- `Code` for paths, commands, syntax: `Data/SKSE/Plugins/`

**Code Blocks**

Use fenced code blocks with language specification:

````markdown
```ini
Outfit = MyOutfit|ActorTypeNPC|NONE|NONE|F
```
````

**Links**

Use descriptive link text:
```markdown
See the [Distribution Overview](#distribution-overview) section.
```

Not:
```markdown
Click [here](#distribution-overview) for more info.
```

**Images**

Include alt text and captions:
```markdown
![Settings panel showing data path configuration](https://nexus-url.com/image.png)
*The Settings panel with Skyrim Data path configured*
```

### Screenshot Guidelines

When contributing screenshots:

- **Resolution**: 1920x1080 minimum
- **Format**: PNG (lossless)
- **Theme**: Use light or dark theme consistently
- **Content**: No personal information visible
- **Quality**: Clear, readable text
- **Annotations**: Add arrows/boxes to highlight key elements
- **Cropping**: Include enough context, remove unnecessary UI

### Documentation Structure

Maintain the existing structure:

1. **Introduction**: What and why
2. **Setup**: How to get started
3. **Quick Start**: Simplest use case
4. **Feature Guides**: Detailed explanations
5. **Workflows**: Complete examples
6. **Troubleshooting**: Common problems
7. **FAQ**: Quick answers
8. **Reference**: Technical details

### Common Patterns

**Introducing a Feature**
```markdown
## Feature Name

Brief description of what it does (1-2 sentences).

### When to Use This Feature

- Use case 1
- Use case 2

### How It Works

Step-by-step explanation.

![Screenshot](url)
*Caption*
```

**Troubleshooting Entry**
```markdown
### Problem Description

**Symptom**: What the user sees

**Cause**: Why it happens

**Solution**:
1. Step to fix
2. Another step
3. Verify fix worked

**Prevention**: How to avoid in future
```

**Workflow Example**
```markdown
### Workflow: Descriptive Name

**Goal**: What you'll accomplish

**Prerequisites**:
- Requirement 1
- Requirement 2

**Steps**:
1. First action
2. Second action
   - Sub-step if needed
3. Final action

**Result**: What you should see

![Screenshot](url)
*Result caption*
```

## Review Process

Pull requests will be reviewed for:

1. **Accuracy**: Information is correct and up-to-date
2. **Clarity**: Explanations are easy to understand
3. **Completeness**: No critical information missing
4. **Style**: Follows this style guide
5. **Formatting**: Markdown renders correctly

## Questions?

If you have questions about contributing:

- Review existing documentation for examples
- Check MAINTENANCE.md for technical details
- Open an issue for clarification
- Ask in pull request comments

Thank you for helping improve Boutique's documentation!
