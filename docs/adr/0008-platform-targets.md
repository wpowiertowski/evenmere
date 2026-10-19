# 0008. macOS and Linux/Steam Deck first, Windows last

Status: Accepted · Date: 2026-10-18

## Context

The developer works on macOS. Steam Deck is a good fit for a calm builder, and Godot exports Linux cheaply. The v0.1 plan assumed Windows first.

## Decision

- **Launch platforms (M4):**
  - macOS: a universal binary (arm64 + x86_64), code-signed and notarised.
  - Linux x86_64, verified on Steam Deck.
- **Windows x64** is the last port (M5). The codebase stays Windows-safe throughout: no POSIX-only APIs, and paths always go through `Path.Combine` or Godot `res://` and `user://`.
- **Input:** full gamepad support and a Steam Deck layout at 1280 × 800 are first-class requirements from M1, not a port task. Every UI action is reachable without a mouse.
- **Renderer:** Forward+ (Metal on macOS, Vulkan on Linux). A Compatibility-renderer fallback is not planned.
- **CI matrix:** before M4, CI runs `verify.sh` on `ubuntu-latest` and `macos-latest`. From M4 it adds export jobs for macOS and Linux, and Windows joins in M5.

## Consequences

- UI work must be checked at 1280 × 800 and with a gamepad from the first panel onward.
- macOS notarisation needs an Apple Developer account and secrets in CI. The developer provides these before M4.
- Windows-specific issues (path casing, console windows, DPI) are found late, which is accepted.
