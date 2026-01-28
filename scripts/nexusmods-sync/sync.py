#!/usr/bin/env python3
"""
Main orchestrator for NexusMods to GitHub Issues sync.

Environment variables:
    NEXUSMODS_GAME: Game identifier (e.g., "skyrimspecialedition")
    NEXUSMODS_MOD_ID: Mod ID number
    GITHUB_TOKEN: GitHub API token
    GITHUB_REPO: Target repository (e.g., "owner/repo")

Usage:
    python sync.py              # Normal sync
    python sync.py --dry-run    # Test without creating/updating issues
"""

import argparse
import os
import sys

from github_sync import GitHubSync
from nexusmods_scraper import NexusModsScraper


def get_env_var(name: str, required: bool = True) -> str:
    """Get environment variable with validation."""
    value = os.environ.get(name, "").strip()
    if required and not value:
        print(f"Error: {name} environment variable is required")
        sys.exit(1)
    return value


def main():
    """Main entry point for the sync process."""
    parser = argparse.ArgumentParser(description="Sync NexusMods bugs to GitHub Issues")
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Test mode: scrape and check for existing issues, but don't create or update",
    )
    args = parser.parse_args()

    print("=" * 60)
    print("NexusMods to GitHub Issues Sync")
    if args.dry_run:
        print("*** DRY RUN MODE - No issues will be created or updated ***")
    print("=" * 60)

    game = get_env_var("NEXUSMODS_GAME")
    mod_id = get_env_var("NEXUSMODS_MOD_ID")
    github_token = get_env_var("GITHUB_TOKEN")
    github_repo = get_env_var("GITHUB_REPO", required=False)

    if not github_repo:
        github_repo = get_env_var("GITHUB_REPOSITORY")

    print(f"Game: {game}")
    print(f"Mod ID: {mod_id}")
    print(f"Target repo: {github_repo}")
    print("-" * 60)

    print("\n[Step 1] Scraping bugs from NexusMods...")
    scraper = NexusModsScraper(game, mod_id)
    all_bugs = scraper.scrape_bugs(fetch_details=True)

    if not all_bugs:
        print("No bugs found on NexusMods. Nothing to sync.")
        return

    bugs = [b for b in all_bugs if b.status.lower() not in ("fixed", "closed")]
    skipped = len(all_bugs) - len(bugs)

    print(f"Found {len(all_bugs)} bug(s) on NexusMods, {skipped} fixed/closed (skipped), {len(bugs)} to sync")

    if not bugs:
        print("No open bugs to sync.")
        return

    print("\n[Step 2] Syncing to GitHub Issues...")
    github_sync = GitHubSync(github_token, github_repo, dry_run=args.dry_run)
    results = github_sync.sync_bugs(bugs)

    print("\n" + "-" * 60)
    summary = github_sync.get_sync_summary(results)
    print(summary)

    for result in results:
        action = result["action"]
        bug_id = result["bug_id"]
        issue_num = result["issue_number"]
        url = result["issue_url"]
        print(f"  Bug #{bug_id}: {action} -> Issue #{issue_num} ({url})")

    print("=" * 60)
    print("Sync completed successfully!")


if __name__ == "__main__":
    main()
