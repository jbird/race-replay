#!/usr/bin/env bash
# Applies race-replay's GitHub settings. Safe to re-run.
# Requires: gh (authenticated as a repository admin), jq. Usage: scripts/setup-github.sh [OWNER/REPO]
set -euo pipefail
command -v jq >/dev/null || { echo "jq is required" >&2; exit 2; }
cd "$(dirname "$0")/.."
repo="${1:-$(gh repo view --json nameWithOwner -q .nameWithOwner)}"

# Merging: squash only, tidy branches, Discussions on, wiki off (docs live in docs/)
gh repo edit "$repo" --enable-squash-merge --enable-merge-commit=false --enable-rebase-merge=false \
  --delete-branch-on-merge --enable-discussions --enable-wiki=false

# Actions: read-only GITHUB_TOKEN by default; workflows can't approve PRs; GitHub-owned actions only,
# pinned to full commit SHAs
gh api -X PUT "repos/$repo/actions/permissions/workflow" \
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=false
gh api -X PUT "repos/$repo/actions/permissions" -F enabled=true -f allowed_actions=selected \
  -F sha_pinning_required=true
gh api -X PUT "repos/$repo/actions/permissions/selected-actions" \
  -F github_owned_allowed=true -F verified_allowed=false

# Pages: deploy from GitHub Actions (create, or update if it already exists)
gh api -X POST "repos/$repo/pages" -f build_type=workflow >/dev/null 2>&1 \
  || gh api -X PUT "repos/$repo/pages" -f build_type=workflow

# Security features
gh api -X PUT "repos/$repo/vulnerability-alerts"
gh api -X PUT "repos/$repo/automated-security-fixes"
gh api -X PUT "repos/$repo/private-vulnerability-reporting"
gh api -X PATCH "repos/$repo/code-scanning/default-setup" -f state=configured >/dev/null
gh api -X PATCH "repos/$repo" --input - >/dev/null <<'JSON'
{ "security_and_analysis": {
    "secret_scanning": { "status": "enabled" },
    "secret_scanning_push_protection": { "status": "enabled" } } }
JSON

# Rulesets: create or replace from the committed JSON (07 §5.2)
for file in .github/rulesets/*.json; do
  name=$(jq -r .name "$file")
  id=$(gh api "repos/$repo/rulesets" -q ".[] | select(.name==\"$name\") | .id")
  if [[ -n "$id" ]]; then gh api -X PUT "repos/$repo/rulesets/$id" --input "$file" >/dev/null
  else gh api -X POST "repos/$repo/rulesets" --input "$file" >/dev/null; fi
done

# Labels: shared scheme with racesim (type:, area:), plus Race Replay's own
for t in feature bug spike docs chore; do gh label create "type:$t" -R "$repo" --color 1D76DB --force; done
for a in domain analysis application bundle ingest synthetic playback cli web ci docs; do
  gh label create "area:$a" -R "$repo" --color 0E8A16 --force
done
gh label create data-policy -R "$repo" --color B60205 --force \
  --description "Changes what data is built, packaged or published (ADR-0008)"
gh label create "good first issue" -R "$repo" --color 7057FF --force

# Milestones from the implementation plan (08); created only if missing
existing=$(gh api "repos/$repo/milestones?state=all&per_page=100" -q '.[].title')
for m in "M0 Bootstrap" "M1 Walking skeleton" "S1+S2 Spikes" "M2 Analysis core" \
         "M3 OpenF1, Application, CLI" "M4 Bundle format and packages" \
         "M5 Charts and context" "M6 Delivery" "M7 Hardening and v1.0"; do
  grep -Fxq "$m" <<<"$existing" || gh api "repos/$repo/milestones" -f title="$m" >/dev/null
done

echo "Done. Now complete the manual checklist in docs/07-repository-ci-and-delivery.md §5.3."
