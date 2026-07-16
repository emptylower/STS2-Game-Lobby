#!/usr/bin/env bash

release_die() {
  printf '[release] ERROR: %s\n' "$*" >&2
  exit 1
}

release_normalize_path() {
  local raw_path="$1"
  local absolute_path
  local component
  local normalized=""
  local cursor
  local physical
  local missing_suffix=""

  [[ -n "$raw_path" ]] || release_die "--output-dir requires a non-empty value"
  [[ "$raw_path" != *$'\n'* && "$raw_path" != *$'\r'* && "$raw_path" != *$'\t'* ]] || \
    release_die "--output-dir contains a control character"
  case "/$raw_path/" in
    */../*) release_die "--output-dir must not contain '..' path components" ;;
  esac

  if [[ "$raw_path" == /* ]]; then
    absolute_path="$raw_path"
  else
    absolute_path="$PWD/$raw_path"
  fi

  IFS='/' read -r -a path_components <<< "$absolute_path"
  for component in "${path_components[@]}"; do
    [[ -z "$component" || "$component" == "." ]] && continue
    normalized="$normalized/$component"
  done
  [[ -n "$normalized" ]] || normalized="/"

  cursor="$normalized"
  while [[ ! -e "$cursor" ]]; do
    missing_suffix="/$(basename "$cursor")$missing_suffix"
    cursor="$(dirname "$cursor")"
  done
  [[ -d "$cursor" ]] || release_die "Nearest existing output ancestor is not a directory: $cursor"
  physical="$(cd -P "$cursor" && pwd -P)"
  printf '%s%s\n' "${physical%/}" "$missing_suffix"
}

release_prepare_output_dir() {
  local raw_path="$1"
  local repository_root="$2"
  local allow_historical="${3:-0}"
  local candidate
  local protected
  local repository_physical
  repository_physical="$(cd -P "$repository_root" && pwd -P)"
  local -a protected_roots=(
    "$repository_physical/releases"
    "$repository_physical/sts2-lan-connect/release"
    "$repository_physical/lobby-service/release"
  )

  candidate="$(release_normalize_path "$raw_path")"
  [[ "$candidate" != "/" ]] || release_die "Refusing filesystem root as --output-dir"
  [[ "$candidate" != "$HOME" ]] || release_die "Refusing HOME as --output-dir"
  [[ "$candidate" != "$repository_root" ]] || release_die "Refusing repository root as --output-dir"
  [[ "$candidate" != "$repository_root/sts2-lan-connect" ]] || release_die "Refusing client source root as --output-dir"
  [[ "$candidate" != "$repository_root/lobby-service" ]] || release_die "Refusing service source root as --output-dir"

  if [[ "$allow_historical" -ne 1 ]]; then
    for protected in "${protected_roots[@]}"; do
      if [[ "$candidate" == "$protected" || "$candidate" == "$protected/"* ]]; then
        release_die "Refusing protected release output path: $candidate"
      fi
    done
    if [[ "$candidate" == "$repository_physical" || "$candidate" == "$repository_physical/"* ]]; then
      release_die "Refusing explicit package output inside the repository tree: $candidate"
    fi
  fi

  if [[ -e "$candidate" && ! -d "$candidate" ]]; then
    release_die "--output-dir exists and is not a directory: $candidate"
  fi
  mkdir -p "$candidate"
  RELEASE_OUTPUT_DIR="$(cd -P "$candidate" && pwd -P)"
  export RELEASE_OUTPUT_DIR
}

release_require_regular_file() {
  local path="$1"
  [[ -f "$path" && ! -L "$path" ]] || release_die "Required regular source file is missing or unsafe: $path"
}

release_copy_file() {
  local source="$1"
  local destination="$2"
  local mode="${3:-0644}"
  release_require_regular_file "$source"
  mkdir -p "$(dirname "$destination")"
  cp "$source" "$destination"
  chmod "$mode" "$destination"
}

release_assert_manifest() {
  local package_dir="$1"
  local expected_manifest="$2"
  local work_dir="$3"
  local actual_manifest="$work_dir/actual-files.txt"
  local sorted_expected="$work_dir/expected-files.sorted.txt"

  if find "$package_dir" -type l -print -quit | grep -q .; then
    release_die "Package contains a symbolic link: $package_dir"
  fi
  (cd "$package_dir" && find . -type f -print | sed 's#^\./##' | LC_ALL=C sort) > "$actual_manifest"
  LC_ALL=C sort "$expected_manifest" > "$sorted_expected"
  cmp -s "$actual_manifest" "$sorted_expected" || {
    diff -u "$sorted_expected" "$actual_manifest" >&2 || true
    release_die "Package manifest does not match the exact allowlist"
  }
}

release_normalize_tree() {
  local tree="$1"
  find "$tree" -type d -exec chmod 0755 {} +
  find "$tree" -type f -exec touch -t 200001010000 {} +
  find "$tree" -type d -exec touch -t 200001010000 {} +
}

release_create_deterministic_zip() {
  local stage_parent="$1"
  local package_name="$2"
  local zip_path="$3"

  rm -f "$zip_path"
  (
    cd "$stage_parent"
    export TZ=UTC
    LC_ALL=C find "$package_name" -print | LC_ALL=C sort | zip -X -q "$zip_path" -@
  )
}

release_assert_zip_manifest() {
  local zip_path="$1"
  local package_name="$2"
  local expected_manifest="$3"
  local work_dir="$4"
  local zip_files="$work_dir/zip-files.txt"
  local sorted_expected="$work_dir/zip-expected.sorted.txt"

  zipinfo -1 "$zip_path" | while IFS= read -r entry; do
    [[ "$entry" != /* && "$entry" != *'\\'* ]] || release_die "Unsafe zip entry: $entry"
    case "/$entry/" in
      */../*) release_die "Unsafe zip traversal entry: $entry" ;;
    esac
    [[ "$entry" == "$package_name" || "$entry" == "$package_name/"* ]] || \
      release_die "Zip entry escapes package root: $entry"
    [[ "$entry" == */ ]] && continue
    printf '%s\n' "${entry#"$package_name/"}"
  done | LC_ALL=C sort > "$zip_files"
  LC_ALL=C sort "$expected_manifest" > "$sorted_expected"
  cmp -s "$zip_files" "$sorted_expected" || {
    diff -u "$sorted_expected" "$zip_files" >&2 || true
    release_die "Zip manifest does not match the exact allowlist"
  }
}

release_assert_legal_bytes() {
  local package_dir="$1"
  local zip_path="$2"
  local package_name="$3"
  local repository_root="$4"
  local legal_file

  for legal_file in LICENSE THIRD_PARTY_NOTICES; do
    cmp -s "$repository_root/$legal_file" "$package_dir/$legal_file" || \
      release_die "$legal_file differs from the repository source"
    [[ "$(zipinfo -1 "$zip_path" | awk -v entry="$package_name/$legal_file" '$0 == entry { count++ } END { print count + 0 }')" -eq 1 ]] || \
      release_die "Zip must contain $legal_file exactly once"
    unzip -p "$zip_path" "$package_name/$legal_file" | cmp -s - "$repository_root/$legal_file" || \
      release_die "Zip $legal_file differs from the repository source"
  done
}

release_publish_atomic() {
  local staged_package="$1"
  local staged_zip="$2"
  local output_dir="$3"
  local package_name="$4"
  local zip_name="$5"
  local work_dir="$6"
  local final_package="$output_dir/$package_name"
  local final_zip="$output_dir/$zip_name"
  local old_package="$work_dir/previous-package"
  local old_zip="$work_dir/previous.zip"

  [[ ! -L "$final_package" && ! -L "$final_zip" ]] || \
    release_die "Refusing to replace a symbolic-link package output"
  [[ ! -e "$final_package" || -d "$final_package" ]] || \
    release_die "Existing package output is not a directory: $final_package"
  [[ ! -e "$final_zip" || -f "$final_zip" ]] || \
    release_die "Existing archive output is not a regular file: $final_zip"

  if [[ -e "$final_package" ]]; then
    mv "$final_package" "$old_package"
  fi
  if [[ -e "$final_zip" ]]; then
    mv "$final_zip" "$old_zip"
  fi
  if ! mv "$staged_package" "$final_package"; then
    [[ ! -e "$old_package" ]] || mv "$old_package" "$final_package"
    [[ ! -e "$old_zip" ]] || mv "$old_zip" "$final_zip"
    release_die "Failed to publish package directory"
  fi
  if ! mv "$staged_zip" "$final_zip"; then
    rm -rf "$final_package"
    [[ ! -e "$old_package" ]] || mv "$old_package" "$final_package"
    [[ ! -e "$old_zip" ]] || mv "$old_zip" "$final_zip"
    release_die "Failed to publish package archive"
  fi
  rm -rf "$old_package" "$old_zip"
}
