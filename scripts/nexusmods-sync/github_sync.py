"""GitHub Issues manager for syncing NexusMods bugs."""

import re
from github import Github
from github.Issue import Issue
from github.Repository import Repository
from typing import Optional

from nexusmods_scraper import BugReport


class GitHubSync:
  """Manages GitHub Issues for NexusMods bug sync."""

  NEXUSMODS_LABEL = "nexusmods-bug"
  SYNCED_LABEL = "synced"
  TITLE_PREFIX = "[NexusMods Bug #"

  def __init__(self, token: str, repo: str, dry_run: bool = False):
    """
    Initialize GitHub sync.

    Args:
        token: GitHub API token
        repo: Repository in "owner/repo" format
        dry_run: If True, don't create/update issues, just report what would happen
    """
    self.github = Github(token)
    self.repo: Repository = self.github.get_repo(repo)
    self.dry_run = dry_run
    if not dry_run:
      self._ensure_labels()

  def _ensure_labels(self) -> None:
    """Ensure required labels exist in the repository."""
    existing_labels = {label.name for label in self.repo.get_labels()}

    if self.NEXUSMODS_LABEL not in existing_labels:
      self.repo.create_label(
        name=self.NEXUSMODS_LABEL,
        color="7B68EE",
        description="Bug report synced from NexusMods",
      )

    if self.SYNCED_LABEL not in existing_labels:
      self.repo.create_label(
        name=self.SYNCED_LABEL,
        color="0E8A16",
        description="Automatically synced issue",
      )

  def _make_issue_title(self, bug: BugReport) -> str:
    """Generate GitHub issue title from bug report."""
    return f"{self.TITLE_PREFIX}{bug.bug_id}] {bug.title}"

  def _make_issue_body(self, bug: BugReport) -> str:
    """Generate GitHub issue body from bug report."""
    body_parts = [
      "## Bug Report from NexusMods",
      "",
      bug.description or "*No description provided*",
      "",
      "---",
      "",
      "### Metadata",
      f"- **Original URL:** {bug.url}",
      f"- **Author:** {bug.author}",
      f"- **Date Posted:** {bug.date_posted}",
      f"- **Status on NexusMods:** {bug.status}",
      "",
      f"<!-- nexusmods-bug-id:{bug.bug_id} -->",
      f"<!-- content-hash:{bug.content_hash()} -->",
    ]
    return "\n".join(body_parts)

  def _extract_content_hash(self, body: str) -> Optional[str]:
    """Extract content hash from issue body."""
    match = re.search(r"<!-- content-hash:([a-f0-9]+) -->", body)
    return match.group(1) if match else None

  def _find_existing_issue(self, bug_id: str) -> Optional[Issue]:
    """Find existing GitHub issue for a NexusMods bug ID."""
    search_query = f'repo:{self.repo.full_name} "{self.TITLE_PREFIX}{bug_id}]" in:title'

    try:
      results = self.github.search_issues(search_query)
      for issue in results:
        if f"{self.TITLE_PREFIX}{bug_id}]" in issue.title:
          return issue
    except Exception as e:
      print(f"Error searching for issue: {e}")

    # Fallback: iterate through labeled issues
    try:
      issues = self.repo.get_issues(
        state="all", labels=[self.NEXUSMODS_LABEL]
      )
      for issue in issues:
        if f"{self.TITLE_PREFIX}{bug_id}]" in issue.title:
          return issue
    except Exception as e:
      print(f"Error listing issues: {e}")

    return None

  def sync_bug(self, bug: BugReport) -> dict:
    """
    Sync a single bug report to GitHub Issues.

    Args:
        bug: BugReport to sync

    Returns:
        Dict with sync result info
    """
    result = {
      "bug_id": bug.bug_id,
      "action": None,
      "issue_number": None,
      "issue_url": None,
    }

    existing_issue = self._find_existing_issue(bug.bug_id)
    dry_run_prefix = "[DRY RUN] " if self.dry_run else ""

    if existing_issue:
      existing_hash = self._extract_content_hash(existing_issue.body or "")
      new_hash = bug.content_hash()

      if existing_hash == new_hash:
        result["action"] = "unchanged"
        result["issue_number"] = existing_issue.number
        result["issue_url"] = existing_issue.html_url
        print(f"{dry_run_prefix}Bug #{bug.bug_id}: No changes detected")
      else:
        if self.dry_run:
          result["action"] = "would_update"
          result["issue_number"] = existing_issue.number
          result["issue_url"] = existing_issue.html_url
          print(f"{dry_run_prefix}Bug #{bug.bug_id}: Would update issue #{existing_issue.number}")
        else:
          new_body = self._make_issue_body(bug)
          existing_issue.edit(body=new_body)
          result["action"] = "updated"
          result["issue_number"] = existing_issue.number
          result["issue_url"] = existing_issue.html_url
          print(f"Bug #{bug.bug_id}: Updated issue #{existing_issue.number}")
    else:
      if self.dry_run:
        result["action"] = "would_create"
        result["issue_number"] = "(new)"
        result["issue_url"] = "(not created)"
        print(f"{dry_run_prefix}Bug #{bug.bug_id}: Would create new issue")
        print(f"  Title: {self._make_issue_title(bug)}")
      else:
        title = self._make_issue_title(bug)
        body = self._make_issue_body(bug)
        labels = [self.NEXUSMODS_LABEL, self.SYNCED_LABEL]

        new_issue = self.repo.create_issue(
          title=title,
          body=body,
          labels=labels,
        )
        result["action"] = "created"
        result["issue_number"] = new_issue.number
        result["issue_url"] = new_issue.html_url
        print(f"Bug #{bug.bug_id}: Created issue #{new_issue.number}")

    return result

  def sync_bugs(self, bugs: list[BugReport]) -> list[dict]:
    """
    Sync multiple bug reports to GitHub Issues.

    Args:
        bugs: List of BugReport objects to sync

    Returns:
        List of sync result dicts
    """
    results = []
    for bug in bugs:
      result = self.sync_bug(bug)
      results.append(result)
    return results

  def get_sync_summary(self, results: list[dict]) -> str:
    """Generate a summary of sync results."""
    created = sum(1 for r in results if r["action"] == "created")
    updated = sum(1 for r in results if r["action"] == "updated")
    unchanged = sum(1 for r in results if r["action"] == "unchanged")
    would_create = sum(1 for r in results if r["action"] == "would_create")
    would_update = sum(1 for r in results if r["action"] == "would_update")

    if self.dry_run:
      return (
        f"Dry run complete: {would_create} would be created, "
        f"{would_update} would be updated, {unchanged} unchanged"
      )
    return (
      f"Sync complete: {created} created, {updated} updated, "
      f"{unchanged} unchanged"
    )


def main():
  """Test GitHub sync."""
  import os

  token = os.environ.get("GITHUB_TOKEN")
  repo = os.environ.get("GITHUB_REPO")

  if not token or not repo:
    print("GITHUB_TOKEN and GITHUB_REPO environment variables required")
    return

  sync = GitHubSync(token, repo)
  print(f"Connected to repository: {repo}")


if __name__ == "__main__":
  main()
