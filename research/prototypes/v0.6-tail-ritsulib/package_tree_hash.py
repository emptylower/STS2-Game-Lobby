#!/usr/bin/env python3
"""Hash a released RitsuLib package tree without relying on filesystem ordering."""

from __future__ import annotations

import hashlib
import pathlib
import sys


def file_sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def package_tree_sha256(root: pathlib.Path) -> str:
    if not root.is_dir():
        raise ValueError(f"not a directory: {root}")

    entries = sorted(
        (path for path in root.rglob("*") if path.is_file()),
        key=lambda path: path.relative_to(root).as_posix(),
    )
    digest = hashlib.sha256()
    for path in entries:
        relative = path.relative_to(root).as_posix()
        digest.update(relative.encode("utf-8"))
        digest.update(b"\\0")
        digest.update(file_sha256(path).encode("ascii"))
        digest.update(b"\\n")
    return digest.hexdigest()


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: package_tree_hash.py <package-directory>", file=sys.stderr)
        return 2
    try:
        print(package_tree_sha256(pathlib.Path(sys.argv[1])))
    except (OSError, ValueError) as error:
        print(f"package_tree_hash.py: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
