# Sraz .NET

Sraz-inspired quiz game built with .NET 8 and WPF.

## Solution structure

- `src/Sraz.Domain` - core domain models and contracts
- `src/Sraz.Application` - game logic and use-case services
- `src/Sraz.Infrastructure` - persistence and data access implementations
- `src/Sraz.UI.Wpf` - WPF presentation layer (MVVM)
- `tests/Sraz.Tests` - unit tests
- `data` - seed data (questions)
- `assets/audio` - sound effects and music
- `assets/themes` - theme resources

## Getting started

1. Install .NET 8 SDK
2. Open `Sraz.sln`
3. Restore packages
4. Build solution
5. Run `Sraz.UI.Wpf`

## Notes

This is an initial scaffold. Next steps:
- wire references between projects
- implement quiz engine
- add SQLite persistence
- add question import and validation
