# Sraz .NET

Sraz-inspired quiz game built with .NET 8 and WPF.

## Current status

The project now includes a working MVP flow across the domain, application, infrastructure, and WPF layers.

What has been implemented:

- Domain models for questions, rounds, game sessions, and leaderboard entries
- Application-layer abstractions and services for quiz round creation and session flow
- JSON-backed question loading from `data/questions.seed.json`
- Category and difficulty filtering when generating quiz rounds
- WPF startup screen for player name, category, and difficulty selection
- Game loop with timed multiple-choice questions and feedback states
- Round summary screen with score and stats
- Leaderboard persistence and retrieval for completed games
- Unit tests covering key game logic and filtering behavior

## Solution structure

- `src/Sraz.Domain` - domain models and core entities
- `src/Sraz.Application` - game logic, abstractions, and service orchestration
- `src/Sraz.Infrastructure` - persistence and data providers
- `src/Sraz.UI.Wpf` - WPF presentation layer and MVVM view models
- `tests/Sraz.Tests` - unit tests for business logic
- `data` - seed question data and leaderboard persistence data

## Features in the current version

- Start a new quiz session with a player name
- Select a category and difficulty before starting
- Load a filtered question set from JSON seed data
- Answer timed multiple-choice questions
- Track score, correct answers, wrong answers, and timeouts
- See a final round summary when the quiz ends
- Save results to a leaderboard and view top entries

## Getting started

1. Install the .NET 8 SDK.
2. Open `Sraz.sln` in the repository root.
3. Restore NuGet packages:
   ```bash
   dotnet restore
   ```
4. Build the solution:
   ```bash
   dotnet build Sraz.sln
   ```
5. Run the WPF app:
   ```bash
   dotnet run --project src/Sraz.UI.Wpf/Sraz.UI.Wpf.csproj
   ```

## Project notes

The app is now structured as a functional quiz game MVP rather than a scaffold-only project. The current implementation uses a JSON question store and local leaderboard persistence, which makes the game playable without requiring a database.

## Planned next improvements

- Add stronger validation and normalization for question data
- Expand leaderboard features and sorting/filtering options
- Improve WPF polish and animations
- Add more automated UI and integration tests
- Consider migrating persistence to SQLite or another durable storage layer
