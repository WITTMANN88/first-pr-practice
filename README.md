# First PR Practice

A tiny sandbox repo for practicing the GitHub pull request workflow.

## What is this?

This repo exsits so you can practice: fork/branch, make a small change,
and open your first pull request without any risk to real code.

## How to use it

1. Create a new branch
2. Make a small edit (fix a typo, tweak this file, add a line)
3. Push the branch and open a pull request
4. Review and merge it

Have fun shipping your first PR!

## cleanup.ps1 (Windows)

A PowerShell script that clears temporary files and reclaims disk
space on Windows: the user/Windows temp folders, browser caches
(Edge, Chrome), and the Recycle Bin, plus an optional Disk Cleanup run.

```powershell
.\cleanup.ps1 -DryRun   # preview what would be removed
.\cleanup.ps1           # run it (asks for confirmation)
.\cleanup.ps1 -Yes      # run without the confirmation prompt
```

## cleanup.sh (Linux/macOS)

A small bash script for clearing temporary files and reclaiming disk
space on Linux/macOS: system temp files, user cache, trash, and common
package-manager caches (apt, brew, npm, pip).

```
./cleanup.sh --dry-run   # preview what would be removed
./cleanup.sh             # run it (asks for confirmation)
./cleanup.sh --yes       # run without the confirmation prompt
```
