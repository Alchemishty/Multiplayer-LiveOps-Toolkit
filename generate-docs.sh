#!/bin/bash
set -euo pipefail

# Generate project-specific docs by analyzing the codebase in the current directory.
# Uses Claude to read the code and produce architecture, domain, conventions, and testing docs.
#
# Adapts to codebase size:
#   - Small (<50 files): full source included
#   - Medium (<200 files): first 60 lines per file
#   - Large (200+): file list + key files only (public API surface)
#
# Usage: Run from the root of a Unity project.
#   /path/to/unity-gamedev-harness/generate-docs.sh
#
# Produces: docs/architecture.md, docs/conventions.md, docs/domain.md, docs/testing.md

HARNESS_DIR="$(cd "$(dirname "$0")" && pwd)"
TEMPLATES_DIR="${HARNESS_DIR}/templates"
PROJECT_DIR="$(pwd)"

if [[ ! -d "$TEMPLATES_DIR" ]]; then
    echo "Error: Templates directory not found at ${TEMPLATES_DIR}" >&2
    exit 1
fi

mkdir -p docs

echo "Analyzing codebase in ${PROJECT_DIR}..."

# Count source files
CS_FILES=$(find . -name "*.cs" -not -path "*/Library/*" -not -path "*/Temp/*" -not -path "*/obj/*" -not -path "*/Packages/*" | sort)
FILE_COUNT=$(echo "$CS_FILES" | grep -c . 2>/dev/null || echo 0)
echo "  Found ${FILE_COUNT} C# files"

CODEBASE_SUMMARY=$(mktemp)
{
    echo "# Codebase Structure"
    echo ""
    echo "## Directory Layout (${FILE_COUNT} C# files)"
    echo "$CS_FILES"
    echo ""

    if [[ $FILE_COUNT -le 50 ]]; then
        # Small project: include full source (up to 200 lines per file)
        echo "## C# Source Files (full)"
        echo ""
        for f in $CS_FILES; do
            echo "### ${f}"
            echo '```csharp'
            head -200 "$f"
            flines=$(wc -l < "$f" | tr -d ' ')
            [[ $flines -gt 200 ]] && echo "... (${flines} lines total)"
            echo '```'
            echo ""
        done

    elif [[ $FILE_COUNT -le 200 ]]; then
        # Medium project: class signatures and key methods (first 60 lines)
        echo "## C# Source Files (signatures — ${FILE_COUNT} files)"
        echo ""
        for f in $CS_FILES; do
            echo "### ${f}"
            echo '```csharp'
            head -60 "$f"
            echo '```'
            echo ""
        done

    else
        # Large project: file list + select key files
        echo "## Key Source Files (${FILE_COUNT} total — showing top-level classes)"
        echo ""
        # Include files from the main source directories, skip tests/examples/editor
        KEY_FILES=$(echo "$CS_FILES" | grep -v -E "(Test|Editor|Example|Sample|Demo)" | head -80)
        for f in $KEY_FILES; do
            echo "### ${f}"
            echo '```csharp'
            head -40 "$f"
            echo '```'
            echo ""
        done
    fi
} > "$CODEBASE_SUMMARY" 2>/dev/null

SUMMARY_LINES=$(wc -l < "$CODEBASE_SUMMARY" | tr -d ' ')
echo "  Summary: ${SUMMARY_LINES} lines (strategy: $(
    if [[ $FILE_COUNT -le 50 ]]; then echo "full"
    elif [[ $FILE_COUNT -le 200 ]]; then echo "signatures"
    else echo "key-files"
    fi
))"

for doc in architecture domain conventions testing; do
    TEMPLATE="${TEMPLATES_DIR}/${doc}.prompt"
    OUTPUT="docs/${doc}.md"

    if [[ ! -f "$TEMPLATE" ]]; then
        echo "  Skipping ${doc}: no template found"
        continue
    fi

    echo "  Generating ${doc}.md..."

    {
        cat "$TEMPLATE"
        echo ""
        echo "---"
        echo ""
        cat "$CODEBASE_SUMMARY"
    } | claude -p \
        --bare \
        --tools "" \
        --disable-slash-commands \
        --strict-mcp-config \
        --output-format text \
        > "$OUTPUT" 2>/dev/null

    DOC_LINES=$(wc -l < "$OUTPUT" | tr -d ' ')
    echo "    → ${OUTPUT} (${DOC_LINES} lines)"
done

rm -f "$CODEBASE_SUMMARY"

echo ""
echo "Done. Generated docs in ${PROJECT_DIR}/docs/"
