---
name: Bug report
about: Report a reproducible problem in SharpAmp.
title: ''
labels: ''
assignees: ''
---

<!-- Search existing issues before submitting. Omit sections that do not apply. -->

## Description

Describe the problem and which part of SharpAmp is affected (Core, CLI, Avalonia UI, or documentation).

## Steps to reproduce

1.
2.
3.

Include the exact CLI command, UI actions, or a minimal code example when relevant.

## Expected behavior

What should happen?

## Actual behavior

What happens instead? Include how often it occurs and whether it worked in an earlier version.

## Environment

- SharpAmp version or commit:
- Windows version/build (or other OS for UI-only issues):
- .NET SDK/runtime version (`dotnet --info`):

## Audio setup (for audio or device issues)

- Input device and backend (MME, WASAPI, ASIO, or DirectSound):
- Output device and backend:
- Driver name/version:
- Sample rate, input/output channel counts, and ASIO channel offsets (if applicable):
- Latency and capture buffer settings; ASIO driver buffer size (if applicable):
- DSP chain/settings, and whether the problem also occurs with bypass:

<!-- Device indices can change; include device names and backends, not just indices. -->

## Logs and additional context

Include error messages, stack traces, relevant device-list output, or screenshots. Remove personal information from logs before posting.
