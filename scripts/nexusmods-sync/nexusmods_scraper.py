"""NexusMods bug report scraper."""

import cloudscraper
import random
import re
import time
from bs4 import BeautifulSoup
from dataclasses import dataclass
from typing import Optional

BROWSER_CONFIGS = [
  {"browser": "chrome", "platform": "windows", "mobile": False},
  {"browser": "chrome", "platform": "linux", "mobile": False},
  {"browser": "firefox", "platform": "windows", "mobile": False},
  {"browser": "firefox", "platform": "linux", "mobile": False},
]


@dataclass
class BugReport:
  """Represents a bug report from NexusMods."""

  bug_id: str
  title: str
  description: str
  author: str
  date_posted: str
  status: str
  url: str

  def content_hash(self) -> str:
    """Generate a hash of the bug content for change detection."""
    import hashlib

    content = f"{self.title}|{self.description}|{self.status}"
    return hashlib.md5(content.encode()).hexdigest()


class NexusModsScraper:
  """Scrapes bug reports from a NexusMods mod's bugs tab."""

  BASE_URL = "https://www.nexusmods.com"
  USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/120.0.0.0 Safari/537.36"
  )

  def __init__(self, game: str, mod_id: str, delay: float = 1.0):
    """
    Initialize the scraper.

    Args:
        game: Game identifier (e.g., "skyrimspecialedition")
        mod_id: Mod ID number
        delay: Delay between requests in seconds (to avoid rate limiting)
    """
    self.game = game
    self.mod_id = mod_id
    self.delay = delay
    self.session = cloudscraper.create_scraper(
      browser=random.choice(BROWSER_CONFIGS)
    )

  def _get_bugs_url(self, page: int = 1) -> str:
    """Get the URL for the bugs tab."""
    url = f"{self.BASE_URL}/{self.game}/mods/{self.mod_id}?tab=bugs"
    if page > 1:
      url += f"&BusLoadAllRecords=false&page={page}"
    return url

  def _fetch_page(self, url: str, retries: int = 5) -> Optional[BeautifulSoup]:
    """Fetch and parse a page with retry logic."""
    for attempt in range(retries):
      try:
        if attempt > 0:
          wait_time = (2 ** attempt) + random.uniform(1, 3)
          browser_config = random.choice(BROWSER_CONFIGS)
          print(
            f"  Retry {attempt}/{retries - 1} after {wait_time:.1f}s (trying {browser_config['browser']}/{browser_config['platform']})...")
          time.sleep(wait_time)
          self.session = cloudscraper.create_scraper(browser=browser_config)
        else:
          time.sleep(random.uniform(0.5, 2))
        response = self.session.get(url, timeout=30)
        response.raise_for_status()
        return BeautifulSoup(response.text, "html.parser")
      except Exception as e:
        if attempt == retries - 1:
          print(f"Error fetching {url} after {retries} attempts: {e}")
          return None
    return None

  def _parse_bug_row(self, row) -> Optional[BugReport]:
    """Parse a bug report from a table row."""
    try:
      bug_id = row.get("data-issue-id")
      if not bug_id:
        return None

      title_elem = row.select_one(".issue-title")
      title = title_elem.get_text(strip=True) if title_elem else "Unknown"

      url = f"{self.BASE_URL}/{self.game}/mods/{self.mod_id}?tab=bugs&issue_id={bug_id}"

      status_elem = row.select_one(".table-bug-status span")
      status = status_elem.get_text(strip=True) if status_elem else "Unknown"

      date_elem = row.select_one("time")
      if date_elem:
        date_posted = date_elem.get("data-date") or date_elem.get_text(strip=True)
      else:
        date_posted = "Unknown"

      return BugReport(
        bug_id=bug_id,
        title=title,
        description="",
        author="Unknown",
        date_posted=date_posted,
        status=status,
        url=url,
      )
    except Exception as e:
      print(f"Error parsing bug row: {e}")
      return None

  def _fetch_bug_details(self, bug: BugReport) -> BugReport:
    """Fetch full bug details from the bug's page."""
    time.sleep(self.delay)
    soup = self._fetch_page(bug.url)
    if not soup:
      return bug

    # Try to find the bug description
    desc_selectors = [
      ".bug-description",
      ".description",
      ".bug-content",
      ".comment-content",
      "article",
      ".bug-report-body",
    ]

    for selector in desc_selectors:
      desc_elem = soup.select_one(selector)
      if desc_elem:
        bug.description = desc_elem.get_text(strip=True)
        break

    # If no description found, try the main content area
    if not bug.description:
      main_content = soup.select_one("main, .main-content, #content")
      if main_content:
        # Get first few paragraphs
        paragraphs = main_content.select("p")
        if paragraphs:
          bug.description = "\n\n".join(
            p.get_text(strip=True) for p in paragraphs[:3]
          )

    return bug

  def scrape_bugs(self, fetch_details: bool = True) -> list[BugReport]:
    """
    Scrape all bug reports from the mod's bugs tab.

    Args:
        fetch_details: Whether to fetch full details for each bug

    Returns:
        List of BugReport objects
    """
    bugs = []
    page = 1

    while True:
      print(f"Fetching bugs page {page}...")
      url = self._get_bugs_url(page)
      soup = self._fetch_page(url)

      if not soup:
        break

      bug_rows = soup.select("table.forum-bugs tbody tr.mod-issue-row")

      if not bug_rows:
        bug_rows = soup.select("tr[data-issue-id]")

      if not bug_rows:
        print(f"No bug rows found on page {page}")
        break

      page_bugs = []
      for row in bug_rows:
        bug = self._parse_bug_row(row)
        if bug:
          page_bugs.append(bug)

      if not page_bugs:
        break

      bugs.extend(page_bugs)

      # Check for next page
      next_link = soup.select_one(
        "a.next, a[rel='next'], .pagination .next, a[aria-label='Next']"
      )
      if not next_link:
        break

      page += 1
      time.sleep(self.delay)

    # Fetch full details for each bug
    if fetch_details:
      print(f"Fetching details for {len(bugs)} bugs...")
      for i, bug in enumerate(bugs):
        print(f"  Fetching bug {i + 1}/{len(bugs)}: {bug.bug_id}")
        bugs[i] = self._fetch_bug_details(bug)

    return bugs


def main():
  """Test the scraper."""
  import os

  game = os.environ.get("NEXUSMODS_GAME", "skyrimspecialedition")
  mod_id = os.environ.get("NEXUSMODS_MOD_ID", "266")

  scraper = NexusModsScraper(game, mod_id)
  bugs = scraper.scrape_bugs(fetch_details=True)

  print(f"\nFound {len(bugs)} bugs:")
  for bug in bugs:
    print(f"  [{bug.bug_id}] {bug.title} - {bug.status}")
    if bug.description:
      print(f"      {bug.description[:100]}...")


if __name__ == "__main__":
  main()
