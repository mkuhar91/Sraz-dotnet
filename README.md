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

## How it works

1. The user launches the WPF app and enters a player name.
2. The main screen lets them choose a category and difficulty before starting.
3. `MainWindow` loads the JSON question provider, creates a filtered round, and opens the question window.
4. Each question is displayed with multiple-choice answers and a countdown timer.
5. The selected answer is validated against the correct option index and the round score is updated.
6. When the timer expires or the user answers, the UI shows the explanation and then advances to the next question.
7. After the last question, the round closes and a summary window shows score, totals, and timeout stats.
8. The completed result is persisted to the leaderboard so the player can compare scores over time.

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

## Run the Blazor Server scaffold with Docker

1. Copy `.env.example` to `.env` and set a local PostgreSQL password.
2. Start the app and database:
   ```bash
   docker compose up --build
   ```
3. Open [http://localhost:8080](http://localhost:8080). The `/health` endpoint reports application health.

The Blazor app applies database migrations and imports the seed questions at startup. Keep real credentials out of version control. The WPF app continues to use its existing JSON question and leaderboard stores; PostgreSQL is configured only by the Blazor app.

## Project notes

The app is now structured as a functional quiz game MVP rather than a scaffold-only project. The current implementation uses a JSON question store and local leaderboard persistence, which makes the game playable without requiring a database.

## Planned next improvements

- Add stronger validation and normalization for question data
- Expand leaderboard features and sorting/filtering options
- Improve WPF polish and animations
- Add more automated UI and integration tests
- Consider migrating persistence to SQLite or another durable storage layer
