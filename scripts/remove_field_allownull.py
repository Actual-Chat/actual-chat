#!/usr/bin/env python3
"""
Script to remove [field: AllowNull, MaybeNull] from C# files.
Run this script in the directory where you want to make changes.

Usage:
    cd src/dotnet && python ../../scripts/remove_field_allownull.py
    cd tests && python ../scripts/remove_field_allownull.py
"""

import os
import re

# Pattern to match the attribute with flexible whitespace
ATTRIBUTE_PATTERN = r'\[field:\s*AllowNull,\s*MaybeNull\]'

# Pattern for whole line (just whitespace + attribute + whitespace)
WHOLE_LINE_PATTERN = re.compile(r'^\s*' + ATTRIBUTE_PATTERN + r'\s*$')

# Pattern for inline removal
INLINE_PATTERN = re.compile(ATTRIBUTE_PATTERN + r'\s*')

EXTENSIONS = ('.cs', '.razor')


def process_file(filepath):
    """Process a single file and remove the attribute."""
    try:
        # Read raw bytes to preserve BOM and detect encoding
        with open(filepath, 'rb') as f:
            raw_content = f.read()
    except Exception as e:
        print(f"Error reading {filepath}: {e}")
        return False

    # Detect and preserve BOM
    bom = b''
    content_bytes = raw_content
    if raw_content.startswith(b'\xef\xbb\xbf'):  # UTF-8 BOM
        bom = b'\xef\xbb\xbf'
        content_bytes = raw_content[3:]

    # Decode content
    try:
        content = content_bytes.decode('utf-8')
    except UnicodeDecodeError:
        print(f"Skipping {filepath}: not valid UTF-8")
        return False

    # Detect line ending style
    if '\r\n' in content:
        line_ending = '\r\n'
    elif '\r' in content:
        line_ending = '\r'
    else:
        line_ending = '\n'

    # Split into lines preserving line endings for reconstruction
    lines = content.split(line_ending)

    new_lines = []
    modified = False

    for i, line in enumerate(lines):
        # Check if the whole line is just the attribute
        if WHOLE_LINE_PATTERN.match(line):
            modified = True
            continue  # Skip this line entirely

        # Otherwise, try to remove inline occurrences
        new_line = INLINE_PATTERN.sub('', line)
        if new_line != line:
            modified = True
        new_lines.append(new_line)

    if modified:
        # Reconstruct content with original line endings
        new_content = line_ending.join(new_lines)
        new_content_bytes = bom + new_content.encode('utf-8')

        with open(filepath, 'wb') as f:
            f.write(new_content_bytes)
        return True
    return False


def main():
    modified_count = 0

    for root, dirs, files in os.walk('.'):
        for filename in files:
            if filename.endswith(EXTENSIONS):
                filepath = os.path.join(root, filename)
                if process_file(filepath):
                    print(f"Modified: {filepath}")
                    modified_count += 1

    print(f"\nTotal files modified: {modified_count}")


if __name__ == '__main__':
    main()