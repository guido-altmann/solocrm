"""Merges the Cobertura reports of all test projects and prints the line coverage per assembly.

A line counts as covered if any test project covers it. Usage: python3 tests/coverage-summary.py <report> ...
Writes a Markdown table to stdout (CI: appended to the job summary).
"""
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

covered = defaultdict(set)
valid = defaultdict(set)

for path in sys.argv[1:]:
    for package in ET.parse(path).getroot().iter("package"):
        name = package.get("name")
        for cls in package.iter("class"):
            filename = cls.get("filename")
            for line in cls.iter("line"):
                key = (filename, int(line.get("number")))
                valid[name].add(key)
                if int(line.get("hits")) > 0:
                    covered[name].add(key)

if not valid:
    sys.exit("No coverage data found.")

print("| Assembly | Lines | Covered | Line coverage |")
print("|---|---:|---:|---:|")
for name in sorted(valid):
    total, hit = len(valid[name]), len(covered[name])
    print(f"| {name} | {total} | {hit} | {hit / total:.1%} |")
all_valid = sum(len(v) for v in valid.values())
all_covered = sum(len(c) for c in covered.values())
print(f"| **Total** | {all_valid} | {all_covered} | **{all_covered / all_valid:.1%}** |")
