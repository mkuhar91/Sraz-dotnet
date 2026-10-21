# Plan: Sraz na Blazor Serveru (više uređaja, PostgreSQL)

Novi projekt `src/Sraz.UI.Blazor` u istom rješenju. Slojevi `Domain`, `Application` i `Infrastructure` se zadržavaju, a WPF verzija ostaje.

## 0. Odluke

| Tema | Odluka |
|---|---|
| Tehnologija | Blazor Server (.NET 8), SignalR |
| Igra | više uređaja, sobe s kodom |
| Baza | PostgreSQL (EF Core + Npgsql) |
| Identitet | nadimak bez računa, `PlayerId` u cookieju |
| Odspajanje | nakon 60 s NPC preuzima pijune odspojenog igrača |
| Vrijeme | 30 s po pitanju, istek se računa kao netočan odgovor |
| NPC | domaćin može dodati NPC-e; ljudi + NPC <= 4 |
| Spremanje | snapshot stanja igre nakon svakog koraka, oporavak pri startu servera |
| Hosting | Docker (docker-compose), kasnije moguće Azure |

## 1. Arhitektura

```
Sraz.Domain          (bez promjena, uz sitna proširenja)
Sraz.Application     (GameEngine, NPC, BoardFactory + servisi za sobe)
Sraz.Infrastructure  (EF Core + Npgsql, repozitoriji)
Sraz.UI.Blazor       (NOVO: Blazor Server, komponente)
Sraz.UI.Wpf          (ostaje)
Sraz.Tests           (+ novi testovi)
```

## 2. Stanje igre na serveru
- `GameRoom`: soba s kodom (npr. `ABCD`) i vlastitim `GameEngine`; popis igrača, domaćin, stanje (`Lobby`, `InProgress`, `Finished`), zadnja aktivnost; `SemaphoreSlim` oko poteza.
- `IRoomManager` / `RoomManager` (singleton): stvaranje soba, traženje po kodu, brisanje neaktivnih (npr. nakon 2 h), učitavanje soba iz baze pri startu.
- `GameEngine` se ne prepisuje; instancira se po sobi.

## 3. Autorizacija poteza
- Svaki igrač ima `PlayerId` (cookie). Svaka akcija provjerava da `PlayerId` odgovara `CurrentPlayer`.
- Klijent nikad ne šalje ishod ("točno"/"netočno"), nego samo odabrani indeks odgovora.
- Točan odgovor se ne šalje klijentu. Pitanja dvoboja priprema server i čuva u sobi.

## 4. Događaji
- `GameRoom` objavljuje: `PlayerJoined`, `GameStarted`, `PawnMoved`, `PawnCaptured`, `TurnChanged`, `GameFinished`.
- Komponente se pretplate na događaje svoje sobe i pozivaju `InvokeAsync(StateHasChanged)`.

## 5. Spremanje stanja nakon svakog koraka
- Tablica `GameSnapshots`: cijelo stanje igre kao JSON (pijuni s koordinatama, igrači, čiji je red, `HasActedThisTurn`, `State`, `Winner`, seed ploče, `SchemaVersion`).
- Snapshot se sprema nakon svakog ishoda poteza, promjene reda, ulaska/izlaska igrača i na kraju igre.
- Zapis ide u istoj transakciji s potezom; ako zapis ne uspije, potez se odbija.
- Ploča se rekonstruira iz `seed` vrijednosti, a pijuni se spremaju kao ravan popis (bez cikličkih referenci).
- Pitanje u tijeku sprema se zajedno s vremenom isteka. Nakon restarta servera takvo pitanje računa se kao netočno.
- Pri pokretanju `RoomManager` učitava sobe sa stanjem `InProgress`; igrači se vraćaju preko koda sobe i cookieja.

## 6. Odspajanje i NPC preuzimanje
- Igrač ima 60 s za povratak. Nakon toga njegov `Player` postaje `IsNpc = true` (uz zapamćeni `OriginalPlayerId`).
- Ako je odspojeni igrač na redu, a istekne mu 30 s, NPC odmah preuzima.
- Ako se igrač vrati dok igra traje, preuzima pijune natrag na početku svog sljedećeg reda.
- Pobjeda NPC-a koji je preuzeo ne pripisuje se nikome u ljestvici.

## 7. Mjerač od 30 s
- Server vodi vrijeme (`CancellationTokenSource` po pitanju), klijent samo prikazuje odbrojavanje.
- Istek je netočan odgovor i troši akciju pijuna. U dvoboju vrijedi 30 s po pitanju, a istek prekida dvoboj.

## 8. Broj igrača
- Ljudi + NPC <= 4. Lobby ima gumb "Dodaj NPC" (onemogućen kad je soba puna) i uklanjanje NPC-a.
- Pridruživanje u punu sobu se odbija.
- Igra se može pokrenuti s 1 čovjekom i 1 NPC-om; raspored kutova koristi postojeću logiku.

## 9. Baza (PostgreSQL)

| Tablica | Sadržaj |
|---|---|
| `Questions` | tema, tekst, ponuđeni odgovori (JSON), indeks točnog |
| `LeaderboardEntries` | igrač, pobjede, odigrane igre, datum |
| `GameRooms` | kod, stanje, domaćin, kreirano |
| `GameSnapshots` | serijalizirano stanje igre (JSON) |

- `SrazDbContext` prelazi na `UseNpgsql(...)`; EF Core migracije u `Sraz.Infrastructure/Migrations`.
- `DbQuestionProvider` iza postojećeg `IQuestionProvider`; importer učitava `data/questions.seed.json` u `Questions`.
- Connection string preko varijabli okoline (`ConnectionStrings__Default`) i user secrets; nikad u repozitorij.

## 10. UI komponente (`Sraz.UI.Blazor`)

| Komponenta | Opis |
|---|---|
| `Pages/Index.razor` | nadimak, "Stvori sobu", "Pridruži se kodu" |
| `Pages/Lobby.razor` | igrači, kod sobe, veličina ploče, "Dodaj NPC", "Pokreni" (samo domaćin) |
| `Pages/Game.razor` | ploča, igrači, obavijesti |
| `Components/Board.razor`, `Cell.razor` | CSS grid n×n, crni okvir za dopuštene poteze |
| `Components/Pawn.razor` | inline SVG pijun u boji igrača |
| `Components/QuestionDialog.razor` | pitanje s višestrukim izborom + odbrojavanje 30 s |
| `Components/DuelDialog.razor` | 3 pitanja s višestrukim izborom, prekid na prvom netočnom |
| `Components/Legend.razor` | boje i teme |
| `Pages/Leaderboard.razor` | ljestvica iz baze |
| `Pages/GameOver.razor` | pobjednik i "Nova igra" |

- Boje tema kroz CSS varijable; lokalizacija HR/EN preko `IStringLocalizer` (resursi iz `Localizer.cs`).

## 11. Sigurnost i robusnost
- Server provjerava svaki potez; nasumični kodovi soba; ograničenje broja soba i igrača po IP-u; brisanje neaktivnih soba; HTTPS i sigurni cookieji u produkciji.

## 12. Docker
- `Dockerfile` (multi-stage) i `docker-compose.yml` (aplikacija + PostgreSQL s volumenom).
- Migracije se primjenjuju pri pokretanju; `/health` endpoint; logiranje na konzolu; konfiguracija samo iz okoline (priprema za Azure).
- Blazor Server treba WebSocket; za više instanci treba sticky sessions ili Azure SignalR Service.

## 13. Testovi
- Postojeći testovi logike ostaju.
- Novi: `RoomManager` (stvaranje, pridruživanje, nepostojeći kod, puna soba); autorizacija poteza; točan odgovor se ne šalje klijentu; istek 30 s = netočno (potez i dvoboj); snapshot nakon svakog koraka i identičan oporavak; restart usred pitanja = netočno; odspajanje i NPC preuzimanje te povratak; ljudi + NPC > 4 odbijeno; istovremeni potezi; neuspjeh zapisa = potez nije primijenjen; `DbQuestionProvider` i importer; repozitorij ljestvice; bUnit za `Board` i `QuestionDialog`.

## 14. Faze implementacije

| Faza | Sadržaj |
|---|---|
| 1 | Projekt `Sraz.UI.Blazor`, Docker i compose, Npgsql, migracije, `DbQuestionProvider`, importer pitanja |
| 2 | `GameRoom`, `RoomManager`, autorizacija poteza, događaji, snapshot nakon svakog koraka i oporavak pri startu |
| 3 | UI: početna, lobby (s NPC-ima), ploča, pijuni, pitanja, dvoboj, mjerač 30 s |
| 4 | Odspajanje i NPC preuzimanje, ljestvica, kraj igre, lokalizacija |
| 5 | Testovi, dokumentacija, završni Docker i provjera na dva uređaja |

Svaka faza ide u zaseban pull request.

## 15. Status faze 2: sobe i snapshoti (implementirano)

Samo serverska logika, bez UI-ja (UI je faza 3, odspajanje/NPC preuzimanje i ljestvica faza 4).

**Slojevi**
- `Sraz.Application/Rooms`: `GameRoom`, `RoomManager` / `IRoomManager`, `RoomSnapshot`, `IRoomStore`, `QuestionDto`, `RoomEvent`.
- `Sraz.Infrastructure`: `EfRoomStore` (zapis u `GameRooms` + `GameSnapshots`) i `CachedQuestionProvider` (singleton-siguran čitač pitanja iz baze).
- `Sraz.UI.Blazor`: DI registracija, `CookiePlayerIdentity` (`PlayerId` u cookieju `sraz_player_id`, HttpOnly) i `RoomMaintenanceService` (briše neaktivne sobe svakih 5 min).
- `GameEngine` / `Board`: samo dodani hookovi (`StartGame` s popisom `(Name, IsNpc)`, `BoardSeed`, `CurrentPlayerIndex`, `RestoreGame`, `Board.RestorePawn`); pravila igre nisu mijenjana.

**Sobe**
- Kod sobe ima 5 znakova iz abecede bez dvosmislenih znakova, generira se kriptografskim generatorom.
- Ljudi + NPC <= 4; najmanje 1 čovjek; igra se pokreće s najmanje 2 igrača (npr. 1 čovjek + 1 NPC). Dodavanje/uklanjanje NPC-a i pokretanje igre samo za domaćina. Izlazak iz sobe moguć je samo u lobbyju (izlazak usred igre je faza 4).
- Sve radnje jedne sobe idu kroz `SemaphoreSlim`; paralelni potezi: prolazi samo jedan.
- Konfiguracija (`Rooms` odjeljak): `InactivityTimeout` (zadano 2 h), `QuestionTimeLimit` (30 s), `MaxRooms`.

**Autorizacija i pitanja**
- `BeginMoveAsync(playerId, pawnId, direction)` provjerava da je igrač u sobi, na redu i da pijun pripada njemu; server bira pitanje (ili 3 pitanja dvoboja) i drži ih u sobi.
- `AnswerAsync(playerId, selectedIndex)`: klijent šalje samo indeks. `QuestionDto` nema točan odgovor ni objašnjenje.
- Mjerač: `CancellationTokenSource` s `TimeProvider`-om, 30 s po pitanju (i po pitanju dvoboja). Istek = netočan odgovor, troši akciju pijuna, a u dvoboju prekida dvoboj.
- Događaji (`Subscribe` vraća `IDisposable`; pogreška pretplatnika ne ruši sobu): `PlayerJoined`, `PlayerLeft`, `GameStarted`, `PawnMoved`, `PawnCaptured`, `TurnChanged`, `GameFinished` te dodatni `ActionResolved`. Objavljuju se nakon uspješnog spremanja.
- NPC igrači igraju automatski (bez pauza; pauze su UI/faza 3). Ako su svi ljudi eliminirani, NPC-i staju.

**Snapshot**
- JSON (`SchemaVersion` = 1): pijuni kao ravan popis s koordinatama, igrači, čiji je red, `HasActedThisTurn`, stanje i pobjednik, seed i veličina ploče, te otvoreno pitanje (pijun, smjer, odgovori dvoboja, rok). Ploča se rekonstruira iz seeda.
- Sprema se nakon svake promjene (ulazak/izlazak, NPC, start, otvaranje pitanja, ishod poteza, promjena reda, kraj igre). Jedan red po sobi, upsert u jednom `SaveChanges` (jedna transakcija).
- Ako zapis ne uspije, radnja se odbija (`PersistenceFailed`), a memorijsko stanje se vraća na prethodni snapshot. Ako zapis ne uspije pri istjecanju vremena, pitanje ostaje otvoreno s novim mjeračem od 30 s.
- Pri startu `RoomManager.LoadAsync` učitava sobe `Lobby`/`InProgress`; pitanje u tijeku računa se kao netočno (kod dvoboja prekida dvoboj) i taj ishod se sprema.
