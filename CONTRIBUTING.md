# Contributing to SharpAmp

Contributions are welcome, including bug reports, documentation improvements, tests, and code. Please follow our [Code of Conduct](CODE_OF_CONDUCT.md) in all project interactions.

## Report a bug or suggest a feature

Search existing issues and pull requests first. Use the repository's issue templates to describe a reproducible bug or explain a feature's use case and expected behavior.

For bugs, include reproduction steps, expected and actual results, and relevant version information. For audio issues, include devices, backends, drivers, and settings. Remove personal information and secrets from logs and screenshots before sharing them.

For substantial features or architectural changes, open an issue to discuss the approach before investing in implementation.

## Set up a development environment

Install Git and the .NET 10 SDK on Windows, then fork and clone the repository. From the repository root, run:

```powershell
dotnet restore SharpAmp.slnx
dotnet build SharpAmp.slnx
dotnet test SharpAmp.slnx
```

The solution includes Core, CLI, and Tests. The Avalonia UI is currently separate; for UI changes, also run:

```powershell
dotnet build SharpAmp.Avalonia/SharpAmp.Avalonia.csproj
```

See the [README](README.md) for usage, project architecture, and audio verification guidance.

## Make a change

- Create a branch for your contribution and keep each pull request focused on one problem or improvement.
- Follow the surrounding code style and `.editorconfig`. Avoid unrelated formatting changes or refactors.
- Add or update tests for behavior changes where practical, and update documentation when usage or behavior changes.
- Keep audio processing work efficient: avoid blocking operations, I/O, and per-sample allocations on the playback thread.
- Do not commit secrets, personal settings, build outputs, or unrelated files.
- Understand and review everything you submit, including code produced with AI tools. You are responsible for its correctness and maintainability.

## Verify and submit a pull request

Run the relevant build and tests before submitting. The automated suite uses simulated devices; it does not establish hardware compatibility or audible latency. For audio or device changes, describe any manual hardware checks and use headphones at low output volume. State clearly when hardware testing was not performed. For UI changes, describe UI verification and include screenshots when useful.

Open a pull request using the provided template. Explain the problem, the resulting behavior, related issues, and validation results. Note any skipped checks, compatibility changes, and known limitations. Documentation-only changes generally need a review of wording, links, and formatting rather than a build.

Respond constructively to review feedback and keep follow-up changes within the agreed scope. Maintainers may request revisions or decline changes that do not fit the project's goals.

## License

By submitting a contribution, you agree that it may be distributed under the project's [license](LICENSE.txt). Only submit work you have the right to contribute.
