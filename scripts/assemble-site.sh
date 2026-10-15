#!/usr/bin/env bash
# Assembles the static site around a published RaceReplay.Web. Runs on Linux (CI, Codespaces).
# Usage: scripts/assemble-site.sh <site-dir> [--base-path /race-replay/] [--hosted <bundles-dir>]
set -euo pipefail
site="${1:?usage: assemble-site.sh <site-dir> [--base-path /race-replay/] [--hosted <bundles-dir>]}"; shift
base="/"; hosted=""
while (($#)); do
  case "$1" in
    --base-path) base="$2"; shift 2 ;;
    --hosted)    hosted="$2"; shift 2 ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done
# A base path is "/" or "/segment/…/"; plain path characters only, which also keeps the sed below safe.
if [[ ! "$base" =~ ^/([A-Za-z0-9._~-]+/)*$ ]]; then
  echo "invalid --base-path '$base' (expected / or /name/)" >&2; exit 2
fi
mkdir -p "$site/bundles"

# The CLI is built once, on first use, rather than by every `dotnet run`.
cli_built=""
cli() {
  if [[ -z "$cli_built" ]]; then
    dotnet build src/RaceReplay.Cli -c Release --nologo -v quiet
    cli_built=1
  fi
  dotnet run --project src/RaceReplay.Cli -c Release --no-build -- "$@"
}

# 1. Synthetic demo races: deterministic, no network
for seed in $(jq -r '.races[].seed' config/demo-races.json); do
  cli build --source synthetic --seed "$seed" --layout directory --out "$site/bundles/demo"
done

# 2. Hosted bundles: opt-in only (ADR-0008)
if [[ -n "$hosted" ]]; then cp -r "$hosted/." "$site/bundles/"; fi

# 3. Data-policy check: nothing but synthetic data unless hosted mode is on
if [[ -z "$hosted" ]]; then
  while IFS= read -r -d '' manifest; do
    origin=$(jq -r '.provenance.source' "$manifest")
    if [[ "$origin" != "synthetic" ]]; then
      echo "::error file=$manifest::'$origin' bundle found but hosted mode is off (ADR-0008)"; exit 1
    fi
  done < <(find "$site/bundles" -name manifest.json -print0)
fi

# 4. Catalogue (carries the site mode, so published app files are never edited) and schemas.
#    The catalogue step arrives with the CLI's `catalog` command in M1 (RR-012); schemas in M4 (RR-052).
if [[ -d docs/schema ]]; then cp -r docs/schema "$site/schema"; fi

# 5. Compression: Pages can't negotiate pre-compressed files. Keep the Brotli copies for the
#    in-browser decoder (05 §12); drop the unused gzip copies of framework files.
find "$site/_framework" -name '*.gz' -delete

# 6. Base path and SPA deep-link fallback
sed -i "s|<base href=\"/\" />|<base href=\"${base}\" />|" "$site/index.html"
if ! grep -qF "<base href=\"${base}\" />" "$site/index.html"; then
  echo "::error file=$site/index.html::base href not rewritten to '${base}' (has the template's <base> tag changed?)"; exit 1
fi
rm -f "$site/index.html.br" "$site/index.html.gz"   # stale after the rewrite; Pages never serves them
cp "$site/index.html" "$site/404.html"

# 7. Size budget: a published Pages site may be at most 1 GB
size_mb=$(du -sm "$site" | cut -f1)
echo "Site size: ${size_mb} MB" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
if (( size_mb >= 900 )); then echo "::error::Site is ${size_mb} MB (Pages limit 1 GB)"; exit 1; fi
if (( size_mb >= 500 )); then echo "::warning::Site is ${size_mb} MB (budget warning at 500 MB)"; fi
